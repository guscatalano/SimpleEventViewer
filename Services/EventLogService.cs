using System.Collections.Concurrent;
using System.IO;
using System.Xml;
using SimpleEventViewer_WinUI.Models;
using System.Diagnostics.Eventing.Reader;

namespace SimpleEventViewer_WinUI.Services;

public class EventLogService
{
    private static readonly Lazy<EventLogService> _instance = new(() => new EventLogService());
    public static EventLogService Instance => _instance.Value;

    private readonly ConcurrentBag<EventLogEntry> _events = new();
    private readonly ConcurrentDictionary<string, int> _sourceCounts = new();
    private readonly ConcurrentDictionary<string, int> _processCounts = new();
    private readonly ConcurrentDictionary<string, int> _userCounts = new();
    private readonly ConcurrentDictionary<string, int> _computerCounts = new();
    private readonly ConcurrentDictionary<string, int> _channelCounts = new();

    public IReadOnlyList<EventLogEntry> Events => _events.ToList().AsReadOnly();
    public IReadOnlyDictionary<string, int> SourceCounts => _sourceCounts.ToDictionary(k => k.Key, v => v.Value);
    public IReadOnlyDictionary<string, int> ProcessCounts => _processCounts.ToDictionary(k => k.Key, v => v.Value);
    public IReadOnlyDictionary<string, int> UserCounts => _userCounts.ToDictionary(k => k.Key, v => v.Value);
    public IReadOnlyDictionary<string, int> ComputerCounts => _computerCounts.ToDictionary(k => k.Key, v => v.Value);
    public IReadOnlyDictionary<string, int> ChannelCounts => _channelCounts.ToDictionary(k => k.Key, v => v.Value);

    private EventLogService() { }

    private void TrackCounts(EventLogEntry entry)
    {
        _sourceCounts.AddOrUpdate(entry.ProviderName, 1, (k, c) => c + 1);
        if (entry.ProcessId > 0)
            _processCounts.AddOrUpdate(entry.ProcessId.ToString(), 1, (k, c) => c + 1);
        if (!string.IsNullOrEmpty(entry.Username))
            _userCounts.AddOrUpdate(entry.Username, 1, (k, c) => c + 1);
        if (!string.IsNullOrEmpty(entry.Computer))
            _computerCounts.AddOrUpdate(entry.Computer, 1, (k, c) => c + 1);
        if (!string.IsNullOrEmpty(entry.Channel))
            _channelCounts.AddOrUpdate(entry.Channel, 1, (k, c) => c + 1);
    }

    private static string BuildQuery(TimeSpan? lookback, DateTime? start = null, DateTime? end = null)
    {
        if (lookback.HasValue)
        {
            var millis = (long)lookback.Value.TotalMilliseconds;
            return $"*[System[TimeCreated[timediff(@SystemTime) <= {millis}]]]";
        }

        if (start.HasValue || end.HasValue)
        {
            var conditions = new List<string>();
            if (start.HasValue)
                conditions.Add($"@SystemTime>='{start.Value.ToUniversalTime():o}'");
            if (end.HasValue)
                conditions.Add($"@SystemTime<='{end.Value.ToUniversalTime():o}'");
            return $"*[System[TimeCreated[{string.Join(" and ", conditions)}]]]";
        }

        return "*";
    }

    public event Action<int>? OnEventsLoaded;
    public event Action<List<EventLogEntry>>? OnEventBatchLoaded;
    public event Action? OnLoadComplete;

    public int CountSystemEvents(TimeSpan? lookback = null, DateTime? start = null, DateTime? end = null)
    {
        var count = 0;
        var query = new EventLogQuery("Application", PathType.LogName, BuildQuery(lookback, start, end))
        {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);
        while (reader.ReadEvent() != null)
        {
            count++;
        }
        return count;
    }

    public void AppendOlderSystemLogs(DateTime olderThan, System.Threading.CancellationToken token)
    {
        // Query events older than olderThan, do NOT clear existing data
        var query = new EventLogQuery("Application", PathType.LogName, BuildQuery(null, null, olderThan))
        {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);

        const int batchSize = 100;
        var batch = new List<EventLogEntry>(batchSize);

        while (!token.IsCancellationRequested)
        {
            var entry = reader.ReadEvent();
            if (entry == null) break;

            var logEntry = ConvertToEntry(entry);
            if (logEntry != null)
            {
                _events.Add(logEntry);
                batch.Add(logEntry);
                TrackCounts(logEntry);

                if (batch.Count >= batchSize)
                {
                    OnEventBatchLoaded?.Invoke(batch);
                    batch = new List<EventLogEntry>(batchSize);
                }
            }
        }

        if (batch.Count > 0)
        {
            OnEventBatchLoaded?.Invoke(batch);
        }

        if (!token.IsCancellationRequested)
        {
            OnLoadComplete?.Invoke();
        }
    }

