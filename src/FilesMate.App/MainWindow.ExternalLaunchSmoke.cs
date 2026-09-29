#if FILESMATE_UI_TEST
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using FilesMate.Platform.Windows.Operations;
using FilesMate.Platform.Windows.Processes;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task<object> RunExternalLaunchSmokeAsync()
    {
        // Explicit opt-in: copied applications and disposable documents only.
        if (Environment.GetEnvironmentVariable("FILESMATE_EXTERNAL_LAUNCH_SMOKE") != "1") return "Not requested";
        var root = Path.Combine(AppContext.BaseDirectory, "external-fixtures");
        var viewer = Path.Combine(root, "JPEGView", "JPEGView.exe");
        var sevenZip = Path.Combine(root, "7-Zip", "7zFM.exe");
        var sevenZipGui = Path.Combine(root, "7-Zip", "7zG.exe");
        if (!File.Exists(viewer) || !File.Exists(sevenZip) || !File.Exists(sevenZipGui))
            throw new FileNotFoundException("Copy external fixtures beneath the isolated test build first.");
        var sample = Path.Combine(root, "FilesMate sample.png");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "compact-list-Light.png"), sample, true);
        var archive = Path.Combine(root, "FilesMate sample-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) zip.CreateEntryFromFile(sample, Path.GetFileName(sample));
        var results = new List<object>();
        var sourceWindow = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        AppWindow.Move(new(80, 80));
        AppWindow.Show();
        try
        {
            await Verify(viewer, [sample], broker: true);
            await Verify(viewer, [sample], broker: false);
            await Verify(sevenZip, [archive], broker: true);
            await Verify(sevenZipGui, ["a", "-ad", Path.Combine(root, "new-archive.zip"), sample], broker: false);
        }
        finally { AppWindow.Move(new(-10000, -10000)); }
        return results;

        async Task Verify(string executable, string[] arguments, bool broker)
        {
            ExternalWindowProbe.SetForegroundWindow(sourceWindow);
            await Task.Delay(150);
            var sourceForeground = ExternalWindowProbe.GetForegroundWindow() == sourceWindow;
            Process? child = null;
            nint window = 0;
            try
            {
                await ShellOperationWorker.RunAsync(() =>
                {
                    if (broker) DetachedProcess.Open(executable, string.Join(' ', arguments.Select(a => "\"" + a + "\"")));
                    else DetachedProcess.Start(executable, arguments);
                });
                for (var attempt = 0; attempt < 160 && window == 0; attempt++)
                {
                    foreach (var candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
                    {
                        if (string.Equals(candidate.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        { child?.Dispose(); child = candidate; window = ExternalWindowProbe.VisibleWindow(child.Id); }
                        else candidate.Dispose();
                    }
                    if (window == 0) await Task.Delay(50);
                }
                if (child is null || window == 0) throw new InvalidOperationException("No visible window: " + executable);
                await Task.Delay(600);
                var owner = ExternalWindowProbe.GetWindow(window, 4);
                var extendedStyle = ExternalWindowProbe.GetWindowLongPtr(window, -20).ToInt64();
                var toolWindow = (extendedStyle & 0x80) != 0;
                var foreground = ExternalWindowProbe.GetForegroundWindow() == window;
                results.Add(new { App = Path.GetFileName(executable), Route = broker ? "Explorer broker" : "Detached process",
                    SourceForeground = sourceForeground, TargetForeground = foreground, Owner = owner.ToInt64(), ToolWindow = toolWindow,
                    TaskbarEligible = !toolWindow && (owner == 0 || (extendedStyle & 0x40000) != 0) });
                if (owner == sourceWindow || toolWindow) throw new InvalidOperationException("External window attached to FilesMate: " + executable);
                if (sourceForeground && !foreground) throw new InvalidOperationException("External window did not activate: " + executable);
            }
            finally
            {
                if (child is not null)
                {
                    // Only close the isolated copy launched above, never an existing user app.
                    if (!child.HasExited && window != 0) ExternalWindowProbe.PostMessage(window, 0x10, 0, 0);
                    for (var i = 0; i < 100 && !child.HasExited; i++) await Task.Delay(50);
                    child.Dispose();
                }
            }
        }
    }

    private static class ExternalWindowProbe
    {
        private delegate bool EnumWindowsCallback(nint window, nint parameter);
        internal static nint VisibleWindow(int processId)
        {
            nint found = 0;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var pid);
                if (pid != processId || !IsWindowVisible(window)) return true;
                found = window;
                return false;
            }, 0);
            return found;
        }
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
        [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    }
}
#endif
