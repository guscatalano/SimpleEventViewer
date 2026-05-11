using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleEventViewer_WinUI.Models;
using SimpleEventViewer_WinUI.Services;
using SimpleEventViewer_WinUI.ViewModels;
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
        ViewModel.SelectedType = null;
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

    private void CollapseFilters_Click(object sender, RoutedEventArgs e)
    {
        _savedFilterWidth = FilterColumn.Width.Value;
        FilterColumn.Width = new GridLength(0);
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
}
