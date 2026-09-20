namespace FilesMate.App.Animations;

public interface IAnimatableVisual
{
    public double Opacity { get; set; }

    public float TranslationX { get; set; }

    public float TranslationY { get; set; }

    public float ScaleX { get; set; }

    public float ScaleY { get; set; }

    public object? Clip { get; set; }

    public bool HasImplicitAnimations { get; set; }
}
