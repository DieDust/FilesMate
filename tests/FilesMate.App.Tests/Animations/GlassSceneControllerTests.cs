using FilesMate.App.Animations;
using FilesMate.App.Models;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Animations;

public sealed class GlassSceneControllerTests
{
    [Theory]
    [InlineData(0, 1d)]
    [InlineData(50, 0.5d)]
    [InlineData(100, 0d)]
    public void Custom_percentage_controls_material_coverage_in_both_styles(int percent, double expected)
    {
        foreach (var style in Enum.GetValues<ShellStyleKind>())
        {
            var settings = AppearanceSettings.Default with { ShellStyle = style, TransparencyPercent = percent };
            Assert.Equal(expected, GlassSceneState.Resolve(settings).SurfaceOpacity);
            Assert.Equal(1, GlassSceneState.Resolve(settings with { Backdrop = BackdropKind.Solid }).SurfaceOpacity);
            Assert.Equal(1, GlassSceneState.Resolve(settings with { GlassEffect = GlassEffectMode.Off }).SurfaceOpacity);
        }
    }

    [Fact]
    public void Off_uses_an_opaque_static_surface()
    {
        var state = GlassSceneState.Resolve(
            GlassEffectMode.Off,
            systemAnimationsEnabled: true,
            windowActive: true,
            remoteSession: false);

        Assert.False(state.UseStaticGlass);
        Assert.Equal(1, state.SurfaceOpacity);
        Assert.Equal(0, state.HighlightOpacity);
    }

    [Fact]
    public void Balanced_uses_a_calm_static_glass_surface()
    {
        var state = GlassSceneState.Resolve(
            GlassEffectMode.Balanced,
            systemAnimationsEnabled: true,
            windowActive: true,
            remoteSession: false);

        Assert.True(state.UseStaticGlass);
        Assert.InRange(state.SurfaceOpacity, 0.7, 0.95);
        Assert.True(state.HighlightOpacity > 0);
    }

    [Fact]
    public void Immersive_increases_transparency_without_animation()
    {
        var state = GlassSceneState.Resolve(
            GlassEffectMode.Immersive,
            systemAnimationsEnabled: true,
            windowActive: true,
            remoteSession: false);

        Assert.True(state.UseStaticGlass);
        Assert.True(state.SurfaceOpacity < 0.8);
        Assert.True(state.HighlightOpacity > 0);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Motion_remote_and_activation_rules_do_not_change_the_static_material(
        bool animationsEnabled,
        bool windowActive,
        bool remoteSession)
    {
        var state = GlassSceneState.Resolve(
            GlassEffectMode.Immersive,
            animationsEnabled,
            windowActive,
            remoteSession);

        Assert.True(state.UseStaticGlass);
        Assert.True(state.SurfaceOpacity < 1);
    }

    [Fact]
    public void Controller_has_no_pointer_or_forever_running_scene()
    {
        var source = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Animations",
            "GlassSceneController.cs"));

        Assert.DoesNotContain("PointerMoved +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerPosition", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AmbientPhase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IterationBehavior.Forever", source, StringComparison.Ordinal);
        Assert.Contains("IDisposable", source, StringComparison.Ordinal);

        var styles = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Themes",
            "LiquidGlassStyles.xaml"));
        Assert.DoesNotContain("PART_PointerLight", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("PART_AmbientSheen", styles, StringComparison.Ordinal);

        var window = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));
        Assert.Contains("GlassSceneController", window, StringComparison.Ordinal);
        Assert.Contains("_glassScene.Apply", window, StringComparison.Ordinal);
        Assert.Contains("_glassScene.Dispose", window, StringComparison.Ordinal);
    }
}
