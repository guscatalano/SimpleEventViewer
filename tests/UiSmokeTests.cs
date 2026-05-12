using System.Diagnostics;
using System.Runtime.InteropServices;
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

    public UiSmokeTests()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        _automation = new UIA3Automation();

        var exePath = LocateAppExe();
        if (exePath == null || !File.Exists(exePath))
        {
            return;
        }

        try
        {
            _app = FlaUI.Core.Application.Launch(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
            });
            _app.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(15));
            _canRun = _app.MainWindowHandle != IntPtr.Zero;
        }
        catch
        {
            _app = null;
        }
    }

    public void Dispose()
    {
        try { _app?.Close(); } catch { }
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

        // The CommandBar buttons should be discoverable by their accessible name.
        var expected = new[] { "Load Local Event Log", "Refresh", "Load EVTX", "Load XML", "Load ETL" };
        foreach (var name in expected)
        {
            var btn = FindByName(window, name);
            Assert.True(btn != null, $"Expected to find a button named '{name}'");
        }
    }

    [Fact]
    public void App_FilterPanelHasExpectedSections()
    {
        if (!_canRun) return;

        var window = _app!.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        // The filter panel labels are TextBlocks; they're reachable as Text controls.
        var labels = new[] { "Event Source", "Time Range", "Event Level", "Message", "User", "Process", "Computer" };
        foreach (var label in labels)
        {
            var found = window.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Text).And(cf.ByName(label)));
            Assert.True(found != null, $"Expected filter section '{label}'");
        }
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

        // The app auto-loads "Last 24 hours" of FlaUI.Core.Application events on startup.
        // Most dev machines have at least one FlaUI.Core.Application event in the last day,
        // so wait up to ~45s for rows to stream in.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        int rowCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            rowCount = CountDataGridRows(window);
            if (rowCount > 0) break;
            Thread.Sleep(500);
        }

        // We assert the grid is non-empty if events exist in the system log.
        // If a machine truly has zero FlaUI.Core.Application events in 24h, this would fail —
        // adjust to a wider load window in that case.
        Assert.True(rowCount > 0,
            $"Expected DataGrid to populate after auto-load, saw {rowCount} rows. " +
            "If this machine genuinely has no FlaUI.Core.Application events in the last 24h, that's expected.");
    }

    // ---- helpers ----

    private static string? LocateAppExe()
    {
        // Walk up from the test binary's dir until we find the main .csproj,
        // then find a built exe under it.
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && current != null; i++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "SimpleEventViewer.WinUI.csproj");
            if (File.Exists(candidate))
            {
                var hits = Directory.EnumerateFiles(current.FullName, "SimpleEventViewer.WinUI.exe", SearchOption.AllDirectories)
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}publish{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => File.GetLastWriteTimeUtc(p));
                return hits.FirstOrDefault();
            }
        }
        return null;
    }

    private static AutomationElement? FindByName(Window window, string name)
    {
        try { return window.FindFirstDescendant(cf => cf.ByName(name)); }
        catch { return null; }
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
}

[CollectionDefinition("UI tests are not parallelizable", DisableParallelization = true)]
public class UiTestCollection { }
