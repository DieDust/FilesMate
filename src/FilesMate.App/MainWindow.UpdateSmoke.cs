#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Services;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunUpdateSmokeAsync()
    {
        if (Environment.GetEnvironmentVariable("FILESMATE_UPDATE_DEMO") == "1")
        {
            await RunUpdateDemoAsync();
            return;
        }
        var results = new Dictionary<string, object>();
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            for (int i = 0; i < 150 && !CanInstallUpdate; i++) await Task.Delay(100);
            await Task.Delay(500);
            var release = await App.Updates.CheckAsync(manual: true) ?? throw new Exception("Public feed did not offer fixture update");
            results["FeedSignature"] = "Passed";
            using (FileOperationLifetime.Begin())
            {
                try { await App.Updates.InstallAsync(release, "not-used.exe"); throw new Exception("Busy file work allowed update"); }
                catch (InvalidOperationException) { results["FileWorkProtected"] = "Passed"; }
            }
            ShowUpdateNotice(release);
            if (_updateNotice is not { Visibility: Visibility.Visible } || _updateNotice.FindName("InstallButton") is not Button) throw new Exception("Update prompt has no action");
            results["UpdatePrompt"] = "Passed";
            var invoked = false;
            App.Updates.TestInstallerStart = start =>
            {
                if (!File.Exists(start.FileName) || !start.ArgumentList.Contains("/FILESMATEUPDATE=1")
                    || !start.ArgumentList.Contains("/DIR=" + AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))
                    throw new Exception("Unsafe installer handoff");
                invoked = true;
                results["DownloadedBytes"] = new FileInfo(start.FileName).Length;
            };
            await DownloadAndInstallUpdateAsync(release);
            if (!invoked) throw new Exception("Installer handoff was not reached");
            results["DownloadHashAndHandoff"] = "Passed";
            var about = new AboutPage();
            if (about.FindName("AutoUpdateCheck") is not ToggleSwitch || about.FindName("CheckUpdateButton") is not Button)
                throw new Exception("Update settings missing");
            results["SettingsControls"] = "Passed";
            results["InstallerActuallyLaunched"] = false;
            results["Passed"] = true;
        }
        catch (Exception error) { results["Error"] = error.ToString(); results["Passed"] = false; }
        finally { App.Updates.TestInstallerStart = null; }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "update-smoke.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RunUpdateDemoAsync()
    {
        try
        {
            Title = "FilesMate 更新交互演示";
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 1050));
            await App.AppearanceViewModel!.SetThemeAsync(Models.AppThemeKind.Light);
            await App.AppearanceViewModel.SetBackdropAsync(Models.BackdropKind.Solid);
            App.Updates.SetAutomatic(false);
            // Demonstrate the shipped controls without downloading or installing anything.
            App.Updates.TestDownload = async (progress, cancellation) =>
            {
                for (var i = 0; ; i = (i + 1) % 91)
                {
                    progress.Report(i / 100d);
                    await Task.Delay(180, cancellation);
                }
            };
            var release = new FilesMate.Core.Updates.UpdateRelease(1, "FilesMate", "1.1.56.0",
                "1.1.56-preview.20260916", "FilesMate-Setup-1.1.56-preview.20260916-win-x64.exe",
                84960518, new string('0', 64), "优化批量重命名与标签界面；修复浅色主题、右键菜单及主题切换。");
            await Task.Delay(5000);
            ShowUpdateNotice(release);
            _updateNotice!.ShowDemoLabel();
            await Task.Delay(600);
            await Capture((UIElement)Content, "update-demo-notice.png");
            var downloading = DownloadAndInstallUpdateAsync(release);
            await Task.Delay(1000);
            var dialog = _updateDemoDialog ?? throw new InvalidOperationException("Download dialog was not created.");
            await Capture(FindDescendant<Border>(dialog, b => b.Name == "BackgroundElement") ?? (UIElement)dialog, "update-demo-download.png");
            var cancel = FindDescendant<Button>(dialog, b => b.Name == "CancelButton")!;
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(cancel)
                .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await downloading;
            if (_updateDialogOpen || _updateNotice?.Visibility != Visibility.Visible) throw new InvalidOperationException("Cancellation did not restore the update entry.");
            await App.AppearanceViewModel.SetThemeAsync(Models.AppThemeKind.Dark);
            await Task.Delay(500);
            await Capture((UIElement)Content, "update-demo-notice-dark.png");
            var darkDownload = DownloadAndInstallUpdateAsync(release);
            await Task.Delay(700);
            dialog = _updateDemoDialog!;
            await Capture(FindDescendant<Border>(dialog, b => b.Name == "BackgroundElement") ?? (UIElement)dialog, "update-demo-download-dark.png");
            dialog.Hide();
            await darkDownload;
            await App.AppearanceViewModel.SetThemeAsync(Models.AppThemeKind.Light);
            App.Updates.TestDownload = (_, _) => Task.FromException<string>(new IOException("Demonstration of offline recovery"));
            var failure = DownloadAndInstallUpdateAsync(release);
            await Task.Delay(500);
            dialog = _updateDemoDialog!;
            if (FindDescendant<TextBlock>(dialog, t => t.Text.Contains("原版本可以继续使用")) is null)
                throw new InvalidOperationException("Download failure was not shown.");
            dialog.Hide();
            await failure;
            App.Updates.TestDownload = async (progress, token) =>
            {
                for (var i = 0; ; i = (i + 1) % 91) { progress.Report(i / 100d); await Task.Delay(180, token); }
            };
            ShowUpdateNotice(release);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "update-demo.done"), "Notice and download captured. Demo cannot install.");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "update-demo.error"), error.ToString());
        }
    }
}
#endif
