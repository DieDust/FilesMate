using FilesMate.App.Models;
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
    private readonly ScaleTransform _paletteScale = new(1, 1);
    private readonly TranslateTransform _resultsTranslation = new();
    private bool _closing;
    internal bool IsClosing => _closing;
    private bool PaletteMotionEnabled => SystemParameters.ClientAreaAnimation && _appearance.ReduceMotion != ReduceMotionKind.On;

    // Rendering mode must not silently disable user-enabled transitions.
    private bool AnimateResultLayout => PaletteMotionEnabled;

    private void PreparePaletteOpen()
    {
        if (!PaletteMotionEnabled) return;
        Shell.Opacity = 0;
        _paletteTranslation.Y = 12;
        _paletteScale.ScaleX = _paletteScale.ScaleY = .98;
    }

    private void ResetPaletteMotion()
    {
        PanelBody.BeginAnimation(UIElement.OpacityProperty, null);
        Shell.BeginAnimation(UIElement.OpacityProperty, null);
        _paletteTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        _paletteScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _paletteScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _paletteScale.ScaleX = _paletteScale.ScaleY = 1;
        PanelBody.Opacity = 1;
        Shell.Opacity = 1;
        _paletteTranslation.Y = 0;
        PanelBody.RenderTransform = Transform.Identity;
        Shell.RenderTransformOrigin = new Point(.5, 0);
        Shell.RenderTransform = new TransformGroup { Children = { _paletteScale, _paletteTranslation } };
    }

    private void AnimatePaletteOpen()
    {
        if (!PaletteMotionEnabled) return;
        var duration = TimeSpan.FromMilliseconds(240);
        var easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
        Shell.Opacity = 1;
        _paletteTranslation.Y = 0;
        _paletteScale.ScaleX = _paletteScale.ScaleY = 1;
        Shell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        _paletteTranslation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
        _paletteScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.98, 1, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
        _paletteScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.98, 1, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
    }

    private async Task AnimatePaletteCloseAsync(int openVersion)
    {
        if (PaletteMotionEnabled && IsVisible)
        {
            var duration = TimeSpan.FromMilliseconds(160);
            var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
            Shell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = easing });
            _paletteTranslation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, duration) { EasingFunction = easing });
            _paletteScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.98, duration) { EasingFunction = easing });
            _paletteScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.98, duration) { EasingFunction = easing });
            await Task.Delay(duration);
        }
        // A quick reopen belongs to a new interaction; the previous launch must
        // neither hide it nor reset its input when the animation completes.
        if (openVersion == _openVersion) { Dismiss(notifyHost: false); ResetPaletteMotion(); }
    }

    internal async void DismissAnimated()
    {
        if (_closing || _dismissed) return;
        _closing = true;
        var version = _openVersion;
        try
        {
            await AnimatePaletteCloseAsync(version);
            if (version == _openVersion && !_launching) _host.WindowDismissed();
        }
        finally { if (version == _openVersion) _closing = false; }
    }

    private void AnimateSection(FrameworkElement panel)
    {
        panel.BeginAnimation(UIElement.OpacityProperty, null);
        if (PaletteMotionEnabled && panel.IsVisible && !_opening)
            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.6, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void AnimateSearchLayout()
    {
        var from = SearchPanel.ActualHeight;
        SearchPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
        SearchPanel.Height = double.NaN;
        if (!IsVisible || SearchPanel.Visibility != Visibility.Visible || !AnimateResultLayout) return;
        SearchPanel.Measure(new Size(Math.Max(1, Shell.ActualWidth - 26), double.PositiveInfinity));
        var target = SearchPanel.DesiredSize.Height - SearchPanel.Margin.Top - SearchPanel.Margin.Bottom;
        if (Math.Abs(from - target) < 1) return;
        SearchPanel.BeginAnimation(FrameworkElement.HeightProperty,
            new DoubleAnimation(Math.Max(0, from), Math.Max(0, target), TimeSpan.FromMilliseconds(240))
            { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    private void AnimateResultsChanged()
    {
        Results.RenderTransform = _resultsTranslation;
        Results.BeginAnimation(UIElement.OpacityProperty, null);
        _resultsTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        if (!PaletteMotionEnabled) return;
        var duration = TimeSpan.FromMilliseconds(140);
        Results.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.65, 1, duration));
        _resultsTranslation.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(3, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private async Task ShowSearchActivityAsync(CancellationTokenSource request)
    {
        try
        {
            await Task.Delay(120, request.Token);
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
        SearchPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
        SearchPanel.Height = double.NaN;
        Results.BeginAnimation(UIElement.OpacityProperty, null);
        _resultsTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        StopSearchActivity();
    }

    private void Interaction_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.Template is null) return;
        var selected = control is ListBoxItem { IsSelected: true } or ToggleButton { IsChecked: true };
        UpdateLayer("InteractionHover", control.IsMouseOver && !selected);
        UpdateLayer("InteractionSelection", selected);
        void UpdateLayer(string name, bool visible)
        {
            if (control.Template.FindName(name, control) is not FrameworkElement layer) return;
            var from = layer.Opacity;
            layer.BeginAnimation(UIElement.OpacityProperty, null);
            layer.Opacity = visible ? 1 : 0;
            // Loaded/recycled containers get their final state immediately.
            if (PaletteMotionEnabled && e.RoutedEvent != FrameworkElement.LoadedEvent)
                layer.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(from, layer.Opacity, TimeSpan.FromMilliseconds(100)) { FillBehavior = FillBehavior.Stop });
        }
    }
}
