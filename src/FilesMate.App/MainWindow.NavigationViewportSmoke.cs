#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Views;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunNavigationViewportSmokeAsync()
    {
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(2000, 1400));
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { OpenFoldersInNewTab = false, ShowAlphabetNavigation = false });
            // Let the page's queued startup navigation finish before the fixture
            // navigates away. Other settings fixtures may choose a startup folder.
            for (var i = 0; i < 200; i++)
            {
                if (TabHost.Content is NavigatorPage { IsLoaded: true } page && !page.ViewModel.IsLoading
                    && page.ViewModel.Navigation.CurrentPath is { Length: > 0 }) break;
                await Task.Delay(50);
            }
            await ((NavigatorPage)TabHost.Content).RunNavigationViewportSmokeAsync();
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "navigation-viewport-smoke.json"),
                JsonSerializer.Serialize(new { Passed = false, Error = error.ToString() }));
        }
    }
}
#endif
