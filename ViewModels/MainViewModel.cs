using CommunityToolkit.Mvvm.ComponentModel;
using SimpleEventViewer.Models;
using SimpleEventViewer.Services;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
            new() { Level = LogLevel.Critical, Name = "Critical" },
            new() { Level = LogLevel.Error, Name = "Error" },
            new() { Level = LogLevel.Warning, Name = "Warning" },
            new() { Level = LogLevel.Information, Name = "Information" },
            new() { Level = LogLevel.Verbose, Name = "Verbose" }
        };
        foreach (var t in AvailableTypes) t.PropertyChanged += OnLevelItemChanged;
        _selectedType = null!; // unused with multi-select; kept to satisfy non-nullable field

        SettingsService.Instance.MultiSelectChanged += OnMultiSelectChanged;

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

        // Empty selections = "show all". Each category list is repopulated
        // from event data once UpdateSourceCategories runs after load.
        ClearCategoryList(SourceCategories);
        ClearCategoryList(ProcessCategories);
        ClearCategoryList(UserCategories);
        ClearCategoryList(ComputerCategories);
        ClearCategoryList(ChannelCategories);

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
    /// Rebuild each filter dropdown so it shows values that exist in the
    /// event subset matched by the OTHER active filters. The user's
    /// selection is preserved by name across rebuilds (stale entries kept
    /// with Count=0 when a previously-selected value is no longer in the
    /// filtered subset).
    ///
    /// Heavy lifting (5 filter sweeps over up to ~200k events) runs on a
    /// background <see cref="Task.Run"/>; only the final list swap touches
    /// the UI thread. Concurrent invocations cancel the in-flight one.
    /// </summary>
    private void UpdateSourceCategories(SettingsService.FilterDimension? skipDim = null)
    {
        var master = EventLogService.Instance.Events;
        var startTime = GetEffectiveStartTime();
        var endTime = GetEffectiveEndTime();
        var sources   = SelectedSourceNames();
        var processes = SelectedProcessNames();
        var users     = SelectedUserNames();
        var computers = SelectedComputerNames();
        var channels  = SelectedChannelNames();
        var levels    = SelectedLevelSet();
        var search    = SearchText;

        _filterOptionsCts?.Cancel();
        _filterOptionsCts = new System.Threading.CancellationTokenSource();
        var token = _filterOptionsCts.Token;

        _ = Task.Run(() =>
        {
            try
            {
                List<SourceCategory>? sourceOpts = null, processOpts = null, userOpts = null, compOpts = null, chanOpts = null;

                if (skipDim != SettingsService.FilterDimension.Source)
                {
                    sourceOpts = AggregateOptions(EventFilter.Apply(master, null, levels, startTime, endTime, users, search, processes, computers, channels), e => string.IsNullOrEmpty(e.ProviderName) ? null : e.ProviderName, numericSort: false, token);
                    if (token.IsCancellationRequested) return;
                }
                if (skipDim != SettingsService.FilterDimension.Process)
                {
                    processOpts = AggregateOptions(EventFilter.Apply(master, sources, levels, startTime, endTime, users, search, null, computers, channels), e => e.ProcessId > 0 ? e.ProcessId.ToString() : null, numericSort: true, token);
                    if (token.IsCancellationRequested) return;
                }
                if (skipDim != SettingsService.FilterDimension.User)
                {
                    userOpts = AggregateOptions(EventFilter.Apply(master, sources, levels, startTime, endTime, null, search, processes, computers, channels), e => string.IsNullOrEmpty(e.Username) ? null : e.Username, numericSort: false, token);
                    if (token.IsCancellationRequested) return;
                }
                if (skipDim != SettingsService.FilterDimension.Computer)
                {
                    compOpts = AggregateOptions(EventFilter.Apply(master, sources, levels, startTime, endTime, users, search, processes, null, channels), e => string.IsNullOrEmpty(e.Computer) ? null : e.Computer, numericSort: false, token);
                    if (token.IsCancellationRequested) return;
                }
                if (skipDim != SettingsService.FilterDimension.Channel)
                {
                    chanOpts = AggregateOptions(EventFilter.Apply(master, sources, levels, startTime, endTime, users, search, processes, computers, null), e => string.IsNullOrEmpty(e.Channel) ? null : e.Channel, numericSort: false, token);
                    if (token.IsCancellationRequested) return;
                }

                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _suppressApplyFilters = true;
                    try
                    {
                        if (sourceOpts  != null) ApplyOptions(SourceCategories,   sourceOpts,  sources);
                        if (processOpts != null) ApplyOptions(ProcessCategories,  processOpts, processes);
                        if (userOpts    != null) ApplyOptions(UserCategories,     userOpts,    users);
                        if (compOpts    != null) ApplyOptions(ComputerCategories, compOpts,    computers);
                        if (chanOpts    != null) ApplyOptions(ChannelCategories,  chanOpts,    channels);
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
    /// Background work: count occurrences in <paramref name="filtered"/> per
    /// distinct key and return a sorted list of new SourceCategory instances
    /// (IsSelected starts false; the UI thread applies preservation).
    /// </summary>
    private static List<SourceCategory> AggregateOptions(
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

        var list = new List<SourceCategory>(counts.Count);
        foreach (var kv in ordered)
        {
            list.Add(new SourceCategory { Name = kv.Key, Count = kv.Value });
        }
        return list;
    }

    /// <summary>
    /// UI-thread step: replace the items in the bound collection, restoring
    /// IsSelected for any entry whose Name is in <paramref name="preserved"/>.
    /// Selections that no longer correspond to any entry in the filtered set
    /// are re-added at the end as zero-count "stale" entries — so the user's
    /// pick survives a tight filter without silently disappearing.
    /// </summary>
    private void ApplyOptions(
        ObservableCollection<SourceCategory> bound,
        List<SourceCategory> next,
        HashSet<string> preserved)
    {
        foreach (var old in bound) old.PropertyChanged -= OnCategoryItemChanged;
        bound.Clear();

        foreach (var item in next)
        {
            item.IsSelected = preserved.Contains(item.Name);
            item.PropertyChanged += OnCategoryItemChanged;
            bound.Add(item);
        }

        // Preserve selections that are no longer in the filtered subset.
        foreach (var name in preserved)
        {
            if (!bound.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                var stale = new SourceCategory { Name = name, Count = 0, IsSelected = true };
                stale.PropertyChanged += OnCategoryItemChanged;
                bound.Add(stale);
            }
        }
    }

    // --- multi-select selection helpers ------------------------------------

    private static HashSet<string> SelectionFrom(IEnumerable<SourceCategory> items)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in items)
        {
            if (c.IsSelected && !c.IsAllSources) set.Add(c.Name);
        }
        return set;
    }

    private HashSet<string> SelectedSourceNames()   => SelectionFrom(SourceCategories);
    private HashSet<string> SelectedProcessNames()  => SelectionFrom(ProcessCategories);
    private HashSet<string> SelectedUserNames()     => SelectionFrom(UserCategories);
    private HashSet<string> SelectedComputerNames() => SelectionFrom(ComputerCategories);
    private HashSet<string> SelectedChannelNames()  => SelectionFrom(ChannelCategories);

    private HashSet<LogLevel> SelectedLevelSet()
    {
        var set = new HashSet<LogLevel>();
        foreach (var t in AvailableTypes)
        {
            if (t.IsSelected && t.Level.HasValue) set.Add(t.Level.Value);
        }
        return set;
    }

    /// <summary>Clear every item in a category list (unsubscribe + Clear).</summary>
    private void ClearCategoryList(ObservableCollection<SourceCategory> list)
    {
        foreach (var c in list) c.PropertyChanged -= OnCategoryItemChanged;
        list.Clear();
    }

    private void OnCategoryItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SourceCategory.IsSelected) || _suppressApplyFilters) return;

        SettingsService.FilterDimension? dim = null;
        if (sender is SourceCategory cat)
        {
            var located = LocateDimension(cat);
            if (located.Item1 != null) dim = located.Item2;
            if (cat.IsSelected) EnforceSingleSelectInCategory(cat);
        }
        ApplyFilters(dim);
    }

    private void OnLevelItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EventTypeItem.IsSelected) || _suppressApplyFilters) return;

        if (sender is EventTypeItem level && level.IsSelected
            && !SettingsService.Instance.IsMultiSelectEnabled(SettingsService.FilterDimension.Level))
        {
            _suppressApplyFilters = true;
            try
            {
                foreach (var t in AvailableTypes)
                {
                    if (!ReferenceEquals(t, level) && t.IsSelected) t.IsSelected = false;
                }
            }
            finally { _suppressApplyFilters = false; }
        }
        ApplyFilters(SettingsService.FilterDimension.Level);
    }

    /// <summary>
    /// If the dimension this category lives in is in single-select mode and
    /// the user just checked one entry, deselect every other entry in the
    /// same list so the visual + filter state match.
    /// </summary>
    private void EnforceSingleSelectInCategory(SourceCategory justSelected)
    {
        var (collection, dim) = LocateDimension(justSelected);
        if (collection == null) return;
        if (SettingsService.Instance.IsMultiSelectEnabled(dim)) return;

        _suppressApplyFilters = true;
        try
        {
            foreach (var c in collection)
            {
                if (!ReferenceEquals(c, justSelected) && c.IsSelected) c.IsSelected = false;
            }
        }
        finally { _suppressApplyFilters = false; }
    }

    private (ObservableCollection<SourceCategory>?, SettingsService.FilterDimension) LocateDimension(SourceCategory cat)
    {
        if (SourceCategories.Contains(cat))   return (SourceCategories,   SettingsService.FilterDimension.Source);
        if (ProcessCategories.Contains(cat))  return (ProcessCategories,  SettingsService.FilterDimension.Process);
        if (UserCategories.Contains(cat))     return (UserCategories,     SettingsService.FilterDimension.User);
        if (ComputerCategories.Contains(cat)) return (ComputerCategories, SettingsService.FilterDimension.Computer);
        if (ChannelCategories.Contains(cat))  return (ChannelCategories,  SettingsService.FilterDimension.Channel);
        return (null, default);
    }

    private void OnMultiSelectChanged(SettingsService.FilterDimension dim)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            // If the user just turned MULTI off for a dimension that currently
            // has more than one selection, collapse to the first one so the
            // filter result matches what the user can see.
            if (!SettingsService.Instance.IsMultiSelectEnabled(dim))
            {
                CollapseToFirstSelection(dim);
            }

            OnPropertyChanged(dim switch
            {
                SettingsService.FilterDimension.Source   => nameof(SourceSelectionMode),
                SettingsService.FilterDimension.Process  => nameof(ProcessSelectionMode),
                SettingsService.FilterDimension.User     => nameof(UserSelectionMode),
                SettingsService.FilterDimension.Computer => nameof(ComputerSelectionMode),
                SettingsService.FilterDimension.Channel  => nameof(ChannelSelectionMode),
                SettingsService.FilterDimension.Level    => nameof(LevelSelectionMode),
                _ => string.Empty
            });
        });
    }

    private void CollapseToFirstSelection(SettingsService.FilterDimension dim)
    {
        if (dim == SettingsService.FilterDimension.Level)
        {
            var first = AvailableTypes.FirstOrDefault(t => t.IsSelected);
            if (first == null) return;
            _suppressApplyFilters = true;
            try
            {
                foreach (var t in AvailableTypes)
                {
                    if (!ReferenceEquals(t, first) && t.IsSelected) t.IsSelected = false;
                }
            }
            finally { _suppressApplyFilters = false; }
            ApplyFilters();
            return;
        }

        var list = dim switch
        {
            SettingsService.FilterDimension.Source   => SourceCategories,
            SettingsService.FilterDimension.Process  => ProcessCategories,
            SettingsService.FilterDimension.User     => UserCategories,
            SettingsService.FilterDimension.Computer => ComputerCategories,
            SettingsService.FilterDimension.Channel  => ChannelCategories,
            _ => null
        };
        if (list == null) return;
        var firstCat = list.FirstOrDefault(c => c.IsSelected);
        if (firstCat == null) return;
        _suppressApplyFilters = true;
        try
        {
            foreach (var c in list)
            {
                if (!ReferenceEquals(c, firstCat) && c.IsSelected) c.IsSelected = false;
            }
        }
        finally { _suppressApplyFilters = false; }
        ApplyFilters();
    }

    // --- filter button labels + ListView selection modes -------------------

    public string SourceFilterSummary   => Summarize(SourceCategories,   "All Sources");
    public string ProcessFilterSummary  => Summarize(ProcessCategories,  "All Processes");
    public string UserFilterSummary     => Summarize(UserCategories,     "All Users");
    public string ComputerFilterSummary => Summarize(ComputerCategories, "All Computers");
    public string ChannelFilterSummary  => Summarize(ChannelCategories,  "All Channels");
    public string LevelFilterSummary
    {
        get
        {
            var sel = AvailableTypes.Where(t => t.IsSelected && t.Level.HasValue).ToList();
            if (sel.Count == 0) return "All Levels";
            if (sel.Count == 1) return sel[0].Name;
            return $"{sel[0].Name} +{sel.Count - 1}";
        }
    }

    private static string Summarize(IEnumerable<SourceCategory> items, string allLabel)
    {
        var selected = items.Where(c => c.IsSelected && !c.IsAllSources).ToList();
        if (selected.Count == 0) return allLabel;
        if (selected.Count == 1) return selected[0].Name;
        return $"{selected[0].Name} +{selected.Count - 1}";
    }

    public ListViewSelectionMode SourceSelectionMode   => Mode(SettingsService.FilterDimension.Source);
    public ListViewSelectionMode ProcessSelectionMode  => Mode(SettingsService.FilterDimension.Process);
    public ListViewSelectionMode UserSelectionMode     => Mode(SettingsService.FilterDimension.User);
    public ListViewSelectionMode ComputerSelectionMode => Mode(SettingsService.FilterDimension.Computer);
    public ListViewSelectionMode ChannelSelectionMode  => Mode(SettingsService.FilterDimension.Channel);
    public ListViewSelectionMode LevelSelectionMode    => Mode(SettingsService.FilterDimension.Level);

    private static ListViewSelectionMode Mode(SettingsService.FilterDimension dim) =>
        SettingsService.Instance.IsMultiSelectEnabled(dim) ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single;

    /// <summary>Reset every multi-select dimension's selection state.</summary>
    public void ClearAllSelections()
    {
        _suppressApplyFilters = true;
        try
        {
            foreach (var c in SourceCategories)   c.IsSelected = false;
            foreach (var c in ProcessCategories)  c.IsSelected = false;
            foreach (var c in UserCategories)     c.IsSelected = false;
            foreach (var c in ComputerCategories) c.IsSelected = false;
            foreach (var c in ChannelCategories)  c.IsSelected = false;
            foreach (var t in AvailableTypes)     t.IsSelected = false;
        }
        finally { _suppressApplyFilters = false; }
        ApplyFilters();
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
        var sources   = SelectedSourceNames();
        var processes = SelectedProcessNames();
        var users     = SelectedUserNames();
        var computers = SelectedComputerNames();
        var channels  = SelectedChannelNames();
        var levels    = SelectedLevelSet();

        return (sources.Count == 0    || sources.Contains(entry.ProviderName)) &&
               (levels.Count == 0     || levels.Contains(entry.Level)) &&
               (entry.TimeCreated >= startTime) &&
               (entry.TimeCreated <= endTime) &&
               (processes.Count == 0  || processes.Contains(entry.ProcessId.ToString())) &&
               (users.Count == 0      || users.Contains(entry.Username ?? string.Empty)) &&
               (computers.Count == 0  || computers.Contains(entry.Computer ?? string.Empty)) &&
               (channels.Count == 0   || channels.Contains(entry.Channel ?? string.Empty)) &&
               (string.IsNullOrEmpty(SearchText) || entry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }

    public void ApplyFilters() => ApplyFilters(null);

    /// <summary>
    /// <paramref name="changedDim"/> is the dimension whose selection just
    /// flipped. That dimension's *own* dropdown list doesn't need to rebuild
    /// (its options are derived from the OTHER filters, none of which moved)
    /// — keeping its ListView stable avoids the flicker that made it look
    /// like a backend reload.
    /// </summary>
    public void ApplyFilters(SettingsService.FilterDimension? changedDim)
    {
        if (_suppressApplyFilters) return;

        var startTime = GetEffectiveStartTime();
        var endTime = GetEffectiveEndTime();
        var sources   = SelectedSourceNames();
        var processes = SelectedProcessNames();
        var users     = SelectedUserNames();
        var computers = SelectedComputerNames();
        var channels  = SelectedChannelNames();
        var levels    = SelectedLevelSet();

        var filtered = EventFilter.Apply(
            EventLogService.Instance.Events,
            sources, levels, startTime, endTime, users,
            SearchText, processes, computers, channels);

        if (filtered.Count > 500)
        {
            FilteredEvents = new ObservableCollection<EventLogEntry>(filtered);
        }
        else
        {
            FilteredEvents.Clear();
            foreach (var entry in filtered) FilteredEvents.Add(entry);
        }

        // Summaries change whenever any selection changes.
        OnPropertyChanged(nameof(SourceFilterSummary));
        OnPropertyChanged(nameof(ProcessFilterSummary));
        OnPropertyChanged(nameof(UserFilterSummary));
        OnPropertyChanged(nameof(ComputerFilterSummary));
        OnPropertyChanged(nameof(ChannelFilterSummary));
        OnPropertyChanged(nameof(LevelFilterSummary));

        UpdateSourceCategories(changedDim);
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
