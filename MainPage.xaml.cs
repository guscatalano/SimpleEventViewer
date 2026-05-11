using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleEventViewer_WinUI.Models;
using SimpleEventViewer_WinUI.Services;
using SimpleEventViewer_WinUI.ViewModels;
using System.Linq;
using System.Text;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace SimpleEventViewer_WinUI;

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

        var simplified = entries.Select(en => new
        {
            en.TimeCreated,
            Level = en.LevelName,
            en.Id,
            Source = en.ProviderName,
            en.TaskName,
            en.Keywords,
            User = en.Username,
            en.ProcessId,
            en.ThreadId,
            en.Computer,
            en.Message,
            en.Xml
        });

        var json = JsonSerializer.Serialize(simplified, new JsonSerializerOptions { WriteIndented = true });
        SetClipboard(json);
    }

    private void OnThemeChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var temp = ViewModel.FilteredEvents.ToList();
            ViewModel.FilteredEvents.Clear();
            foreach (var item in temp)
            {
                ViewModel.FilteredEvents.Add(item);
            }
        });
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
        ViewModel.SelectedSource = ViewModel.SourceCategories.Count > 0 ? ViewModel.SourceCategories[0] : null;
        ViewModel.SelectedProcess = ViewModel.ProcessCategories.Count > 0 ? ViewModel.ProcessCategories[0] : null;
        ViewModel.SelectedUser = ViewModel.UserCategories.Count > 0 ? ViewModel.UserCategories[0] : null;
        ViewModel.SelectedComputer = ViewModel.ComputerCategories.Count > 0 ? ViewModel.ComputerCategories[0] : null;
        ViewModel.SelectedChannel = ViewModel.ChannelCategories.Count > 0 ? ViewModel.ChannelCategories[0] : null;
        if (ViewModel.AvailableTypes.Count > 0)
        {
            ViewModel.SelectedType = ViewModel.AvailableTypes[0];
        }
        ViewModel.StartTime = null;
        ViewModel.EndTime = null;
        ViewModel.StartTimeOfDay = TimeSpan.Zero;
        ViewModel.EndTimeOfDay = new TimeSpan(23, 59, 59);
        ViewModel.SearchText = string.Empty;
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

        var sb = new StringBuilder();
        sb.AppendLine("Time,Level,ID,Source,User,Message");
        foreach (var entry in entries)
        {
            sb.Append(CsvEscape(entry.TimeCreatedDisplay)).Append(',');
            sb.Append(CsvEscape(entry.LevelName)).Append(',');
            sb.Append(entry.Id).Append(',');
            sb.Append(CsvEscape(entry.ProviderName)).Append(',');
            sb.Append(CsvEscape(entry.Username)).Append(',');
            sb.AppendLine(CsvEscape(entry.Message));
        }
        SetClipboard(sb.ToString());
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        var entries = GetSelectedEntries().ToList();
        if (entries.Count == 0) return;
        var text = string.Join(Environment.NewLine + Environment.NewLine,
            entries.Select(en => en.Message));
        SetClipboard(text);
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
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
