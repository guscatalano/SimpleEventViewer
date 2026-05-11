using CommunityToolkit.Mvvm.ComponentModel;
using SimpleEventViewer_WinUI.Models;
using SimpleEventViewer_WinUI.Services;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace SimpleEventViewer_WinUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ConcurrentQueue<EventLogEntry> _pendingEvents = new();
    private DispatcherQueueTimer? _flushTimer;

    private string _statusMessage = "Ready";
    private SourceCategory? _selectedSource;
    private EventTypeItem _selectedType = null!;
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
    private LoadWindowItem _selectedLoadWindow;
    private bool _isFileSource = false;
    // The earliest time covered by the currently loaded events (DateTime.MinValue means "all time")
    private DateTime _loadedSinceTime = DateTime.MinValue;
    // Whether we currently have any data loaded
    private bool _hasLoadedData = false;
    private System.Threading.CancellationTokenSource? _prefetchCts;
    private bool _isPrefetching = false;

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
        _selectedType = AvailableTypes[0]; // default to All Levels

        LoadWindows = new List<LoadWindowItem>
        {
            new() { Name = "Last hour", Lookback = TimeSpan.FromHours(1) },
            new() { Name = "Last 24 hours", Lookback = TimeSpan.FromDays(1) },
            new() { Name = "Last 7 days", Lookback = TimeSpan.FromDays(7) },
            new() { Name = "Last 30 days", Lookback = TimeSpan.FromDays(30) },
            new() { Name = "All time", Lookback = null },
            new() { Name = "Custom range...", IsCustom = true }
        };
        _selectedLoadWindow = LoadWindows[1]; // default to Last 24 hours

        EventLogService.Instance.OnEventsLoaded += count =>
        {
            if (count % 500 == 0)
            {
                var total = _totalEventCount > 0 ? _totalEventCount.ToString() : "counting...";
                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusMessage = $"Loading system logs... ({count}/{total})";
                });
            }
        };

        // Background thread: just queue the entries - flush timer handles UI updates
        EventLogService.Instance.OnEventBatchLoaded += batch =>
        {
            if (!_isStreaming && !_isPrefetching) return;

            foreach (var entry in batch)
            {
                _pendingEvents.Enqueue(entry);
            }
        };

        EventLogService.Instance.OnLoadComplete += () =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                FlushPendingEvents(); // Final flush
                _isStreaming = false;
                _flushTimer?.Stop();
            });
        };

        // Setup throttled flush timer
        _flushTimer = _dispatcherQueue.CreateTimer();
        _flushTimer.Interval = TimeSpan.FromMilliseconds(200);
        _flushTimer.Tick += (s, e) => FlushPendingEvents();

        _ = LoadSystemLogsAsync();
    }

    private void SchedulePrefetch()
    {
        CancelPrefetch();
        var cts = new System.Threading.CancellationTokenSource();
        _prefetchCts = cts;
        var olderThan = _loadedSinceTime;

        _ = Task.Run(async () =>
        {
            try
            {
                // Wait a few seconds so the UI is idle before we start pulling more
                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

                if (cts.IsCancellationRequested) return;

                _dispatcherQueue.TryEnqueue(() =>
                {
                    _isPrefetching = true;
                    _flushTimer?.Start();
                    StatusMessage = $"Prefetching older events in background...";
                });

                EventLogService.Instance.AppendOlderSystemLogs(olderThan, cts.Token);

                _dispatcherQueue.TryEnqueue(() =>
                {
                    FlushPendingEvents();
                    while (_pendingEvents.Count > 0) FlushPendingEvents();

                    _isPrefetching = false;
                    if (!cts.IsCancellationRequested)
                    {
                        // We now have everything older than _loadedSinceTime, so update it
                        _loadedSinceTime = DateTime.MinValue;
                        if (!_isStreaming) _flushTimer?.Stop();
                        UpdateSourceCategories();
                        StatusMessage = $"All events loaded ({EventLogService.Instance.Events.Count} total)";
                    }
                });
            }
            catch (TaskCanceledException) { }
            catch { }
        }, cts.Token);
    }

    private void CancelPrefetch()
    {
        _prefetchCts?.Cancel();
        _prefetchCts = null;
        _isPrefetching = false;
    }

    private void FlushPendingEvents()
    {
        // Drain up to N events per tick to keep UI responsive
        const int maxPerTick = 500;
        int drained = 0;
        while (drained < maxPerTick && _pendingEvents.TryDequeue(out var entry))
        {
            if (MatchesFilters(entry))
            {
                FilteredEvents.Add(entry);
            }
            drained++;
        }
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

    public EventTypeItem SelectedType
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
                OnTimeRangeChanged();
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
                OnTimeRangeChanged();
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
                OnTimeRangeChanged();
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
                OnTimeRangeChanged();
            }
        }
    }

    private void OnTimeRangeChanged()
    {
        // For file sources, never reload - just filter the existing data.
        if (_isFileSource)
        {
            ApplyFilters();
            return;
        }

        // For live system logs in Custom mode, reload only if the new start extends earlier than loaded.
        if (_selectedLoadWindow.IsCustom && _hasLoadedData)
        {
            var newRangeStart = GetRequestedRangeStart();
            if (newRangeStart < _loadedSinceTime)
            {
                _ = LoadSystemLogsAsync();
            }
            else
            {
                ApplyFilters();
            }
        }
        else
        {
            ApplyFilters();
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
    public IReadOnlyList<LoadWindowItem> LoadWindows { get; }

    public LoadWindowItem SelectedLoadWindow
    {
        get => _selectedLoadWindow;
        set
        {
            if (SetProperty(ref _selectedLoadWindow, value))
            {
                OnPropertyChanged(nameof(IsCustomRange));

                // File sources never reload from the live system - filter only.
                if (_isFileSource)
                {
                    ApplyFilters();
                    return;
                }

                // If we don't have data yet, load it.
                if (!_hasLoadedData)
                {
                    _ = LoadSystemLogsAsync();
                    return;
                }

                // Decide: does the new range fit within what's already loaded?
                var newRangeStart = GetRequestedRangeStart();
                if (newRangeStart >= _loadedSinceTime)
                {
                    // New range is a subset of loaded data - just filter
                    ApplyFilters();
                }
                else
                {
                    // New range extends earlier than loaded data - need to reload
                    _ = LoadSystemLogsAsync();
                }
            }
        }
    }

    private DateTime GetRequestedRangeStart()
    {
        if (_selectedLoadWindow.IsCustom)
        {
            if (!StartTime.HasValue) return DateTime.MinValue;
            return StartTime.Value.Date + StartTimeOfDay;
        }
        if (_selectedLoadWindow.Lookback.HasValue)
        {
            return DateTime.Now - _selectedLoadWindow.Lookback.Value;
        }
        return DateTime.MinValue;
    }

    public Visibility IsCustomRange => _selectedLoadWindow.IsCustom ? Visibility.Visible : Visibility.Collapsed;

    private async Task LoadSystemLogsAsync()
    {
        StatusMessage = "Loading system logs...";
        _isFileSource = false;
        CancelPrefetch();
        FilteredEvents.Clear();
        SourceCategories.Clear();
        SourceCategories.Add(new SourceCategory { Name = "All Sources", Count = 0, IsAllSources = true });
        SelectedSource = SourceCategories[0];
        _totalEventCount = -1;

        // Clear any leftover queue
        while (_pendingEvents.TryDequeue(out _)) { }

        try
        {
            DateTime? customStart = null;
            DateTime? customEnd = null;
            TimeSpan? lookback = _selectedLoadWindow.Lookback;

            if (_selectedLoadWindow.IsCustom)
            {
                if (StartTime.HasValue) customStart = StartTime.Value.Date + StartTimeOfDay;
                if (EndTime.HasValue) customEnd = EndTime.Value.Date + EndTimeOfDay;
                lookback = null;
                _loadedSinceTime = customStart ?? DateTime.MinValue;
            }
            else if (lookback.HasValue)
            {
                _loadedSinceTime = DateTime.Now - lookback.Value;
            }
            else
            {
                _loadedSinceTime = DateTime.MinValue; // "All time"
            }

            // Kick off count in parallel
            _ = Task.Run(() =>
            {
                try
                {
                    var count = EventLogService.Instance.CountSystemEvents(lookback, customStart, customEnd);
                    _totalEventCount = count;
                }
                catch { }
            });

            // Enable streaming and start the flush timer
            _isStreaming = true;
            _flushTimer?.Start();

            await Task.Run(() => EventLogService.Instance.LoadCurrentSystemLogs(lookback, customStart, customEnd));

            _dispatcherQueue.TryEnqueue(() =>
            {
                // Final drain of any remaining events
                FlushPendingEvents();
                while (_pendingEvents.Count > 0)
                {
                    FlushPendingEvents();
                }

                _isStreaming = false;
                _flushTimer?.Stop();
                _hasLoadedData = true;
                UpdateSourceCategories();
                StatusMessage = $"Loaded {EventLogService.Instance.Events.Count} events from system";

                // Schedule a background prefetch of older events for instant time-range widening later.
                // Skip if we already have everything (lookback was null = "All time").
                if (_loadedSinceTime > DateTime.MinValue)
                {
                    SchedulePrefetch();
                }
            });
        }
        catch (Exception ex)
        {
            _isStreaming = false;
            _flushTimer?.Stop();
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
        if (_selectedLoadWindow.IsCustom)
        {
            if (!StartTime.HasValue) return DateTime.MinValue;
            return StartTime.Value.Date + StartTimeOfDay;
        }

        if (_selectedLoadWindow.Lookback.HasValue)
        {
            // For file sources, anchor relative to the newest event so presets are useful
            // For live data, the lookback is already applied at load time, so this is a no-op
            var reference = _isFileSource ? GetNewestEventTime() : DateTime.Now;
            return reference - _selectedLoadWindow.Lookback.Value;
        }

        return DateTime.MinValue; // "All time"
    }

    private DateTime GetEffectiveEndTime()
    {
        if (_selectedLoadWindow.IsCustom)
        {
            if (!EndTime.HasValue) return DateTime.MaxValue;
            return EndTime.Value.Date + EndTimeOfDay;
        }

        return DateTime.MaxValue;
    }

    private DateTime GetNewestEventTime()
    {
        var events = EventLogService.Instance.Events;
        return events.Count > 0 ? events[0].TimeCreated : DateTime.Now;
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
        _isFileSource = false;
        _ = LoadSystemLogsAsync();
    }

    public void LoadFile(string filePath, string fileType)
    {
        _ = LoadFileAsync(filePath, fileType);
    }

    private async Task LoadFileAsync(string filePath, string fileType)
    {
        StatusMessage = $"Loading {fileType} file...";
        _isFileSource = true;
        CancelPrefetch();

        // Default to "All time" when loading a file - the file's events likely aren't in the last 24h
        var allTime = LoadWindows.FirstOrDefault(w => w.Lookback == null && !w.IsCustom);
        if (allTime != null && _selectedLoadWindow != allTime)
        {
            // Set the field directly to avoid triggering reload
            _selectedLoadWindow = allTime;
            OnPropertyChanged(nameof(SelectedLoadWindow));
            OnPropertyChanged(nameof(IsCustomRange));
        }

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

public class LoadWindowItem
{
    public string Name { get; set; } = string.Empty;
    public TimeSpan? Lookback { get; set; }
    public bool IsCustom { get; set; }
}