    public void LoadCurrentSystemLogs(TimeSpan? lookback = null, DateTime? start = null, DateTime? end = null)
    {
        _events.Clear();
        _sourceCounts.Clear();
        _processCounts.Clear();
        _userCounts.Clear();
        _computerCounts.Clear();
        _channelCounts.Clear();

        var query = new EventLogQuery("Application", PathType.LogName, BuildQuery(lookback, start, end))
        {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);

        const int batchSize = 100;
        var batch = new List<EventLogEntry>(batchSize);

        while (true)
        {
            var entry = reader.ReadEvent();
            if (entry == null) break;

            var logEntry = ConvertToEntry(entry);
            if (logEntry != null)
            {
                _events.Add(logEntry);
                batch.Add(logEntry);
                TrackCounts(logEntry);

                if (batch.Count >= batchSize)
                {
                    OnEventBatchLoaded?.Invoke(batch);
                    batch = new List<EventLogEntry>(batchSize);
                }
            }

            OnEventsLoaded?.Invoke(_events.Count);
        }

        // Flush any remaining events in the partial batch
        if (batch.Count > 0)
        {
            OnEventBatchLoaded?.Invoke(batch);
        }

        OnLoadComplete?.Invoke();
    }

    public void LoadEtlFile(string filePath)
    {
        _events.Clear();
        _sourceCounts.Clear();
        _processCounts.Clear();
        _userCounts.Clear();
        _computerCounts.Clear();
        _channelCounts.Clear();

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "wevtutil",
            Arguments = $"qe \"{filePath}\" /f:xml /c:* /rd:true",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                ParseEvtxXmlOutput(output);
            }
        }
        catch { }
    }

    public void LoadXmlFile(string filePath)
    {
        _events.Clear();
        _sourceCounts.Clear();
        _processCounts.Clear();
        _userCounts.Clear();
        _computerCounts.Clear();
        _channelCounts.Clear();

        var xmlContent = File.ReadAllText(filePath);

        if (xmlContent.Contains("<Event>"))
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);

            var events = doc.SelectNodes("//Event");
            if (events != null)
            {
                foreach (XmlNode node in events)
                {
                    var entry = ParseEventXmlNode(node);
                    if (entry != null)
                    {
                        _events.Add(entry);
                        TrackCounts(entry);
                    }
                }
            }
        }
    }

    public void LoadEvtxFile(string filePath)
    {
        _events.Clear();
        _sourceCounts.Clear();
        _processCounts.Clear();
        _userCounts.Clear();
        _computerCounts.Clear();
        _channelCounts.Clear();

        // Use EventLogReader directly - it can read .evtx files natively
        var query = new EventLogQuery(filePath, PathType.FilePath, "*")
        {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);

        const int batchSize = 100;
        var batch = new List<EventLogEntry>(batchSize);

        while (true)
        {
            var entry = reader.ReadEvent();
            if (entry == null) break;

            var logEntry = ConvertToEntry(entry);
            if (logEntry != null)
            {
                logEntry.IsSystemLog = false;
                _events.Add(logEntry);
                batch.Add(logEntry);
                TrackCounts(logEntry);

                if (batch.Count >= batchSize)
                {
                    OnEventBatchLoaded?.Invoke(batch);
                    batch = new List<EventLogEntry>(batchSize);
                }
            }
        }

        if (batch.Count > 0)
        {
            OnEventBatchLoaded?.Invoke(batch);
        }

        OnLoadComplete?.Invoke();
    }

    public List<EventLogEntry> FilterEvents(
        string? source = null,
        LogLevel? level = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        string? username = null,
        string? searchTerms = null,
        string? processId = null,
        string? computer = null,
        string? channel = null)
    {
        var allEvents = _events.ToList();
        return allEvents.Where(e =>
            (string.IsNullOrEmpty(source) || e.ProviderName == source) &&
            (level == null || e.Level == level.Value) &&
            (!startTime.HasValue || e.TimeCreated >= startTime.Value) &&
            (!endTime.HasValue || e.TimeCreated <= endTime.Value) &&
            (string.IsNullOrEmpty(username) || e.Username == username) &&
            (string.IsNullOrEmpty(processId) || e.ProcessId.ToString() == processId) &&
            (string.IsNullOrEmpty(computer) || e.Computer == computer) &&
            (string.IsNullOrEmpty(channel) || e.Channel == channel) &&
            (string.IsNullOrEmpty(searchTerms) || e.Message.Contains(searchTerms, StringComparison.OrdinalIgnoreCase))
        ).OrderByDescending(e => e.TimeCreated).ToList();
    }

    public List<string> GetAvailableSources() => _sourceCounts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    public List<string> GetAvailableProcesses() => _processCounts.Keys.OrderBy(k => int.TryParse(k, out var n) ? n : int.MaxValue).ToList();
    public List<string> GetAvailableUsers() => _userCounts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    public List<string> GetAvailableComputers() => _computerCounts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    public List<string> GetAvailableChannels() => _channelCounts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    private EventLogEntry? ConvertToEntry(EventRecord record)
    {
        if (record == null) return null;

        try
        {
            LogLevel level;
            if (record.Level != null)
            {
                level = (LogLevel)(byte)record.Level;
            }
            else
            {
                level = LogLevel.Information;
            }

            var username = string.Empty;
            if (record.UserId != null)
            {
                try
                {
                    var sid = new System.Security.Principal.SecurityIdentifier(record.UserId.Value);
                    username = sid.Translate(typeof(System.Security.Principal.NTAccount)).ToString() ?? string.Empty;
                }
                catch { }
            }

            var keywords = record.Keywords > 0 ? string.Join(", ", record.Keywords) : string.Empty;

            return new EventLogEntry
            {
                Id = record.Id,
                Level = level,
                TimeCreated = record.TimeCreated ?? DateTime.Now,
                ProviderName = record.ProviderName ?? "Unknown",
                ProviderGuid = record.ProviderId?.ToString() ?? string.Empty,
                Channel = record.LogName ?? string.Empty,
                TaskName = record.TaskDisplayName ?? string.Empty,
                Keywords = keywords,
                Username = username,
                Message = GetMessage(record),
                Xml = GetXml(record),
                ProcessId = record.ProcessId ?? 0,
                ThreadId = record.ThreadId ?? 0,
                Computer = record.MachineName ?? Environment.MachineName,
                IsSystemLog = true
            };
        }
        catch
        {
            return null;
        }
    }

    private string GetMessage(EventRecord record)
    {
        try
        {
            return record.FormatDescription() ?? "No description available";
        }
        catch
        {
            return "No description available";
        }
    }

    private string GetXml(EventRecord record)
    {
        try
        {
            return record.ToXml();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ParseEvtxXmlOutput(string output)
    {
        try
        {
            // Wrap in a root element if not already wrapped
            if (!output.TrimStart().StartsWith("<Events"))
            {
                output = $"<Events>{output}</Events>";
            }

            var doc = new XmlDocument();
            doc.LoadXml(output);

            var events = doc.SelectNodes("//Event");
            if (events != null)
            {
                foreach (XmlNode node in events)
                {
                    var entry = ParseEventXmlNode(node);
                    if (entry != null)
                    {
                        _events.Add(entry);
                        TrackCounts(entry);
                    }
                }
            }
        }
        catch { }
    }

    private EventLogEntry? ParseEventXmlNode(XmlNode node)
    {
        if (node == null) return null;

        try
        {
            var idNode = node.SelectSingleNode("System/EventID");
            var id = idNode?.InnerText ?? "0";
            var levelNode = node.SelectSingleNode("System/Level");
            var level = levelNode != null && int.TryParse(levelNode.InnerText, out var levelVal) ? (LogLevel)levelVal : LogLevel.Information;
            var timeNode = node.SelectSingleNode("System/TimeCreated");
            var time = timeNode?.Attributes["SystemTime"]?.Value;
            var providerNode = node.SelectSingleNode("System/Provider");
            var provider = providerNode?.Attributes["Name"]?.Value ?? "Unknown";
            var providerGuid = providerNode?.Attributes["Guid"]?.Value ?? string.Empty;
            var channel = node.SelectSingleNode("System/Channel")?.InnerText ?? string.Empty;
            var task = node.SelectSingleNode("System/Task")?.InnerText ?? string.Empty;
            var keywords = node.SelectSingleNode("System/Keywords")?.InnerText ?? string.Empty;
            var userNode = node.SelectSingleNode("System/Security")?.Attributes["UserID"]?.Value;
            var messageNode = node.SelectSingleNode("RenderingInfo/Message");
            var message = messageNode?.InnerText ?? string.Empty;

            var username = GetUsernameFromSid(userNode);

            return new EventLogEntry
            {
                Id = int.TryParse(id, out var idVal) ? idVal : 0,
                Level = level,
                TimeCreated = DateTime.TryParse(time, out var dt) ? dt : DateTime.Now,
                ProviderName = provider,
                ProviderGuid = providerGuid,
                Channel = channel,
                TaskName = task,
                Keywords = keywords,
                Username = username,
                Message = message,
                Xml = node.OuterXml,
                ProcessId = 0,
                ThreadId = 0,
                Computer = Environment.MachineName,
                IsSystemLog = false
            };
        }
        catch
        {
            return null;
        }
    }

    private string GetUsernameFromSid(string? sid)
    {
        if (string.IsNullOrEmpty(sid)) return string.Empty;

        try
        {
            var securityIdentifier = new System.Security.Principal.SecurityIdentifier(sid);
            return securityIdentifier.Translate(typeof(System.Security.Principal.NTAccount)).ToString() ?? string.Empty;
        }
        catch
        {
            return sid ?? string.Empty;
        }
    }
}
