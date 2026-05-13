using CommunityToolkit.Mvvm.ComponentModel;
using SimpleEventViewer.Models;
using SimpleEventViewer.Services;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace SimpleEventViewer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ConcurrentQueue<EventLogEntry> _pendingEvents = new();
    private DispatcherQueueTimer? _flushTimer;

    private string _statusMessage = "Ready";
    private SourceCategory? _selectedSource;
    private SourceCategory? _selectedProcess;
    private SourceCategory? _selectedUser;
    private SourceCategory? _selectedComputer;
    private SourceCategory? _selectedChannel;
    private EventTypeItem _selectedType = null!;
    private DateTimeOffset? _startTime;
    private DateTimeOffset? _endTime;
    private TimeSpan _startTimeOfDay = TimeSpan.Zero;
    private TimeSpan _endTimeOfDay = new TimeSpan(23, 59, 59);
    private string _searchText = string.Empty;
    private EventLogEntry? _selectedEvent;
    private ObservableCollection<SourceCategory> _sourceCategories = new();
    private ObservableCollection<SourceCategory> _processCategories = new();
    private ObservableCollection<SourceCategory> _userCategories = new();
    private ObservableCollection<SourceCategory> _computerCategories = new();
    private ObservableCollection<SourceCategory> _channelCategories = new();
    private ObservableCollection<EventLogEntry> _filteredEvents = new();
    private int _totalEventCount = -1;
    private bool _isStreaming = false;
    private LoadWindowItem _selectedLoadWindow;
    private bool _isFileSource = false;
    private string _currentSource = "Live system logs";
    // The earliest time covered by the currently loaded events (DateTime.MinValue means "all time")
    private DateTime _loadedSinceTime = DateTime.MinValue;
    // Whether we currently have any data loaded
    private bool _hasLoadedData = false;
    private System.Threading.CancellationTokenSource? _prefetchCts;
    private bool _isPrefetching = false;
    private bool _suppressApplyFilters = false;
    private System.Threading.CancellationTokenSource? _filterOptionsCts;

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

    public ObservableCollection<SourceCategory> ProcessCategories
    {
        get => _processCategories;
        set => SetProperty(ref _processCategories, value);
    }

    public ObservableCollection<SourceCategory> UserCategories
    {
        get => _userCategories;
        set => SetProperty(ref _userCategories, value);
    }

    public ObservableCollection<SourceCategory> ComputerCategories
    {
        get => _computerCategories;
        set => SetProperty(ref _computerCategories, value);
    }

    public ObservableCollection<SourceCategory> ChannelCategories
    {
        get => _channelCategories;
        set => SetProperty(ref _channelCategories, value);
    }

    public SourceCategory? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (SetProperty(ref _selectedChannel, value))
            {
                ApplyFilters();
            }
        }
    }

    public SourceCategory? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (SetProperty(ref _selectedProcess, value))
            {
                ApplyFilters();
            }
        }
    }

    public SourceCategory? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (SetProperty(ref _selectedUser, value))
            {
                ApplyFilters();
            }
        }
    }

    public SourceCategory? SelectedComputer
    {
        get => _selectedComputer;
        set
        {
            if (SetProperty(ref _selectedComputer, value))
            {
                ApplyFilters();
            }
        }
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

    public string CurrentSource
    {
        get => _currentSource;
        set
        {
            if (SetProperty(ref _currentSource, value))
            {
                EventLogService.Instance.CurrentSource = value;
            }
        }
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

        ProcessCategories.Clear();
        ProcessCategories.Add(new SourceCategory { Name = "All Processes", Count = 0, IsAllSources = true });
        SelectedProcess = ProcessCategories[0];

        UserCategories.Clear();
        UserCategories.Add(new SourceCategory { Name = "All Users", Count = 0, IsAllSources = true });
        SelectedUser = UserCategories[0];

        ComputerCategories.Clear();
        ComputerCategories.Add(new SourceCategory { Name = "All Computers", Count = 0, IsAllSources = true });
        SelectedComputer = ComputerCategories[0];

        ChannelCategories.Clear();
        ChannelCategories.Add(new SourceCategory { Name = "All Channels", Count = 0, IsAllSources = true });
        SelectedChannel = ChannelCategories[0];

        CurrentSource = "Live system logs";
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

    /// <summary>
    /// Rebuild every filter dropdown so it shows only values that exist in the
    /// event subset matched by the OTHER currently-active filters. The dropdown
    /// for filter X is computed with X excluded from the criteria so the user
    /// can still switch their selection within X. The selected value is kept
    /// in the list (as a stale entry) when it no longer matches, so we don't
    /// silently drop the user's choice.
    ///
    /// Heavy lifting (5× full-table filter sweeps) runs on a background thread
    /// since the master list can be 200k+ events on a "Last 30 days" lookback.
    /// The UI thread only does the final ObservableCollection swap. Concurrent
    /// invocations are cancelled so only the latest result is applied.
    /// </summary>
    private void UpdateSourceCategories()
    {
        // Snapshot the criteria on the UI thread. The events list itself is
        // already a copy (EventLogService.Events.ToList()).
        var master = EventLogService.Instance.Events;
        var startTime = GetEffectiveStartTime();
        var endTime = GetEffectiveEndTime();
        var sourceName = SelectedSource is { IsAllSources: false } ? SelectedSource.Name : null;
        var processId  = SelectedProcess is { IsAllSources: false } ? SelectedProcess.Name : null;
        var userName   = SelectedUser is { IsAllSources: false } ? SelectedUser.Name : null;
        var computer   = SelectedComputer is { IsAllSources: false } ? SelectedComputer.Name : null;
        var channel    = SelectedChannel is { IsAllSources: false } ? SelectedChannel.Name : null;
        var level      = SelectedType?.Level;
        var search     = SearchText;

        var currentSourceName   = SelectedSource?.Name;
        var currentProcessName  = SelectedProcess?.Name;
        var currentUserName     = SelectedUser?.Name;
        var currentComputerName = SelectedComputer?.Name;
        var currentChannelName  = SelectedChannel?.Name;

        _filterOptionsCts?.Cancel();
        _filterOptionsCts = new System.Threading.CancellationTokenSource();
        var token = _filterOptionsCts.Token;

        _ = Task.Run(() =>
        {
            try
            {
                // Each dimension is computed with its own filter nulled out.
                var sourceOptions   = BuildOptions(master, "All Sources",   EventFilter.Apply(master, null,       level, startTime, endTime, userName, search, processId, computer, channel), e => string.IsNullOrEmpty(e.ProviderName) ? null : e.ProviderName, numericSort: false, token);
                if (token.IsCancellationRequested) return;
                var processOptions  = BuildOptions(master, "All Processes", EventFilter.Apply(master, sourceName, level, startTime, endTime, userName, search, null,       computer, channel), e => e.ProcessId > 0 ? e.ProcessId.ToString() : null, numericSort: true, token);
                if (token.IsCancellationRequested) return;
                var userOptions     = BuildOptions(master, "All Users",     EventFilter.Apply(master, sourceName, level, startTime, endTime, null,     search, processId, computer, channel), e => string.IsNullOrEmpty(e.Username) ? null : e.Username, numericSort: false, token);
                if (token.IsCancellationRequested) return;
                var computerOptions = BuildOptions(master, "All Computers", EventFilter.Apply(master, sourceName, level, startTime, endTime, userName, search, processId, null,     channel), e => string.IsNullOrEmpty(e.Computer) ? null : e.Computer, numericSort: false, token);
                if (token.IsCancellationRequested) return;
                var channelOptions  = BuildOptions(master, "All Channels",  EventFilter.Apply(master, sourceName, level, startTime, endTime, userName, search, processId, computer, null   ), e => string.IsNullOrEmpty(e.Channel) ? null : e.Channel, numericSort: false, token);
                if (token.IsCancellationRequested) return;

                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _suppressApplyFilters = true;
                    try
                    {
                        ApplyOptions(SourceCategories,   sourceOptions,   currentSourceName,   v => SelectedSource   = v);
                        ApplyOptions(ProcessCategories,  processOptions,  currentProcessName,  v => SelectedProcess  = v);
                        ApplyOptions(UserCategories,     userOptions,     currentUserName,     v => SelectedUser     = v);
                        ApplyOptions(ComputerCategories, computerOptions, currentComputerName, v => SelectedComputer = v);
                        ApplyOptions(ChannelCategories,  channelOptions,  currentChannelName,  v => SelectedChannel  = v);
                    }
                    finally
                    {
                        _suppressApplyFilters = false;
                    }
                });
            }
            catch { }
        }, token);
    }

    /// <summary>
    /// Pure-CPU work: aggregate counts and produce a fresh List of
    /// SourceCategory entries. Runs on a background thread.
    /// </summary>
    private static List<SourceCategory> BuildOptions(
        IReadOnlyList<EventLogEntry> master,
        string allLabel,
        List<EventLogEntry> filtered,
        Func<EventLogEntry, string?> keySelector,
        bool numericSort,
        System.Threading.CancellationToken token)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < filtered.Count; i++)
        {
            if ((i & 4095) == 0 && token.IsCancellationRequested) return new List<SourceCategory>();
            var key = keySelector(filtered[i]);
            if (key == null) continue;
            counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        IEnumerable<KeyValuePair<string, int>> ordered = numericSort
            ? counts.OrderBy(kv => int.TryParse(kv.Key, out var n) ? n : int.MaxValue)
            : counts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase);

        var list = new List<SourceCategory>(counts.Count + 1)
        {
            new SourceCategory { Name = allLabel, Count = filtered.Count, IsAllSources = true }
        };
        foreach (var kv in ordered)
        {
            list.Add(new SourceCategory { Name = kv.Key, Count = kv.Value, IsAllSources = false });
        }
        return list;
    }

    /// <summary>
    /// UI-thread swap: clear the bound collection and re-add the new items in
    /// a single pass, then re-select the user's previous choice (kept as a
    /// stale zero-count entry if it's no longer in the filtered set).
    /// </summary>
    private static void ApplyOptions(
        ObservableCollection<SourceCategory> bound,
        List<SourceCategory> next,
        string? currentName,
        Action<SourceCategory?> setSelected)
    {
        bound.Clear();
        foreach (var item in next) bound.Add(item);

        if (!string.IsNullOrEmpty(currentName))
        {
            var match = bound.FirstOrDefault(c => c.Name == currentName);
            if (match == null && bound.Count > 0 && bound[0].Name != currentName)
            {
                match = new SourceCategory { Name = currentName, Count = 0, IsAllSources = false };
                bound.Add(match);
            }
            setSelected(match ?? (bound.Count > 0 ? bound[0] : null));
        }
        else if (bound.Count > 0)
        {
            setSelected(bound[0]);
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
        var processId = SelectedProcess == null || SelectedProcess.IsAllSources ? null : SelectedProcess.Name;
        var userName = SelectedUser == null || SelectedUser.IsAllSources ? null : SelectedUser.Name;
        var computer = SelectedComputer == null || SelectedComputer.IsAllSources ? null : SelectedComputer.Name;
        var channel = SelectedChannel == null || SelectedChannel.IsAllSources ? null : SelectedChannel.Name;

        return (string.IsNullOrEmpty(sourceName) || entry.ProviderName == sourceName) &&
               (SelectedType?.Level == null || entry.Level == SelectedType.Level.Value) &&
               (entry.TimeCreated >= startTime) &&
               (entry.TimeCreated <= endTime) &&
               (string.IsNullOrEmpty(processId) || entry.ProcessId.ToString() == processId) &&
               (string.IsNullOrEmpty(userName) || entry.Username == userName) &&
               (string.IsNullOrEmpty(computer) || entry.Computer == computer) &&
               (string.IsNullOrEmpty(channel) || entry.Channel == channel) &&
               (string.IsNullOrEmpty(SearchText) || entry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }


    public void ApplyFilters()
    {
        if (_suppressApplyFilters) return;

        var startTime = GetEffectiveStartTime();
        var endTime = GetEffectiveEndTime();
        var sourceName = SelectedSource == null || SelectedSource.IsAllSources ? null : SelectedSource.Name;
        var processId = SelectedProcess == null || SelectedProcess.IsAllSources ? null : SelectedProcess.Name;
        var userName = SelectedUser == null || SelectedUser.IsAllSources ? null : SelectedUser.Name;
        var computer = SelectedComputer == null || SelectedComputer.IsAllSources ? null : SelectedComputer.Name;
        var channel = SelectedChannel == null || SelectedChannel.IsAllSources ? null : SelectedChannel.Name;

        var filtered = EventLogService.Instance.FilterEvents(
            sourceName,
            SelectedType?.Level,
            startTime,
            endTime,
            userName,
            SearchText,
            processId,
            computer,
            channel
        );

        // For large result sets, replacing the collection is far cheaper than
        // N individual Add calls (each of which triggers a DataGrid layout pass).
        if (filtered.Count > 500)
        {
            FilteredEvents = new ObservableCollection<EventLogEntry>(filtered);
        }
        else
        {
            FilteredEvents.Clear();
            foreach (var entry in filtered)
            {
                FilteredEvents.Add(entry);
            }
        }

        // Keep dropdown contents in sync with the active filter set so e.g.
        // picking "Last 24 hours" prunes the Source/User/Process/etc. lists
        // to values that actually appear in that window.
        UpdateSourceCategories();
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
        CurrentSource = System.IO.Path.GetFileName(filePath);
        CancelPrefetch();
        FilteredEvents.Clear();
        // Don't stream file loads: a typical EVTX has tens of thousands of events.
        // Draining them one-by-one on the UI thread freezes the app.
        // Load everything on a background thread, then do a single bulk replace.
        _isStreaming = false;
        _flushTimer?.Stop();
        while (_pendingEvents.TryDequeue(out _)) { }

        // Reset all filter selections so previously-selected values don't filter out the new file's events.
        // Setting fields directly + raising PropertyChanged avoids triggering ApplyFilters on every reset.
        var allTime = LoadWindows.FirstOrDefault(w => w.Lookback == null && !w.IsCustom);
        if (allTime != null) _selectedLoadWindow = allTime;
        _selectedType = AvailableTypes[0];
        _selectedSource = null;
        _selectedProcess = null;
        _selectedUser = null;
        _selectedComputer = null;
        _selectedChannel = null;
        _startTime = null;
        _endTime = null;
        _startTimeOfDay = TimeSpan.Zero;
        _endTimeOfDay = new TimeSpan(23, 59, 59);
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SelectedLoadWindow));
        OnPropertyChanged(nameof(IsCustomRange));
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(SelectedProcess));
        OnPropertyChanged(nameof(SelectedUser));
        OnPropertyChanged(nameof(SelectedComputer));
        OnPropertyChanged(nameof(SelectedChannel));
        OnPropertyChanged(nameof(StartTime));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(StartTimeOfDay));
        OnPropertyChanged(nameof(EndTimeOfDay));
        OnPropertyChanged(nameof(SearchText));

        try
        {
            _suppressApplyFilters = true;
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

            // We're back on the UI thread after await. Rebuild categories without
            // each rebuild triggering a redundant ApplyFilters call.
            UpdateSourceCategories();
            _suppressApplyFilters = false;
            ApplyFilters();
            StatusMessage = $"Loaded {EventLogService.Instance.Events.Count} events from {fileType} file";
        }
        catch (Exception ex)
        {
            _suppressApplyFilters = false;
            _isStreaming = false;
            _flushTimer?.Stop();
            StatusMessage = $"Error: {ex.Message}";
        }
    }
}

public class EventTypeItem : System.ComponentModel.INotifyPropertyChanged
{
    public LogLevel? Level { get; set; }
    public string Name { get; set; } = string.Empty;

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

public class LoadWindowItem
{
    public string Name { get; set; } = string.Empty;
    public TimeSpan? Lookback { get; set; }
    public bool IsCustom { get; set; }
}
