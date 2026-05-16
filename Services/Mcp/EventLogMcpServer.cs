using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SimpleEventViewer.Models;
using Windows.Storage;

namespace SimpleEventViewer.Services.Mcp;

/// <summary>
/// In-process MCP (Model Context Protocol) server that exposes the
/// currently-loaded events over a local HTTP+JSON-RPC endpoint so an LLM
/// client can query them.
///
/// Bound to 127.0.0.1 by default; not reachable from the network. Uses
/// Streamable HTTP transport: a single POST endpoint that accepts JSON-RPC
/// requests and returns JSON-RPC responses. The simpler SSE stream isn't
/// needed since we never push server-initiated notifications.
///
/// Reads from <see cref="EventLogService.Instance"/>; the app's UI thread is
/// not involved in serving a request.
/// </summary>
public sealed class EventLogMcpServer
{
    private static readonly Lazy<EventLogMcpServer> _instance = new(() => new EventLogMcpServer());
    public static EventLogMcpServer Instance => _instance.Value;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    private const string ProtocolVersion = "2024-11-05";
    private const string ServerName = "SimpleEventViewer";
    private const string ServerVersion = "1.3.0";

    /// <summary>
    /// Max ports to probe above the preferred port when auto-port is on. So
    /// with default 7321, we'll try 7321..7330. Keeps the scan bounded so a
    /// totally-saturated range fails fast.
    /// </summary>
    private const int AutoPortScanCount = 10;

    public bool IsRunning => _listener?.IsListening == true;

    /// <summary>The port we are actually bound to (may differ from the
    /// preferred port when AutoPort took the next available slot).</summary>
    public int Port { get; private set; }

    /// <summary>The port the user asked for in settings.</summary>
    public int PreferredPort { get; private set; }

    public bool AutoPortEnabled { get; private set; }

    /// <summary>
    /// Set when Start() fails. Cleared on successful Start or when AutoPort
    /// salvages the situation by binding a different port. The Settings UI
    /// shows this to explain why the listener isn't running.
    /// </summary>
    public string? LastStartError { get; private set; }

    /// <summary>
    /// Discovery file listing every running Simple Event Viewer instance that
    /// is hosting an MCP server. MCP clients can read this to find all
    /// instances (PID + port). Path is exposed in Settings so users can copy
    /// it.
    /// </summary>
    public static string DiscoveryFilePath
    {
        get
        {
            try { return Path.Combine(ApplicationData.Current.LocalFolder.Path, "mcp-instances.json"); }
            catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleEventViewer", "mcp-instances.json"); }
        }
    }

    /// <summary>
    /// Hook for the load_live_logs tool. The UI layer sets this so the MCP
    /// server can ask MainViewModel to reload the live Windows event log on
    /// the UI thread.
    /// </summary>
    public Action? OnLoadLiveRequested { get; set; }

    /// <summary>
    /// Hook for the load_evtx_file tool. The UI layer sets this so the MCP
    /// server can ask MainViewModel to load a given EVTX file on the UI
    /// thread. Receives the absolute file path.
    /// </summary>
    public Action<string>? OnLoadFileRequested { get; set; }

    private EventLogMcpServer() { }

    /// <summary>
    /// Start the listener on 127.0.0.1:port. Idempotent — calling Start while
    /// running on the same configuration is a no-op; otherwise restarts.
    ///
    /// When <paramref name="autoPort"/> is true, if the preferred port is in
    /// use the listener probes the next <see cref="AutoPortScanCount"/> ports
    /// before giving up. Throws <see cref="HttpListenerException"/> if no
    /// port could be bound.
    /// </summary>
    public void Start(int port, bool autoPort)
    {
        if (IsRunning && PreferredPort == port && AutoPortEnabled == autoPort) return;
        Stop();

        PreferredPort = port;
        AutoPortEnabled = autoPort;
        LastStartError = null;

        var attempts = autoPort ? AutoPortScanCount : 1;
        HttpListener? bound = null;
        int boundPort = port;
        HttpListenerException? lastEx = null;

        for (int i = 0; i < attempts; i++)
        {
            var tryPort = port + i;
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add($"http://127.0.0.1:{tryPort}/");
                l.Start();
                bound = l;
                boundPort = tryPort;
                break;
            }
            catch (HttpListenerException ex)
            {
                lastEx = ex;
            }
        }

