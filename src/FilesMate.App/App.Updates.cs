using FilesMate.App.Updates;
using FilesMate.App.Services;
using Microsoft.UI.Xaml;

namespace FilesMate.App;

public partial class App
{
    internal static UpdateController Updates { get; } = new();

    private async Task CheckForUpdatesAfterStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        try
        {
            if (CurrentWindow is null) return;
            var release = await Updates.CheckAsync(manual: false);
            if (release is not null) CurrentWindow?.ShowUpdateNotice(release);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            // Offline startup is silent; About offers an explicit retry with feedback.
            LogFailure("UpdateCheck", error);
        }
    }

    internal static bool CanInstallUpdate() => !FileOperationLifetime.IsBusy
        && Current is App app && app._windows.All(window => window.CanInstallUpdate);

    internal static void CloseForUpdate()
    {
        if (Current is not App app) return;
        foreach (var window in app._windows.ToArray()) window.Close();
    }
}
