namespace FilesMate.App.Tests.Navigation;

public sealed class NavigationReentrancyContractTests
{
    [Fact]
    public void Navigator_binds_each_entry_snapshot_once()
    {
        var source = File.ReadAllText(AppSource("Views", "NavigatorPage.xaml.cs"));
        var start = source.IndexOf("private void ViewModel_PropertyChanged", StringComparison.Ordinal);
        var end = source.IndexOf("private void AfterNavigate", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var handler = source[start..end];

        Assert.Contains("nameof(PaneViewModel.Store)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("or nameof(PaneViewModel.ViewIndex)", handler, StringComparison.Ordinal);
        Assert.Contains("ScheduleChrome()", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncChrome();", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_and_toolbar_updates_are_deferred_off_the_repeater_bind()
    {
        var navigator = File.ReadAllText(AppSource("Views", "NavigatorPage.xaml.cs"));
        var start = navigator.IndexOf("private void FileSurface_SelectionChanged", StringComparison.Ordinal);
        var end = navigator.IndexOf("private void ApplySelectionUi", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var handler = navigator[start..end];
        Assert.Contains("TryEnqueue(ApplySelectionUi)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncCommandBar();", handler, StringComparison.Ordinal);

        var bind = File.ReadAllText(AppSource("Controls", "FileSurface", "FileDetailsSurface.xaml.cs"));
        Assert.Contains("var selectionChanged = false;", bind, StringComparison.Ordinal);
        Assert.Contains("if (selectionChanged)", bind, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_surface_binding_is_guarded_against_duplicate_resets()
    {
        var source = File.ReadAllText(AppSource("Controls", "FileSurface", "FileDetailsSurface.xaml.cs"));

        Assert.Contains("var hadPublishedView = _items.Store is not null || _items.Index is not null;", source, StringComparison.Ordinal);
        Assert.Contains("if (hadPublishedView)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Double_click_opens_a_folder_once_against_the_path_captured_in_the_parent()
    {
        var surface = File.ReadAllText(AppSource("Controls", "FileSurface", "FileDetailsSurface.xaml"));
        var surfaceCode = File.ReadAllText(AppSource("Controls", "FileSurface", "FileDetailsSurface.xaml.cs"));
        Assert.DoesNotContain("DoubleTapped=", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("OnDoubleTapped", surfaceCode, StringComparison.Ordinal);
        Assert.Contains("IsDoubleClick(_pressViewIndex)", surfaceCode, StringComparison.Ordinal);
        Assert.Contains("PointerUpdateKind.LeftButtonPressed", surfaceCode, StringComparison.Ordinal);
        Assert.Contains("_lastClickViewIndex = -1;", surfaceCode, StringComparison.Ordinal);

        var navigator = File.ReadAllText(AppSource("Views", "NavigatorPage.xaml.cs"));
        var start = navigator.IndexOf("private void FileSurface_OpenRequested", StringComparison.Ordinal);
        var end = navigator.IndexOf("private void RefreshAccelerator_Invoked", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var handler = navigator[start..end];
        Assert.Contains("var path = ViewModel.FullPath(entry);", handler, StringComparison.Ordinal);
        Assert.Contains("ScheduleNavigation(() => ViewModel.Navigate(path));", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleNavigation(() => ViewModel.Open", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Tag_decoration_never_starts_database_work_inside_repeater_layout()
    {
        var source = File.ReadAllText(AppSource("Views", "NavigatorPage.xaml.cs"));

        Assert.Contains("Task.Run(", source, StringComparison.Ordinal);
        Assert.Contains("ProbeTagCatalogAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("private readonly SemaphoreSlim _tagLoadGate = new(2, 2);", source, StringComparison.Ordinal);
        Assert.Contains("RefreshRealizedTags(entryId)", source, StringComparison.Ordinal);
        Assert.Contains("ScheduleNavigation", source, StringComparison.Ordinal);
        Assert.Contains("TryEnqueue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpsertFileIdentityAsync(identity)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void First_directory_load_waits_until_the_page_is_loaded()
    {
        var source = File.ReadAllText(AppSource("Views", "NavigatorPage.xaml.cs"));
        var ctorStart = source.IndexOf("public NavigatorPage", StringComparison.Ordinal);
        var ctorEnd = source.IndexOf("public PaneViewModel ViewModel", StringComparison.Ordinal);
        Assert.True(ctorStart >= 0 && ctorEnd > ctorStart);
        var ctor = source[ctorStart..ctorEnd];
        Assert.Contains("_startupPath = start;", ctor, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.Navigate(", ctor, StringComparison.Ordinal);
        Assert.DoesNotContain("FileSurface.SetLayout", ctor, StringComparison.Ordinal);

        var loadedStart = source.IndexOf("private void Page_Loaded", StringComparison.Ordinal);
        var loadedEnd = source.IndexOf("private void HookWidthStates", StringComparison.Ordinal);
        Assert.True(loadedStart >= 0 && loadedEnd > loadedStart);
        var loaded = source[loadedStart..loadedEnd];
        Assert.Contains("FileSurface.SetLayout", loaded, StringComparison.Ordinal);
        Assert.Contains("ScheduleNavigation(() => ViewModel.Navigate(startPath))", loaded, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_box_does_not_replace_the_path_bar()
    {
        var code = File.ReadAllText(AppSource("Controls", "Omnibar", "Omnibar.xaml.cs"));
        var xaml = File.ReadAllText(AppSource("Controls", "Omnibar", "Omnibar.xaml"));
        Assert.Contains("x:Name=\"SearchBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"2\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"Search\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SearchChosen", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigate(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimateSearchWidth", code, StringComparison.Ordinal);
        Assert.Contains("OmnibarSearchLayout", code, StringComparison.Ordinal);

        var navigator = File.ReadAllText(AppSource("Views", "NavigatorPage.xaml.cs"));
        Assert.Contains("Omni_SearchChosen", navigator, StringComparison.Ordinal);
        Assert.Contains("ScheduleNavigation(() => ViewModel.Navigate(path))", navigator, StringComparison.Ordinal);
    }

    private static string AppSource(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine([root, "src", "FilesMate.App", .. segments]);
    }
}
