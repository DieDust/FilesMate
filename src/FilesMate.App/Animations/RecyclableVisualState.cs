namespace FilesMate.App.Animations;

public static class RecyclableVisualState
{
    public static void Reset(IAnimatableVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        visual.Opacity = 1;
        visual.TranslationX = 0;
        visual.TranslationY = 0;
        visual.ScaleX = 1;
        visual.ScaleY = 1;
        visual.Clip = null;
        visual.HasImplicitAnimations = false;
    }
}
