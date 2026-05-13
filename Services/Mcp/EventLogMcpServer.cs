using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SimpleEventViewer.Models;

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
    private const string ServerVersion = "1.1.0";

    public bool IsRunning => _listener?.IsListening == true;
    public int Port { get; private set; }

    private EventLogMcpServer() { }

    /// <summary>
    /// Start the listener on 127.0.0.1:port. Idempotent — calling Start while
    /// running on the same port is a no-op; on a different port restarts.
    /// </summary>
    public void Start(int port)
    {
        if (IsRunning && Port == port) return;
        Stop();

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        _listener = listener;
        Port = port;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _runTask = Task.Run(() => RunLoop(listener, token), token);
    }

    /// <summary>
    /// Convenience entry point: start if enabled, stop if not. Used both at
    /// app startup and when the user changes the toggle in settings.
    /// </summary>
    public void ApplyConfiguration(bool enabled, int port)
    {
        if (!enabled)
        {
            Stop();
            return;
        }
        try
        {
            Start(port);
        }
        catch (HttpListenerException ex)
        {
            // Port in use, ACL missing, etc. Surface via Debug; UI can show this
            // by polling IsRunning after toggling.
            System.Diagnostics.Debug.WriteLine($"MCP server failed to start on port {port}: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        _cts = null;
        _runTask = null;
    }

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
                "current_source" => Tool_CurrentSource(),
                "event_summary"  => Tool_EventSummary(),
                "list_events"    => Tool_ListEvents(args),
                "search_events"  => Tool_SearchEvents(args),
                "get_event"      => Tool_GetEvent(args),
                _                => null
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