        if (bound == null)
        {
            LastStartError = autoPort
                ? $"Ports {port}–{port + attempts - 1} are all in use. Another app (or {attempts} other Simple Event Viewer instances) is holding them."
                : $"Port {port} is in use — likely another Simple Event Viewer instance. Enable \"Auto-pick port\" below to bind the next free port.";
            throw lastEx ?? new HttpListenerException();
        }

        _listener = bound;
        Port = boundPort;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _runTask = Task.Run(() => RunLoop(bound, token), token);

        WriteDiscoveryEntry();
    }

    /// <summary>
    /// Convenience entry point: start if enabled, stop if not. Used both at
    /// app startup and when the user changes the toggle in settings.
    /// </summary>
    public void ApplyConfiguration(bool enabled, int port, bool autoPort)
    {
        if (!enabled)
        {
            Stop();
            LastStartError = null;
            return;
        }
        try
        {
            Start(port, autoPort);
        }
        catch (HttpListenerException ex)
        {
            // LastStartError was set in Start; this catch suppresses the
            // exception for the UI's benefit. Settings polls IsRunning +
            // LastStartError to render the status row.
            System.Diagnostics.Debug.WriteLine($"MCP server failed to start on port {port} (autoPort={autoPort}): {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        RemoveDiscoveryEntry();
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        _cts = null;
        _runTask = null;
    }

    private void WriteDiscoveryEntry()
    {
        try
        {
            var path = DiscoveryFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var entries = ReadDiscoveryEntries();
            var pid = Environment.ProcessId;
            entries.RemoveAll(e => e.pid == pid || !IsProcessAlive(e.pid));
            entries.Add(new DiscoveryEntry(pid, Port, DateTime.UtcNow.ToString("o")));

            File.WriteAllText(path, JsonSerializer.Serialize(
                entries,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MCP discovery write failed: {ex.Message}");
        }
    }

    private void RemoveDiscoveryEntry()
    {
        try
        {
            var path = DiscoveryFilePath;
            if (!File.Exists(path)) return;
            var entries = ReadDiscoveryEntries();
            var pid = Environment.ProcessId;
            entries.RemoveAll(e => e.pid == pid);
            if (entries.Count == 0)
                File.Delete(path);
            else
                File.WriteAllText(path, JsonSerializer.Serialize(
                    entries,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static List<DiscoveryEntry> ReadDiscoveryEntries()
    {
        try
        {
            var path = DiscoveryFilePath;
            if (!File.Exists(path)) return new List<DiscoveryEntry>();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<DiscoveryEntry>>(json) ?? new List<DiscoveryEntry>();
        }
        catch { return new List<DiscoveryEntry>(); }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var _ = System.Diagnostics.Process.GetProcessById(pid);
            return true;
        }
        catch { return false; }
    }

    private record DiscoveryEntry(int pid, int port, string started_at);

    private async Task RunLoop(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                break; // listener stopped
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequest(ctx, token), token);
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx, CancellationToken token)
    {
        try
        {
            // Permissive CORS so a browser-based MCP client can reach us.
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "*";
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "POST, GET, OPTIONS";

            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

            if (ctx.Request.HttpMethod == "GET")
            {
                await WriteText(ctx, 200, "application/json",
                    JsonSerializer.Serialize(new
                    {
                        server = ServerName,
                        version = ServerVersion,
                        protocol = ProtocolVersion,
                        endpoint = $"http://127.0.0.1:{Port}/"
                    }));
                return;
            }

            if (ctx.Request.HttpMethod != "POST")
            {
                ctx.Response.StatusCode = 405;
                ctx.Response.Close();
                return;
            }

            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
            {
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            JsonNode? request;
            try { request = JsonNode.Parse(body); }
            catch
            {
                await WriteJsonRpcError(ctx, null, -32700, "Parse error");
                return;
            }
            if (request == null)
            {
                await WriteJsonRpcError(ctx, null, -32600, "Invalid request");
                return;
            }

            await DispatchJsonRpc(ctx, request);
        }
        catch (Exception ex)
        {
            try { await WriteJsonRpcError(ctx, null, -32603, "Internal error: " + ex.Message); } catch { }
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private async Task DispatchJsonRpc(HttpListenerContext ctx, JsonNode request)
    {
        var id = request["id"];
        var method = request["method"]?.GetValue<string>();
        var @params = request["params"];

        // Notifications (no id) get an empty 204 response.
        if (id == null)
        {
            ctx.Response.StatusCode = 204;
            return;
        }

        switch (method)
        {
            case "initialize":
                await WriteJsonRpcResult(ctx, id, new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = ServerName,
                        ["version"] = ServerVersion
                    }
                });
                return;

            case "ping":
                await WriteJsonRpcResult(ctx, id, new JsonObject());
                return;

            case "tools/list":
                await WriteJsonRpcResult(ctx, id, new JsonObject { ["tools"] = BuildToolsList() });
                return;

            case "tools/call":
                await HandleToolCall(ctx, id, @params);
                return;

            default:
                await WriteJsonRpcError(ctx, id, -32601, $"Method not found: {method}");
                return;
        }
    }

    private JsonArray BuildToolsList()
    {
        return new JsonArray
        {
            new JsonObject
            {
                ["name"] = "current_source",
                ["description"] = "Returns the human-readable label for whatever is currently loaded (e.g. \"Live system logs\" or a filename).",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                }
            },
            new JsonObject
            {
                ["name"] = "event_summary",
                ["description"] = "Returns total event count and a breakdown by level (Critical/Error/Warning/Information/Verbose) for the currently loaded data set.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                }
            },
            new JsonObject
            {
                ["name"] = "list_events",
                ["description"] = "Returns a slice of events sorted newest-first. Use 'limit' (default 50, max 500), 'offset' (default 0), and optional 'level' / 'source' filters.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max events to return (default 50, max 500)" },
                        ["offset"] = new JsonObject { ["type"] = "integer", ["description"] = "How many newest events to skip (default 0)" },
                        ["level"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by level name: Critical, Error, Warning, Information, Verbose" },
                        ["source"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by provider/source name (exact match)" }
                    }
                }
            },
            new JsonObject
            {
                ["name"] = "search_events",
                ["description"] = "Substring search across event messages. Case-insensitive. Returns up to 'limit' newest matches (default 25, max 200).",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Substring to look for in event messages" },
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max matches to return (default 25, max 200)" }
                    },
                    ["required"] = new JsonArray { "query" }
                }
            },
            new JsonObject
            {
                ["name"] = "get_event",
                ["description"] = "Returns full details for a single event by its index in the newest-first list. Use list_events / search_events to discover indices.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["index"] = new JsonObject { ["type"] = "integer", ["description"] = "Zero-based index in the newest-first list" }
                    },
                    ["required"] = new JsonArray { "index" }
                }
            },
            new JsonObject
            {
                ["name"] = "load_live_logs",
                ["description"] = "Switch the running app to the live Windows event log and reload. Returns immediately; call current_source after a moment to confirm.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                }
            },
            new JsonObject
            {
                ["name"] = "load_evtx_file",
                ["description"] = "Load a .evtx file in the running app. Returns immediately. The file must exist on the local filesystem and be readable by the app.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Absolute path to a .evtx file" }
                    },
                    ["required"] = new JsonArray { "path" }
                }
            }
        };
    }

    private async Task HandleToolCall(HttpListenerContext ctx, JsonNode id, JsonNode? @params)
    {
        var name = @params?["name"]?.GetValue<string>();
        var args = @params?["arguments"] as JsonObject;

        try
        {
            JsonNode? payload = name switch
            {
                "current_source"  => Tool_CurrentSource(),
                "event_summary"   => Tool_EventSummary(),
                "list_events"     => Tool_ListEvents(args),
                "search_events"   => Tool_SearchEvents(args),
                "get_event"       => Tool_GetEvent(args),
                "load_live_logs"  => Tool_LoadLiveLogs(),
                "load_evtx_file"  => Tool_LoadEvtxFile(args),
                _                 => null
            };

            if (payload == null)
            {
                await WriteJsonRpcError(ctx, id, -32602, $"Unknown tool: {name}");
                return;
            }

            await WriteJsonRpcResult(ctx, id, new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await WriteJsonRpcResult(ctx, id, new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Tool '{name}' failed: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    // -- Tool implementations -----------------------------------------------

    private static IReadOnlyList<EventLogEntry> SnapshotEvents()
    {
        // Already returns a List copy
        return EventLogService.Instance.Events
            .OrderByDescending(e => e.TimeCreated)
            .ToList();
    }

    private static JsonNode Tool_CurrentSource()
    {
        return new JsonObject
        {
            ["source"] = ResolveCurrentSource(),
            ["event_count"] = EventLogService.Instance.Events.Count
        };
    }

    private static string ResolveCurrentSource()
    {
        var src = EventLogService.Instance.CurrentSource;
        return string.IsNullOrEmpty(src) ? "Live system logs" : src;
    }

    private static JsonNode Tool_EventSummary()
    {
        var events = EventLogService.Instance.Events;
        var byLevel = new Dictionary<string, int>
        {
            ["Critical"] = 0,
            ["Error"] = 0,
            ["Warning"] = 0,
            ["Information"] = 0,
            ["Verbose"] = 0,
            ["LogAlways"] = 0
        };
        foreach (var e in events)
        {
            var key = e.LevelName;
            if (!byLevel.ContainsKey(key)) byLevel[key] = 0;
            byLevel[key]++;
        }

        var levels = new JsonObject();
        foreach (var kv in byLevel) levels[kv.Key] = kv.Value;

        return new JsonObject
        {
            ["total"] = events.Count,
            ["by_level"] = levels,
            ["source"] = ResolveCurrentSource()
        };
    }

    private static JsonNode Tool_ListEvents(JsonObject? args)
    {
        var limit = Math.Clamp(args?["limit"]?.GetValue<int>() ?? 50, 1, 500);
        var offset = Math.Max(0, args?["offset"]?.GetValue<int>() ?? 0);
        var levelFilter = args?["level"]?.GetValue<string>();
        var sourceFilter = args?["source"]?.GetValue<string>();

        IEnumerable<EventLogEntry> seq = SnapshotEvents();
        if (!string.IsNullOrEmpty(levelFilter))
        {
            seq = seq.Where(e => string.Equals(e.LevelName, levelFilter, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrEmpty(sourceFilter))
        {
            seq = seq.Where(e => string.Equals(e.ProviderName, sourceFilter, StringComparison.OrdinalIgnoreCase));
        }

        var all = seq.ToList();
        var page = all.Skip(offset).Take(limit).ToList();

        var arr = new JsonArray();
        for (int i = 0; i < page.Count; i++)
        {
            arr.Add(SummarizeEvent(page[i], offset + i));
        }

        return new JsonObject
        {
            ["total_matching"] = all.Count,
            ["offset"] = offset,
            ["returned"] = page.Count,
            ["events"] = arr
        };
    }

    private static JsonNode Tool_SearchEvents(JsonObject? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return new JsonObject { ["error"] = "query is required" };
        }
        var limit = Math.Clamp(args?["limit"]?.GetValue<int>() ?? 25, 1, 200);

        var snapshot = SnapshotEvents();
        var arr = new JsonArray();
        int matched = 0;
        for (int i = 0; i < snapshot.Count && arr.Count < limit; i++)
        {
            var e = snapshot[i];
            if (e.Message.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matched++;
                arr.Add(SummarizeEvent(e, i));
            }
        }

        return new JsonObject
        {
            ["query"] = query,
            ["returned"] = matched,
            ["events"] = arr
        };
    }

    private static JsonNode Tool_GetEvent(JsonObject? args)
    {
        var index = args?["index"]?.GetValue<int>() ?? -1;
        var snapshot = SnapshotEvents();
        if (index < 0 || index >= snapshot.Count)
        {
            return new JsonObject { ["error"] = $"Index {index} is out of range (count = {snapshot.Count})" };
        }

        var e = snapshot[index];
        return new JsonObject
        {
            ["index"] = index,
            ["id"] = e.Id,
            ["level"] = e.LevelName,
            ["time"] = e.TimeCreated.ToString("o"),
            ["provider"] = e.ProviderName,
            ["provider_guid"] = e.ProviderGuid,
            ["channel"] = e.Channel,
            ["task"] = e.TaskName,
            ["keywords"] = e.Keywords,
            ["user"] = e.Username,
            ["process_id"] = e.ProcessId,
            ["thread_id"] = e.ThreadId,
            ["computer"] = e.Computer,
            ["message"] = e.Message,
            ["xml"] = e.Xml
        };
    }

    private JsonNode Tool_LoadLiveLogs()
    {
        if (OnLoadLiveRequested == null)
        {
            return new JsonObject
            {
                ["status"] = "error",
                ["message"] = "The app is not currently running an active UI bound to the MCP server. " +
                              "Launch Simple Event Viewer and enable the MCP server in Settings."
            };
        }
        OnLoadLiveRequested.Invoke();
        return new JsonObject
        {
            ["status"] = "ok",
            ["message"] = "Live Windows event log load requested. Call current_source in a few seconds to confirm."
        };
    }

    private JsonNode Tool_LoadEvtxFile(JsonObject? args)
    {
        var path = args?["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
        {
            return new JsonObject { ["status"] = "error", ["message"] = "'path' argument is required" };
        }
        if (!System.IO.File.Exists(path))
        {
            return new JsonObject { ["status"] = "error", ["message"] = $"File not found: {path}" };
        }
        if (!path.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonObject
            {
                ["status"] = "error",
                ["message"] = "Only .evtx files are supported by this tool. " +
                              "Pass a path ending in .evtx."
            };
        }
        if (OnLoadFileRequested == null)
        {
            return new JsonObject
            {
                ["status"] = "error",
                ["message"] = "The app is not currently running an active UI bound to the MCP server."
            };
        }
        OnLoadFileRequested.Invoke(path);
        return new JsonObject
        {
            ["status"] = "ok",
            ["message"] = $"EVTX load requested for {System.IO.Path.GetFileName(path)}. " +
                          "Call current_source / event_summary in a few seconds to confirm."
        };
    }

    private static JsonObject SummarizeEvent(EventLogEntry e, int index)
    {
        return new JsonObject
        {
            ["index"] = index,
            ["id"] = e.Id,
            ["level"] = e.LevelName,
            ["time"] = e.TimeCreated.ToString("o"),
            ["provider"] = e.ProviderName,
            ["message_preview"] = Trim(e.Message, 200)
        };
    }

    private static string Trim(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "…";
    }

    // -- JSON-RPC plumbing --------------------------------------------------

    private static async Task WriteJsonRpcResult(HttpListenerContext ctx, JsonNode id, JsonNode result)
    {
        var resp = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result
        };
        await WriteText(ctx, 200, "application/json", resp.ToJsonString());
    }

    private static async Task WriteJsonRpcError(HttpListenerContext ctx, JsonNode? id, int code, string message)
    {
        var resp = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        await WriteText(ctx, 200, "application/json", resp.ToJsonString());
    }

    private static async Task WriteText(HttpListenerContext ctx, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType + "; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }
}
