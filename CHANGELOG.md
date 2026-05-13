# Changelog

All notable changes to Simple Event Viewer are documented here. Versions follow [Semantic Versioning](https://semver.org/).

## [1.1.0] — 2026-05-12

Focused on filtering, persistence, and getting the data out. Quality-of-life pass across the whole filter panel plus a handful of UX fixes.

### Added
- **Multi-select filters.** Each of *Event Source / Event Level / User / Process / Computer / Channel* can pick one or many values; selections are OR-d within a dimension. Each dimension has its own multi-select toggle under **Settings → Filter multi-select** (defaults: Source + Level multi, the rest single). Single-mode shows a classic ComboBox; multi-mode shows a DropDownButton with a checkbox flyout.
- **Cross-aware filter options.** Every dropdown now shows only values that exist in the events matched by the *other* active filters, with live counts.
- **Active-filter accent bar.** A thin colored bar next to each filter's label appears whenever that filter has a non-default selection (including time range and message search).
- **Save / load filter presets** to a JSON file. Captures every dimension's selection, time range (preset or custom), and message search. Round-trip safe; loaded presets that no longer match the current data are kept as stale zero-count entries instead of being dropped.
- **Export current view** to CSV / JSON / XML from a toolbar button. Picks file + format, then offers to open the containing folder in Explorer.
- **Copy as → XML** added to the right-click menu alongside CSV and JSON. All three share `Services/EventExporter` so on-disk and on-clipboard payloads match.
- **Persisted column visibility.** The toolbar **Columns** menu and a new **Settings → Default columns shown** checklist write to the same storage. Restored on next launch.
- **Restore Defaults** button in Settings (with confirmation dialog) resets every preference on the page.
- **Smart Refresh.** The Refresh toolbar button now re-reads the *currently loaded* source (live OR file) and re-applies active filters. Loading EVTX then hitting Refresh no longer silently swaps back to live logs.
- **Refresh button label is contextual** — "Refresh live logs" or "Reload sample.evtx".
- **Title bar reflects what's loaded** — e.g. *"sample.evtx — Simple Event Viewer"*. Configurable via **Settings → Title Bar Format** (3 options).
- **Consolidated Open menu.** "Load Local Event Log" + "Load EVTX" / XML / ETL collapsed into a single **Open** dropdown.
- **Theme presets.** Five new bundled themes — **High Contrast, Nord, Dracula, Solarized (dark), Sepia (light)** — each with a curated accent palette + page/card/text surface colors.
- **App-wide accent.** The Color Scheme picker now drives `SystemAccentColor` and the AccentFillColor brushes app-wide (buttons, focus rings, DataGrid selection) instead of only the level badges.
- **Settings page is navigation-cached.** Going to Settings and back no longer resets MainPage state.
- **About** card shows the running version, read from `Package.Current.Id.Version`.

### Changed
- **Default Row Color Style** is now `Entire row` instead of `Badge only`.
- **Level column is a real badge** — colored pill with text, instead of plain text relying on the row tint.
- **Settings ToggleSwitches** right-aligned (consistent edge with ComboBoxes).
- **`XML` / `ETL` loaders** are now gated behind **Settings → Experimental features**; off by default.
- Default `MaxRowLines` selection no longer always re-displays as "1 line" after relaunch (regression fix).

### Fixed
- Stack overflow when changing a single-select filter — root cause was the setter's `OnPropertyChanged` re-entering itself via the ComboBox's TwoWay binding. Guarded with `_inSingleSelectionSetter`.
- "Extra space" above the first real item (e.g. Critical) in the filter dropdowns — the synthetic "All X" row is now collapsed at the `ListViewItem` container level.
- Theme change freeze with 50k+ events loaded — was rebuilding the entire `FilteredEvents` collection on every theme switch; now walks visible `DataGridRow` instances and updates `Background` in place.
- Crashes on startup with the original brush-mutation theme code (defensive `try/catch` around `AccentTheme.ApplyToApplication` / `ApplyTheme` plus a global `UnhandledException` logger to `%TEMP%\simpleeventviewer-crash.log`).
- "Backend reload" flicker when toggling a single filter — the dimension whose selection changed no longer rebuilds its own options list (its options derive from the *other* filters anyway).
- MCP `current_source` tool now returns `"Live system logs"` for the default state instead of an empty string.

### MCP server
- Bumped server `version` field to `1.1.0`.
- No protocol changes; same five tools (`current_source`, `event_summary`, `list_events`, `search_events`, `get_event`).

## [1.0.0] — 2026-05-11

First official release. MSIX + MSI installers attached to the GitHub Release.

### Added
- Live Windows event log reader (Application channel) with streaming load + total-count progress.
- `.evtx` file loading via `EventLogReader` + `PathType.FilePath`.
- Experimental `.xml` and `.etl` loaders.
- Filter panel: Event Source, Time Range (presets + custom), Event Level, Message, User, Process, Computer, Channel.
- Sortable / resizable / reorderable columns via CommunityToolkit DataGrid.
- Multi-select rows + Copy row / cell / message + Copy as CSV / JSON.
- Settings: Theme (System / Light / Dark), Color Scheme, Row Color Style, Message Lines, Remember Column Widths.
- Optional local **MCP server** on `127.0.0.1`. Tools: `current_source`, `event_summary`, `list_events`, `search_events`, `get_event`. Settings page has copyable client configs for VS Code, Cursor, Claude Desktop, and Claude Code.
- Microsoft Store identity (`GusCatalano.SimpleEventViewer` / `CN=119E0257-…`) wired into `Package.appxmanifest`.
- GitHub Actions workflow that builds MSIX + MSI and publishes a Release on every `v*` tag.
