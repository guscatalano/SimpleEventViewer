using CommunityToolkit.Mvvm.ComponentModel;
using SimpleEventViewer_WinUI.Models;
using SimpleEventViewer_WinUI.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace SimpleEventViewer_WinUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcherQueue;

    private string _statusMessage = "Ready";
    private SourceCategory? _selectedSource;
    private EventTypeItem? _selectedType;
    private DateTimeOffset? _startTime;
    private DateTimeOffset? _endTime;
    private TimeSpan _startTimeOfDay = TimeSpan.Zero;
    private TimeSpan _endTimeOfDay = new TimeSpan(23, 59, 59);
    private string _searchText = string.Empty;
    private EventLogEntry? _selectedEvent;
    private ObservableCollection<SourceCategory> _sourceCategories = new();
    private ObservableCollection<EventLogEntry> _filteredEvents = new();
    private int _totalEventCount = -1;
    private bool _isStreaming = false;

    public MainViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        AvailableTypes = new List<EventTypeItem>
        {
            new() { Level = null, Name = "All Levels" },
            new() { Level = LogLevel.Critical, Name = "Critical" },
            new() { Level = LogLevel.Error, Name = "Error" },
            new() { Level = LogLevel.Warning, Name = "Warning" },
            new() { Level = LogLevel.Information, Name = "Information" },
            new() { Level = LogLevel.Verbose, Name = "Verbose" }
        };

        EventLogService.Instance.OnEventsLoaded += count =>
        {
            if (count % 100 == 0 || count == _totalEventCount)
            {
                var total = _totalEventCount > 0 ? _totalEventCount.ToString() : "counting...";
                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusMessage = $"Loading system logs... ({count}/{total})";
                });
            }
        };

        EventLogService.Instance.OnEventBatchLoaded += batch =>
        {
            if (!_isStreaming) return;

            // Capture batch on background thread then marshal to UI thread
            var batchCopy = batch.ToList();
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!_isStreaming) return;

                // Apply filters to the batch and add matching entries
                foreach (var entry in batchCopy)
                {
                    if (MatchesFilters(entry))
                    {
                        InsertSorted(entry);
                    }
                }
            });
        };

        EventLogService.Instance.OnLoadComplete += () =>
        {
            _isStreaming = false;
        };

        _ = LoadSystemLogsAsync();
    }

    public ObservableCollection<SourceCategory> SourceCategories
    {
        get => _sourceCategories;
        set => SetProperty(ref _sourceCategories, value);
    }

    public ObservableCollection<EventLogEntry> FilteredEvents
    {
        get => _filteredEvents;
        set => SetProperty(ref _filteredEvents, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public SourceCategory? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetProperty(ref _selectedSource, value))
            {
                ApplyFilters();
            }
        }
    }

    public EventTypeItem? SelectedType
    {
        get => _selectedType;
        set
        {
            if (SetProperty(ref _selectedType, value))
            {
                ApplyFilters();
            }
        }
    }

    public DateTimeOffset? StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, value))
            {
                ApplyFilters();
            }
        }
    }

    public DateTimeOffset? EndTime
    {
        get => _endTime;
        set
        {
            if (SetProperty(ref _endTime, value))
            {
                ApplyFilters();
            }
        }
    }

    public TimeSpan StartTimeOfDay
    {
        get => _startTimeOfDay;
        set
        {
            if (SetProperty(ref _startTimeOfDay, value))
            {
                ApplyFilters();
            }
        }
    }

    public TimeSpan EndTimeOfDay
    {
        get => _endTimeOfDay;
        set
        {
            if (SetProperty(ref _endTimeOfDay, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public EventLogEntry? SelectedEvent
    {
        get => _selectedEvent;
        set => SetProperty(ref _selectedEvent, value);
    }

    public IReadOnlyList<EventTypeItem> AvailableTypes { get; }

    private async Task LoadSystemLogsAsync()
    {
        StatusMessage = "Loading system logs...";
        FilteredEvents.Clear();
        SourceCategories.Clear();
        SourceCategories.Add(new SourceCategory { Name = "All Sources", Count = 0, IsAllSources = true });
        SelectedSource = SourceCategories[0];
        _totalEventCount = -1;

        try
        {
            // Kick off count in parallel - results update progress display once known
            _ = Task.Run(() =>
            {
                try
                {
                    var count = EventLogService.Instance.CountSystemEvents();
                    _totalEventCount = count;
                }
                catch { }
            });

            // Enable streaming - events will flow in via OnEventBatchLoaded as they load
            _isStreaming = true;

            await Task.Run(() => EventLogService.Instance.LoadCurrentSystemLogs());

            _dispatcherQueue.TryEnqueue(() =>
            {
                _isStreaming = false;
                UpdateSourceCategories();
                StatusMessage = $"Loaded {EventLogService.Instance.Events.Count} events from system";
            });
        }
        catch (Exception ex)
        {
            _isStreaming = false;
            _dispatcherQueue.TryEnqueue(() =>
            {
                StatusMessage = $"Error: {ex.Message}";
            });
        }
    }

    private void UpdateSourceCategories()
    {
        // Preserve current selection
        var currentSelectionName = SelectedSource?.Name;

        SourceCategories.Clear();
        SourceCategories.Add(new SourceCategory { Name = "All Sources", Count = EventLogService.Instance.Events.Count, IsAllSources = true });

        foreach (var source in EventLogService.Instance.GetAvailableSources())
        {
            var count = EventLogService.Instance.SourceCounts[source];
            SourceCategories.Add(new SourceCategory { Name = source, Count = count, IsAllSources = false });
        }

        // Restore selection by name
        if (!string.IsNullOrEmpty(currentSelectionName))
        {
            var match = SourceCategories.FirstOrDefault(c => c.Name == currentSelectionName);
            if (match != null)
            {
                SelectedSource = match;
            }
            else
            {
                SelectedSource = SourceCategories[0];
            }
        }
        else if (SourceCategories.Count > 0)
        {
            SelectedSource = SourceCategories[0];
        }
    }

    private DateTime GetEffectiveStartTime()
    {
        if (!StartTime.HasValue) return DateTime.MinValue;
        return StartTime.Value.Date + StartTimeOfDay;
    }

    private DateTime GetEffectiveEndTime()
    {
        if (!EndTime.HasValue) return DateTime.MaxValue;
        return EndTime.Value.Date + EndTimeOfDay;
    }

    private bool MatchesFilters(EventLogEntry entry)
    {
        var startTime = GetEffectiveStartTime();
        var endTime = GetEffectiveEndTime();
        var sourceName = SelectedSource == null || SelectedSource.IsAllSources ? null : SelectedSource.Name;

        return (string.IsNullOrEmpty(sourceName) || entry.ProviderName == sourceName) &&
               (SelectedType?.Level == null || entry.Level == SelectedType.Level.Value) &&
               (entry.TimeCreated >= startTime) &&
               (entry.TimeCreated <= endTime) &&
               (string.IsNullOrEmpty(SearchText) || entry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }

    private void InsertSorted(EventLogEntry entry)
    {
        // Most recent events first - binary search for insertion point
        int low = 0;
        int high = FilteredEvents.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (FilteredEvents[mid].TimeCreated > entry.TimeCreated)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }
        FilteredEvents.Insert(low, entry);
    }

    public void ApplyFilters()
    {
        var startTime = GetEffectiveStartTime();
        var endTime = GetEffectiveEndTime();
        var sourceName = SelectedSource == null || SelectedSource.IsAllSources ? null : SelectedSource.Name;

        var filtered = EventLogService.Instance.FilterEvents(
            sourceName,
            SelectedType?.Level,
            startTime,
            endTime,
            null,
            SearchText
        );

        FilteredEvents.Clear();
        foreach (var entry in filtered)
        {
            FilteredEvents.Add(entry);
        }
    }

    public void RefreshCurrentView()
    {
        _ = LoadSystemLogsAsync();
    }

    public void LoadFile(string filePath, string fileType)
    {
        _ = LoadFileAsync(filePath, fileType);
    }

    private async Task LoadFileAsync(string filePath, string fileType)
    {
        StatusMessage = $"Loading {fileType} file...";

        try
        {
            await Task.Run(() =>
            {
                switch (fileType.ToLower())
                {
                    case "etl":
                        EventLogService.Instance.LoadEtlFile(filePath);
                        break;
                    case "xml":
                        EventLogService.Instance.LoadXmlFile(filePath);
                        break;
                    case "evtx":
                        EventLogService.Instance.LoadEvtxFile(filePath);
                        break;
                }
            });

            _dispatcherQueue.TryEnqueue(() =>
            {
                UpdateSourceCategories();
                ApplyFilters();
                StatusMessage = $"Loaded {EventLogService.Instance.Events.Count} events from {fileType} file";
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                StatusMessage = $"Error: {ex.Message}";
            });
        }
    }
}

public class EventTypeItem
{
    public LogLevel? Level { get; set; }
    public string Name { get; set; } = string.Empty;
}
