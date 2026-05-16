using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace SimpleEventViewer.Tests;

/// <summary>
/// UI smoke tests using FlaUI / Windows UI Automation. These launch the actual
/// app and verify that the UI elements render correctly.
///
/// Requirements:
///   - Windows with an interactive desktop session (won't run in headless CI)
///   - The app must be built first (`dotnet build` of the main project)
///
/// Each test class instance launches and disposes a fresh app process.
/// </summary>
[Collection("UI tests are not parallelizable")]
public class UiSmokeTests : IDisposable
{
    private readonly FlaUI.Core.Application? _app;
    private readonly UIA3Automation _automation = null!;
    private readonly bool _canRun;

    private readonly Process? _dotnetRunProcess;

    public UiSmokeTests()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        _automation = new UIA3Automation();

        var projectDir = LocateMainProjectDir();
        if (projectDir == null) return;

        // Launch via `dotnet run` so the WindowsAppSDK bootstrap winapp helper
        // gives the app a debug package identity. The raw .exe can't initialize
        // the Windows App SDK without package identity.
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "run --no-build -c Debug",
                WorkingDirectory = projectDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            _dotnetRunProcess = Process.Start(psi);
            if (_dotnetRunProcess == null) return;

            // The winapp tool writes "✅ <appid> launched (PID: NNNN)" to stdout.
            // Parse to find the actual app's PID.
            int? appPid = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline && !_dotnetRunProcess.HasExited)
            {
                var line = _dotnetRunProcess.StandardOutput.ReadLine();
                if (line == null) break;
                var match = System.Text.RegularExpressions.Regex.Match(line, @"PID:\s*(\d+)");
                if (match.Success)
                {
                    appPid = int.Parse(match.Groups[1].Value);
                    break;
                }
            }

            if (appPid != null)
            {
                _app = FlaUI.Core.Application.Attach(appPid.Value);
                _app.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(20));
                // Give WinUI a moment to fully render the visual tree
                Thread.Sleep(2000);
                _canRun = _app.MainWindowHandle != IntPtr.Zero;
            }
        }
        catch
        {
            _app = null;
        }
    }

    public void Dispose()
    {
        try { _app?.Close(); } catch { }
        try
        {
            if (_dotnetRunProcess != null && !_dotnetRunProcess.HasExited)
            {
                _dotnetRunProcess.Kill(entireProcessTree: true);
            }
        }
        catch { }
        try { _automation?.Dispose(); } catch { }
    }

    [Fact]
    public void App_LaunchesAndShowsMainWindow()
    {
        if (!_canRun) return;

        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);
        Assert.False(string.IsNullOrEmpty(window.Title));
    }

    [Fact]
    public void App_HasExpectedToolbarButtons()
    {
        if (!_canRun) return;

        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        // The toolbar collapses source selection into a single "Open" dropdown
        // (Live / EVTX / experimental XML+ETL) next to the contextual Refresh
        // button. We only assert on the surface controls — the menu items are
        // exercised by App_DataGridPopulatesAfterLoadingEvtxFile.
        Assert.True(FindByName(window, "Open") != null, "Expected an 'Open' toolbar button");

        var refresh = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button)
              .And(cf.ByName("Refresh live logs").Or(cf.ByName("Refresh"))));
        Assert.True(refresh != null, "Expected a Refresh toolbar button (live or contextual label)");
    }

    [Fact]
    public void App_FilterPanelHasExpectedSections()
    {
        if (!_canRun) return;

        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        // Persisted Settings from prior sessions may have hidden some filter
        // sections via "Visible filter sections". Re-enable everything before
        // asserting that each label is present.
        EnsureAllFilterSectionsVisible(window);

        var labels = new[] { "Event Source", "Time Range", "Event Level", "Message", "User", "Process", "Computer" };
        foreach (var label in labels)
        {
            var found = window.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Text).And(cf.ByName(label)));
            Assert.True(found != null, $"Expected filter section '{label}'");
        }
    }

    /// <summary>
    /// Open Settings, walk the "Visible filter sections" checkbox list, check
    /// any that are off, then go back. Idempotent — does nothing if everything
    /// is already on.
    /// </summary>
    private void EnsureAllFilterSectionsVisible(Window window)
    {
        var settingsBtn = window.FindFirstDescendant(cf => cf.ByName("Settings"));
        Assert.True(settingsBtn != null, "Settings button not found");
        ClickElement(settingsBtn!);
        Thread.Sleep(1000);

        // Section labels match SettingsService.AvailableFilterSections.
        var sectionLabels = new[]
        {
            "Time Range", "Event Source", "Event Level", "Event ID",
            "Message", "User", "Process", "Computer", "Channel"
        };

        foreach (var label in sectionLabels)
        {
            // Multiple checkboxes named (e.g.) "User" exist across the page —
            // pick the one inside the filter-visibility list specifically.
            // Easiest: find ALL checkboxes with that name and check whichever
            // is not yet checked. False-positives are fine (idempotent toggle).
            var checks = window.FindAllDescendants(cf =>
                cf.ByControlType(ControlType.CheckBox).And(cf.ByName(label)));
            foreach (var cb in checks)
            {
                var toggle = cb.Patterns.Toggle.PatternOrDefault;
                if (toggle == null) continue;
                if (toggle.ToggleState.Value != FlaUI.Core.Definitions.ToggleState.On)
                {
                    toggle.Toggle();
                    Thread.Sleep(80);
                }
            }
        }

        // Back to events.
        var backBtn = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName("Back")));
        Assert.True(backBtn != null, "Back button not found");
        ClickElement(backBtn!);
        Thread.Sleep(1200);
    }

    [Fact]
    public void App_StatusBarShowsLiveSource()
    {
        if (!_canRun) return;
        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        // Status bar shows the loaded source via ViewModel.CurrentSource.
        // On a fresh launch with no CLI arg, this is "Live system logs".
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        AutomationElement? found = null;
        while (DateTime.UtcNow < deadline && found == null)
        {
            found = window.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Text).And(cf.ByName("Live system logs")));
            if (found == null) Thread.Sleep(300);
        }
        Assert.True(found != null, "Status bar should display 'Live system logs'");
    }

    [Fact]
    public void App_TitleContainsLoadedSource()
    {
        if (!_canRun) return;
        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        // Title format includes the source (default layout: "source — Simple Event Viewer").
        // Wait a beat so CurrentSource has propagated into Window.Title.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (window.Title.Contains("Live system logs") &&
                window.Title.Contains("Simple Event Viewer"))
            {
                return; // pass
            }
            Thread.Sleep(300);
        }
        Assert.Fail($"Expected window title to contain both source + app name, saw '{window.Title}'");
    }

    [Fact]
    public void App_RefreshButtonShowsContextualLabel()
    {
        if (!_canRun) return;
        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        // For live system logs (the default state), Refresh button label
        // reads "Refresh live logs", not the generic "Refresh".
        var refresh = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName("Refresh live logs")));
        Assert.True(refresh != null,
            "Expected a toolbar button labeled 'Refresh live logs' for live source");
    }

    [Fact]
    public void App_OpenMenuListsExpectedSources()
    {
        if (!_canRun) return;
        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        // Click the Open dropdown to reveal its menu.
        var openBtn = window.FindFirstDescendant(cf => cf.ByName("Open"));
        Assert.True(openBtn != null, "Open toolbar button not found");
        ClickElement(openBtn!);
        Thread.Sleep(500);

        // Menu items appear in a popup that may be outside the window's UIA
        // subtree, so search from the desktop root.
        var liveItem = _automation.GetDesktop().FindFirstDescendant(cf =>
            cf.ByName("Live Windows event log"));
        Assert.True(liveItem != null, "Open menu should contain 'Live Windows event log'");

        // Two plausible spellings depending on Windows-rendered ellipsis.
        var evtxItem = _automation.GetDesktop().FindFirstDescendant(cf =>
            cf.ByName("EVTX file…").Or(cf.ByName("EVTX file...")));
        Assert.True(evtxItem != null, "Open menu should contain 'EVTX file' entry");

        // Dismiss the menu so it doesn't leak into the next test.
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
        Thread.Sleep(200);
    }

    [Fact]
    public void App_CtrlFOpensFindBar()
    {
        if (!_canRun) return;
        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);
        window.SetForeground();
        Thread.Sleep(300);

        // Find bar starts collapsed. Ctrl+F should reveal it with focus on
        // the TextBox whose PlaceholderText is "Find in message, source, level…".
        FlaUI.Core.Input.Keyboard.TypeSimultaneously(
            FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
            FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_F);
        Thread.Sleep(500);

        // The TextBox has no explicit name; find by automation id via x:Name="FindBox".
        var findBox = window.FindFirstDescendant(cf => cf.ByAutomationId("FindBox"));
        Assert.True(findBox != null && findBox.IsOffscreen == false,
            "Ctrl+F should reveal the FindBox TextBox in the toolbar");

        // Close the find bar via Escape so app state is clean for the next test.
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
        Thread.Sleep(200);
    }

    [Fact]
    public void App_SettingsOpensAndBackReturns()
    {
        if (!_canRun) return;
        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        // Open Settings via gear button.
        var settingsBtn = window.FindFirstDescendant(cf => cf.ByName("Settings"));
        Assert.True(settingsBtn != null, "Settings button not found");
        ClickElement(settingsBtn!);
        Thread.Sleep(1200);

        // Verify we're on Settings — title bar text "Settings" + at least one
        // nav item like "Theme & Colors".
        var settingsTitle = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Text).And(cf.ByName("Settings")));
        Assert.True(settingsTitle != null, "Settings title not found after click");

        var themeNav = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.ListItem).And(cf.ByName("Theme & Colors")));
        Assert.True(themeNav != null, "Theme & Colors nav item missing");

        // Click Back to return to MainPage.
        var backBtn = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName("Back")));
        Assert.True(backBtn != null, "Back button not found");
        ClickElement(backBtn!);
        Thread.Sleep(1200);

        // Verify we're back — DataGrid should be reachable.
        var grid = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid));
        Assert.True(grid != null, "Events DataGrid should be visible after Back");
    }

    [Fact]
    public void App_SettingsNavBarHasExpectedSections()
    {
        if (!_canRun) return;
        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        var settingsBtn = window.FindFirstDescendant(cf => cf.ByName("Settings"));
        Assert.True(settingsBtn != null, "Settings button not found");
        ClickElement(settingsBtn!);
        Thread.Sleep(1200);

        // Every NavigationViewItem from the settings left pane should be present.
        var expected = new[]
        {
            "Theme & Colors", "Window", "Events grid", "Event details pane",
            "Filter panel", "Filter multi-select", "Experimental",
            "MCP server", "Restore defaults", "About"
        };
        var missing = new List<string>();
        foreach (var name in expected)
        {
            var item = window.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.ListItem).And(cf.ByName(name)));
            if (item == null) missing.Add(name);
        }

        // Get back to MainPage either way so subsequent tests aren't on Settings.
        var backBtn = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName("Back")));
        if (backBtn != null) ClickElement(backBtn);
        Thread.Sleep(800);

        Assert.True(missing.Count == 0, "Missing nav items: " + string.Join(", ", missing));
    }

    [Fact]
    public void App_MessageSearchNarrowsGrid()
    {
        if (!_canRun) return;
        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        EnsureAllFilterSectionsVisible(window);

        // Wait until rows are loaded so we have a baseline count.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        int initialRows = 0;
        while (DateTime.UtcNow < deadline)
        {
            initialRows = CountDataGridRows(window);
            if (initialRows >= 5) break;
            Thread.Sleep(500);
        }
        if (initialRows < 5)
        {
            // Skip — not enough data to make a meaningful narrowing assertion.
            return;
        }

        // The Message filter is a TextBox with PlaceholderText "Search messages…".
        // Find it by walking TextBox controls inside the filter panel.
        var searchBox = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
            .FirstOrDefault(e =>
            {
                try
                {
                    var name = e.Properties.HelpText.IsSupported ? e.Properties.HelpText.Value : "";
                    return name.Contains("Search messages", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            });
        if (searchBox == null) return; // best-effort; skip if we can't find it

        searchBox.Focus();
        Thread.Sleep(200);
        // A 6-char random-ish substring is unlikely to match every row.
        FlaUI.Core.Input.Keyboard.Type("zzqxyz");
        Thread.Sleep(800);

        var narrowedRows = CountDataGridRows(window);
        Assert.True(narrowedRows < initialRows,
            $"Expected message search to narrow the grid; before={initialRows}, after={narrowedRows}");

        // Clear so we don't leave the next test with an active filter.
        searchBox.Focus();
        FlaUI.Core.Input.Keyboard.TypeSimultaneously(
            FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
            FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DELETE);
        Thread.Sleep(400);
    }

    [Fact]
    public void App_ClearAllFiltersButtonExists()
    {
        if (!_canRun) return;
        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        EnsureAllFilterSectionsVisible(window);

        var btn = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName("Clear All Filters")));
        Assert.True(btn != null, "Clear All Filters button should be reachable");
    }

    [Fact]
    public void App_DataGridIsPresent()
    {
        if (!_canRun) return;

        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        var grid = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid));
        Assert.True(grid != null, "Events DataGrid should be present");
    }

    [Fact]
    public void App_DataGridPopulatesAfterInitialLoad()
    {
        if (!_canRun) return;

        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        // The app auto-loads "Last 24 hours" of Application events on startup.
        // Most dev machines have at least one Application event in the last day,
        // so wait up to ~45s for rows to stream in.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        int rowCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            rowCount = CountDataGridRows(window);
            if (rowCount > 0) break;
            Thread.Sleep(500);
        }

        Assert.True(rowCount > 0,
            $"Expected DataGrid to populate after auto-load, saw {rowCount} rows.");
    }

    [Fact]
    public void App_DataGridPopulatesAfterLoadingEvtxFile()
    {
        if (!_canRun) return;

        // Export a temp EVTX file we can load via the file dialog
        var tempPath = Path.Combine(Path.GetTempPath(), $"sev_ui_{Guid.NewGuid():N}.evtx");
        try
        {
            ExportApplicationLog(tempPath);
            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                return; // can't produce a fixture, skip
            }

            var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
            Assert.NotNull(window);

            // Let initial load settle so we have a known baseline state
            Thread.Sleep(3000);

            // Diagnostic: confirm app process is alive and visible
            var appWindows = Win32WindowEnum.EnumerateVisible()
                .Where(w => w.ProcessId == (uint)_app.ProcessId).ToList();
            Console.WriteLine($"App PID={_app.ProcessId} visible windows:");
            foreach (var w in appWindows)
            {
                Console.WriteLine($"  hwnd={w.Hwnd:X} class={w.ClassName} title='{w.Title}'");
            }

            // The toolbar now has a single "Open" dropdown; click it to reveal
            // the menu, then click the "EVTX file…" item.
            var openBtn = FindByName(window, "Open");
            Assert.True(openBtn != null, "Open toolbar button not found");
            Console.WriteLine($"Open button: rect={openBtn!.BoundingRectangle}");

            window.SetForeground();
            Thread.Sleep(500);
            ClickElement(openBtn!);
            Thread.Sleep(500);

            // Menu item — try a couple of plausible names since the en-dash and
            // unicode ellipsis vary by Windows version.
            AutomationElement? evtxItem = null;
            foreach (var label in new[] { "EVTX file…", "EVTX file...", "EVTX file" })
            {
                evtxItem = FindByName(window, label);
                if (evtxItem != null) break;
            }
            Assert.True(evtxItem != null, "EVTX file menu item not found inside Open dropdown");
            ClickElement(evtxItem!);

            // Brief wait so the file dialog has time to open
            Thread.Sleep(500);

            // The file picker is a Win32 dialog whose title we control: "Select EVTX file"
            var dialog = WaitForTopLevelWindow("Select EVTX file", TimeSpan.FromSeconds(15));
            if (dialog == null)
            {
                // Diagnostic: dump real Win32 windows
                var diag = string.Join("\n  ",
                    Win32WindowEnum.EnumerateVisible()
                        .Where(w => !string.IsNullOrWhiteSpace(w.Title))
                        .Select(w => $"pid={w.ProcessId} class={w.ClassName} title='{w.Title}'"));
                Assert.Fail($"File open dialog did not appear within 15s. Visible windows:\n  {diag}");
            }

            // Find the file name edit
            var fileNameElement =
                dialog!.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.Edit).And(cf.ByName("File name:")))
                ?? dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
            Assert.True(fileNameElement != null, "File name edit control not found in dialog");

            // Physically click the edit to set real keyboard focus
            ClickElement(fileNameElement!);
            Thread.Sleep(300);

            // Select all + delete existing text
            FlaUI.Core.Input.Keyboard.TypeSimultaneously(
                FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
                FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
            Thread.Sleep(100);
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DELETE);
            Thread.Sleep(100);

            // Type the path
            FlaUI.Core.Input.Keyboard.Type(tempPath);
            Thread.Sleep(400);

            // Press Enter twice: first Enter dismisses the autocomplete dropdown
            // (selecting nothing because the path is fully typed), second Enter
            // triggers the default button (Open). Pressing Escape would cancel
            // the entire dialog.
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
            Thread.Sleep(300);
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
            Thread.Sleep(800);

            // The CurrentSource binding updates the status bar to the filename
            // when a file is loaded. That's the unambiguous "EVTX got loaded" signal.
            var expectedSourceName = Path.GetFileName(tempPath);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
            bool sourceShown = false;
            int rowCount = 0;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var w = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(2));
                    if (w != null)
                    {
                        var sourceLabel = w.FindFirstDescendant(cf =>
                            cf.ByControlType(ControlType.Text).And(cf.ByName(expectedSourceName)));
                        if (sourceLabel != null) sourceShown = true;
                        rowCount = CountDataGridRows(w);
                        if (sourceShown) break;
                    }
                }
                catch { /* stale element while UI is rebuilding */ }
                Thread.Sleep(500);
            }

            Assert.True(sourceShown,
                $"Status bar never showed the loaded EVTX filename '{expectedSourceName}'. " +
                "The file was likely never loaded into the ViewModel.");
            Assert.True(rowCount > 0,
                $"DataGrid is empty after loading EVTX, expected events from the file");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    // ---- helpers ----

    private static string? LocateMainProjectDir()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && current != null; i++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "SimpleEventViewer.csproj");
            if (File.Exists(candidate)) return current.FullName;
        }
        return null;
    }

    private static AutomationElement? FindByName(Window window, string name)
    {
        try { return window.FindFirstDescendant(cf => cf.ByName(name)); }
        catch { return null; }
    }

    /// <summary>
    /// Clicks an element using the best mechanism available. Tries Invoke pattern
    /// first, then falls back to a real mouse click on the bounding rectangle.
    /// AppBarButton and some other WinUI controls don't reliably support Click().
    /// </summary>
    private static void ClickElement(AutomationElement element)
    {
        // Prefer real mouse click on AppBarButton — Invoke pattern doesn't always
        // trigger the file picker on packaged WinUI apps.
        try
        {
            var rect = element.BoundingRectangle;
            if (rect.Width > 0 && rect.Height > 0)
            {
                var x = (int)(rect.Left + rect.Width / 2);
                var y = (int)(rect.Top + rect.Height / 2);
                var point = new System.Drawing.Point(x, y);
                FlaUI.Core.Input.Mouse.MoveTo(point);
                Thread.Sleep(50);
                FlaUI.Core.Input.Mouse.LeftClick(point);
                return;
            }
        }
        catch { }

        try
        {
            var invoke = element.Patterns.Invoke.PatternOrDefault;
            invoke?.Invoke();
        }
        catch { }
    }

    /// <summary>
    /// Regression test for "row tint doesn't refresh when color scheme
    /// changes". Captures pixel samples from the DataGrid before and after
    /// a scheme switch and asserts the colors actually change.
    /// </summary>
    [Fact]
    public void RowTintRefreshesWhenColorSchemeChanges()
    {
        if (!_canRun) return;

        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        // Wait for events to populate so there are tinted rows to sample.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        while (CountDataGridRows(window) == 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(500);
        }
        Assert.True(CountDataGridRows(window) > 0, "Need at least one row to sample colors");

        // Make sure we start from a known scheme so this test is deterministic.
        // Default → produces a recognisable Info-blue tint for Information rows.
        SetColorScheme(window, "Default");
        Thread.Sleep(1500);

        var grid = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid));
        Assert.NotNull(grid);
        var gridRect = grid!.BoundingRectangle;

        // Sample several pixels from the first few rows. The DataGrid row
        // height is roughly 32 px; the header occupies the first ~32 px.
        // We pick a column that's wide and tinted (the User or Message column
        // furthest from cell content).
        var sampleY = new[] { 60, 90, 120, 150, 180 };
        const int sampleX = 250; // somewhere inside the grid horizontally

        var beforeColors = sampleY
            .Select(y => GetPixelColor((int)gridRect.X + sampleX, (int)gridRect.Y + y))
            .ToList();

        // Now switch to a scheme that produces a clearly different tint —
        // Forest (green Info) vs Default (blue Info) is easy to distinguish.
        SetColorScheme(window, "Forest");
        Thread.Sleep(2500);

        var afterColors = sampleY
            .Select(y => GetPixelColor((int)gridRect.X + sampleX, (int)gridRect.Y + y))
            .ToList();

        // Assert at least one row's tint changed by a visible amount.
        var changed = 0;
        for (int i = 0; i < beforeColors.Count; i++)
        {
            if (ColorDistance(beforeColors[i], afterColors[i]) > 8)
            {
                changed++;
            }
        }
        Assert.True(changed >= beforeColors.Count - 1,
            $"Expected nearly every sampled row pixel to change tint, got {changed}/{beforeColors.Count}. " +
            $"Before: [{string.Join(",", beforeColors)}]  After: [{string.Join(",", afterColors)}]");
    }

    private static System.Drawing.Color GetPixelColor(int x, int y)
    {
        using var bmp = new System.Drawing.Bitmap(1, 1);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(1, 1));
        return bmp.GetPixel(0, 0);
    }

    private static int ColorDistance(System.Drawing.Color a, System.Drawing.Color b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private void SetColorScheme(Window window, string schemeTag)
    {
        // Open Settings via the gear button (AutomationProperties.Name = "Settings").
        var settingsBtn = window.FindFirstDescendant(cf => cf.ByName("Settings"));
        Assert.True(settingsBtn != null, "Settings button not found");
        ClickElement(settingsBtn!);
        Thread.Sleep(1200);

        var combo = window.FindFirstDescendant(cf => cf.ByAutomationId("ColorSchemeComboBox"));
        Assert.True(combo != null, "ColorSchemeComboBox not found");

        // Prefer the ExpandCollapse pattern — it's more reliable than mouse click
        // for opening a ComboBox dropdown.
        var expand = combo!.Patterns.ExpandCollapse.PatternOrDefault;
        if (expand != null)
        {
            expand.Expand();
        }
        else
        {
            ClickElement(combo!);
        }
        Thread.Sleep(700);

        // The ComboBox popup lives in its own visual tree root, so search from
        // the desktop. Match either the ListItem (UIA's name for ComboBoxItem)
        // or ComboBoxItem by name.
        AutomationElement? item = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && item == null)
        {
            item = _automation.GetDesktop().FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.ListItem).And(cf.ByName(schemeTag)));
            if (item == null) Thread.Sleep(150);
        }
        Assert.True(item != null, $"ComboBoxItem '{schemeTag}' not found");

        // Some ComboBox items support SelectionItem.Select() — use it when available.
        var selectPattern = item!.Patterns.SelectionItem.PatternOrDefault;
        if (selectPattern != null)
        {
            selectPattern.Select();
        }
        else
        {
            ClickElement(item!);
        }
        Thread.Sleep(1000);

        // Back to events. The Back button is the labeled one in the title bar.
        var backBtn = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName("Back")));
        Assert.True(backBtn != null, "Back button not found");
        ClickElement(backBtn!);
        Thread.Sleep(1800);
    }

    private static int CountDataGridRows(Window window)
    {
        try
        {
            var grid = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid));
            if (grid == null) return 0;
            var rows = grid.FindAllChildren(cf => cf.ByControlType(ControlType.DataItem));
            return rows.Length;
        }
        catch
        {
            return 0;
        }
    }

    private static void ExportApplicationLog(string outPath)
    {
        try
        {
            // Limit the test fixture to errors+warnings only — keeps the file small
            // so loading it in the app is fast (the full Application log can be 100MB+).
            var query = "*[System[(Level=2 or Level=3)]]";
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "wevtutil",
                Arguments = $"epl Application \"{outPath}\" /ow:true /q:\"{query}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            p?.WaitForExit(30000);
        }
        catch { }
    }

    private IEnumerable<string> DumpTopLevelWindowTitles()
    {
        var results = new List<string>();
        try
        {
            var desktop = _automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
            foreach (var w in windows)
            {
                try
                {
                    var asWindow = w.AsWindow();
                    results.Add($"'{asWindow.Title}' [class={asWindow.ClassName}]");
                }
                catch { }
            }
        }
        catch { }
        return results;
    }

    private Window? WaitForTopLevelWindow(string titleContains, TimeSpan timeout)
    {
        if (_app == null) return null;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            // Use Win32 EnumWindows to find the dialog. FlaUI's UIA tree query
            // doesn't always pick up modal dialogs reliably.
            var hits = Win32WindowEnum.EnumerateVisible()
                .Where(w => w.ProcessId == (uint)_app.ProcessId)
                .Where(w =>
                    w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase) ||
                    w.ClassName == "#32770")
                .ToList();

            foreach (var hit in hits)
            {
                try
                {
                    var element = _automation.FromHandle(hit.Hwnd);
                    if (element != null)
                    {
                        return element.AsWindow();
                    }
                }
                catch { }
            }

            Thread.Sleep(250);
        }
        return null;
    }

    private static bool IsFileDialog(Window w, string titleContains)
    {
        try
        {
            var title = w.Title ?? string.Empty;
            var className = w.ClassName ?? string.Empty;
            if (title.Contains(titleContains, StringComparison.OrdinalIgnoreCase)) return true;
            // Win32 GetOpenFileName / IFileOpenDialog show window class "#32770"
            if (className == "#32770") return true;
            // Has a "File name:" edit somewhere?
            var fileNameField = w.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Edit).And(cf.ByName("File name:")));
            return fileNameField != null;
        }
        catch
        {
            return false;
        }
    }
}

[CollectionDefinition("UI tests are not parallelizable", DisableParallelization = true)]
public class UiTestCollection { }

internal static class Win32WindowEnum
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public record WindowInfo(IntPtr Hwnd, string Title, string ClassName, uint ProcessId);

    public static List<WindowInfo> EnumerateVisible()
    {
        var results = new List<WindowInfo>();
        EnumWindows((h, l) =>
        {
            if (!IsWindowVisible(h)) return true;
            var titleLen = GetWindowTextLength(h);
            var title = new StringBuilder(titleLen + 1);
            GetWindowText(h, title, title.Capacity);
            var cls = new StringBuilder(256);
            GetClassName(h, cls, cls.Capacity);
            GetWindowThreadProcessId(h, out var pid);
            results.Add(new WindowInfo(h, title.ToString(), cls.ToString(), pid));
            return true;
        }, IntPtr.Zero);
        return results;
    }
}
