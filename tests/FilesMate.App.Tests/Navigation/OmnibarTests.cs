using System.Xml.Linq;

using FilesMate.App.Navigation;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Navigation;

public sealed class OmnibarTests
{
    [Fact]
    public async Task Folder_menu_returns_the_first_alphabetic_page_and_handles_missing_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 119; i >= 0; i--)
            {
                Directory.CreateDirectory(Path.Combine(root, $"Folder-{i:D3}"));
            }

            var items = await PathChildren.FoldersAsync(root, showHidden: false);
            Assert.Equal(Enumerable.Range(0, PathChildren.Limit).Select(i => $"Folder-{i:D3}"), items.Select(item => item.Name));
            Assert.Empty(await PathChildren.FoldersAsync(Path.Combine(root, "missing"), showHidden: false));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PathChildren.FoldersAsync(root, false, canceled.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Search_and_folder_requests_are_canceled_and_file_results_keep_their_target()
    {
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Omnibar", "Omnibar.xaml.cs"));
        Assert.Contains("_searchRequest?.Cancel()", code, StringComparison.Ordinal);
        Assert.Contains("index.SearchAsync(query, scope, rank, request.Token)", code, StringComparison.Ordinal);
        Assert.Contains("PathChildren.FoldersAsync", code, StringComparison.Ordinal);
        Assert.Contains("Unloaded +=", code, StringComparison.Ordinal);
        Assert.Contains("var path = hit.Path;", code, StringComparison.Ordinal);
        var navigator = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));
        Assert.Contains("OpenLaunchTarget(LaunchPath.Parse([\"/open\", path]))", navigator, StringComparison.Ordinal);
    }

    [Fact]
    public void Modes_follow_keyboard_and_escape()
    {
        var session = new OmnibarSession();
        session.SetPath(@"C:\Users\a\Documents");
        Assert.Equal(OmnibarMode.PathDisplay, session.Mode);

        session.BeginPathEdit();
        Assert.Equal(OmnibarMode.PathEdit, session.Mode);
        Assert.Equal(@"C:\Users\a\Documents", session.Draft);

        session.SetDraft(@"C:\Users\a\Downloads");
        Assert.True(session.Cancel());
        Assert.Equal(OmnibarMode.PathDisplay, session.Mode);
        Assert.Equal(@"C:\Users\a\Documents", session.Draft);
        Assert.Null(session.PathError);

        session.BeginSearch();
        session.SetFilter("report");
        Assert.Equal(OmnibarMode.PathDisplay, session.Mode);
        Assert.True(session.Cancel() is false);
        Assert.Equal("report", session.Filter);
        Assert.False(session.Cancel());
    }

    [Fact]
    public void Invalid_path_keeps_edit_mode_and_committed_location()
    {
        var session = new OmnibarSession();
        session.SetPath(@"C:\Users\a");
        session.BeginPathEdit();
        session.SetDraft("not-a-path");

        Assert.False(session.SubmitPath(_ => throw new ArgumentException("Path must be absolute.")));
        Assert.Equal(OmnibarMode.PathEdit, session.Mode);
        Assert.Equal("not-a-path", session.Draft);
        Assert.Equal(@"C:\Users\a", session.Path);
        Assert.Equal("Path must be absolute.", session.PathError);

        session.SetDraft(@"C:\Users\a\Music");
        Assert.True(session.SubmitPath(draft => draft));
        Assert.Equal(OmnibarMode.PathDisplay, session.Mode);
        Assert.Equal(@"C:\Users\a\Music", session.Path);
        Assert.Null(session.PathError);
    }

    [Fact]
    public void Search_does_not_replace_the_committed_path()
    {
        var session = new OmnibarSession();
        session.SetPath(@"D:\Projects\FilesMate");
        session.BeginSearch();
        session.SetFilter("report");

        Assert.Equal(OmnibarMode.PathDisplay, session.Mode);
        Assert.Equal(@"D:\Projects\FilesMate", session.Path);
        Assert.Equal("report", session.Filter);
        Assert.False(session.Cancel());
    }

    [Fact]
    public void Breadcrumb_segments_expose_human_readable_automation_text()
    {
        var segment = new PathSegment("FilesMate", @"D:\FilesMate");

        Assert.Equal("FilesMate", segment.ToString());
        Assert.False(segment.HasIcon);

        var tagged = new PathSegment("测试", TagLocation.Uri(1), LocationCaption.TagGlyph);
        Assert.True(tagged.HasIcon);
        Assert.Equal(LocationCaption.TagGlyph, tagged.Glyph);
    }

    [Fact]
    public void Path_children_list_immediate_folders_and_skip_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Alpha"));
        Directory.CreateDirectory(Path.Combine(root, "beta"));
        File.WriteAllText(Path.Combine(root, "skip.txt"), "x");
        try
        {
            var folders = PathChildren.Folders(root, showHidden: false);
            Assert.Equal(["Alpha", "beta"], folders.Select(item => item.Name));
            Assert.Empty(PathChildren.Folders(TagLocation.Uri(1), showHidden: false));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Overflow_hides_up_before_refresh()
    {
        var wide = OmnibarOverflow.ForWidth(440, forceCompact: false);
        Assert.True(wide.ShowForward && wide.ShowUp && wide.ShowRefresh);
        Assert.False(wide.ShowOverflow);

        var medium = OmnibarOverflow.ForWidth(420, forceCompact: false);
        Assert.True(medium.ShowForward && medium.ShowUp && medium.ShowRefresh);
        Assert.False(medium.ShowOverflow);

        var compact = OmnibarOverflow.ForWidth(380, forceCompact: false);
        Assert.True(compact.ShowForward && compact.ShowRefresh);
        Assert.False(compact.ShowUp);
        Assert.True(compact.ShowOverflow);

        var narrow = OmnibarOverflow.ForWidth(320, forceCompact: false);
        Assert.False(narrow.ShowForward || narrow.ShowUp || narrow.ShowRefresh);
        Assert.True(narrow.ShowOverflow);

        var forced = OmnibarOverflow.ForWidth(1920, forceCompact: true);
        Assert.False(forced.ShowForward || forced.ShowUp || forced.ShowRefresh);
        Assert.True(forced.ShowOverflow);
    }

    [Fact]
    public void Omnibar_keeps_the_path_bar_and_searches_beside_it()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Omnibar", "Omnibar.xaml"));
        Assert.Contains("FilesMate.Control.Height.Omnibar", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Corner.Omnibar", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource FilesMate.LiquidGlassSurfaceStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchSizer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchCluster\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SurfaceKind=\"Command\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"36\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchHost.Visibility", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"SearchWell\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchButton.Visibility", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchPlaceholderLabel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchSuggestPopup\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoSuggestBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SuggestionChosen", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Filter this folder", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"Search\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"FilterBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PathEdit", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Motion.Standard", xaml, StringComparison.Ordinal);
        Assert.Contains("TextControlBorderThemeThicknessFocused", xaml, StringComparison.Ordinal);
        Assert.Contains("IsSpellCheckEnabled=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTextPredictionEnabled=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextControlBackgroundFocused", xaml, StringComparison.Ordinal);
        Assert.Contains("TextControlBorderBrushFocused", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PathClearMask", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Padding=\"0,0,32,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Left\"", xaml, StringComparison.Ordinal);
        var breadcrumbs = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Omnibar", "Omnibar.Breadcrumbs.cs"));
        Assert.Contains("segment.HasIcon", breadcrumbs, StringComparison.Ordinal);
        Assert.Contains("Glyph = segment.Glyph", breadcrumbs, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Left\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CrumbChevron_Click", breadcrumbs, StringComparison.Ordinal);
        Assert.DoesNotContain("BreadcrumbBar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", xaml, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        Assert.Contains("Omnibar", page, StringComparison.Ordinal);
        Assert.Contains("SearchChosen=\"Omni_SearchChosen\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AddressBar", page, StringComparison.Ordinal);
        Assert.DoesNotContain("FilterBox", page, StringComparison.Ordinal);
        Assert.Contains("Key=\"F\"", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml")), StringComparison.Ordinal);

        var toolbar = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "NavigationToolbar.xaml"));
        Assert.Contains("Height=\"32\"", toolbar, StringComparison.Ordinal);
        Assert.Contains("Width=\"36\"", toolbar, StringComparison.Ordinal);
        Assert.Contains("OverflowButton", toolbar, StringComparison.Ordinal);
        Assert.Contains("Placement=\"Bottom\"", toolbar, StringComparison.Ordinal);

        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Omnibar", "Omnibar.xaml.cs"));
        Assert.DoesNotContain("MessageBox", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigate(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("e.OriginalSource is TextBlock", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_pointerSequence", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_breadcrumbSequence", code, StringComparison.Ordinal);
        Assert.Contains("CrumbName_Click", code, StringComparison.Ordinal);
        Assert.Contains("CrumbChevron_Click", code, StringComparison.Ordinal);
        Assert.Contains("RaiseCrumb", code, StringComparison.Ordinal);
        Assert.Contains("ShowCrumbFolders", code, StringComparison.Ordinal);
        Assert.Contains("PathChildren.Folders", code, StringComparison.Ordinal);
        Assert.DoesNotContain("{x:Bind Path,", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag = segment.Path", breadcrumbs, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", code, StringComparison.Ordinal);
        Assert.Contains("BeginPathEdit", code, StringComparison.Ordinal);
        Assert.Contains("SearchChosen", code, StringComparison.Ordinal);
        Assert.Contains("SearchAsync(query, scope, rank, request.Token)", code, StringComparison.Ordinal);
        Assert.Contains("PublishHits", code, StringComparison.Ordinal);
        Assert.Contains("RequestSearch", code, StringComparison.Ordinal);
        Assert.Contains("ScheduleOverflow", code, StringComparison.Ordinal);
        Assert.Contains("ApplyPendingOverflow", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Root_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyOverflow()", code, StringComparison.Ordinal);
        Assert.Contains("ApplySearchOpen(animate", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimateSearchWidth", code, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableDependentAnimation", code, StringComparison.Ordinal);
        Assert.Contains("SearchSizer.Width", code, StringComparison.Ordinal);
        Assert.Contains("OmnibarSearchLayout.Width", code, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<HomeSearchHit> _hits", code, StringComparison.Ordinal);
        Assert.Contains("DismissSearch", code, StringComparison.Ordinal);
        Assert.Contains("IsSearchSource", code, StringComparison.Ordinal);
        Assert.Contains("IsCrumbFolderSource", code, StringComparison.Ordinal);
        Assert.Contains("DismissCrumbFolders", code, StringComparison.Ordinal);
        Assert.Contains("AnimateCrumbFolder", code, StringComparison.Ordinal);
        Assert.Contains("PopupPlacementMode.BottomEdgeAlignedLeft", code, StringComparison.Ordinal);
        Assert.Contains("IsLightDismissEnabled=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CrumbFolderPopup\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ClearCrumbHover", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new MenuFlyout", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAt(chevron", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OverlayInputPassThroughElement", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAt(PathHost", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OnOutsideSearchPressed", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HookOutsideSearchPress", code, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlRoot.Content", code, StringComparison.Ordinal);
        Assert.DoesNotContain("        SearchButton.Visibility", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FilterBox", code, StringComparison.Ordinal);
        Assert.Contains("SearchPlaceholderHome", code, StringComparison.Ordinal);
        Assert.Contains("SearchPlaceholderFolder", code, StringComparison.Ordinal);
        Assert.Contains("SearchBox_GotFocus", code, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Selection.AccentBrush", code, StringComparison.Ordinal);
        Assert.Contains("SearchHitItem", xaml, StringComparison.Ordinal);
        Assert.Contains("SearchHits_ItemClick", code, StringComparison.Ordinal);
        Assert.Contains("FocusState.Keyboard", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchBox.Focus(FocusState.Programmatic)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"SearchCaret\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StartSearchCaret", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateTimer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromSeconds(0.5)", code, StringComparison.Ordinal);
        Assert.Contains("FilesMate.AddressBar.BorderBrush", xaml, StringComparison.Ordinal);
        var hitItem = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Omnibar", "SearchHitItem.xaml.cs"));
        Assert.Contains("ShellIconBinder.BindPath", hitItem, StringComparison.Ordinal);
        Assert.Contains("FocusManager.GetFocusedElement(XamlRoot)", code, StringComparison.Ordinal);
        Assert.Contains("PathBox_LostFocus", code, StringComparison.Ordinal);
        Assert.Contains("CancelMode();", code, StringComparison.Ordinal);
        Assert.Contains("_session.Mode == OmnibarMode.PathEdit ? _session.PathError : _session.Path", code, StringComparison.Ordinal);

        var navigator = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));
        Assert.Contains("ShellRoot.AddHandler", navigator, StringComparison.Ordinal);
        Assert.Contains("handledEventsToo: true", navigator, StringComparison.Ordinal);
        Assert.Contains("Omni.IsSearchSource", navigator, StringComparison.Ordinal);
        Assert.Contains("Omni.IsCrumbFolderSource", navigator, StringComparison.Ordinal);
        Assert.Contains("Omni.DismissSearch", navigator, StringComparison.Ordinal);
        Assert.Contains("Omni.DismissCrumbFolders", navigator, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlRoot.Content", navigator, StringComparison.Ordinal);
        Assert.Contains("File.Exists(normalized)", navigator, StringComparison.Ordinal);
        Assert.Contains("TryOpenAsFile", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Navigation", "PaneViewModel.cs")), StringComparison.Ordinal);

        var document = XDocument.Parse(xaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var pathBox = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "PathBox");
        var crumbs = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "Crumbs");
        var searchBox = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "SearchBox");
        Assert.Equal("10,7,0,0", (string?)pathBox.Attribute("Padding"));
        Assert.Equal("False", (string?)pathBox.Attribute("IsSpellCheckEnabled"));
        Assert.Equal("False", (string?)pathBox.Attribute("IsTextPredictionEnabled"));
        Assert.Equal("{ThemeResource FilesMate.Text.PrimaryBrush}", (string?)pathBox.Attribute("Foreground"));
        Assert.Equal("StackPanel", crumbs.Name.LocalName);
        var searchHost = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "SearchHost");
        var searchFill = searchBox.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "TextControlBackground");
        Assert.Equal("0", (string?)searchHost.Attribute("Padding"));
        Assert.Equal("{x:Null}", (string?)searchHost.Attribute("Shadow"));
        Assert.Equal("Transparent", (string?)searchFill.Attribute("Color"));
        Assert.Equal("{ThemeResource FilesMate.TextControl.CaretHostBrush}", (string?)searchBox.Attribute("Background"));
        Assert.Equal("12,10,4,0", (string?)searchBox.Attribute("Padding"));
        Assert.Equal("Center", (string?)searchBox.Attribute("VerticalContentAlignment"));
        Assert.Equal("TextBox", searchBox.Name.LocalName);
        Assert.Null(searchBox.Attribute("PlaceholderText"));
        Assert.Contains("FilesMate.TextControl.CaretHostBrush", xaml, StringComparison.Ordinal);
    }
}
