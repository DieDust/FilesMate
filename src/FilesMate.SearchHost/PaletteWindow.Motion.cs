using FilesMate.App.Animations;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private int _openVersion;
    private readonly TranslateTransform _paletteTranslation = new();
    private bool _closing;
    private CancellationTokenSource? _paletteCloseCancellation;
    internal bool IsClosing => _closing;
    private bool PaletteMotionEnabled => !SystemParameters.HighContrast
        && ReduceMotionGate.AnimationsEnabled(SystemParameters.ClientAreaAnimation, _appearance.ReduceMotion);

    // Rendering mode must not silently disable user-enabled transitions.
    private bool AnimateResultLayout => PaletteMotionEnabled;

    private void PreparePaletteOpen()
    {
        if (!PaletteMotionEnabled) return;
        Shell.Opacity = 0;
        _paletteTranslation.Y = 4;
    }

    private void ResetPaletteMotion()
    {
        PanelBody.BeginAnimation(UIElement.OpacityProperty, null);
        Shell.BeginAnimation(UIElement.OpacityProperty, null);
        _paletteTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        PanelBody.Opacity = 1;
        Shell.Opacity = 1;
        _paletteTranslation.Y = 0;
        PanelBody.RenderTransform = Transform.Identity;
        Shell.RenderTransform = _paletteTranslation;
    }

    private void AnimatePaletteOpen()
    {
        if (!PaletteMotionEnabled) return;
        var duration = MotionDurations.Fast;
        var easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
        Shell.Opacity = 1;
        _paletteTranslation.Y = 0;
        Shell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.Stop });
        _paletteTranslation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, 0, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
    }

    private void CancelPaletteClose()
    {
        var cancellation = _paletteCloseCancellation;
        _paletteCloseCancellation = null;
        cancellation?.Cancel();
    }

    private async Task<bool> AnimatePaletteCloseAsync(int openVersion)
    {
        CancelPaletteClose();
        using var cancellation = new CancellationTokenSource();
        _paletteCloseCancellation = cancellation;
        try
        {
            if (PaletteMotionEnabled && IsVisible)
            {
                var duration = MotionDurations.Fast;
                var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
                Shell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = easing });
                _paletteTranslation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, duration) { EasingFunction = easing });
                await Task.Delay(duration, cancellation.Token);
            }
            // A reopen owns a new version and cancels the previous transition.
            if (openVersion != _openVersion || cancellation.IsCancellationRequested) return false;
            Dismiss(notifyHost: false);
            ResetPaletteMotion();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        finally { if (ReferenceEquals(_paletteCloseCancellation, cancellation)) _paletteCloseCancellation = null; }
    }

    internal async void DismissAnimated()
    {
        if (_closing || _dismissed) return;
        _closing = true;
        var version = _openVersion;
        try
        {
            var closed = await AnimatePaletteCloseAsync(version);
            if (closed && version == _openVersion && !_launching) _host.WindowDismissed();
        }
        finally { if (version == _openVersion) _closing = false; }
    }

    private void AnimateSection(FrameworkElement panel)
    {
        panel.BeginAnimation(UIElement.OpacityProperty, null);
        if (PaletteMotionEnabled && panel.IsVisible && !_opening)
            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.7, 1, MotionDurations.Fast) { FillBehavior = FillBehavior.Stop });
    }

    private void AnimateSearchLayout()
    {
        var from = SearchPanel.ActualHeight;
        SearchPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
        SearchPanel.Height = double.NaN;
        if (!IsVisible || SearchPanel.Visibility != Visibility.Visible || !AnimateResultLayout
            || Results.ItemContainerGenerator.Status == GeneratorStatus.GeneratingContainers) return;
        SearchPanel.Measure(new Size(Math.Max(1, Shell.ActualWidth - 26), double.PositiveInfinity));
        var target = SearchPanel.DesiredSize.Height - SearchPanel.Margin.Top - SearchPanel.Margin.Bottom;
        if (Math.Abs(from - target) < 1) return;
        SearchPanel.BeginAnimation(FrameworkElement.HeightProperty,
            new DoubleAnimation(Math.Max(0, from), Math.Max(0, target), MotionDurations.Standard)
            { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    private async Task ShowSearchActivityAsync(CancellationTokenSource request)
    {
        try
        {
            await Task.Delay(MotionDurations.Fast, request.Token);
            if (_query != request || !IsVisible) return;
            SearchActivity.IsIndeterminate = PaletteMotionEnabled;
            SearchActivity.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { }
    }

    private void StopSearchActivity()
    {
        SearchActivity.IsIndeterminate = false;
        SearchActivity.Visibility = Visibility.Hidden;
    }

    private void ResetSearchMotion()
    {
        ResetSearchAnimations();
        StopSearchActivity();
    }

    private void ResetSearchAnimations()
    {
        SearchPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
        SearchPanel.Height = double.NaN;
        Results.BeginAnimation(UIElement.OpacityProperty, null);
        SearchEmptyCard.BeginAnimation(UIElement.OpacityProperty, null);
        SearchStateCard.BeginAnimation(UIElement.OpacityProperty, null);
        SearchKeyboardHints.BeginAnimation(UIElement.OpacityProperty, null);
    }

    private void Interaction_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.Template is null) return;
        if (e.RoutedEvent == FrameworkElement.LoadedEvent)
        {
            control.IsEnabledChanged -= InteractionEnabled_Changed;
            control.IsEnabledChanged += InteractionEnabled_Changed;
        }
        var selected = control is ListBoxItem { IsSelected: true } or ToggleButton { IsChecked: true };
        UpdateLayer("InteractionHover", control.IsEnabled && control.IsMouseOver && !selected);
        UpdateLayer("InteractionSelection", selected);
        void UpdateLayer(string name, bool visible)
        {
            if (control.Template.FindName(name, control) is not FrameworkElement layer) return;
            var from = layer.Opacity;
            layer.BeginAnimation(UIElement.OpacityProperty, null);
            layer.Opacity = visible ? 1 : 0;
            // Loaded/recycled containers get their final state immediately.
            if (PaletteMotionEnabled && e.RoutedEvent != FrameworkElement.LoadedEvent
                && !(name == "InteractionSelection" && _appearance.Skin is not null))
                layer.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(from, layer.Opacity, MotionDurations.Fast) { FillBehavior = FillBehavior.Stop });
        }
    }

    private void InteractionEnabled_Changed(object sender, DependencyPropertyChangedEventArgs e) =>
        Interaction_Changed(sender, new RoutedEventArgs());
}
