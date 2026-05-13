using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleEventViewer.Services;
using SimpleEventViewer.Services.Mcp;
using System.Collections.Generic;
using Windows.UI;

namespace SimpleEventViewer;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            AppVersionText.Text = $"Version {v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            AppVersionText.Text = "Version 1.0.0";
        }

        var theme = SettingsService.Instance.Theme;
        ThemeComboBox.SelectedIndex = (int)theme;

        var color = SettingsService.Instance.AccentColor;
        for (int i = 0; i < ColorSchemeComboBox.Items.Count; i++)
        {
            if (ColorSchemeComboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == color)
            {
                ColorSchemeComboBox.SelectedIndex = i;
                break;
            }
        }

        RowColorModeComboBox.SelectedIndex = (int)SettingsService.Instance.RowColorMode;
        TitleFormatComboBox.SelectedIndex = (int)SettingsService.Instance.TitleFormat;

        var savedLines = SettingsService.Instance.MaxRowLines;
        for (int i = 0; i < MaxRowLinesComboBox.Items.Count; i++)
        {
            if (MaxRowLinesComboBox.Items[i] is ComboBoxItem item && item.Tag != null
                && int.TryParse(item.Tag.ToString(), out var lines) && lines == savedLines)
            {
                MaxRowLinesComboBox.SelectedIndex = i;
                break;
            }
        }
        RememberColumnWidthsSwitch.IsOn = SettingsService.Instance.RememberColumnWidths;
        ExperimentalFormatsSwitch.IsOn = SettingsService.Instance.ExperimentalFileFormats;

        BuildColumnVisibilityChecks();
        BuildMultiSelectToggles();

        // MCP server
        _initializingMcp = true;
        McpEnabledSwitch.IsOn = SettingsService.Instance.McpServerEnabled;
        McpPortBox.Value = SettingsService.Instance.McpServerPort;
        _initializingMcp = false;
        RefreshMcpStatus();

        UpdateSwatches(color);
    }

    private void RememberColumnWidthsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.RememberColumnWidths = RememberColumnWidthsSwitch.IsOn;
    }

    private void ExperimentalFormatsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.ExperimentalFileFormats = ExperimentalFormatsSwitch.IsOn;
    }

    private void TitleFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TitleFormatComboBox.SelectedItem is ComboBoxItem item && item.Tag != null
            && int.TryParse(item.Tag.ToString(), out var v))
        {
            SettingsService.Instance.TitleFormat = (TitleFormat)v;
        }
    }

    private void BuildColumnVisibilityChecks()
    {
        ColumnVisibilityPanel.Children.Clear();
        var saved = SettingsService.Instance.LoadColumnVisibility();

        foreach (var (tag, label, defaultVisible) in SettingsService.AvailableColumns)
        {
            var isOn = saved != null && saved.TryGetValue(tag, out var v) ? v : defaultVisible;
            var check = new CheckBox { Content = label, Tag = tag, IsChecked = isOn };
            check.Checked += ColumnCheck_Changed;
            check.Unchecked += ColumnCheck_Changed;
            ColumnVisibilityPanel.Children.Add(check);
        }
    }

    private void ColumnCheck_Changed(object sender, RoutedEventArgs e)
    {
        var visibility = new Dictionary<string, bool>();
        foreach (var child in ColumnVisibilityPanel.Children)
        {
            if (child is CheckBox cb && cb.Tag is string tag)
            {
                visibility[tag] = cb.IsChecked == true;
            }
        }
        SettingsService.Instance.SaveColumnVisibility(visibility);
    }

    private void BuildMultiSelectToggles()
    {
        MultiSelectTogglesPanel.Children.Clear();
        foreach (SettingsService.FilterDimension dim in
                 System.Enum.GetValues(typeof(SettingsService.FilterDimension)))
        {
            var label = dim switch
            {
                SettingsService.FilterDimension.Source   => "Event Source",
                SettingsService.FilterDimension.Level    => "Event Level",
                SettingsService.FilterDimension.User     => "User",
                SettingsService.FilterDimension.Process  => "Process",
                SettingsService.FilterDimension.Computer => "Computer",
                SettingsService.FilterDimension.Channel  => "Channel",
                _ => dim.ToString()
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            var toggle = new ToggleSwitch
            {
                IsOn = SettingsService.Instance.IsMultiSelectEnabled(dim),
                Tag = dim,
                MinWidth = 200,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            toggle.Toggled += MultiSelectToggle_Toggled;
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);

            MultiSelectTogglesPanel.Children.Add(grid);
        }
    }

    private void MultiSelectToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts && ts.Tag is SettingsService.FilterDimension dim)
        {
            SettingsService.Instance.SetMultiSelectEnabled(dim, ts.IsOn);
        }
    }

    // Guard the NumberBox + ToggleSwitch event handlers from firing while we
    // hydrate them in SettingsPage_Loaded.
    private bool _initializingMcp;

    private void McpEnabledSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializingMcp) return;
        SettingsService.Instance.McpServerEnabled = McpEnabledSwitch.IsOn;
        EventLogMcpServer.Instance.ApplyConfiguration(
            SettingsService.Instance.McpServerEnabled,
            SettingsService.Instance.McpServerPort);
        RefreshMcpStatus();
    }

    private void McpPortBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_initializingMcp) return;
        if (double.IsNaN(args.NewValue)) return;
        var port = (int)args.NewValue;
        if (port < 1024 || port > 65535) return;
        SettingsService.Instance.McpServerPort = port;
        if (SettingsService.Instance.McpServerEnabled)
        {
            EventLogMcpServer.Instance.ApplyConfiguration(true, port);
        }
        RefreshMcpStatus();
    }

    private void RefreshMcpStatus()
    {
        var running = EventLogMcpServer.Instance.IsRunning;
        var port = SettingsService.Instance.McpServerPort;
        if (running)
        {
            McpStatusText.Text = $"Running on 127.0.0.1:{port}";
            McpEndpointText.Text = $"Endpoint: http://127.0.0.1:{port}/";
        }
        else if (SettingsService.Instance.McpServerEnabled)
        {
            McpStatusText.Text = "Enabled, but listener is not running (port may be in use)";
            McpEndpointText.Text = string.Empty;
        }
        else
        {
            McpStatusText.Text = "Off — no port is open";
            McpEndpointText.Text = string.Empty;
        }

        // Refresh client config snippets with the current port.
        var url = $"http://127.0.0.1:{port}/";

        VsCodeConfigBox.Text =
            "{\r\n" +
            "  \"servers\": {\r\n" +
            "    \"simple-event-viewer\": {\r\n" +
            "      \"type\": \"http\",\r\n" +
            $"      \"url\": \"{url}\"\r\n" +
            "    }\r\n" +
            "  }\r\n" +
            "}";

        CursorConfigBox.Text =
            "{\r\n" +
            "  \"mcpServers\": {\r\n" +
            "    \"simple-event-viewer\": {\r\n" +
            $"      \"url\": \"{url}\"\r\n" +
            "    }\r\n" +
            "  }\r\n" +
            "}";

        ClaudeDesktopConfigBox.Text =
            "{\r\n" +
            "  \"mcpServers\": {\r\n" +
            "    \"simple-event-viewer\": {\r\n" +
            "      \"command\": \"npx\",\r\n" +
            $"      \"args\": [\"mcp-remote\", \"{url}\"]\r\n" +
            "    }\r\n" +
            "  }\r\n" +
            "}";

        ClaudeCodeCommandBox.Text =
            $"claude mcp add --transport http simple-event-viewer {url}";
    }

    private static void CopyToClipboard(string text)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    private void CopyVsCodeConfig_Click(object sender, RoutedEventArgs e) => CopyToClipboard(VsCodeConfigBox.Text);
    private void CopyCursorConfig_Click(object sender, RoutedEventArgs e) => CopyToClipboard(CursorConfigBox.Text);
    private void CopyClaudeDesktopConfig_Click(object sender, RoutedEventArgs e) => CopyToClipboard(ClaudeDesktopConfigBox.Text);
    private void CopyClaudeCodeCommand_Click(object sender, RoutedEventArgs e) => CopyToClipboard(ClaudeCodeCommandBox.Text);

    private void MaxRowLinesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MaxRowLinesComboBox.SelectedItem is ComboBoxItem item && item.Tag != null
            && int.TryParse(item.Tag.ToString(), out var lines))
        {
            SettingsService.Instance.MaxRowLines = lines;
        }
    }

    private void RowColorModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RowColorModeComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            if (int.TryParse(item.Tag.ToString(), out var modeValue))
            {
                SettingsService.Instance.RowColorMode = (RowColorMode)modeValue;
            }
        }
    }

    private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            if (int.TryParse(item.Tag.ToString(), out var themeValue))
            {
                await RunWithBusyOverlayAsync("Applying theme…", () =>
                {
                    SettingsService.Instance.Theme = (AppTheme)themeValue;
                });
            }
        }
    }

    private async void ColorSchemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColorSchemeComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            var colorName = item.Tag.ToString() ?? "Default";
            await RunWithBusyOverlayAsync("Applying color scheme…", () =>
            {
                SettingsService.Instance.AccentColor = colorName;
            });
            UpdateSwatches(colorName);
        }
    }

    /// <summary>
    /// Show the overlay, yield to the dispatcher so the spinner paints, run
    /// the work, then hide the overlay. Setting RequestedTheme on the root
    /// element and the cascading row-color refresh both run on the UI thread,
    /// so this is "make the freeze obvious" rather than truly async — but it
    /// keeps the user from thinking the app hung.
    /// </summary>
    private async System.Threading.Tasks.Task RunWithBusyOverlayAsync(string message, Action work)
    {
        BusyText.Text = message;
        BusyOverlay.Visibility = Visibility.Visible;
        await System.Threading.Tasks.Task.Delay(20);
        try { work(); } catch { }
        await System.Threading.Tasks.Task.Delay(20);
        BusyOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateSwatches(string colorScheme)
    {
        var (critical, error, warning, info) = ColorSchemes.GetColors(colorScheme);
        CriticalSwatch.Background = new SolidColorBrush(critical);
        ErrorSwatch.Background = new SolidColorBrush(error);
        WarningSwatch.Background = new SolidColorBrush(warning);
        InfoSwatch.Background = new SolidColorBrush(info);
    }

    private async void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "Restore default settings?",
            Content = "All preferences on this page will revert to their first-launch values. Loaded events and the current filter selection are untouched.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        await RunWithBusyOverlayAsync("Restoring defaults…", () =>
        {
            SettingsService.Instance.RestoreDefaults();
        });

        // Re-hydrate every control on this page so it reflects the defaults.
        SettingsPage_Loaded(this, new RoutedEventArgs());
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(MainPage));
        }
    }
}

