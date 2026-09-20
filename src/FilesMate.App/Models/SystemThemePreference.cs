namespace FilesMate.App.Models;

public static class SystemThemePreference
{
    public static AppThemeKind FromAppsUseLightTheme(object? value, AppThemeKind fallback = AppThemeKind.Light)
    {
        if (value is null)
        {
            return fallback is AppThemeKind.Light or AppThemeKind.Dark ? fallback : AppThemeKind.Light;
        }

        try
        {
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 0
                ? AppThemeKind.Dark
                : AppThemeKind.Light;
        }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException)
        {
            return fallback is AppThemeKind.Light or AppThemeKind.Dark ? fallback : AppThemeKind.Light;
        }
    }
}
