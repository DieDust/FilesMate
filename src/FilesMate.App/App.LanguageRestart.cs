using System.Diagnostics;
using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.Platform.Windows.Processes;

namespace FilesMate.App;

public partial class App
{
    private bool _languageRestartPending;

    internal async Task RestartForLanguageAsync()
    {
        if (_languageRestartPending) throw new InvalidOperationException("A language restart is already pending.");
        _languageRestartPending = true;
        string? handoff = null;
        try
        {
            while (FileOperationLifetime.IsBusy) await FileOperationLifetime.WhenIdleAsync();
            if (_windows.Count == 0) return;
            var profile = Path.GetDirectoryName(Program.SettingsPath(LanguageSettings.DefaultFilePath))!;
            await GlobalSearchService.RestartForLanguageAsync(profile);
            // File work may have started while the independent host was restarting.
            while (FileOperationLifetime.IsBusy) await FileOperationLifetime.WhenIdleAsync();
            if (_windows.Count == 0) return;
            var windows = _windows.OrderBy(w => ReferenceEquals(w, CurrentWindow)).ToArray();
            using var process = Process.GetCurrentProcess();
            var state = new LanguageRestartSession(Environment.ProcessId, process.StartTime.ToUniversalTime().Ticks,
                windows.Select(w => w.CaptureSession()).ToArray());
            var token = state.Save(profile);
            handoff = LanguageRestartSession.FilePath(profile, token);
            var arguments = new List<string> { "--language-restart", token };
#if FILESMATE_UI_TEST
            if (Environment.GetEnvironmentVariable("FILESMATE_LANGUAGE_RESTART_SMOKE") == "1"
                || Environment.GetCommandLineArgs().Contains("--language-restart-smoke")) arguments.Add("--language-restart-smoke");
#endif
            arguments.Add(Program.UseNativeShellHost ? "--native-shell" : "--no-native-shell");
            // The successor waits for this process to exit before acquiring the single-instance mutex.
            DetachedProcess.Start(Environment.ProcessPath!, arguments);
            foreach (var window in windows) window.Close();
        }
        catch
        {
            if (handoff is not null && File.Exists(handoff)) File.Delete(handoff);
            _languageRestartPending = false;
            throw;
        }
    }
}
