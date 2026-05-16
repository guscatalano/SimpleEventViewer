namespace SimpleEventViewer.Models;

public class EventLogEntry : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Manually fire INPC for a property. Used to force level-badge bindings
    /// in visible DataGrid rows to re-evaluate their converter (e.g. when
    /// the color scheme changes); the property values themselves don't
    /// actually change.
    /// </summary>
    public void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));

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
    /// True for real rows; false for the synthetic "All X" entry. The
    /// multi-select ListView binds its container's Visibility to this via
    /// a BoolToVisibility converter so the "All" row stays out of the
    /// checkbox flyout. Kept as a bool (not a Visibility) so this model
    /// remains free of Microsoft.UI.Xaml — the tests project compiles it.
    /// </summary>
    public bool IsListRow => !IsAllSources;

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
