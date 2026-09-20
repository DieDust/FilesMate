using FilesMate.App.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Theming;

/// <summary>
/// WinUI ContentDialog paints from Application.RequestedTheme, which is frozen after
/// the first window. Force Light/Dark from FilesMate's setting and the host window.
/// </summary>
internal static class ContentDialogTheme
{
    public static void Apply(ContentDialog dialog, FrameworkElement? host = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        host ??= dialog.XamlRoot?.Content as FrameworkElement;
        var theme = Resolve(host);
        Assign(dialog, theme);
        dialog.Opened += (_, _) => Assign(dialog, theme);
    }

    internal static ElementTheme Resolve(FrameworkElement? host)
    {
        var setting = App.AppearanceViewModel?.Current.Theme;
        if (setting == AppThemeKind.Light)
        {
            return ElementTheme.Light;
        }

        if (setting == AppThemeKind.Dark)
        {
            return ElementTheme.Dark;
        }

        if (host is not null)
        {
            if (host.RequestedTheme is ElementTheme.Light or ElementTheme.Dark)
            {
                return host.RequestedTheme;
            }

            if (host.ActualTheme is ElementTheme.Light or ElementTheme.Dark)
            {
                return host.ActualTheme;
            }
        }

        return SystemThemeResolver.ResolveElementTheme();
    }

    private static void Assign(ContentDialog dialog, ElementTheme theme)
    {
        dialog.RequestedTheme = theme;
        if (dialog.Content is FrameworkElement content)
        {
            content.RequestedTheme = theme;
        }
    }
}
