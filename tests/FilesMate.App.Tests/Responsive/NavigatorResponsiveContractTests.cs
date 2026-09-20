using System.Xml.Linq;

using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Responsive;

public sealed class NavigatorResponsiveContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Navigator_page_declares_four_width_visual_states()
    {
        var states = LoadVisualStates();
        Assert.Equal(["Narrow", "Compact", "Medium", "Wide"], states.Keys);

        Assert.Equal(1, MinWindowWidth(states["Narrow"]));
        Assert.Equal(720, MinWindowWidth(states["Compact"]));
        Assert.Equal(900, MinWindowWidth(states["Medium"]));
        Assert.Equal(1180, MinWindowWidth(states["Wide"]));
    }

    [Fact]
    public void Each_width_state_defines_sidebar_omnibar_and_toolbar_presentation()
    {
        var states = LoadVisualStates();
        foreach (var (name, state) in states)
        {
            var targets = SetterTargets(state);
            Assert.True(
                targets.Any(target => target.Contains("ShellSplit", StringComparison.Ordinal)
                    || target.Contains("Sidebar", StringComparison.Ordinal)),
                $"{name} must change sidebar presentation.");
            Assert.True(
                targets.Any(target => target.Contains("Omni", StringComparison.Ordinal)
                    || target.Contains("Omnibar", StringComparison.Ordinal)
                    || target.Contains("Filter", StringComparison.Ordinal)),
                $"{name} must change search/omnibar presentation.");
            Assert.True(
                targets.Any(target => target.Contains("Commands", StringComparison.Ordinal)
                    || target.Contains("CommandBar", StringComparison.Ordinal)),
                $"{name} must change toolbar presentation.");
        }
    }

    [Fact]
    public void Shell_keeps_responsive_connected_regions_with_a_defined_file_surface()
    {
        var page = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        Assert.Contains("FilePaneChrome", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContentChrome\"", page, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", page, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Sidebar.BackgroundBrush", page, StringComparison.Ordinal);
        Assert.Contains("FilesMate.CommandBar.BackgroundBrush", page, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Shell.SeparatorBrush", page, StringComparison.Ordinal);
        Assert.Contains("AdaptiveTrigger", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellSplit\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommandBarCard\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesMate.Pane.Gutter", page, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesMate.Corner.Card", page, StringComparison.Ordinal);
        Assert.DoesNotContain("RowSpacing", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Target=\"ShellSplit.IsPaneOpen\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("IsPaneOpen=\"True\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Target=\"PaneToggle.Visibility\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellSplit.DisplayMode\" Value=\"Overlay\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("CompactInline", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PaneResizeThumb\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PreviewResizeThumb\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PreviewSplitterColumn\"", page, StringComparison.Ordinal);

        var chrome = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FilePaneChrome.xaml"));
        Assert.Contains("FilesMate.FileArea.BackgroundBrush", chrome, StringComparison.Ordinal);
        Assert.Contains("FilesMate.FileArea.InactiveBackgroundBrush", chrome, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Control.Height.StatusBar", chrome, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BodyPresenter\"", chrome, StringComparison.Ordinal);
        Assert.Contains("LoadingPresenter", chrome, StringComparison.Ordinal);
        Assert.Contains("InfoStateView", chrome, StringComparison.Ordinal);
        Assert.Contains("FileStatusBar", chrome, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Corner.Card", chrome, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PaneCard\"", chrome, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"1\"", chrome, StringComparison.Ordinal);
        Assert.Contains("FilesMate.FileContent.BackgroundBrush", chrome, StringComparison.Ordinal);
        Assert.Contains("Margin=\"8,0,8,8\"", chrome, StringComparison.Ordinal);
    }

    [Fact]
    public void Resize_does_not_navigate_or_enumerate()
    {
        var pageCode = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));
        Assert.Contains("ShellRoot.SizeChanged += (_, _) => PositionShelf()", pageCode, StringComparison.Ordinal);
        var customization = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.Customization.cs"));
        var resize = customization[customization.IndexOf("private void PositionShelf()", StringComparison.Ordinal)
            ..customization.IndexOf("private void ShelfDragOver", StringComparison.Ordinal)];
        Assert.DoesNotContain("Navigate", resize, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerate", resize, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadAsync", resize, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerate", pageCode, StringComparison.Ordinal);
        Assert.Contains("CurrentStateChanged", pageCode, StringComparison.Ordinal);
        Assert.Contains("SchedulePaneOpen", pageCode, StringComparison.Ordinal);
        Assert.Contains("TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ApplyPendingPaneOpen)", pageCode, StringComparison.Ordinal);
        Assert.Contains("ApplyPaneOpen", pageCode, StringComparison.Ordinal);
        Assert.Contains("ApplySidebarWidth", pageCode, StringComparison.Ordinal);
        Assert.Contains("width < 720", pageCode, StringComparison.Ordinal);
        Assert.Contains("ShellSplit.IsPaneOpen = !App.Features.SidebarCollapsed", pageCode, StringComparison.Ordinal);
        Assert.Contains("PaneResize_PointerMoved", pageCode, StringComparison.Ordinal);
        Assert.Contains("ApplyPreviewWidth", pageCode, StringComparison.Ordinal);
        Assert.Contains("PreviewResize_PointerMoved", pageCode, StringComparison.Ordinal);

        var chromeCode = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FilePaneChrome.xaml.cs"));
        Assert.DoesNotContain("Navigate(", chromeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IDirectoryEnumerator", chromeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("PaneViewModel", chromeCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_width_tokens_match_the_responsive_spec()
    {
        var tokens = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "DesignTokens.xaml"));
        Assert.Contains("x:Key=\"FilesMate.Sidebar.Width\">236", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilesMate.Sidebar.Width.Medium\">220", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilesMate.Sidebar.Width.Compact\">52", tokens, StringComparison.Ordinal);
    }

    private static Dictionary<string, XElement> LoadVisualStates()
    {
        var document = XDocument.Load(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        var group = document.Descendants(Presentation + "VisualStateGroup")
            .FirstOrDefault(element => (string?)element.Attribute(Xaml + "Name") == "WidthStates");
        Assert.NotNull(group);

        var map = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var state in group!.Elements(Presentation + "VisualState"))
        {
            var name = (string?)state.Attribute(Xaml + "Name");
            Assert.False(string.IsNullOrWhiteSpace(name), "VisualState is missing x:Name.");
            Assert.True(map.TryAdd(name, state), $"Duplicate VisualState '{name}'.");
        }

        return map;
    }

    private static double MinWindowWidth(XElement state)
    {
        var trigger = state
            .Descendants(Presentation + "AdaptiveTrigger")
            .FirstOrDefault();
        Assert.NotNull(trigger);
        return (double)trigger!.Attribute("MinWindowWidth")!;
    }

    private static string[] SetterTargets(XElement state)
    {
        return state
            .Descendants(Presentation + "Setter")
            .Select(setter => (string?)setter.Attribute("Target") ?? string.Empty)
            .Where(target => target.Length > 0)
            .ToArray();
    }
}
