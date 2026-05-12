using SimpleEventViewer.Models;

namespace SimpleEventViewer.Services;

/// <summary>
/// Pure filter logic for event log entries. Lives in its own file with no
/// dependencies on Windows-specific APIs so it can be unit tested cross-platform.
/// </summary>
public static class EventFilter
{
    public static List<EventLogEntry> Apply(
        IEnumerable<EventLogEntry> events,
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
        return events.Where(e =>
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
}
