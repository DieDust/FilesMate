using FilesMate.App.Animations;
using FilesMate.App.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Glass;

public enum GlassSurfaceKind
{
    Chrome,
    Sidebar,
    Command,
    Card,
    Preview,
    Dialog,
}

public sealed class LiquidGlassSurface : ContentControl
{
    public static readonly DependencyProperty SurfaceKindProperty = DependencyProperty.Register(
        nameof(SurfaceKind),
        typeof(GlassSurfaceKind),
        typeof(LiquidGlassSurface),
        new PropertyMetadata(GlassSurfaceKind.Card, OnSurfaceKindChanged));

    private GlassSceneController? _controller;
    private FrameworkElement? _innerHighlight;
    private FrameworkElement? _materialLayer;
    private FrameworkElement? _solidLayer;
    private FrameworkElement? _staticSheen;
    private GlassSceneState _state = GlassSceneState.Resolve(
        GlassEffectMode.Balanced,
        systemAnimationsEnabled: true,
        windowActive: true,
        remoteSession: false);

    public LiquidGlassSurface()
    {
        Loaded += LiquidGlassSurface_Loaded;
        Unloaded += LiquidGlassSurface_Unloaded;
    }

    public GlassSurfaceKind SurfaceKind
    {
        get => (GlassSurfaceKind)GetValue(SurfaceKindProperty);
        set => SetValue(SurfaceKindProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _solidLayer = GetTemplateChild("PART_SolidLayer") as FrameworkElement;
        _materialLayer = GetTemplateChild("PART_MaterialLayer") as FrameworkElement;
        _innerHighlight = GetTemplateChild("PART_InnerHighlight") as FrameworkElement;
        _staticSheen = GetTemplateChild("PART_StaticSheen") as FrameworkElement;
        ApplySceneState(_state);
    }

    internal void AttachScene(GlassSceneController controller, GlassSceneState state)
    {
        _controller = controller;
        ApplySceneState(state);
    }

    internal void DetachScene(GlassSceneController controller)
    {
        if (ReferenceEquals(_controller, controller))
        {
            _controller = null;
        }
    }

    internal void ApplySceneState(GlassSceneState state)
    {
        _state = state;
        var connected = SurfaceKind is GlassSurfaceKind.Chrome
            or GlassSurfaceKind.Sidebar
            or GlassSurfaceKind.Command;

        // Connected chrome uses the brush set on the control. The default card
        // template otherwise paints an opaque solid layer that reads as a
        // nested panel.
        if (_solidLayer is not null)
        {
            // The shared material brush already supplies its opaque fallback.
            // A second solid layer would replace card-specific colors when glass is off.
            _solidLayer.Opacity = 0;
        }

        if (_materialLayer is not null)
        {
            // Acrylic tint is controlled centrally alongside menus and floating
            // panels. Fading it again would make the same material look different.
            _materialLayer.Opacity = 1;
        }

        if (_innerHighlight is not null)
        {
            _innerHighlight.Opacity = connected ? 0 : state.HighlightOpacity * 0.72;
        }

        if (_staticSheen is not null)
        {
            _staticSheen.Opacity = connected ? 0 : state.HighlightOpacity * 0.55;
        }
    }

    private static void OnSurfaceKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LiquidGlassSurface surface)
        {
            surface.ApplySceneState(surface._state);
        }
    }

    private void LiquidGlassSurface_Loaded(object sender, RoutedEventArgs e)
    {
        if (_controller is null && GlassSceneController.TryGet(XamlRoot, out var controller))
        {
            controller!.RegisterSurface(this);
        }
    }

    private void LiquidGlassSurface_Unloaded(object sender, RoutedEventArgs e) =>
        _controller?.UnregisterSurface(this);
}
