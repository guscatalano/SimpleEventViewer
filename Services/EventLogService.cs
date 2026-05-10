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

    public IReadOnlyList<EventLogEntry> Events => _events.ToList().AsReadOnly();
    public IReadOnlyDictionary<string, int> SourceCounts => _sourceCounts.ToDictionary(k => k.Key, v => v.Value) as IReadOnlyDictionary<string, int>;

    private EventLogService() { }

    public event Action<int>? OnEventsLoaded;

    public int CountSystemEvents()
    {
        var count = 0;
        var query = new EventLogQuery("Application", PathType.LogName, "*")
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

    public void LoadCurrentSystemLogs()
    {
        _events.Clear();
        _sourceCounts.Clear();

        var query = new EventLogQuery("Application", PathType.LogName, "*")
        {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);

        while (true)
        {
            var entry = reader.ReadEvent();
            if (entry == null) break;

            var logEntry = ConvertToEntry(entry);
            if (logEntry != null)
            {
                _events.Add(logEntry);
                var source = logEntry.ProviderName;
                _sourceCounts.AddOrUpdate(source, 1, (key, count) => count + 1);
            }

            OnEventsLoaded?.Invoke(_events.Count);
        }
    }

    public void LoadEtlFile(string filePath)
    {
        _events.Clear();
        _sourceCounts.Clear();

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
                        var source = entry.ProviderName;
                        _sourceCounts.AddOrUpdate(source, 1, (key, count) => count + 1);
                    }
                }
            }
        }
    }

    public void LoadEvtxFile(string filePath)
    {
        _events.Clear();
        _sourceCounts.Clear();

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

    public List<EventLogEntry> FilterEvents(
        string? source = null,
        LogLevel? level = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        string? username = null,
        string? searchTerms = null)
    {
        var allEvents = _events.ToList();
        return allEvents.Where(e =>
            (string.IsNullOrEmpty(source) || e.ProviderName == source) &&
            (level == null || e.Level == level.Value) &&
            (!startTime.HasValue || e.TimeCreated >= startTime.Value) &&
            (!endTime.HasValue || e.TimeCreated <= endTime.Value) &&
            (string.IsNullOrEmpty(username) || e.Username.Contains(username, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(searchTerms) || e.Message.Contains(searchTerms, StringComparison.OrdinalIgnoreCase))
        ).OrderByDescending(e => e.TimeCreated).ToList();
    }

    public List<string> GetAvailableSources() => _sourceCounts.OrderByDescending(x => x.Value).Select(x => x.Key).ToList();

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
                        var source = entry.ProviderName;
                        _sourceCounts.AddOrUpdate(source, 1, (key, count) => count + 1);
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
            var provider = node.SelectSingleNode("System/Provider")?.Attributes["Name"]?.Value ?? "Unknown";
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
