using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using SimpleEventViewer.Models;

namespace SimpleEventViewer.Services;

/// <summary>
/// Pure file-reading logic for .evtx and .etl files. Separated from EventLogService
/// so it can be unit tested without a Windows App SDK runtime.
/// </summary>
[SupportedOSPlatform("windows")]
public static class EventLogFileReader
{
    /// <summary>
    /// Reads all events from an evtx/etl file and returns them as EventLogEntry objects.
    /// </summary>
    public static List<EventLogEntry> Read(string filePath)
    {
        var entries = new List<EventLogEntry>();
        var query = new EventLogQuery(filePath, PathType.FilePath, "*") { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        while (true)
        {
            var record = reader.ReadEvent();
            if (record == null) break;
            var entry = Convert(record);
            if (entry != null)
            {
                entry.IsSystemLog = false;
                entries.Add(entry);
            }
        }
        return entries;
    }

    public static EventLogEntry? Convert(EventRecord record)
    {
        if (record == null) return null;
        try
        {
            var level = record.Level != null ? (LogLevel)(byte)record.Level : LogLevel.Information;

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
                Message = SafeFormatDescription(record),
                Xml = SafeToXml(record),
                ProcessId = record.ProcessId ?? 0,
                ThreadId = record.ThreadId ?? 0,
                Computer = record.MachineName ?? Environment.MachineName,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string SafeFormatDescription(EventRecord record)
    {
        try { return record.FormatDescription() ?? "No description available"; }
        catch { return "No description available"; }
    }

    private static string SafeToXml(EventRecord record)
    {
        try { return record.ToXml(); }
        catch { return string.Empty; }
    }
}
