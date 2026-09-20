using FilesMate.App.Animations;

namespace FilesMate.App.Tests.Animations;

public sealed class MotionServiceTests
{
    [Fact]
    public void Duration_tokens_match_the_motion_spec()
    {
        Assert.Equal(80, MotionDurations.Instant.TotalMilliseconds);
        Assert.Equal(120, MotionDurations.Fast.TotalMilliseconds);
        Assert.Equal(180, MotionDurations.Standard.TotalMilliseconds);
        Assert.Equal(240, MotionDurations.Emphasized.TotalMilliseconds);
    }

    [Fact]
    public void Disabled_animations_resolve_to_zero_duration()
    {
        var motion = new MotionService(new FakeAnimationSettings(false));
        Assert.Equal(TimeSpan.Zero, motion.Instant);
        Assert.Equal(TimeSpan.Zero, motion.Fast);
        Assert.Equal(TimeSpan.Zero, motion.Standard);
        Assert.Equal(TimeSpan.Zero, motion.Emphasized);
        Assert.Equal(TimeSpan.Zero, motion.Resolve(MotionDurations.Fast));
    }

    [Fact]
    public void Enabled_animations_keep_the_requested_duration()
    {
        var motion = new MotionService(new FakeAnimationSettings(true));
        Assert.Equal(MotionDurations.Fast, motion.Resolve(MotionDurations.Fast));
    }

    [Fact]
    public async Task Translate_fade_rests_at_identity_when_motion_is_disabled()
    {
        var motion = new MotionService(new FakeAnimationSettings(false));
        var visual = new FakeVisual { Opacity = 1, TranslationY = 0 };
        await motion.TranslateFadeAsync(visual, 4f, MotionDurations.Standard, CancellationToken.None);
        Assert.Equal(0, visual.TranslationY);
        Assert.Equal(1, visual.Opacity);
    }

    [Fact]
    public async Task Fade_is_a_no_op_when_motion_is_disabled()
    {
        var motion = new MotionService(new FakeAnimationSettings(false));
        var visual = new FakeVisual { Opacity = 0.2 };
        await motion.FadeAsync(visual, 0.2, 1, MotionDurations.Emphasized, CancellationToken.None);
        Assert.Equal(1, visual.Opacity);
    }

    [Fact]
    public async Task Cancelled_fade_completes_without_throwing()
    {
        var motion = new MotionService(new FakeAnimationSettings(true));
        var visual = new FakeVisual();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var task = motion.FadeAsync(visual, 1, 0.5, MotionDurations.Emphasized, cts.Token);
        await task;
        Assert.True(task.IsCompleted);
        Assert.False(task.IsFaulted);
    }

    [Fact]
    public async Task Window_scope_cancellation_ends_in_flight_motion()
    {
        var motion = new MotionService(new FakeAnimationSettings(true));
        var visual = new FakeVisual();
        var task = motion.FadeAsync(visual, 1, 0, TimeSpan.FromSeconds(5), CancellationToken.None);
        motion.CancelWindowScope();
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    private sealed class FakeAnimationSettings(bool enabled) : IAnimationSettings
    {
        public bool AnimationsEnabled { get; } = enabled;
    }
}
