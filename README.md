# Simple Event Viewer

A modern Windows Event Log viewer built with **WinUI 3** and the Windows App SDK. Reads live system event logs and saved `.evtx` / `.xml` files, with rich filtering, sorting, and copy/export options.

![App icon](Assets/AppIcon.png)

## Features

### Data sources
- **Live system logs** (Application channel) via `EventLogReader`
- **EVTX files** (`.evtx`) — native read via `EventLogReader` with `PathType.FilePath`
- **XML files** (`.xml`) — parsed events from saved event XML
- **ETL files** (`.etl`) — converted via `wevtutil`

The current source is shown in the status bar (e.g. "Live system logs" or "MyExport.evtx").

### Time range
Unified time selector in the filter panel with presets — "Last hour", "Last 24 hours", "Last 7 days", "Last 30 days", "All time", "Custom range...". Defaults to **Last 24 hours** so startup is fast.

- For **live logs**: the preset narrows the EventLogQuery (XPath `timediff` filter), so we only read events from disk that the user wants.
- For **file sources**: the preset is computed relative to the *newest event in the file* and applied as a display filter (no re-read).
- **Custom range** exposes date+time pickers for start and end. Live data reloads with the exact range; file data filters in-memory.

### Smart loading
- **Streaming** — events flow into the UI as they're read from the log. A `ConcurrentQueue` collects them off the UI thread; a `DispatcherQueueTimer` drains up to 500 per tick (every 200 ms). The UI stays responsive even when 50k+ events are loading.
- **Total count** is queried on a parallel background task so progress displays `Loading system logs... (N/M events)` instead of `(N/?)`.
- **Background prefetch** — after the initial load completes, an idle prefetch (delayed 5 s) reads *older* events into memory so widening the time range later is instant. Cancels on refresh or file load.
- **Smart range switching** — widening (24h → 7d) reloads only if older events aren't already cached; narrowing (7d → 24h) just refilters the existing data.

### Filters
Filter panel (left side, resizable + collapsible):

1. **Event Source** (provider name)
2. **Time Range** (presets + custom)
3. **Event Level** (Critical / Error / Warning / Information / Verbose / All)
4. **Message** (search substring in event message)
5. **User**
6. **Process** (PID, sorted numerically)
7. **Computer**
8. **Channel** (log channel like `Application`, `Security`, etc.)

All dropdowns show value + count and are alphabetically sorted (numerically for PIDs). "Clear All Filters" button resets everything.

### Events grid
- Multi-select (Ctrl/Shift+click) with Ctrl+A to select all
- Sortable columns — click a header to sort, click again to reverse
- Resizable, reorderable columns
- **Columns** button (top-right) opens a checklist to show/hide columns (`Channel` is hidden by default)
- Row coloring: either a small level badge (default) or full-row tint — configurable in Settings
- Message row height is capped (1, 2, 3, 4, 5, or 10 lines), but each row only takes the height it actually needs

### Event details
Bottom panel (resizable + collapsible) with full event info: ID, Level, Time, Provider, Provider GUID, Channel, Task, Keywords, User, Process/Thread, Computer, Message, and a collapsible XML view.

- Each field has a copy button next to its label
- All text is selectable
- A "copy all" button in the header copies the full detail block

### Copy/export
Right-click on a row (or selection) for:
- **Copy row** — tab-separated, ideal for pasting into spreadsheets (Ctrl+C also works)
- **Copy cell** — the specific cell you right-clicked
- **Copy message only**
- **Copy as... → CSV** (with header + escaping)
- **Copy as... → JSON** (pretty-printed)

### Settings
Available via the gear icon in the toolbar:
- **Theme** — System / Light / Dark
- **Color scheme** — Default, Blue, Green, Purple, Orange, Red (drives the level badges and row tints)
- **Row color style** — Badge only / Entire row
- **Message lines** — 1, 2, 3, 4, 5, or 10 line cap per row

