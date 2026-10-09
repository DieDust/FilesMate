using System.Runtime.InteropServices;
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
    private readonly ViewManagementSubscription _effectsSubscription;
    private readonly ViewManagementSubscription _contrastSubscription;
    private readonly ViewManagementSubscription _colorsSubscription;
    private BackdropKind? _kind;
    private readonly Windows.UI.ViewManagement.UISettings _systemSettings = new();
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();
    internal SolidColorBrush TintBrush { get; } = new();
    internal bool HasSystemBackdrop => _material.SystemBackdrop is not null;

    public FrostedBackdrop()
    {
        _effectsSubscription = new(
            () => _systemSettings.AdvancedEffectsEnabledChanged += SystemEffectsChanged,
            () => _systemSettings.AdvancedEffectsEnabledChanged -= SystemEffectsChanged);
        _contrastSubscription = new(
            () => _accessibility.HighContrastChanged += SystemContrastChanged,
            () => _accessibility.HighContrastChanged -= SystemContrastChanged);
        _colorsSubscription = new(
            () => _systemSettings.ColorValuesChanged += SystemEffectsChanged,
            () => _systemSettings.ColorValuesChanged -= SystemEffectsChanged);
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
            if (!_listening)
            {
                App.AppearanceChanged += AppearanceChanged;
                _listening = true;
                try
                {
                    _effectsSubscription.Start();
                    _contrastSubscription.Start();
                    // Color changes retain system/high-contrast refresh when this
                    // desktop host cannot provide one of the dedicated events.
                    if (_effectsSubscription.IsUnavailable || _contrastSubscription.IsUnavailable)
                        _colorsSubscription.Start();
                }
                catch
                {
                    StopListening();
                    throw;
                }
            }
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            StopListening();
            _material.SystemBackdrop = null;
            _kind = null;
        };
        ActualThemeChanged += (_, _) => Refresh();
    }

    private void StopListening()
    {
        try { _colorsSubscription.Stop(); }
        finally
        {
            try { _contrastSubscription.Stop(); }
            finally
            {
                try { _effectsSubscription.Stop(); }
                finally
                {
                    if (_listening) App.AppearanceChanged -= AppearanceChanged;
                    _listening = false;
                }
            }
        }
    }

    private void AppearanceChanged(object? sender, AppearanceSettings settings) => Refresh(settings);
    private void SystemEffectsChanged(Windows.UI.ViewManagement.UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => Refresh());
    private void SystemContrastChanged(Windows.UI.ViewManagement.AccessibilitySettings sender, object args) => DispatcherQueue.TryEnqueue(() => Refresh());

    private void Refresh(AppearanceSettings? preview = null)
    {
        var settings = preview ?? App.AppearanceViewModel?.Effective ?? AppearanceSettings.Default;
        var theme = ContentDialogTheme.Resolve(this);
        var highContrast = GlassEnvironment.HighContrast;
        TintBrush.Color = highContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : FloatingColor(theme == ElementTheme.Dark, settings);
        var effective = EffectiveBackdrop(settings);
        TintBrush.Opacity = effective == BackdropKind.Solid ? 1
            : GlassMaterialPolicy.FloatingCoverage(GlassSceneState.Resolve(settings).SurfaceOpacity);
        var kind = IsLoaded && Visibility == Visibility.Visible && !highContrast ? EffectiveBackdrop(settings) : BackdropKind.Solid;
        _material.RequestedTheme = theme;
        if (_kind == kind) return;
        _material.SystemBackdrop = CreateBackdrop(kind);
        _kind = kind;
    }

    internal static Color FloatingColor(bool dark, AppearanceSettings? settings = null) => Themes.ShellStyleResources.FromArgb(
        SkinPalette.For(dark, settings ?? App.AppearanceViewModel?.Effective ?? AppearanceSettings.Default).Floating);
    internal static BackdropKind EffectiveBackdrop(AppearanceSettings settings) => settings.GlassEffect == GlassEffectMode.Off
        || settings.EffectiveTransparencyPercent == 0 || !GlassEnvironment.TransparencyEnabled ? BackdropKind.Solid : settings.Backdrop;
    internal static SystemBackdrop? CreateBackdrop(BackdropKind kind) => kind switch
    {
        BackdropKind.Solid => null,
        BackdropKind.Mica => new MicaBackdrop { Kind = MicaKind.Base },
        BackdropKind.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
        _ => new DesktopAcrylicBackdrop(),
    };
}

// This desktop host can report ERROR_NOT_FOUND while registering a WinRT
// view-management event. Keep successful registrations independent so a
// partial start neither duplicates handlers nor revokes an absent event.
internal sealed class ViewManagementSubscription(Action subscribe, Action unsubscribe)
{
    internal bool IsSubscribed { get; private set; }
    internal bool IsUnavailable { get; private set; }
    private const int EventUnavailable = unchecked((int)0x80070490);

    internal void Start()
    {
        if (IsSubscribed || IsUnavailable) return;
        try
        {
            subscribe();
            IsSubscribed = true;
        }
        catch (COMException error) when (error.HResult == EventUnavailable)
        {
            IsUnavailable = true;
            System.Diagnostics.Trace.TraceWarning("View-management event unavailable: {0}", error);
        }
    }

    internal void Stop()
    {
        if (!IsSubscribed) return;
        try
        {
            unsubscribe();
            IsSubscribed = false;
        }
        catch (COMException error) when (error.HResult == EventUnavailable)
        {
            // The source no longer has this event; there is no token to revoke.
            IsSubscribed = false;
            IsUnavailable = true;
            System.Diagnostics.Trace.TraceWarning("View-management event already unavailable: {0}", error);
        }
    }
}
