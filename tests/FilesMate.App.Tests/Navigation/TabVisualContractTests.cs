using System.Globalization;
using System.Xml.Linq;

using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Navigation;

public sealed class TabVisualContractTests
{
    [Fact]
    public void App_xaml_merges_tab_styles_after_navigation_styles()
    {
        Assert.Equal(
            [
                "Themes/DesignTokens.xaml",
                "Themes/AppThemeResources.xaml",
                "Themes/LiquidGlassStyles.xaml",
                "Themes/ButtonStyles.xaml",
                "Themes/NavigationStyles.xaml",
                "Themes/TabStyles.xaml",
                "Themes/FileSurfaceStyles.xaml",
                "Themes/MenuStyles.xaml",
            ],
            ThemeXaml.MergeOrder);

        var appXaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "App.xaml"));
        var navigation = appXaml.IndexOf("Themes/NavigationStyles.xaml", StringComparison.Ordinal);
        var tabs = appXaml.IndexOf("Themes/TabStyles.xaml", StringComparison.Ordinal);
        var surface = appXaml.IndexOf("Themes/FileSurfaceStyles.xaml", StringComparison.Ordinal);
        Assert.True(navigation >= 0 && tabs > navigation && surface > tabs);
    }

    [Fact]
    public void Tab_metrics_stay_within_the_title_bar_spec()
    {
        var tokens = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "DesignTokens.xaml"));
        Assert.Contains("x:Key=\"FilesMate.Control.Height.TitleBar\">48", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilesMate.Tab.Height\">38", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilesMate.Tab.MinWidth\">120", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilesMate.Tab.MaxWidth\">240", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilesMate.Tab.DragMinWidth\">24", tokens, StringComparison.Ordinal);

        var styles = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabStyles.xaml"));
        Assert.Contains("FilesMate.Tab.MinWidth", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Tab.MaxWidth", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Corner.Omnibar", styles, StringComparison.Ordinal);
        Assert.Contains("CloseButtonOverlayMode", styles, StringComparison.Ordinal);
        Assert.Contains("OnPointerOver", styles, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource FilesMate.Text.PrimaryBrush}\"", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"1", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"2", styles, StringComparison.Ordinal);

        var bar = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabBarStyles.xaml"));
        Assert.Contains("FilesMate.Tab.SelectedBrush", bar, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", bar, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"0\"", bar, StringComparison.Ordinal);
    }

    [Fact]
    public void Title_bar_keeps_icon_tabs_and_a_dedicated_drag_region()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml"));
        Assert.Contains("x:Name=\"AppTitleBar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Assets/Branding/FilesMate.svg", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Tabs\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleBar.Content", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyboardAcceleratorPlacementMode=\"Hidden\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TabStripFooter", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TabDragRegion\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CaptionPad\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Tab.DragMinWidth", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Tab.Height", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"T\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"W\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Modifiers=\"Control\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanReorderTabs=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanDragTabs=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AllowDropTabs=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanTearOutTabs=\"False\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CanTearOutTabs=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TabDroppedOutside", xaml, StringComparison.Ordinal);
        Assert.Contains("TabDragStarting", xaml, StringComparison.Ordinal);
        Assert.Contains("TabStripTail_Drop", xaml, StringComparison.Ordinal);
        Assert.Contains("AllowDrop=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsAddTabButtonVisible", xaml, StringComparison.Ordinal);
        var newTabButton = xaml.IndexOf("x:Name=\"NewTabButton\"", StringComparison.Ordinal);
        var dragRegion = xaml.IndexOf("x:Name=\"TabDragRegion\"", StringComparison.Ordinal);
        Assert.True(newTabButton >= 0 && dragRegion > newTabButton, "The add-tab button must sit beside the tabs, before the flexible drag region.");

        var styles = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabStyles.xaml"));
        Assert.Contains("HorizontalContentAlignment\" Value=\"Left\"", styles, StringComparison.Ordinal);
        Assert.Contains("TabBarStyles.xaml", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMateTabViewItemStyle", styles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TabViewItemHeaderPadding\">12,0,4,0</Thickness>", styles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TabViewSelectedItemHeaderPadding\">12,0,4,0</Thickness>", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"TabViewSelectedItemHeaderPadding\">0</Thickness>", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("8,8,0,0", styles, StringComparison.Ordinal);

        var bar = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabBarStyles.xaml"));
        Assert.Contains("Adapted from Files Community", bar, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", bar, StringComparison.Ordinal);
        Assert.Contains("FilesMate.ComboBox.BorderBrush", bar, StringComparison.Ordinal);
        Assert.Contains("TabViewSelectedItemBorderThickness", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("TopCornerRadiusFilterConverter", bar, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ContentPresenter\"",
            bar,
            StringComparison.Ordinal);
        Assert.Contains(
            "Foreground=\"{ThemeResource FilesMate.Text.PrimaryBrush}\"",
            bar,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Foreground=\"{ThemeResource TabViewItemHeaderForeground}\"",
            bar,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TemplatedParent}, Path=Foreground", bar, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_tab_uses_a_dedicated_neutral_brush_and_connects_to_the_content_chrome()
    {
        var styles = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabStyles.xaml"));
        var bar = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabBarStyles.xaml"));
        Assert.Contains(
            "TabContainer.Background\" Value=\"{ThemeResource FilesMate.Tab.SelectedBrush}\"",
            bar,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TabViewItemHeaderBackgroundSelected", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesMate.LiquidGlass.FillBrush", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Tab.SelectedCornerRadius", styles, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource FilesMate.Text.PrimaryBrush}\"", styles, StringComparison.Ordinal);
        Assert.Contains("HeaderTemplate", styles, StringComparison.Ordinal);
        Assert.Contains("TabContainer.CornerRadius", bar, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Tab.SelectedCornerRadius", bar, StringComparison.Ordinal);

        var window = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml"));
        var tabs = window.IndexOf("x:Name=\"Tabs\"", StringComparison.Ordinal);
        var content = window.IndexOf("x:Name=\"TabHost\"", tabs, StringComparison.Ordinal);
        Assert.True(tabs >= 0 && content > tabs);
        var tabBlock = window[tabs..content];
        Assert.Contains("VerticalAlignment=\"Bottom\"", tabBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeConnectionShelf", window, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", window, StringComparison.Ordinal);
        Assert.Contains(
            "x:Key=\"TabViewItemHeaderForeground\" ResourceKey=\"FilesMate.Text.PrimaryBrush\"",
            window,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Key=\"TabViewItemHeaderForegroundSelected\" ResourceKey=\"FilesMate.Text.PrimaryBrush\"",
            window,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_code_preserves_interaction_motion_and_the_last_tab()
    {
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));
        Assert.Contains("InputNonClientPointerSource", code, StringComparison.Ordinal);
        Assert.Contains("NonClientRegionKind.Passthrough", code, StringComparison.Ordinal);
        Assert.Contains("SetRegionRects", code, StringComparison.Ordinal);
        Assert.Contains("TabItems.Count <= 1", code, StringComparison.Ordinal);
        Assert.Contains("StringTable.Get(\"NewTab\")", code, StringComparison.Ordinal);
        Assert.Contains("TabHost.Content = content", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueuePriority.Low", code, StringComparison.Ordinal);
        Assert.Contains("LazyTabLoadSession", code, StringComparison.Ordinal);
        Assert.Contains("FilesMateTabViewItemStyle", code, StringComparison.Ordinal);
        Assert.Contains("item.Header = text", code, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Text.PrimaryBrush", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TranslateFadeAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FromMilliseconds", code, StringComparison.Ordinal);
        Assert.Contains("ContentTransitions", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabStyles.xaml")), StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabStyles.xaml")), StringComparison.Ordinal);
        Assert.Contains("PreferredHeightOption", code, StringComparison.Ordinal);
        Assert.Contains("TitleBarHeightOption.Tall", code, StringComparison.Ordinal);
        Assert.Contains("AddNavigatorTab(HomeLocation.Uri)", code, StringComparison.Ordinal);
        Assert.Contains("DetachTab", code, StringComparison.Ordinal);
        Assert.Contains("AttachTab", code, StringComparison.Ordinal);
        Assert.Contains("CloseIfEmpty", code, StringComparison.Ordinal);
        Assert.Contains("window.Activate()", code, StringComparison.Ordinal);
        Assert.Contains("SetTitleBar(AppTitleBar)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetTitleBar(TabDragRegion)", code, StringComparison.Ordinal);
        Assert.Contains("AddTabStripPassthrough", code, StringComparison.Ordinal);
        Assert.Contains("_tabDragging", code, StringComparison.Ordinal);
        Assert.Contains("TabDroppedOutside", code, StringComparison.Ordinal);
        Assert.Contains("TearOutToNewWindow", code, StringComparison.Ordinal);
        Assert.Contains("MoveTabToEnd", code, StringComparison.Ordinal);
        Assert.Contains("ContextFlyout", code, StringComparison.Ordinal);
        Assert.Contains("StringTable.Get(labelKey)", code, StringComparison.Ordinal);
        Assert.Contains("Tab_CloseOthers", code, StringComparison.Ordinal);
        Assert.Contains("Tab_CloseToTheRight", code, StringComparison.Ordinal);
        Assert.Contains("Tab_Duplicate", code, StringComparison.Ordinal);
        Assert.Contains("Tab_MoveToNewWindow", code, StringComparison.Ordinal);
        Assert.Contains("if (_shellHost is not null)", code, StringComparison.Ordinal);
        Assert.Contains("source.SetRegionRects(NonClientRegionKind.Caption", code, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableTabStripDrag", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CanDragItems", code, StringComparison.Ordinal);
        Assert.Contains("RestorePlacement", code, StringComparison.Ordinal);
        Assert.Contains("_restoringPlacement", code, StringComparison.Ordinal);
        Assert.Contains("WindowPlacementService", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Resize(new SizeInt32(1440, 900))", code, StringComparison.Ordinal);
        Assert.Contains("Equals(item.Header, text)", code, StringComparison.Ordinal);
        Assert.Contains("ScheduleNavigatorLoad", code, StringComparison.Ordinal);
        Assert.Contains("TabHost.Content is NavigatorPage && content is not NavigatorPage", code, StringComparison.Ordinal);
        var windowXaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml"));
        var host = windowXaml.IndexOf("x:Name=\"TabHost\"", StringComparison.Ordinal);
        Assert.True(host >= 0);
        Assert.Contains(
            "Background=\"Transparent\"",
            windowXaml.Substring(host, Math.Min(320, windowXaml.Length - host)),
            StringComparison.Ordinal);
        Assert.Contains("ResolveInitialPath", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LocationCaption.Title(page.ViewModel.AddressText)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AddressText ?? tab.RequestedPath", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_header_width_tokens_are_numeric_and_ordered()
    {
        var document = XDocument.Load(Path.Combine(ThemeXaml.AppRoot, "Themes", "DesignTokens.xaml"));
        var min = ReadDouble(document, "FilesMate.Tab.MinWidth");
        var max = ReadDouble(document, "FilesMate.Tab.MaxWidth");
        var height = ReadDouble(document, "FilesMate.Tab.Height");
        Assert.InRange(min, 100, 160);
        Assert.InRange(max, 200, 280);
        Assert.True(max > min);
        Assert.InRange(height, 36, 40);
    }

    private static double ReadDouble(XDocument document, string key)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var value = document.Root?
            .Elements()
            .FirstOrDefault(element => (string?)element.Attribute(xaml + "Key") == key)
            ?.Value;
        Assert.False(string.IsNullOrWhiteSpace(value), $"Missing token '{key}'.");
        return double.Parse(value!, CultureInfo.InvariantCulture);
    }
}
