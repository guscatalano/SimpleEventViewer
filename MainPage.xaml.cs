using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleEventViewer_WinUI.Models;
using SimpleEventViewer_WinUI.Services;
using SimpleEventViewer_WinUI.ViewModels;
using System.Linq;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SimpleEventViewer_WinUI;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        ViewModel = new MainViewModel();
        InitializeComponent();

        // Refresh row colors when theme/color scheme changes
        SettingsService.Instance.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        // Force ListView to re-render items by triggering a refresh of the ItemsSource
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

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshCurrentView();
    }

    private async void LoadEvtxFile_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(".evtx");
        if (!string.IsNullOrEmpty(path))
        {
            ViewModel.LoadFile(path, "evtx");
        }
    }

    private async void LoadXmlFile_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(".xml");
        if (!string.IsNullOrEmpty(path))
        {
            ViewModel.LoadFile(path, "xml");
        }
    }

    private async void LoadEtlFile_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(".etl");
        if (!string.IsNullOrEmpty(path))
        {
            ViewModel.LoadFile(path, "etl");
        }
    }

    private async System.Threading.Tasks.Task<string?> PickFileAsync(string extension)
    {
        var picker = new FileOpenPicker();
        var window = (Application.Current as App)?.MainWindow;
        if (window != null)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        picker.FileTypeFilter.Add(extension);
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedSource = ViewModel.SourceCategories.Count > 0 ? ViewModel.SourceCategories[0] : null;
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

    private void SelectAllRows_Click(object sender, RoutedEventArgs e)
    {
        EventsDataGrid.SelectedItems.Clear();
        foreach (var item in ViewModel.FilteredEvents)
        {
            EventsDataGrid.SelectedItems.Add(item);
        }
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
        if (sender is FrameworkElement fe && fe.Tag is string text)
        {
            SetClipboard(text);
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
