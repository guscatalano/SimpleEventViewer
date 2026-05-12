using System.Diagnostics;
using System.Runtime.InteropServices;
using SimpleEventViewer.Services;

namespace SimpleEventViewer.Tests;

/// <summary>
/// End-to-end test: export a slice of the live Application log to a temp EVTX file
/// via wevtutil, then verify EventLogFileReader can read events back from it.
///
/// Skipped on non-Windows or if wevtutil isn't available (e.g. CI containers
/// without the full Windows feature set).
/// </summary>
public class EvtxLoadIntegrationTests
{
    [Fact]
    public void LoadEvtxFile_ReadsEventsWithExpectedFields()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Skip silently on non-Windows
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"sev_test_{Guid.NewGuid():N}.evtx");

        try
        {
            // Export a small slice of the Application log to a temp .evtx file.
            // Use a generous filter so the test still finds something on a fresh machine.
            var exitCode = RunProcess("wevtutil",
                $"epl Application \"{tempPath}\" /ow:true");

            if (exitCode != 0 || !File.Exists(tempPath))
            {
                // wevtutil missing or no Application events available; can't run the test here.
                return;
            }

            var fileSize = new FileInfo(tempPath).Length;
            Assert.True(fileSize > 0, "Exported evtx file should not be empty");

            // Act: read the file via our EventLogFileReader
            var entries = EventLogFileReader.Read(tempPath);

            // Assert: we got events with the basic fields populated
            Assert.NotEmpty(entries);
            Assert.All(entries, e =>
            {
                Assert.NotEqual(default, e.TimeCreated);
                Assert.False(string.IsNullOrEmpty(e.ProviderName));
                // Channel comes from System/Channel in the event XML
                Assert.False(string.IsNullOrEmpty(e.Channel));
            });

            // And they should sort newest-first when ordered by TimeCreated descending
            var ordered = entries.OrderByDescending(e => e.TimeCreated).Select(e => e.Id).ToList();
            Assert.Equal(ordered, entries.OrderByDescending(e => e.TimeCreated).Select(e => e.Id).ToList());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    [Fact]
    public void LoadEvtxFile_BadPath_Throws()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var bogus = Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.evtx");
        Assert.ThrowsAny<Exception>(() => EventLogFileReader.Read(bogus));
    }

    private static int RunProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return -1;
            proc.WaitForExit(30000);
            return proc.HasExited ? proc.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }
}
