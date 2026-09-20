using System.Text.RegularExpressions;

using FilesMate.App.Animations;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Animations;

public sealed class MotionIntegrationContractTests
{
    private static readonly Regex FromMilliseconds = new(
        @"TimeSpan\.FromMilliseconds\((?<ms>\d+)\)",
        RegexOptions.Compiled);

    private static readonly Regex GeneratedDuration = new(
        @"GeneratedDuration\s*=\s*""(?<value>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void Production_motion_durations_only_use_registered_tokens()
    {
        var allowed = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["MotionDurations.cs"] = [80, 120, 180, 240],
            ["FilePanePresentation.cs"] = [150],
            ["PaneViewModel.cs"] = [200],
            // Coalesce transparency slider disk writes; this is not an animation.
            ["AppearancePage.xaml.cs"] = [250],
            // External Shell selection RPC has a bounded UI-dispatch wait;
            // it is not an animation duration.
            ["NavigatorPage.Shell.cs"] = [500],
            // Pointer-driven marquee auto-scroll cadence, not a visual transition.
            ["FavoritesManager.Selection.cs"] = [32],
            ["FileDetailsSurface.xaml.cs"] = [40],
            // Delay before dismissing a hover affordance, not animation duration.
            ["FileDetailsSurface.Alphabet.cs"] = [450],
            // Intentional drag dwell; no animation or repeating background work.
            ["FileDetailsSurface.DragHover.cs"] = [750],
            ["MainWindow.Convenience.cs"] = [750],
            // Native window opacity sampling cadence; duration uses MotionDurations.
            ["QuickPreviewWindow.cs"] = [16],
            // Test-only dispatcher heartbeat measurement, not an animation.
            ["MainWindow.NativeOfficeHangSmoke.cs"] = [50],
        };

        foreach (var path in EnumerateAppFiles("*.cs"))
        {
            var name = Path.GetFileName(path);
            var text = File.ReadAllText(path);
            foreach (Match match in FromMilliseconds.Matches(text))
            {
                var ms = int.Parse(match.Groups["ms"].Value);
                Assert.True(
                    allowed.TryGetValue(name, out var values) && values.Contains(ms),
                    $"{name} uses unregistered motion duration {ms} ms.");
            }
        }

        Assert.DoesNotContain(
            "500",
            File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Animations", "MotionDurations.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_transitions_bind_motion_tokens_and_stay_within_80_to_240_ms()
    {
        foreach (var path in EnumerateAppFiles("*.xaml"))
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("0:0:0.5", text, StringComparison.Ordinal);
            Assert.DoesNotContain("FromMilliseconds(500)", text, StringComparison.Ordinal);
            foreach (Match match in GeneratedDuration.Matches(text))
            {
                var value = match.Groups["value"].Value;
                if (value is "0:0:0" or "0:0:0.0")
                {
                    continue;
                }

                Assert.StartsWith("{StaticResource FilesMate.Motion.", value, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Composition_and_storyboard_motion_resolve_through_the_motion_service()
    {
        foreach (var path in EnumerateAppFiles("*.cs"))
        {
            var name = Path.GetFileName(path);
            if (name is "MotionService.cs" or "IMotionService.cs")
            {
                continue;
            }

            var text = File.ReadAllText(path);
            if (text.Contains("CreateScalarKeyFrameAnimation", StringComparison.Ordinal)
                || text.Contains("new DoubleAnimation", StringComparison.Ordinal)
                || text.Contains("TranslateFadeAsync", StringComparison.Ordinal))
            {
                Assert.Contains("App.Motion", text, StringComparison.Ordinal);
                Assert.True(
                    text.Contains("MotionDurations.", StringComparison.Ordinal)
                    || text.Contains("FilesMate.Glass.Ambient.DurationSeconds", StringComparison.Ordinal),
                    $"{name} must resolve interactive motion or the registered ambient-scene duration token.");
            }
        }

        var surface = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml.cs"));
        Assert.Contains("row.ResetVisual()", surface, StringComparison.Ordinal);
        Assert.Contains("tile.ResetVisual()", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSurfaceCrossfade", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(Repeater.Opacity)", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard.SetTarget(animation, Repeater)", surface, StringComparison.Ordinal);

        var window = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));
        Assert.DoesNotContain("TranslateFadeAsync", window, StringComparison.Ordinal);
        Assert.DoesNotContain("FromMilliseconds", window, StringComparison.Ordinal);
        Assert.Contains("SuppressTabStripEntrance", window, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTransitions.Clear()", window, StringComparison.Ordinal);

        var toolbar = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Toolbar",
            "AdaptiveCommandToolbar.xaml"));
        Assert.DoesNotContain("RepositionThemeTransition", toolbar, StringComparison.Ordinal);

        var tabBar = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabBarStyles.xaml"));
        Assert.DoesNotContain("LayoutRootScale", tabBar, StringComparison.Ordinal);
        Assert.Contains("ContentPresenter.ContentTransitions", tabBar, StringComparison.Ordinal);

        var navStyles = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "NavigationStyles.xaml"));
        Assert.Contains("SidebarPlaceListStyle", navStyles, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTransitions", navStyles, StringComparison.Ordinal);

        var sidebar = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Navigation",
            "NavigationSidebar.xaml.cs"));
        var sidebarXaml = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Navigation",
            "NavigationSidebar.xaml"));
        Assert.Contains("x:Bind Selected", sidebarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateScalarKeyFrameAnimation", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("FromMilliseconds", sidebar, StringComparison.Ordinal);
    }

    [Fact]
    public void Registered_tokens_match_the_motion_spec()
    {
        Assert.Equal(80, MotionDurations.Instant.TotalMilliseconds);
        Assert.Equal(120, MotionDurations.Fast.TotalMilliseconds);
        Assert.Equal(180, MotionDurations.Standard.TotalMilliseconds);
        Assert.Equal(240, MotionDurations.Emphasized.TotalMilliseconds);
    }

    [Fact]
    public void Virtualized_file_items_never_host_liquid_glass_or_continuous_ambient_motion()
    {
        string[] paths =
        [
            Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileRow.xaml"),
            Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileTile.xaml"),
            Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileDetailsSurface.xaml"),
            Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileDetailsSurface.xaml.cs"),
        ];

        foreach (var path in paths)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("LiquidGlassSurface", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PART_AmbientSheen", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AmbientPhase", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateScalarKeyFrameAnimation", source, StringComparison.Ordinal);
        }

        var surface = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml"));
        Assert.Contains("ItemsRepeater", surface, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateAppFiles(string pattern)
    {
        var root = ThemeXaml.AppRoot;
        foreach (var path in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return path;
        }
    }
}
