using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using FilesMate.App.Controls.Glass;
using FilesMate.App.Animations;
using FilesMate.App.Models;

namespace FilesMate.App.Theming;

/// <summary>Popups must explicitly inherit the window theme, not the frozen application theme.</summary>
internal static class FlyoutTheme
{
    public static void FollowHost(FlyoutBase popup)
    {
        var original = popup is Flyout initialFlyout ? initialFlyout.FlyoutPresenterStyle
            : (popup as MenuFlyout)?.MenuFlyoutPresenterStyle;
        popup.Opening += (_, _) =>
        {
            var theme = ContentDialogTheme.Resolve(popup.Target);
            var settings = App.AppearanceViewModel?.Current ?? AppearanceSettings.Default;
            var highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
            popup.SystemBackdrop = highContrast ? null : FrostedBackdrop.CreateBackdrop(FrostedBackdrop.EffectiveBackdrop(settings));
            Brush? background = highContrast ? null : new SolidColorBrush(FrostedBackdrop.FloatingColor(theme == ElementTheme.Dark))
            { Opacity = GlassMaterialPolicy.FloatingCoverage(GlassSceneState.Resolve(settings).SurfaceOpacity) };
            if (popup is Flyout flyout)
            {
                flyout.FlyoutPresenterStyle = WithTheme(typeof(FlyoutPresenter), original, theme, background);
                if (flyout.Content is FrameworkElement content) content.RequestedTheme = theme;
            }
            else if (popup is MenuFlyout menu)
            {
                menu.MenuFlyoutPresenterStyle = WithTheme(typeof(MenuFlyoutPresenter), original, theme, background);
                foreach (var item in menu.Items) item.RequestedTheme = theme;
            }
        };
        popup.Closed += (_, _) => popup.SystemBackdrop = null;
    }

    private static Style WithTheme(Type type, Style? original, ElementTheme theme, Brush? background)
    {
        var style = new Style(type) { BasedOn = original };
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
        if (background is not null) style.Setters.Add(new Setter(Control.BackgroundProperty, background));
        return style;
    }
}
