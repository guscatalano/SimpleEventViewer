using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SimpleEventViewer;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    public Window? MainWindow { get; private set; }

    /// <summary>
    /// File path passed on the command line, if any. The MainPage reads this
    /// after construction and triggers a file load.
    /// </summary>
    public string? StartupFilePath { get; private set; }

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        StartupFilePath = ParseFilePathFromArgs();

        if (StartupFilePath != null && HandleCliOnly())
        {
            Exit();
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Activate();

        // Apply saved theme
        SimpleEventViewer.Services.SettingsService.Instance.ApplyTheme(
            SimpleEventViewer.Services.SettingsService.Instance.Theme);
    }

    /// <summary>
    /// Parse args to find a file path. Accepts:
    ///   SimpleEventViewer.exe path\to\file.evtx
    ///   SimpleEventViewer.exe --file path\to\file.evtx
    /// </summary>
    private static string? ParseFilePathFromArgs()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            // args[0] is the exe path
            for (int i = 1; i < args.Length; i++)
            {
                var a = args[i];
                if (a is "--file" or "-f" or "/f")
                {
                    if (i + 1 < args.Length) return args[i + 1];
                }
                else if (a is "--help" or "-h" or "/?" or "/help")
                {
                    // No-op; the help text would only matter if we had a console.
                    return null;
                }
                else if (!a.StartsWith('-') && !a.StartsWith('/'))
                {
                    // Positional file path
                    if (System.IO.File.Exists(a)) return a;
                    // If the file doesn't exist, still pass it so the VM can show
                    // an error in the UI rather than silently dropping the arg.
                    return a;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Placeholder for a future "headless mode" where we'd print events without
    /// showing the UI. Currently always returns false — the file is loaded via
    /// the regular UI after the window is shown.
    /// </summary>
    private bool HandleCliOnly() => false;
}