All settings persist to `ApplicationData.Current.LocalSettings`.

## Architecture

```
SimpleEventViewer/
├── Models/
│   ├── EventLevel.cs         # LogLevel enum (Critical, Error, ...)
│   └── EventLogEntry.cs      # Event model + SourceCategory
├── Services/
│   ├── EventLogService.cs    # Singleton; reads logs, streams batches via events
│   └── SettingsService.cs    # Singleton; persisted UI preferences
├── ViewModels/
│   └── MainViewModel.cs      # ObservableObject; bindings + filter logic
├── Converters.cs             # Level→Brush, DateTimeOffset, etc.
├── MainPage.xaml             # Main UI
├── SettingsPage.xaml         # Settings page (Frame navigation from MainPage)
└── installer.wxs             # WiX 5 installer definition
```

### Threading model
- `EventLogService.LoadCurrentSystemLogs` runs on a background thread (`Task.Run`)
- It emits `OnEventBatchLoaded` events with batches of 100 entries
- The ViewModel enqueues batches into a `ConcurrentQueue<EventLogEntry>`
- A `DispatcherQueueTimer` ticks every 200 ms on the UI thread, draining up to 500 entries per tick into the `ObservableCollection`
- Filter changes never reload from disk if the requested range is a subset of what's already in memory

### Why these choices

| Decision | Why |
|---|---|
| **Single-project WinUI 3** (no separate packaging project) | Simpler structure; modern WinAppSDK supports MSIX directly via `EnableMsixTooling`. |
| **CommunityToolkit DataGrid** instead of ListView | Built-in resize/sort/reorder of columns. Pays a render cost vs ListView, mitigated by batched updates. |
| **Throttled UI flush** (timer + queue) | Without it, fast streaming triggers thousands of `CollectionChanged` events that overwhelm the DataGrid. Batching at 200 ms keeps the UI at 60 fps during load. |
| **No sorted insertion during streaming** | `EventLogReader` with `ReverseDirection=true` returns events already in time order. Appending is O(1); binary-search insertion was O(n) per event due to shifting. |
| **XPath query for time presets** | Pushes the time filter into Windows itself instead of reading the entire log and discarding. "Last 24 hours" on a busy machine goes from 30s to <2s. |
| **Background prefetch** | Best of both worlds: fast initial load *and* instant switching to wider ranges later. Cancels cleanly on any user action. |
| **Per-cell copy + per-field copy buttons** | Three-tier copy UX: cell (right-click → Copy cell), row (right-click → Copy / Ctrl+C), and field (button next to label). Detail fields are also `IsTextSelectionEnabled` for partial copies. |
| **Static converter instances** | The DataGrid's `LoadingRow` fires for every row scroll. Reusing a single `LevelToRowBrushConverter` and `SolidColorBrush` instance avoids per-row allocations. |

## Build & run

Requires .NET 10 SDK and Windows 10.0.17763+ for runtime.

```pwsh
dotnet restore
dotnet run
```

For a self-contained published build:

```pwsh
dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish
```

## CI / installers

`.github/workflows/build.yml` builds on every push to `main`, every PR, and every `v*` tag:

- **MSIX** — `msbuild ... /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false`. Unsigned; sign with your own cert for distribution.
- **MSI** — produced from `installer.wxs` (WiX 5) wrapping a self-contained publish. Installs to `Program Files\Simple Event Viewer` with a Start Menu shortcut and uninstall support.

Both are uploaded as workflow artifacts. Pushing a `v*` tag also creates a GitHub Release with both installers attached.

Trimming and ReadyToRun are explicitly disabled during the MSIX build (`PublishTrimmed=false`, `PublishReadyToRun=false`) because they require self-contained publishing. The MSIX uses framework-dependent deployment via the Windows App SDK runtime.

## License & credits

Created by Gus Catalano (https://github.com/guscatalano).
