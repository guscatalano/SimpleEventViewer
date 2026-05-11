namespace SimpleEventViewer_WinUI.Models;

public class EventLogEntry
{
    public int Id { get; set; }
    public LogLevel Level { get; set; }
    public string LevelName => Level.ToString();
    public DateTime TimeCreated { get; set; }
    public string TimeCreatedDisplay => TimeCreated.ToString("yyyy-MM-dd HH:mm:ss");
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderGuid { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public int ThreadId { get; set; }
    public string Computer { get; set; } = string.Empty;
    public bool IsSystemLog { get; set; } = true;
}

public class SourceCategory
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool IsAllSources { get; set; }
    public string Display => $"{Name} ({Count})";
}
