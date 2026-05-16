using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleEventViewer.Services;

/// <summary>
/// Save/load the user's current filter selection to/from a JSON file so a
/// useful set of filters (e.g. "errors and warnings from .NET Runtime in the
/// last 24 hours") can be shared or reapplied later. Just the filter state
/// is serialised — not the loaded events.
/// </summary>
public class FilterSnapshot
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = new();

    [JsonPropertyName("processes")]
    public List<string> Processes { get; set; } = new();

    [JsonPropertyName("users")]
    public List<string> Users { get; set; } = new();

    [JsonPropertyName("computers")]
    public List<string> Computers { get; set; } = new();

    [JsonPropertyName("channels")]
    public List<string> Channels { get; set; } = new();

    [JsonPropertyName("ids")]
    public List<string> Ids { get; set; } = new();

    /// <summary>Stored as level names: Critical / Error / Warning / Information / Verbose.</summary>
    [JsonPropertyName("levels")]
    public List<string> Levels { get; set; } = new();

    /// <summary>Preset name from the Time Range dropdown (e.g. "Last 24 hours", "All time", "Custom range...").</summary>
    [JsonPropertyName("time_preset")]
    public string? TimePreset { get; set; }

    /// <summary>Only used when TimePreset == "Custom range...".</summary>
    [JsonPropertyName("time_start")]
    public DateTime? TimeStart { get; set; }

    [JsonPropertyName("time_end")]
    public DateTime? TimeEnd { get; set; }

    [JsonPropertyName("search")]
    public string Search { get; set; } = string.Empty;
}

public static class FilterPersistence
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Serialize(FilterSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, _options);

    public static FilterSnapshot? Deserialize(string json) =>
        JsonSerializer.Deserialize<FilterSnapshot>(json);
}
