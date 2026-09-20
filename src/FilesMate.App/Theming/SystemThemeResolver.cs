using FilesMate.App.Models;

using Microsoft.Win32;
using Microsoft.UI.Xaml;

using Windows.UI.ViewManagement;

namespace FilesMate.App.Theming;

internal static class SystemThemeResolver
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static AppThemeKind Resolve()
    {
        var fallback = ResolveFromSystemColors();
        try
        {
            return SystemThemePreference.FromAppsUseLightTheme(
                Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", null),
                fallback);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return fallback;
        }
    }

    public static ApplicationTheme ResolveApplicationTheme() =>
        Resolve() == AppThemeKind.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

    public static ElementTheme ResolveElementTheme() =>
        Resolve() == AppThemeKind.Dark ? ElementTheme.Dark : ElementTheme.Light;

    private static AppThemeKind ResolveFromSystemColors()
    {
        try
        {
            var background = new UISettings().GetColorValue(UIColorType.Background);
            var luminance = 0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B;
            return luminance < 128 ? AppThemeKind.Dark : AppThemeKind.Light;
        }
        catch
        {
            return AppThemeKind.Light;
        }
    }
}
