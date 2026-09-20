using FilesMate.App.Animations;

namespace FilesMate.App.Tests.Animations;

internal sealed class FakeVisual : IAnimatableVisual
{
    public double Opacity { get; set; } = 1;

    public float TranslationX { get; set; }

    public float TranslationY { get; set; }

    public float ScaleX { get; set; } = 1;

    public float ScaleY { get; set; } = 1;

    public object? Clip { get; set; }

    public bool HasImplicitAnimations { get; set; }
}
