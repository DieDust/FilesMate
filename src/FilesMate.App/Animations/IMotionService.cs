namespace FilesMate.App.Animations;

public interface IMotionService
{
    public TimeSpan Instant { get; }

    public TimeSpan Fast { get; }

    public TimeSpan Standard { get; }

    public TimeSpan Emphasized { get; }

    public TimeSpan Resolve(TimeSpan duration);

    public Task FadeAsync(
        IAnimatableVisual visual,
        double from,
        double to,
        TimeSpan duration,
        CancellationToken cancellationToken);

    public Task ScalePressAsync(IAnimatableVisual visual, CancellationToken cancellationToken);

    public Task TranslateFadeAsync(
        IAnimatableVisual visual,
        float offsetY,
        TimeSpan duration,
        CancellationToken cancellationToken);

    public void SetImplicitDropTargetAnimations(IAnimatableVisual visual);

    public void ResetVisual(IAnimatableVisual visual);

    public void CancelWindowScope();
}
