using FilesMate.App.Animations;

namespace FilesMate.App.Tests.Animations;

public sealed class RecyclableVisualStateTests
{
    [Fact]
    public void Reset_restores_opacity_translation_scale_and_clip()
    {
        var visual = new FakeVisual
        {
            Opacity = 0.4,
            TranslationX = 8,
            TranslationY = -4,
            ScaleX = 0.98f,
            ScaleY = 0.98f,
            Clip = "rect",
            HasImplicitAnimations = true,
        };

        RecyclableVisualState.Reset(visual);

        Assert.Equal(1, visual.Opacity);
        Assert.Equal(0, visual.TranslationX);
        Assert.Equal(0, visual.TranslationY);
        Assert.Equal(1, visual.ScaleX);
        Assert.Equal(1, visual.ScaleY);
        Assert.Null(visual.Clip);
        Assert.False(visual.HasImplicitAnimations);
    }

    [Fact]
    public void Resetting_one_thousand_fakes_leaves_no_residual_state()
    {
        var motion = new MotionService(new AlwaysOnSettings());
        for (var i = 0; i < 1000; i++)
        {
            var visual = new FakeVisual
            {
                Opacity = 0,
                TranslationX = i,
                ScaleX = 0.5f,
                Clip = i,
                HasImplicitAnimations = true,
            };
            motion.SetImplicitDropTargetAnimations(visual);
            motion.ResetVisual(visual);
            Assert.Equal(1, visual.Opacity);
            Assert.Equal(0, visual.TranslationX);
            Assert.Equal(1, visual.ScaleX);
            Assert.Null(visual.Clip);
            Assert.False(visual.HasImplicitAnimations);
        }
    }

    private sealed class AlwaysOnSettings : IAnimationSettings
    {
        public bool AnimationsEnabled => true;
    }
}
