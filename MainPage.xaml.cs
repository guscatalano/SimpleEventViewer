using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        ViewModel.SearchText = string.Empty;
    }
}
