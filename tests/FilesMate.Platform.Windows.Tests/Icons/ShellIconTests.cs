using System.Runtime.InteropServices;
using FilesMate.Core.Icons;
using FilesMate.Platform.Windows.Icons;

namespace FilesMate.Platform.Windows.Tests.Icons;

public sealed class ShellIconTests
{
    [Fact]
    public async Task Canceled_extraction_does_not_poison_later_requests_for_same_icon()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var key = IconKey.ForPath(path, false, 64);
        var service = new WindowsSystemIconService();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetAsync(key, path, FileAttributes.Normal, false, canceled.Token));
        var icon = await service.GetAsync(key, path, FileAttributes.Normal, false, default);
        Assert.NotNull(icon);
    }

    [Fact]
    public async Task Background_extraction_resolves_executable_and_shortcut_icons()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-icon-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var linkPath = Path.Combine(root, "命令提示符.lnk");
        // WScript's writer uses the system ANSI code page. Create an ASCII-named
        // fixture, then rename it with Unicode filesystem APIs so extraction is
        // still tested with a Chinese path on every Windows display language.
        var stagingLinkPath = Path.Combine(root, "command-prompt.lnk");
        try
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                if (!OperatingSystem.IsWindows()) { ready.SetException(new PlatformNotSupportedException()); return; }
                object? shell = null;
                object? shortcut = null;
                try
                {
                    shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                    shortcut = ((dynamic)shell!).CreateShortcut(stagingLinkPath);
                    ((dynamic)shortcut).TargetPath = target;
                    ((dynamic)shortcut).Save();
                    ready.SetResult();
                }
                catch (Exception error) { ready.SetException(error); }
                finally
                {
                    if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
                    if (shell is not null) Marshal.FinalReleaseComObject(shell);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            await ready.Task;
            File.Move(stagingLinkPath, linkPath);
            var service = new WindowsSystemIconService();
            foreach (var path in new[] { target, linkPath })
            {
                var icon = await service.GetAsync(IconKey.ForPath(path, false, 64), path, FileAttributes.Normal, false, default);
                Assert.NotNull(icon);
                Assert.Contains(Enumerable.Range(0, icon.Width * icon.Height), pixel => icon.Bgra[pixel * 4 + 3] != 0);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
