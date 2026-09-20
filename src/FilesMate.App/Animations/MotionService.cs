namespace FilesMate.App.Animations;

public sealed class MotionService : IMotionService
{
    private readonly IAnimationSettings _settings;
    private CancellationTokenSource _windowScope = new();

    public MotionService(IAnimationSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public TimeSpan Instant => Resolve(MotionDurations.Instant);

    public TimeSpan Fast => Resolve(MotionDurations.Fast);

    public TimeSpan Standard => Resolve(MotionDurations.Standard);

    public TimeSpan Emphasized => Resolve(MotionDurations.Emphasized);

    public TimeSpan Resolve(TimeSpan duration) =>
        _settings.AnimationsEnabled ? duration : TimeSpan.Zero;

    public async Task FadeAsync(
        IAnimatableVisual visual,
        double from,
        double to,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visual);
        visual.Opacity = from;
        var resolved = Resolve(duration);
        if (resolved <= TimeSpan.Zero)
        {
            visual.Opacity = to;
            return;
        }

        await WaitAsync(resolved, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        visual.Opacity = to;
    }

    public async Task ScalePressAsync(IAnimatableVisual visual, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visual);
        var press = Resolve(MotionDurations.Instant);
        var release = Resolve(MotionDurations.Fast);
        visual.ScaleX = 0.98f;
        visual.ScaleY = 0.98f;
        if (press > TimeSpan.Zero)
        {
            await WaitAsync(press, cancellationToken);
        }

        visual.ScaleX = 1;
        visual.ScaleY = 1;
        if (release > TimeSpan.Zero)
        {
            await WaitAsync(release, cancellationToken);
        }
    }

    public async Task TranslateFadeAsync(
        IAnimatableVisual visual,
        float offsetY,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visual);
        visual.TranslationY = offsetY;
        visual.Opacity = 0.96;
        var resolved = Resolve(duration);
        if (resolved <= TimeSpan.Zero)
        {
            visual.TranslationY = 0;
            visual.Opacity = 1;
            return;
        }

        await WaitAsync(resolved, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        visual.TranslationY = 0;
        visual.Opacity = 1;
    }

    public void SetImplicitDropTargetAnimations(IAnimatableVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (!_settings.AnimationsEnabled)
        {
            return;
        }

        visual.HasImplicitAnimations = true;
    }

    public void ResetVisual(IAnimatableVisual visual) => RecyclableVisualState.Reset(visual);

    public void CancelWindowScope()
    {
        var previous = Interlocked.Exchange(ref _windowScope, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    private async Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _windowScope.Token);
        try
        {
            await Task.Delay(duration, linked.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
