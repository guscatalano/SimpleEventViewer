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
        BuildFilterVisibilityChecks();
        BuildDetailFieldVisibilityChecks();
        BuildMultiSelectToggles();

        // MCP server
        _initializingMcp = true;
        McpEnabledSwitch.IsOn = SettingsService.Instance.McpServerEnabled;
        McpPortBox.Value = SettingsService.Instance.McpServerPort;
        McpAutoPortSwitch.IsOn = SettingsService.Instance.McpAutoPort;
        _initializingMcp = false;
        RefreshMcpStatus();

        RefreshMultiInstanceWarning();

        UpdateSwatches(color);
    }

    /// <summary>
    /// Settings live in <c>ApplicationData.Current.LocalSettings</c>, which is
    /// shared across every instance of the packaged app. With two windows
    /// open the in-memory caches diverge and the on-disk store goes
    /// last-write-wins, which can silently overwrite a change the user just
    /// made in the other window. Surface that as a warning when we detect
    /// another instance.
    /// </summary>
    private void RefreshMultiInstanceWarning()
    {
        try
        {
            var name = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            var count = System.Diagnostics.Process.GetProcessesByName(name).Length;
            MultiInstanceInfoBar.IsOpen = count > 1;
        }
        catch
        {
            MultiInstanceInfoBar.IsOpen = false;
        }
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

    private void BuildFilterVisibilityChecks()
    {
        FilterVisibilityPanel.Children.Clear();
        var saved = SettingsService.Instance.LoadFilterVisibility();

        foreach (var (key, label) in SettingsService.AvailableFilterSections)
        {
            var isOn = saved == null || !saved.TryGetValue(key, out var v) || v;
            var check = new CheckBox { Content = label, Tag = key, IsChecked = isOn };
            check.Checked += FilterVisibilityCheck_Changed;
            check.Unchecked += FilterVisibilityCheck_Changed;
            FilterVisibilityPanel.Children.Add(check);
        }
    }

    private void FilterVisibilityCheck_Changed(object sender, RoutedEventArgs e)
    {
        var visibility = new Dictionary<string, bool>();
        foreach (var child in FilterVisibilityPanel.Children)
        {
            if (child is CheckBox cb && cb.Tag is string key)
            {
                visibility[key] = cb.IsChecked == true;
            }
        }
        SettingsService.Instance.SaveFilterVisibility(visibility);
    }

    private void BuildDetailFieldVisibilityChecks()
    {
        DetailFieldVisibilityPanel.Children.Clear();
        var saved = SettingsService.Instance.LoadDetailFieldVisibility();

        foreach (var (key, label) in SettingsService.AvailableDetailFields)
        {
            var isOn = saved == null || !saved.TryGetValue(key, out var v) || v;
            var check = new CheckBox { Content = label, Tag = key, IsChecked = isOn };
            check.Checked += DetailFieldCheck_Changed;
            check.Unchecked += DetailFieldCheck_Changed;
            DetailFieldVisibilityPanel.Children.Add(check);
        }
    }

    private void DetailFieldCheck_Changed(object sender, RoutedEventArgs e)
    {
        var visibility = new Dictionary<string, bool>();
        foreach (var child in DetailFieldVisibilityPanel.Children)
        {
            if (child is CheckBox cb && cb.Tag is string key)
            {
                visibility[key] = cb.IsChecked == true;
            }
        }
        SettingsService.Instance.SaveDetailFieldVisibility(visibility);
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
                SettingsService.FilterDimension.Id       => "Event ID",
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
            SettingsService.Instance.McpServerPort,
            SettingsService.Instance.McpAutoPort);
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
            EventLogMcpServer.Instance.ApplyConfiguration(
                true, port, SettingsService.Instance.McpAutoPort);
        }
        RefreshMcpStatus();
    }

    private void McpAutoPortSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializingMcp) return;
        SettingsService.Instance.McpAutoPort = McpAutoPortSwitch.IsOn;
        if (SettingsService.Instance.McpServerEnabled)
        {
            EventLogMcpServer.Instance.ApplyConfiguration(
                true,
                SettingsService.Instance.McpServerPort,
                SettingsService.Instance.McpAutoPort);
        }
        RefreshMcpStatus();
    }

    private void RefreshMcpStatus()
    {
        var running = EventLogMcpServer.Instance.IsRunning;
        var preferred = SettingsService.Instance.McpServerPort;
        var bound = EventLogMcpServer.Instance.Port;
        var portForSnippets = running ? bound : preferred;

        if (running)
        {
            if (bound != preferred && SettingsService.Instance.McpAutoPort)
            {
                McpStatusText.Text =
                    $"Running on 127.0.0.1:{bound} (preferred {preferred} was in use; auto-port took {bound})";
            }
            else
            {
                McpStatusText.Text = $"Running on 127.0.0.1:{bound}";
            }
            McpEndpointText.Text = $"Endpoint: http://127.0.0.1:{bound}/";
        }
        else if (SettingsService.Instance.McpServerEnabled)
        {
            var err = EventLogMcpServer.Instance.LastStartError;
            McpStatusText.Text = !string.IsNullOrEmpty(err)
                ? $"Not running — {err}"
                : "Enabled, but listener is not running (port may be in use)";
            McpEndpointText.Text = string.Empty;
        }
        else
        {
            McpStatusText.Text = "Off — no port is open";
            McpEndpointText.Text = string.Empty;
        }

        McpDiscoveryPathText.Text = EventLogMcpServer.DiscoveryFilePath;

        // Refresh client config snippets with the current port.
        var url = $"http://127.0.0.1:{portForSnippets}/";

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

    private void SettingsNav_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        BackButton_Click(this, new RoutedEventArgs());
    }

    private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string targetName)
        {
            if (FindName(targetName) is FrameworkElement card)
            {
                card.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalAlignmentRatio = 0.0
                });
            }
        }
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
            "Blue"    => Color.FromArgb(255,   0, 120, 212),
            "Teal"    => Color.FromArgb(255,   0, 137, 123),
            "Cyan"    => Color.FromArgb(255,   0, 188, 212),
            "Green"   => Color.FromArgb(255,  56, 142,  60),
            "Lime"    => Color.FromArgb(255, 124, 179,  66),
            "Amber"   => Color.FromArgb(255, 255, 160,   0),
            "Orange"  => Color.FromArgb(255, 230,  81,   0),
            "Red"     => Color.FromArgb(255, 211,  47,  47),
            "Crimson" => Color.FromArgb(255, 220,  20,  60),
            "Pink"    => Color.FromArgb(255, 233,  30,  99),
            "Magenta" => Color.FromArgb(255, 194,  24,  91),
            "Purple"  => Color.FromArgb(255, 123,  31, 162),
            "Indigo"  => Color.FromArgb(255,  63,  81, 181),
            "Slate"   => Color.FromArgb(255,  84, 110, 122),
            "Brown"   => Color.FromArgb(255, 121,  85,  72),

            // Themed palettes pick a signature hue for the app accent.
            "Pastel"     => Color.FromArgb(255, 129, 199, 132),
            "Vibrant"    => Color.FromArgb(255,   0, 184, 212),
            "Cyberpunk"  => Color.FromArgb(255,   0, 229, 255),
            "Forest"     => Color.FromArgb(255,  46, 125,  50),
            "Ocean"      => Color.FromArgb(255,   0, 151, 167),
            "Sunset"     => Color.FromArgb(255, 255, 152,   0),
            "Earth"      => Color.FromArgb(255, 104, 159,  56),
            "Royal"      => Color.FromArgb(255,  74,  20, 140),
            "Mono"       => Color.FromArgb(255, 117, 117, 117),

            _         => Color.FromArgb(255,   0, 120, 212) // Windows default-ish
        };
    }

    /// <summary>
    /// (critical, error, warning, info) palette for each scheme. Designed so
    /// every level uses a visually distinct hue — Critical and Error stay
    /// red-leaning (severity recognition is universal), Warning is yellow-
    /// or amber-leaning, and Info takes the scheme's signature color. A few
    /// "themed" schemes (Mono, Pastel, Cyberpunk, etc.) break the convention
    /// for variety.
    /// </summary>
    public static (Color critical, Color error, Color warning, Color info) GetColors(string scheme)
    {
        return scheme switch
        {
            // --- single-accent schemes: traffic-light reds + amber + accent ---
            "Blue"    => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(255, 160,  0), RGB( 30, 136, 229)),
            "Teal"    => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(255, 160,  0), RGB(  0, 137, 123)),
            "Cyan"    => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(255, 160,  0), RGB(  0, 172, 193)),
            "Green"   => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(255, 160,  0), RGB( 67, 160,  71)),
            "Lime"    => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(255, 160,  0), RGB(124, 179,  66)),
            "Amber"   => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(251, 140,  0), RGB(255, 193,   7)),
            "Orange"  => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(255, 193,  7), RGB(245, 124,   0)),
            "Red"     => (RGB(127, 17, 17),  RGB(211, 47,  47),  RGB(255, 152,  0), RGB(244, 67,   54)),
            "Crimson" => (RGB(136,  0, 21),  RGB(220, 20,  60),  RGB(255, 167,  38), RGB(216, 27,  96)),
            "Pink"    => (RGB(173, 20, 87),  RGB(229, 57,  53),  RGB(255, 167,  38), RGB(233, 30,   99)),
            "Magenta" => (RGB(136, 14, 79),  RGB(229, 57,  53),  RGB(255, 167,  38), RGB(194, 24,   91)),
            "Purple"  => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(255, 160,  0), RGB(142, 36,  170)),
            "Indigo"  => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(255, 160,  0), RGB( 57,  73, 171)),
            "Slate"   => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(255, 160,  0), RGB( 84, 110, 122)),
            "Brown"   => (RGB(183, 28, 28),  RGB(229, 57,  53),  RGB(255, 160,  0), RGB(109,  76,  65)),

            // --- themed palettes: 4 deliberately different hues ---
            "Pastel"     => (RGB(229, 115, 115), RGB(255, 138, 101), RGB(255, 213, 79),  RGB(129, 199, 132)),
            "Vibrant"    => (RGB(213,   0,   0), RGB(255,  61,   0), RGB(255, 214,   0), RGB(  0, 184, 212)),
            "Cyberpunk"  => (RGB(255,  23,  68), RGB(245,   0,  87), RGB(255, 234,   0), RGB(  0, 229, 255)),
            "Forest"     => (RGB(136,  14,  79), RGB(216,  67,  21), RGB(255, 179,   0), RGB( 46, 125,  50)),
            "Ocean"      => (RGB(136,  14,  79), RGB(211,  47,  47), RGB(255, 193,   7), RGB(  0, 151, 167)),
            "Sunset"     => (RGB(136,   0,  21), RGB(216,  27,  96), RGB(255, 152,   0), RGB(255, 213,  79)),
            "Earth"      => (RGB(141,  37,   8), RGB(191,  54,  12), RGB(255, 167,  38), RGB(104, 159,  56)),
            "Royal"      => (RGB(136,  14,  79), RGB(173,  20,  87), RGB(241, 196,  15), RGB( 74,  20, 140)),
            "Mono"       => (RGB( 33,  33,  33), RGB( 97,  97,  97), RGB(189, 189, 189), RGB(189, 189, 189)),

            _ => (
                RGB(196, 43, 28),
                RGB(232, 17, 35),
                RGB(252, 211, 91),
                RGB(  0, 120, 212)
            )
        };
    }

    private static Color RGB(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
}
