using FilesMate.App.Services;

namespace FilesMate.App;

public partial class App
{
    // A committed appearance choice must survive closing the last window, a
    // language restart, and update installation, just like queued file work.
    internal static bool HasPendingUserWork => FileOperationLifetime.IsBusy
        || AppearanceViewModel?.HasPendingCommits == true;

    internal static async Task WhenPendingUserWorkIdleAsync()
    {
        do
        {
            if (FileOperationLifetime.IsBusy) await FileOperationLifetime.WhenIdleAsync();
            if (AppearanceViewModel is { HasPendingCommits: true } appearance)
                await appearance.WhenIdleAsync();
        }
        while (HasPendingUserWork);
    }
}
