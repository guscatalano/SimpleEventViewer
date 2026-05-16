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
        string? channel = null,
        string? eventId = null)
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
            (string.IsNullOrEmpty(eventId) || e.Id.ToString() == eventId) &&
            (string.IsNullOrEmpty(searchTerms) || e.Message.Contains(searchTerms, StringComparison.OrdinalIgnoreCase))
        ).OrderByDescending(e => e.TimeCreated).ToList();
    }

    /// <summary>
    /// Multi-select-friendly overload. A null or empty set on a dimension means
    /// "no filter on this dimension"; a non-empty set means the entry must
    /// match one of the values in that set (OR within the dimension).
    /// </summary>
    public static List<EventLogEntry> Apply(
        IEnumerable<EventLogEntry> events,
        HashSet<string>? sources,
        HashSet<LogLevel>? levels,
        DateTime? startTime,
        DateTime? endTime,
        HashSet<string>? usernames,
        string? searchTerms,
        HashSet<string>? processIds,
        HashSet<string>? computers,
        HashSet<string>? channels,
        HashSet<string>? eventIds = null,
        string? quickFind = null)
    {
        bool HasAny(HashSet<string>? s) => s != null && s.Count > 0;
        bool HasLevels(HashSet<LogLevel>? s) => s != null && s.Count > 0;
        bool MatchesQuickFind(EventLogEntry e)
        {
            if (string.IsNullOrEmpty(quickFind)) return true;
            return e.Message.Contains(quickFind, StringComparison.OrdinalIgnoreCase)
                || e.ProviderName.Contains(quickFind, StringComparison.OrdinalIgnoreCase)
                || e.LevelName.Contains(quickFind, StringComparison.OrdinalIgnoreCase);
        }

        return events.Where(e =>
            (!HasAny(sources)     || sources!.Contains(e.ProviderName)) &&
            (!HasLevels(levels)   || levels!.Contains(e.Level)) &&
            (!startTime.HasValue  || e.TimeCreated >= startTime.Value) &&
            (!endTime.HasValue    || e.TimeCreated <= endTime.Value) &&
            (!HasAny(usernames)   || usernames!.Contains(e.Username ?? string.Empty)) &&
            (!HasAny(processIds)  || processIds!.Contains(e.ProcessId.ToString())) &&
            (!HasAny(computers)   || computers!.Contains(e.Computer ?? string.Empty)) &&
            (!HasAny(channels)    || channels!.Contains(e.Channel ?? string.Empty)) &&
            (!HasAny(eventIds)    || eventIds!.Contains(e.Id.ToString())) &&
            (string.IsNullOrEmpty(searchTerms) || e.Message.Contains(searchTerms, StringComparison.OrdinalIgnoreCase)) &&
            MatchesQuickFind(e)
        ).OrderByDescending(e => e.TimeCreated).ToList();
    }
}
