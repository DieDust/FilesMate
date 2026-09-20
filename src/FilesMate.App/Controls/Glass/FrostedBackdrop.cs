using FilesMate.App.Animations;
using FilesMate.App.Models;
using FilesMate.App.Theming;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FilesMate.App.Controls.Glass;

/// <summary>A desktop-sampling material with the same neutral tint as floating menus.</summary>
public sealed class FrostedBackdrop : Grid
{
    private readonly SystemBackdropElement _material = new();
    private readonly Border _tint = new();
    private bool _listening;
    private BackdropKind? _kind;
    internal SolidColorBrush TintBrush { get; } = new();
    internal bool HasSystemBackdrop => _material.SystemBackdrop is not null;

    public FrostedBackdrop()
    {
        IsHitTestVisible = false;
        _tint.Background = TintBrush;
        Children.Add(_material);
        Children.Add(_tint);
        RegisterPropertyChangedCallback(CornerRadiusProperty, (_, _) =>
        {
            _material.CornerRadius = CornerRadius;
            _tint.CornerRadius = CornerRadius;
        });
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Refresh());
        Loaded += (_, _) =>
        {
            if (!_listening) { App.AppearanceChanged += AppearanceChanged; _listening = true; }
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            App.AppearanceChanged -= AppearanceChanged;
            _listening = false;
            _material.SystemBackdrop = null;
            _kind = null;
        };
        ActualThemeChanged += (_, _) => Refresh();
    }

    private void AppearanceChanged(object? sender, AppearanceSettings settings) => Refresh(settings);

    private void Refresh(AppearanceSettings? preview = null)
    {
        var settings = preview ?? App.AppearanceViewModel?.Current ?? AppearanceSettings.Default;
        var theme = ContentDialogTheme.Resolve(this);
        var highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        TintBrush.Color = highContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : FloatingColor(theme == ElementTheme.Dark);
        TintBrush.Opacity = highContrast ? 1 : GlassMaterialPolicy.FloatingCoverage(GlassSceneState.Resolve(settings).SurfaceOpacity);
        var kind = IsLoaded && Visibility == Visibility.Visible && !highContrast ? EffectiveBackdrop(settings) : BackdropKind.Solid;
        _material.RequestedTheme = theme;
        if (_kind == kind) return;
        _material.SystemBackdrop = CreateBackdrop(kind);
        _kind = kind;
    }

    internal static Color FloatingColor(bool dark) => dark ? Color.FromArgb(255, 53, 59, 67) : Color.FromArgb(255, 250, 251, 253);
    internal static BackdropKind EffectiveBackdrop(AppearanceSettings settings) => settings.GlassEffect == GlassEffectMode.Off
        || settings.EffectiveTransparencyPercent == 0 ? BackdropKind.Solid : settings.Backdrop;
    internal static SystemBackdrop? CreateBackdrop(BackdropKind kind) => kind switch
    {
        BackdropKind.Solid => null,
        BackdropKind.Mica => new MicaBackdrop { Kind = MicaKind.Base },
        BackdropKind.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
        _ => new DesktopAcrylicBackdrop(),
    };
}
