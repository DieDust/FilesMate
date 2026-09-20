using FilesMate.App.Models;

namespace FilesMate.App.Animations;

internal sealed class CompositeAnimationSettings : IAnimationSettings
{
    private readonly IAnimationSettings _system;
    private readonly Func<ReduceMotionKind> _reduce;

    public CompositeAnimationSettings(IAnimationSettings system, Func<ReduceMotionKind> reduce)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _reduce = reduce ?? throw new ArgumentNullException(nameof(reduce));
    }

    public bool AnimationsEnabled => ReduceMotionGate.AnimationsEnabled(_system.AnimationsEnabled, _reduce());
}
