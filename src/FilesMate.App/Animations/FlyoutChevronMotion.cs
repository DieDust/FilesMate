using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace FilesMate.App.Animations;

/// <summary>Decorative disclosure feedback; never owns the flyout or its input.</summary>
internal sealed class FlyoutChevronMotion : IDisposable
{
    private readonly FrameworkElement _chevron;
    private readonly FlyoutBase _flyout;
    private readonly RotateTransform _rotation = new();
    private UISettings? _systemSettings;
    private Storyboard? _storyboard;
    private bool _attached, _open, _watchingSettings, _disposed;
    private long _generation;

    public FlyoutChevronMotion(FrameworkElement chevron, FlyoutBase flyout)
    {
        _chevron = chevron;
        _flyout = flyout;
        _chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        _chevron.RenderTransform = _rotation;
        _chevron.Loaded += Chevron_Loaded;
        _chevron.Unloaded += Chevron_Unloaded;
        if (_chevron.IsLoaded) Attach();
    }

    private void Chevron_Loaded(object sender, RoutedEventArgs e) => Attach();

    private void Attach()
    {
        if (_disposed || _attached) return;
        _attached = true;
        _flyout.Opened += Flyout_Opened;
        _flyout.Closed += Flyout_Closed;
        // Reattaching an already-open flyout is state restoration, not an entrance.
        _open = _flyout.IsOpen;
        Settle();
    }

    private void Chevron_Unloaded(object sender, RoutedEventArgs e) => Detach();

    private void Detach()
    {
        _attached = false;
        _flyout.Opened -= Flyout_Opened;
        _flyout.Closed -= Flyout_Closed;
        _open = false;
        Settle();
    }

    private void Flyout_Opened(object? sender, object e)
    {
        if (_flyout.IsOpen) SetOpen(true);
    }

    private void Flyout_Closed(object? sender, object e)
    {
        // Ignore a delayed close notification if the same flyout has reopened.
        if (!_flyout.IsOpen) SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        if (!_attached || _disposed || _open == open) return;
        _open = open;
        // Read the current effective animated value before stopping, so a rapid
        // reversal continues from the visible angle rather than from an endpoint.
        var from = _rotation.Angle;
        StopAnimation();
        _rotation.Angle = from;
        var duration = App.Motion.Resolve(MotionDurations.Fast);
        if (duration <= TimeSpan.Zero)
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
        // RenderTransform is independently animated: no layout, frame handler,
        // dependent-animation opt-in, delay, or work on the command path.
        Storyboard.SetTarget(animation, _rotation);
        Storyboard.SetTargetProperty(animation, nameof(RotateTransform.Angle));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += Animation_Completed;
        _storyboard = storyboard;
        WatchSettings();
        // Close the policy race between the first read and event subscription.
        if (App.Motion.Resolve(MotionDurations.Fast) <= TimeSpan.Zero) Settle();
        else storyboard.Begin();
    }

    private double TargetAngle => _open ? 180d : 0d;

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
        // Global/native publishers only retain this binding for its short motion.
        _systemSettings ??= new UISettings();
        _watchingSettings = true;
        App.AppearanceChanged += Appearance_Changed;
        _systemSettings.AnimationsEnabledChanged += System_AnimationsEnabledChanged;
    }

    private void Appearance_Changed(object? sender, AppearanceSettings settings) => PolicyChanged();

    private void System_AnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs e) => PolicyChanged();

    private void PolicyChanged()
    {
        // System settings can notify off-thread. A queued notification may arrive
        // after completion, unload, disposal, or a newer animation; it owns none.
        var generation = _generation;
        void Apply()
        {
            if (_attached && !_disposed && generation == _generation
                && App.Motion.Resolve(MotionDurations.Fast) <= TimeSpan.Zero) Settle();
        }
        if (_chevron.DispatcherQueue.HasThreadAccess) Apply();
        else _chevron.DispatcherQueue.TryEnqueue(Apply);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        _chevron.Loaded -= Chevron_Loaded;
        _chevron.Unloaded -= Chevron_Unloaded;
    }
}
