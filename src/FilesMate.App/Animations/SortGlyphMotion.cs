using System.Runtime.CompilerServices;

using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace FilesMate.App.Animations;

/// <summary>Direction feedback for a header glyph; never owns sorting or input.</summary>
internal sealed class SortGlyphMotion : IDisposable
{
    // Weak keys keep bindings local to their glyphs, including disposed bindings:
    // a late state application cannot recreate motion on a released surface.
    private static readonly ConditionalWeakTable<FrameworkElement, SortGlyphMotion> Bindings = new();
    private readonly FrameworkElement _glyph;
    private readonly RotateTransform _rotation;
    private UISettings? _systemSettings;
    private Storyboard? _storyboard;
    private bool _loaded, _initialized, _active, _ascending, _watchingSettings, _disposed;
    private long _generation;

    private SortGlyphMotion(FrameworkElement glyph)
    {
        _glyph = glyph;
        _rotation = glyph.RenderTransform as RotateTransform ?? new RotateTransform();
        _glyph.RenderTransformOrigin = new Point(0.5, 0.5);
        _glyph.RenderTransform = _rotation;
        _glyph.Loaded += Glyph_Loaded;
        _glyph.Unloaded += Glyph_Unloaded;
        _loaded = _glyph.IsLoaded;
    }

    public static void Apply(FrameworkElement glyph, bool active, bool ascending) =>
        Bindings.GetValue(glyph, static target => new SortGlyphMotion(target)).SetState(active, ascending);

    public static void Release(FrameworkElement glyph) =>
        Bindings.GetValue(glyph, static target => new SortGlyphMotion(target)).Dispose();

    private double TargetAngle => _active && !_ascending ? 180d : 0d;

    private void SetState(bool active, bool ascending)
    {
        if (_disposed || (_initialized && _active == active && (!active || _ascending == ascending))) return;
        // Initial state, a different active column, and unloaded controls restore
        // immediately. Only a real direction change of this visible state rotates.
        var animate = _initialized && _active && active && _loaded;
        _initialized = true;
        _active = active;
        _ascending = ascending;
        // Capture the effective animated angle before Stop removes the old clock.
        var from = _rotation.Angle;
        StopAnimation();
        _rotation.Angle = from;
        var duration = App.Motion.Resolve(MotionDurations.Fast);
        if (!animate || from == TargetAngle || duration <= TimeSpan.Zero)
        {
            Settle();
            return;
        }

        var animation = new DoubleAnimation
        {
            From = from,
            To = TargetAngle,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        // Render-transform interpolation only; no dependent animation or layout.
        Storyboard.SetTarget(animation, _rotation);
        Storyboard.SetTargetProperty(animation, nameof(RotateTransform.Angle));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += Animation_Completed;
        _storyboard = storyboard;
        WatchSettings();
        // Cover a policy change between duration resolution and subscription.
        if (App.Motion.Resolve(MotionDurations.Fast) <= TimeSpan.Zero) Settle();
        else storyboard.Begin();
    }

    private void Glyph_Loaded(object sender, RoutedEventArgs e)
    {
        if (_disposed || _loaded) return;
        _loaded = true;
        Settle();
    }

    private void Glyph_Unloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        Settle();
    }

    private void Animation_Completed(object? sender, object e)
    {
        if (ReferenceEquals(sender, _storyboard)) Settle();
    }

    private void Settle()
    {
        StopAnimation();
        _rotation.Angle = TargetAngle;
    }

    private void StopAnimation()
    {
        _generation++;
        if (_storyboard is { } storyboard)
        {
            _storyboard = null;
            storyboard.Completed -= Animation_Completed;
            storyboard.Stop();
        }
        if (!_watchingSettings) return;
        _watchingSettings = false;
        App.AppearanceChanged -= Appearance_Changed;
        _systemSettings!.AnimationsEnabledChanged -= System_AnimationsEnabledChanged;
    }

    private void WatchSettings()
    {
        // Global/native publishers retain this binding only during its short turn.
        _systemSettings ??= new UISettings();
        _watchingSettings = true;
        App.AppearanceChanged += Appearance_Changed;
        _systemSettings.AnimationsEnabledChanged += System_AnimationsEnabledChanged;
    }

    private void Appearance_Changed(object? sender, AppearanceSettings settings) => PolicyChanged();

    private void System_AnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs e) => PolicyChanged();

    private void PolicyChanged()
    {
        var generation = _generation;
        void Apply()
        {
            // A queued native notification cannot own a newer/reloaded binding.
            if (_loaded && !_disposed && generation == _generation
                && App.Motion.Resolve(MotionDurations.Fast) <= TimeSpan.Zero) Settle();
        }
        if (_glyph.DispatcherQueue.HasThreadAccess) Apply();
        else _glyph.DispatcherQueue.TryEnqueue(Apply);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loaded = false;
        Settle();
        _glyph.Loaded -= Glyph_Loaded;
        _glyph.Unloaded -= Glyph_Unloaded;
    }
}
