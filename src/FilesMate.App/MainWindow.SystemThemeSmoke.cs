#if FILESMATE_UI_TEST
using System.Text.Json;

using FilesMate.App.Models;
using FilesMate.App.Theming;

using Microsoft.UI.Xaml;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunSystemThemeSmokeAsync()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "system-theme-smoke.json");
        try
        {
            await Task.Delay(600);
            var root = (FrameworkElement)Content;
            await App.AppearanceViewModel!.SetThemeAsync(AppThemeKind.Light);
            await Task.Delay(250);
            var light = root.ActualTheme;
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.System);
            await Task.Delay(250);
            var expected = SystemThemeResolver.ResolveElementTheme();
            var followed = root.ActualTheme;
            if (light != ElementTheme.Light || followed != expected)
            {
                throw new InvalidOperationException($"Theme transition mismatch: light={light}, followed={followed}, expected={expected}.");
            }

            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Passed = true,
                Light = light.ToString(),
                FollowSystem = followed.ToString(),
                Expected = expected.ToString(),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Passed = false,
                Error = error.ToString(),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
#endif
