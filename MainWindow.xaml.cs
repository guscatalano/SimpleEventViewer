using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SimpleEventViewer;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));

        Closed += MainWindow_Closed;
    }

    /// <summary>
    /// Update both the OS-level Window.Title and the in-content TitleBar
    /// caption to reflect what's currently loaded — so users with multiple
    /// app windows open can tell them apart from the taskbar.
    /// </summary>
    public void UpdateTitleBar(string sourceLabel)
    {
        const string app = "Simple Event Viewer";
        var format = SimpleEventViewer.Services.SettingsService.Instance.TitleFormat;
        string caption;
        if (string.IsNullOrEmpty(sourceLabel) || format == SimpleEventViewer.Services.TitleFormat.JustApp)
        {
            caption = app;
        }
        else if (format == SimpleEventViewer.Services.TitleFormat.AppThenSource)
        {
            caption = $"{app} — {sourceLabel}";
        }
        else // SourceThenApp (default)
        {
            caption = $"{sourceLabel} — {app}";
        }
        AppTitleBar.Title = caption;
        Title = caption;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // Persist DataGrid column widths from the active MainPage so they
        // survive across launches.
        try
        {
            if (RootFrame.Content is MainPage page)
            {
                page.SaveColumnWidths();
            }
        }
        catch { }

        // Tear down the MCP listener so the port is released cleanly.
        try { SimpleEventViewer.Services.Mcp.EventLogMcpServer.Instance.Stop(); } catch { }
    }
}