public static class ColorSchemes
{
    /// <summary>
    /// The single "primary" accent color for this scheme. Used to drive
    /// SystemAccentColor app-wide (buttons, links, selection highlight).
    /// </summary>
    public static Color GetAccentColor(string scheme)
    {
        return scheme switch
        {
            "Blue"   => Color.FromArgb(255,   0, 120, 212),
            "Green"  => Color.FromArgb(255,  56, 142,  60),
            "Purple" => Color.FromArgb(255, 123,  31, 162),
            "Orange" => Color.FromArgb(255, 230,  81,   0),
            "Red"    => Color.FromArgb(255, 211,  47,  47),
            _        => Color.FromArgb(255,   0, 120, 212) // Windows default-ish
        };
    }

    public static (Color critical, Color error, Color warning, Color info) GetColors(string scheme)
    {
        return scheme switch
        {
            "Blue" => (
                Color.FromArgb(255, 0, 90, 158),
                Color.FromArgb(255, 0, 120, 212),
                Color.FromArgb(255, 144, 202, 249),
                Color.FromArgb(255, 100, 181, 246)
            ),
            "Green" => (
                Color.FromArgb(255, 27, 94, 32),
                Color.FromArgb(255, 56, 142, 60),
                Color.FromArgb(255, 174, 213, 129),
                Color.FromArgb(255, 102, 187, 106)
            ),
            "Purple" => (
                Color.FromArgb(255, 74, 20, 140),
                Color.FromArgb(255, 123, 31, 162),
                Color.FromArgb(255, 206, 147, 216),
                Color.FromArgb(255, 156, 39, 176)
            ),
            "Orange" => (
                Color.FromArgb(255, 191, 54, 12),
                Color.FromArgb(255, 230, 81, 0),
                Color.FromArgb(255, 255, 183, 77),
                Color.FromArgb(255, 255, 152, 0)
            ),
            "Red" => (
                Color.FromArgb(255, 183, 28, 28),
                Color.FromArgb(255, 211, 47, 47),
                Color.FromArgb(255, 239, 154, 154),
                Color.FromArgb(255, 244, 67, 54)
            ),
            _ => (
                Color.FromArgb(255, 196, 43, 28),
                Color.FromArgb(255, 232, 17, 35),
                Color.FromArgb(255, 252, 211, 91),
                Color.FromArgb(255, 0, 120, 212)
            )
        };
    }
}
