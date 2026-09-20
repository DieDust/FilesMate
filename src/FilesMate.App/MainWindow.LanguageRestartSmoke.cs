#if FILESMATE_UI_TEST
using System.Globalization;
using System.Text.Json;
using FilesMate.App.Services;
using FilesMate.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private static bool _languageRestartSmokeStarted;

    private async Task RunLanguageRestartSmokeAsync()
    {
        if (_languageRestartSmokeStarted) return;
        _languageRestartSmokeStarted = true;
        var profile = Path.Combine(AppContext.BaseDirectory, "test-profile");
        var expectedPath = Path.Combine(profile, "language-restart-expected.json");
        var report = Path.Combine(profile, "language-restart-result.json");
        try
        {
            await Task.Delay(1000);
            if (Program.LanguageRestart is { } restored)
            {
                var expected = JsonSerializer.Deserialize<RestartExpectation>(File.ReadAllText(expectedPath))!;
                var session = CaptureSession();
                if (!session.Tabs.SequenceEqual(expected.Session.Tabs) || session.SelectedTabIndex != expected.Session.SelectedTabIndex)
                    throw new InvalidOperationException("Open tabs or selected tab were not restored.");
                if (App.ExplorerPreferences.RestoreLastSession) throw new InvalidOperationException("Normal session preference changed.");
                if (CultureInfo.CurrentUICulture.Name != "ja-JP") throw new InvalidOperationException("Language did not apply.");
                var host = await SearchHostClient.StatusAsync(profile);
                if (expected.SearchPid != 0 && (host is null || host.Pid == expected.SearchPid))
                    throw new InvalidOperationException("Background search did not restart.");
                if (expected.SearchPid == 0 && host is not null) throw new InvalidOperationException("Background search was unexpectedly enabled.");
                File.WriteAllText(report, JsonSerializer.Serialize(new { Passed = true, PreviousPid = restored.ParentPid,
                    Pid = Environment.ProcessId, Language = CultureInfo.CurrentUICulture.Name, Session = session,
                    SearchBefore = expected.SearchPid, SearchAfter = host?.Pid ?? 0, RestoreLastSession = false, WaitedForFileWork = true }));
                return;
            }

            var fixture = Path.Combine(AppContext.BaseDirectory, "restart-fixture");
            Directory.CreateDirectory(fixture);
            AddNavigatorTab(fixture);
            AddNavigatorTab(fixture); // Duplicate locations must survive the one-use handoff.
            Tabs.SelectedItem = Tabs.TabItems[1];
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { RestoreLastSession = false });
            var before = await SearchHostClient.StatusAsync(profile);
            File.WriteAllText(expectedPath, JsonSerializer.Serialize(new RestartExpectation(CaptureSession(), before?.Pid ?? 0)));
            OpenSettings("general");
            await Task.Delay(600);
            var box = Descendants(_settingsPage!).OfType<ComboBox>().Single(e => e.Name == "LanguageBox");
            using (FileOperationLifetime.Begin())
            {
                box.SelectedItem = box.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "ja-JP");
                await Task.Delay(750);
                if (!FileOperationLifetime.IsBusy || _windowClosed || box.IsEnabled)
                    throw new InvalidOperationException("Restart did not wait for active file work.");
                var during = await SearchHostClient.StatusAsync(profile);
                if (during?.Pid != before?.Pid) throw new InvalidOperationException("Search restarted before file work finished.");
            }
        }
        catch (Exception error) { File.WriteAllText(report, JsonSerializer.Serialize(new { Passed = false, Error = error.ToString() })); }

        static IEnumerable<DependencyObject> Descendants(DependencyObject node)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }

    private sealed record RestartExpectation(WindowSession Session, int SearchPid);
}
#endif
