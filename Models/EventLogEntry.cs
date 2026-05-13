namespace SimpleEventViewer.Models;

public class EventLogEntry
{
    public int Id { get; set; }
    public LogLevel Level { get; set; }
    public string LevelName => Level.ToString();
    public DateTime TimeCreated { get; set; }
    public string TimeCreatedDisplay => TimeCreated.ToString("yyyy-MM-dd HH:mm:ss");
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderGuid { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
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

public class SourceCategory : System.ComponentModel.INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool IsAllSources { get; set; }
    public string Display => $"{Name} ({Count})";

    /// <summary>
    /// Collapsed for the synthetic "All X" entries so the multi-select
    /// ListView doesn't render a meaningless checkbox row for them. The
    /// ComboBox still shows them because that's where "no filter active"
    /// is selectable.
    /// </summary>
    public Microsoft.UI.Xaml.Visibility ListRowVisibility =>
        IsAllSources ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
