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
        if (popup is Flyout { FlyoutPresenterStyle: null } rounded
            && Application.Current.Resources.TryGetValue("FilesMate.RoundedFlyoutPresenterStyle", out var value)
            && value is Style roundedStyle)
            rounded.FlyoutPresenterStyle = roundedStyle;
        var original = popup is Flyout initialFlyout ? initialFlyout.FlyoutPresenterStyle
            : (popup as MenuFlyout)?.MenuFlyoutPresenterStyle;
        MainWindow? inputWindow = null;
        void Refresh()
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
                if (flyout.Content is FrameworkElement content) { content.RequestedTheme = theme; AppTypography.Apply(content); }
            }
            else if (popup is MenuFlyout menu)
            {
                menu.MenuFlyoutPresenterStyle = WithTheme(typeof(MenuFlyoutPresenter), original, theme, background);
                ApplyMenuTheme(menu.Items, theme);
            }
        }
        void Changed(object? sender, AppearanceSettings settings) => Refresh();
        popup.Opening += (_, _) =>
        {
            inputWindow = popup.Target is FrameworkElement target ? App.WindowForElement(target) : null;
            inputWindow?.SetPopupInputActive(popup, true);
            App.AppearanceChanged -= Changed;
            App.AppearanceChanged += Changed;
            Refresh();
        };
        popup.Opened += (_, _) =>
        {
            if (popup.Target?.XamlRoot is { } root)
                foreach (var child in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
                {
                    AppTypography.Apply(child.Child);
                }
        };
        popup.Closed += (_, _) =>
        {
            App.AppearanceChanged -= Changed;
            popup.SystemBackdrop = null;
            inputWindow?.SetPopupInputActive(popup, false);
            inputWindow = null;
        };
    }

    internal static void RefreshOpenPopups(FrameworkElement root)
    {
        if (root.XamlRoot is null) return;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot))
            if (popup.Child is FrameworkElement child) { child.RequestedTheme = ContentDialogTheme.Resolve(root); AppTypography.Apply(child); }
    }

    private static void ApplyMenuTheme(IEnumerable<MenuFlyoutItemBase> items, ElementTheme theme)
    {
        foreach (var item in items)
        {
            item.RequestedTheme = theme;
            item.FontFamily = AppTypography.Family;
            if (item is ToggleMenuFlyoutItem && item.Style is null
                && Application.Current.Resources.TryGetValue("FilesMate.ToggleMenuFlyoutItemStyle", out var style) && style is Style toggleStyle)
                item.Style = toggleStyle;
            if (item is MenuFlyoutSubItem submenu) ApplyMenuTheme(submenu.Items, theme);
        }
    }


    private static Style WithTheme(Type type, Style? original, ElementTheme theme, Brush? background)
    {
        var style = new Style(type) { BasedOn = original };
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, AppTypography.Family));
        if (background is not null) style.Setters.Add(new Setter(Control.BackgroundProperty, background));
        return style;
    }
}
