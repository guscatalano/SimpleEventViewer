using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleEventViewer.Models;
using SimpleEventViewer.Services;
using SimpleEventViewer.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace SimpleEventViewer;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    private readonly MenuFlyout _rowContextMenu;

    public MainPage()
    {
        ViewModel = new MainViewModel();
        _rowContextMenu = BuildRowContextMenu();
        InitializeComponent();

        // Refresh row colors when theme/color scheme changes
        SettingsService.Instance.ThemeChanged += OnThemeChanged;
        SettingsService.Instance.ExperimentalFormatsChanged += OnExperimentalFormatsChanged;
        SettingsService.Instance.ColumnVisibilityChanged += OnColumnVisibilityChanged;
        SettingsService.Instance.TitleFormatChanged += OnTitleFormatChanged;

        // Mirror the loaded source into the title bar so multiple open windows
        // can be told apart from the taskbar / Alt-Tab.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentSource))
            {
                UpdateWindowTitle();
            }
        };

        // If a file was passed on the command line, load it after the page is ready.
        Loaded += MainPage_Loaded;
    }

    private void OnExperimentalFormatsChanged()
    {
        DispatcherQueue.TryEnqueue(UpdateExperimentalButtons);
    }

    private void OnColumnVisibilityChanged()
    {
        DispatcherQueue.TryEnqueue(RestoreColumnVisibility);
    }

    private void OnTitleFormatChanged()
    {
        DispatcherQueue.TryEnqueue(UpdateWindowTitle);
    }

    private void UpdateWindowTitle()
    {
        if (Application.Current is App app && app.MainWindow is MainWindow mw)
        {
            mw.UpdateTitleBar(ViewModel.CurrentSource ?? string.Empty);
        }
    }

    private void UpdateExperimentalButtons()
    {
        var on = SettingsService.Instance.ExperimentalFileFormats;
        LoadXmlMenuItem.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        LoadEtlMenuItem.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainPage_Loaded;

        RestoreColumnWidths();
        RestoreColumnVisibility();
        UpdateExperimentalButtons();
        UpdateWindowTitle();

        var startupFile = (Application.Current as App)?.StartupFilePath;
        if (!string.IsNullOrEmpty(startupFile))
        {
            var ext = System.IO.Path.GetExtension(startupFile).TrimStart('.').ToLowerInvariant();
            if (ext is "evtx" or "xml" or "etl")
            {
                ViewModel.LoadFile(startupFile, ext);
            }
            else
            {
                ViewModel.StatusMessage = $"Unsupported file type: .{ext}";
            }
        }
    }

    /// <summary>
    /// Restore saved column widths if the setting is enabled. The "Message"
    /// star column is intentionally skipped so it keeps consuming the
    /// remaining horizontal space.
    /// </summary>
    private void RestoreColumnWidths()
    {
        if (!SettingsService.Instance.RememberColumnWidths) return;
        var widths = SettingsService.Instance.LoadColumnWidths();
        if (widths == null) return;

        foreach (var col in EventsDataGrid.Columns)
        {
            var tag = col.Tag?.ToString();
            if (tag == null) continue;
            if (tag == "Message") continue; // star-sized
            if (widths.TryGetValue(tag, out var w) && w > 20 && w < 4000)
            {
                col.Width = new DataGridLength(w);
            }
        }
    }

    /// <summary>
    /// Restore which columns the user had visible last session. Independent
    /// of RememberColumnWidths so a user who wants the layout to stay
    /// dynamic still gets persistent show/hide.
    /// </summary>
    private void RestoreColumnVisibility()
    {
        var visibility = SettingsService.Instance.LoadColumnVisibility();
        if (visibility == null) return;

        foreach (var col in EventsDataGrid.Columns)
        {
            var tag = col.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) continue;
            if (visibility.TryGetValue(tag, out var visible))
            {
                col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// Save current column widths to settings. Called from the Window.Closed
    /// hook in MainWindow so widths survive across launches.
    /// </summary>
    public void SaveColumnWidths()
    {
        if (!SettingsService.Instance.RememberColumnWidths) return;
        var widths = new Dictionary<string, double>();
        foreach (var col in EventsDataGrid.Columns)
        {
            var tag = col.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) continue;
            if (tag == "Message") continue;
            var w = col.ActualWidth;
            if (w > 20) widths[tag] = w;
        }
        if (widths.Count > 0)
        {
            SettingsService.Instance.SaveColumnWidths(widths);
        }
    }

    public void SaveColumnVisibility()
    {
        var visibility = new Dictionary<string, bool>();
        foreach (var col in EventsDataGrid.Columns)
        {
            var tag = col.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) continue;
            visibility[tag] = col.Visibility == Visibility.Visible;
        }
        if (visibility.Count > 0)
        {
            SettingsService.Instance.SaveColumnVisibility(visibility);
        }
    }

    private string? _rightClickedCellValue;

    private MenuFlyout BuildRowContextMenu()
    {
        var menu = new MenuFlyout();

        var copyRowItem = new MenuFlyoutItem { Text = "Copy row" };
        copyRowItem.Click += CopyRows_Click;
        copyRowItem.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = Windows.System.VirtualKeyModifiers.Control, Key = Windows.System.VirtualKey.C });
        menu.Items.Add(copyRowItem);

        var copyCellItem = new MenuFlyoutItem { Text = "Copy cell" };
        copyCellItem.Click += CopyCell_Click;
        menu.Items.Add(copyCellItem);

        var copyMessageItem = new MenuFlyoutItem { Text = "Copy message only" };
        copyMessageItem.Click += CopyMessage_Click;
        menu.Items.Add(copyMessageItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var copyAsItem = new MenuFlyoutSubItem { Text = "Copy as..." };
        var csvItem = new MenuFlyoutItem { Text = "CSV" };
        csvItem.Click += CopyRowsAsCsv_Click;
        copyAsItem.Items.Add(csvItem);

        var jsonItem = new MenuFlyoutItem { Text = "JSON" };
        jsonItem.Click += CopyRowsAsJson_Click;
        copyAsItem.Items.Add(jsonItem);

        var xmlItem = new MenuFlyoutItem { Text = "XML" };
        xmlItem.Click += CopyRowsAsXml_Click;
        copyAsItem.Items.Add(xmlItem);

        menu.Items.Add(copyAsItem);

        return menu;
    }

    private void EventsDataGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Capture the right-clicked cell value (for Copy cell)
        _rightClickedCellValue = (e.OriginalSource as TextBlock)?.Text;

        // If the right-clicked row isn't already in the selection, make it the only selection
        var fe = e.OriginalSource as FrameworkElement;
        var dc = fe?.DataContext;
        if (dc is EventLogEntry entry && !EventsDataGrid.SelectedItems.Contains(entry))
        {
            EventsDataGrid.SelectedItems.Clear();
            EventsDataGrid.SelectedItems.Add(entry);
        }

        _rowContextMenu.ShowAt(EventsDataGrid, e.GetPosition(EventsDataGrid));
        e.Handled = true;
    }

    private void CopyCell_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_rightClickedCellValue))
        {
            SetClipboard(_rightClickedCellValue);
        }
    }

    private void CopyRowsAsJson_Click(object sender, RoutedEventArgs e)
    {
        var entries = GetSelectedEntries().ToList();
        if (entries.Count == 0) return;
        SetClipboard(EventExporter.Serialize(entries, EventExporter.Format.Json));
    }

    private void CopyRowsAsXml_Click(object sender, RoutedEventArgs e)
    {
        var entries = GetSelectedEntries().ToList();
        if (entries.Count == 0) return;
        SetClipboard(EventExporter.Serialize(entries, EventExporter.Format.Xml));
    }

    private void OnThemeChanged()
    {
        // Walk visible DataGridRow instances in the visual tree and refresh
        // each row's Background directly. This avoids rebuilding the entire
        // FilteredEvents ObservableCollection, which froze the UI for several
        // seconds with 50k+ events because every Add raised a CollectionChanged
        // that the DataGrid had to react to. Unrealized rows are re-themed by
        // EventsDataGrid_LoadingRow when they scroll into view.
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                foreach (var row in FindVisualChildren<CommunityToolkit.WinUI.UI.Controls.DataGridRow>(EventsDataGrid))
                {
                    if (row.DataContext is EventLogEntry entry)
                    {
                        var brush = _rowBrushConverter.Convert(entry.Level, typeof(Brush), null, "") as Brush;
                        row.Background = brush ?? _transparentBrush;
                    }
                }
            }
            catch { }
        });
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) yield break;
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T tChild) yield return tChild;
            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void MessageText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock tb)
        {
            tb.MaxLines = SettingsService.Instance.MaxRowLines;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshCurrentView();
    }

    private void LoadLocalEventLog_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadLiveLogs();
    }

    private void LoadEvtxFile_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFile("Select EVTX file", "Event log files", ".evtx");
        if (!string.IsNullOrEmpty(path))
        {
            ViewModel.LoadFile(path, "evtx");
        }
    }

    private void LoadXmlFile_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFile("Select XML file", "XML files", ".xml");
        if (!string.IsNullOrEmpty(path))
        {
            ViewModel.LoadFile(path, "xml");
        }
    }

    private void LoadEtlFile_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFile("Select ETL file", "Event trace logs", ".etl");
        if (!string.IsNullOrEmpty(path))
        {
            ViewModel.LoadFile(path, "etl");
        }
    }

    private string? PickFile(string title, string filterLabel, string extension)
    {
        try
        {
            var window = (Application.Current as App)?.MainWindow;
            var hwnd = window != null
                ? WindowNative.GetWindowHandle(window)
                : IntPtr.Zero;
            return Win32FilePicker.PickFile(hwnd, title, filterLabel, extension);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"File picker error: {ex.Message}";
            return null;
        }
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAllSelections();
        ViewModel.StartTime = null;
        ViewModel.EndTime = null;
        ViewModel.StartTimeOfDay = TimeSpan.Zero;
        ViewModel.EndTimeOfDay = new TimeSpan(23, 59, 59);
        ViewModel.SearchText = string.Empty;
    }

    private void SaveFilters_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = (Application.Current as App)?.MainWindow;
            var hwnd = window != null ? WindowNative.GetWindowHandle(window) : IntPtr.Zero;

            var defaultName = $"filters-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var path = Win32FilePicker.SaveFile(hwnd, "Save filters", defaultName,
                new[] { ("Filter preset", ".json") });
            if (string.IsNullOrEmpty(path)) return;

            var snapshot = ViewModel.CaptureFilters();
            var json = FilterPersistence.Serialize(snapshot);
            System.IO.File.WriteAllText(path, json);
            ViewModel.StatusMessage = $"Saved filters to {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Save filters failed: {ex.Message}";
        }
    }

    private void LoadFilters_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = (Application.Current as App)?.MainWindow;
            var hwnd = window != null ? WindowNative.GetWindowHandle(window) : IntPtr.Zero;

            var path = Win32FilePicker.PickFile(hwnd, "Load filters", "Filter preset", ".json");
            if (string.IsNullOrEmpty(path)) return;

            var json = System.IO.File.ReadAllText(path);
            var snapshot = FilterPersistence.Deserialize(json);
            if (snapshot == null)
            {
                ViewModel.StatusMessage = "Couldn't parse filter file";
                return;
            }
            ViewModel.ApplyFilterSnapshot(snapshot);
            ViewModel.StatusMessage = $"Loaded filters from {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Load filters failed: {ex.Message}";
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(SettingsPage));
    }

    private double _savedFilterWidth = 300;
    private double _savedDetailsHeight = 280;

    private void CollapseFilters_Click(object sender, RoutedEventArgs e)
    {
        _savedFilterWidth = FilterColumn.ActualWidth > 50 ? FilterColumn.ActualWidth : 300;
        FilterColumn.Width = GridLength.Auto;
        FilterPanel.Visibility = Visibility.Collapsed;
        FilterSplitter.Visibility = Visibility.Collapsed;
        CollapsedFilterButton.Visibility = Visibility.Visible;
    }

    private void ExpandFilters_Click(object sender, RoutedEventArgs e)
    {
        FilterColumn.Width = new GridLength(_savedFilterWidth > 200 ? _savedFilterWidth : 300);
        FilterPanel.Visibility = Visibility.Visible;
        FilterSplitter.Visibility = Visibility.Visible;
        CollapsedFilterButton.Visibility = Visibility.Collapsed;
    }

    private void CollapseDetails_Click(object sender, RoutedEventArgs e)
    {
        _savedDetailsHeight = DetailsRow.ActualHeight > 60 ? DetailsRow.ActualHeight : 280;
        DetailsRow.Height = GridLength.Auto;
        DetailsPanel.Visibility = Visibility.Collapsed;
        DetailsSplitter.Visibility = Visibility.Collapsed;
        CollapsedDetailsBar.Visibility = Visibility.Visible;
    }

    private void ExpandDetails_Click(object sender, RoutedEventArgs e)
    {
        DetailsRow.Height = new GridLength(_savedDetailsHeight > 100 ? _savedDetailsHeight : 280);
        DetailsPanel.Visibility = Visibility.Visible;
        DetailsSplitter.Visibility = Visibility.Visible;
        CollapsedDetailsBar.Visibility = Visibility.Collapsed;
    }

    private static readonly LevelToRowBrushConverter _rowBrushConverter = new();
    private static readonly SolidColorBrush _transparentBrush = new(Microsoft.UI.Colors.Transparent);

    private void EventsDataGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        var current = e.Column.SortDirection;
        var newDirection = current == DataGridSortDirection.Ascending
            ? DataGridSortDirection.Descending
            : DataGridSortDirection.Ascending;

        foreach (var col in EventsDataGrid.Columns)
        {
            if (col != e.Column) col.SortDirection = null;
        }
        e.Column.SortDirection = newDirection;

        var tag = e.Column.Tag?.ToString() ?? "Time";
        Func<EventLogEntry, object> keySelector = tag switch
        {
            "Time" => entry => entry.TimeCreated,
            "Level" => entry => (int)entry.Level,
            "Id" => entry => entry.Id,
            "Source" => entry => entry.ProviderName,
            "Channel" => entry => entry.Channel,
            "User" => entry => entry.Username,
            "Message" => entry => entry.Message,
            _ => entry => entry.TimeCreated
        };

        var sorted = newDirection == DataGridSortDirection.Ascending
            ? ViewModel.FilteredEvents.OrderBy(keySelector).ToList()
            : ViewModel.FilteredEvents.OrderByDescending(keySelector).ToList();

        ViewModel.FilteredEvents.Clear();
        foreach (var entry in sorted)
        {
            ViewModel.FilteredEvents.Add(entry);
        }
    }

    private void EventsDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is EventLogEntry entry)
        {
            var brush = _rowBrushConverter.Convert(entry.Level, typeof(Brush), null, "") as Brush;
            e.Row.Background = brush ?? _transparentBrush;
        }
    }

    private IEnumerable<EventLogEntry> GetSelectedEntries()
    {
        return EventsDataGrid.SelectedItems.OfType<EventLogEntry>();
    }

    private void CopyRows_Click(object sender, RoutedEventArgs e)
    {
        var entries = GetSelectedEntries().ToList();
        if (entries.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            sb.Append(entry.TimeCreatedDisplay).Append('\t');
            sb.Append(entry.LevelName).Append('\t');
            sb.Append(entry.Id).Append('\t');
            sb.Append(entry.ProviderName).Append('\t');
            sb.Append(entry.Username).Append('\t');
            sb.AppendLine(entry.Message.Replace('\n', ' ').Replace('\r', ' '));
        }
        SetClipboard(sb.ToString());
    }

    private void CopyRowsAsCsv_Click(object sender, RoutedEventArgs e)
    {
        var entries = GetSelectedEntries().ToList();
        if (entries.Count == 0) return;
        SetClipboard(EventExporter.Serialize(entries, EventExporter.Format.Csv));
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        var entries = GetSelectedEntries().ToList();
        if (entries.Count == 0) return;
        var text = string.Join(Environment.NewLine + Environment.NewLine,
            entries.Select(en => en.Message));
        SetClipboard(text);
    }

    private static void SetClipboard(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
    }

    private void CopyDetailField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag != null)
        {
            SetClipboard(fe.Tag.ToString() ?? "");
        }
    }

    private void CopyProcessThread_Click(object sender, RoutedEventArgs e)
    {
        var entry = ViewModel.SelectedEvent;
        if (entry == null) return;
        SetClipboard($"{entry.ProcessId} / {entry.ThreadId}");
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (var fmt in new[] { EventExporter.Format.Csv, EventExporter.Format.Json, EventExporter.Format.Xml })
        {
            var item = new MenuFlyoutItem { Text = EventExporter.FormatLabel(fmt) };
            var capturedFmt = fmt;
            item.Click += (_, _) => ExportToFile(capturedFmt);
            flyout.Items.Add(item);
        }
        if (sender is FrameworkElement fe) flyout.ShowAt(fe);
    }

    private async void ExportToFile(EventExporter.Format format)
    {
        var entries = ViewModel.FilteredEvents.ToList();
        if (entries.Count == 0)
        {
            ViewModel.StatusMessage = "Nothing to export — no events in the current view";
            return;
        }

        try
        {
            var window = (Application.Current as App)?.MainWindow;
            var hwnd = window != null ? WindowNative.GetWindowHandle(window) : IntPtr.Zero;

            var ext = EventExporter.DefaultExtension(format);
            var label = EventExporter.FormatLabel(format);
            var defaultName = $"events-{DateTime.Now:yyyyMMdd-HHmmss}{ext}";

            var path = Win32FilePicker.SaveFile(
                hwnd,
                $"Export {entries.Count} events as {label}",
                defaultName,
                new[] { ($"{label} file", ext) });

            if (string.IsNullOrEmpty(path)) return;

            var contents = EventExporter.Serialize(entries, format);
            System.IO.File.WriteAllText(path, contents);
            ViewModel.StatusMessage = $"Exported {entries.Count} events to {System.IO.Path.GetFileName(path)}";

            var ask = new ContentDialog
            {
                Title = "Export complete",
                Content = $"Exported {entries.Count} events to:\n{path}\n\nOpen the containing folder?",
                PrimaryButtonText = "Open folder",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            if (await ask.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    // /select highlights the file in Explorer instead of just opening the parent dir.
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = false
                    });
                }
                catch (Exception ex)
                {
                    ViewModel.StatusMessage = $"Couldn't open Explorer: {ex.Message}";
                }
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private void ColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (var col in EventsDataGrid.Columns)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = col.Header?.ToString() ?? col.Tag?.ToString() ?? "Column",
                IsChecked = col.Visibility == Visibility.Visible
            };
            var capturedCol = col;
            item.Click += (s, args) =>
            {
                capturedCol.Visibility = ((ToggleMenuFlyoutItem)s).IsChecked ? Visibility.Visible : Visibility.Collapsed;
                SaveColumnVisibility();
            };
            flyout.Items.Add(item);
        }
        if (sender is FrameworkElement fe)
        {
            flyout.ShowAt(fe);
        }
    }

    private void CopyAllDetails_Click(object sender, RoutedEventArgs e)
    {
        var entry = ViewModel.SelectedEvent;
        if (entry == null) return;

        var sb = new StringBuilder();
        sb.AppendLine($"Event ID: {entry.Id}");
        sb.AppendLine($"Level: {entry.LevelName}");
        sb.AppendLine($"Time Created: {entry.TimeCreatedDisplay}");
        sb.AppendLine($"Provider: {entry.ProviderName}");
        sb.AppendLine($"Provider GUID: {entry.ProviderGuid}");
        sb.AppendLine($"Channel: {entry.Channel}");
        sb.AppendLine($"Task: {entry.TaskName}");
        sb.AppendLine($"Keywords: {entry.Keywords}");
        sb.AppendLine($"User: {entry.Username}");
        sb.AppendLine($"Process ID: {entry.ProcessId}");
        sb.AppendLine($"Thread ID: {entry.ThreadId}");
        sb.AppendLine($"Computer: {entry.Computer}");
        sb.AppendLine();
        sb.AppendLine("Message:");
        sb.AppendLine(entry.Message);

        SetClipboard(sb.ToString());
    }
}
