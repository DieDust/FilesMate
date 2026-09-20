using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.FileSurface;

public sealed class GridLayoutTests
{
    [Fact]
    public void Zoom_can_reach_every_large_size_and_step_back_without_skipping()
    {
        var preset = GridSizePreset.Small;
        foreach (var expected in GridSizePreset.All.Skip(1))
        {
            var next = GridSizePreset.Step(FileLayoutKind.Grid, preset, 1);
            Assert.Equal(FileLayoutKind.Grid, next.Layout);
            Assert.Equal(expected, next.Preset);
            preset = next.Preset;
        }

        foreach (var expected in GridSizePreset.All.Reverse().Skip(1))
        {
            var next = GridSizePreset.Step(FileLayoutKind.Grid, preset, -1);
            Assert.Equal(FileLayoutKind.Grid, next.Layout);
            Assert.Equal(expected, next.Preset);
            preset = next.Preset;
        }
    }

    [Fact]
    public void Tile_states_keep_the_same_content_inset()
    {
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(
            ThemeXaml.AppRoot, "Controls", "FileSurface", "FileTile.xaml"));
        var states = document.Descendants().Where(element => element.Name.LocalName == "VisualState");
        Assert.All(states, state =>
        {
            var setters = state.Descendants().Where(element => element.Name.LocalName == "Setter").ToArray();
            var border = setters.Single(element => (string?)element.Attribute("Target") == "Root.BorderThickness");
            var padding = setters.Single(element => (string?)element.Attribute("Target") == "Root.Padding");
            Assert.Equal(2, int.Parse(border.Attribute("Value")!.Value) + int.Parse(padding.Attribute("Value")!.Value));
        });
    }

    [Fact]
    public void Presets_expose_stable_item_icon_and_text_metrics()
    {
        Assert.Equal([96, 120, 160, 200, 256, 320, 384], GridSizePreset.All.Select(preset => preset.Slot));
        foreach (var preset in GridSizePreset.All)
        {
            Assert.True(preset.IconSize < preset.ItemWidth, $"{preset.Slot} icon must fit the tile.");
            Assert.True(preset.TextHeight >= 32, $"{preset.Slot} needs two text lines.");
            Assert.InRange(preset.Gutter, 2, 8);
            Assert.True(preset.ItemHeight > preset.IconSize + preset.TextHeight);
        }

        Assert.Equal(120, GridSizePreset.Default.Slot);
        Assert.Equal(GridSizePreset.Medium, GridSizePreset.Default);
        Assert.True(GridSizePreset.Medium.ChromeWidth < GridSizePreset.Medium.ItemWidth);
        Assert.True(GridSizePreset.Medium.ChromeLeft > 0);
        Assert.Equal(80, GridSizePreset.Medium.ItemWidth);
        Assert.Equal(48, GridSizePreset.Medium.IconSize);
    }

    [Fact]
    public void Column_count_is_stable_for_the_same_viewport()
    {
        const double viewport = 1100;
        var first = GridSizePreset.Columns(viewport, GridSizePreset.Medium);
        var second = GridSizePreset.Columns(viewport, GridSizePreset.Medium);
        Assert.Equal(first, second);
        Assert.Equal(13, first);
        Assert.Equal(14, GridSizePreset.Columns(viewport, GridSizePreset.Small));
        Assert.Equal(9, GridSizePreset.Columns(viewport, GridSizePreset.Large));
        Assert.Equal(7, GridSizePreset.Columns(viewport, GridSizePreset.ExtraLarge));
        Assert.True(GridSizePreset.ItemWidthFor(viewport, GridSizePreset.Medium) >= GridSizePreset.Medium.ItemWidth);
        Assert.Equal(GridSizePreset.Medium.ItemWidth, GridSizePreset.ItemWidthFor(viewport, GridSizePreset.Medium));
        Assert.Equal(GridSizePreset.Medium.ItemWidth, GridSizePreset.ItemWidthFor(1920, GridSizePreset.Medium));
    }

    [Fact]
    public void Layout_switch_does_not_create_a_second_entry_store()
    {
        var surface = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml"));
        var start = surface.IndexOf("public void SetLayout", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var chunk = surface[start..Math.Min(surface.Length, start + 1400)];
        Assert.Contains("Repeater.Layout", chunk, StringComparison.Ordinal);
        Assert.Contains("ItemTemplate", chunk, StringComparison.Ordinal);
        Assert.Contains("Repeater.ItemsSource = null", chunk, StringComparison.Ordinal);
        Assert.Contains("_layoutReady && _layout == kind", chunk, StringComparison.Ordinal);
        Assert.DoesNotContain("new EntryStore", chunk, StringComparison.Ordinal);
        Assert.DoesNotContain("new EntryItemsSource", chunk, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLayout(FileLayoutKind.Grid)", surface, StringComparison.Ordinal);
        Assert.Contains("ItemTemplate=\"{StaticResource TileTemplate}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FileGridLayout\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsJustification=\"Start\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MotionDurations.Fast", surface, StringComparison.Ordinal);
        Assert.Contains("PointerWheelChangedEvent", surface, StringComparison.Ordinal);
        Assert.Contains("GridSizePreset.Step", surface, StringComparison.Ordinal);
        Assert.Contains("ScheduleTileMetricsRefresh", surface, StringComparison.Ordinal);
        Assert.Contains("_tileMetricsPending", surface, StringComparison.Ordinal);
        Assert.Contains("GridSizePreset.IndexFromPoint", surface, StringComparison.Ordinal);
        Assert.Contains("CollectIndicesInRect", surface, StringComparison.Ordinal);
        Assert.Contains("ApplyMarqueeSelection", surface, StringComparison.Ordinal);
        Assert.Contains("ReplaceFromViewIndices", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Clamp((int)(x / stride)", surface, StringComparison.Ordinal);

        var prepared = surface.IndexOf("private void Repeater_ElementPrepared", StringComparison.Ordinal);
        Assert.True(prepared >= 0);
        var clearing = surface.IndexOf("private void Repeater_ElementClearing", prepared, StringComparison.Ordinal);
        var preparedChunk = surface[prepared..clearing];
        var metrics = preparedChunk.IndexOf("tile.ApplyMetrics", StringComparison.Ordinal);
        var binding = preparedChunk.IndexOf("tile.Bind", StringComparison.Ordinal);
        Assert.True(metrics >= 0 && metrics < binding, "Recycled tiles must use current dimensions before binding.");
    }

    [Fact]
    public void Ctrl_wheel_steps_between_details_and_grid_presets()
    {
        Assert.Equal(
            (FileLayoutKind.Grid, GridSizePreset.Small),
            GridSizePreset.Step(FileLayoutKind.Details, GridSizePreset.Medium, 1));
        Assert.Equal(
            (FileLayoutKind.Details, GridSizePreset.Medium),
            GridSizePreset.Step(FileLayoutKind.Details, GridSizePreset.Medium, -1));
        Assert.Equal(
            (FileLayoutKind.Grid, GridSizePreset.Large),
            GridSizePreset.Step(FileLayoutKind.Grid, GridSizePreset.Medium, 1));
        Assert.Equal(
            (FileLayoutKind.Grid, GridSizePreset.Small),
            GridSizePreset.Step(FileLayoutKind.Grid, GridSizePreset.Medium, -1));
        Assert.Equal(
            (FileLayoutKind.Details, GridSizePreset.Small),
            GridSizePreset.Step(FileLayoutKind.Grid, GridSizePreset.Small, -1));
        Assert.Equal(
            (FileLayoutKind.Grid, GridSizePreset.Huge),
            GridSizePreset.Step(FileLayoutKind.Grid, GridSizePreset.ExtraLarge, 1));
        Assert.Equal((FileLayoutKind.Grid, GridSizePreset.Maximum),
            GridSizePreset.Step(FileLayoutKind.Grid, GridSizePreset.Maximum, 1));
        Assert.Equal(256, GridSizePreset.Maximum.IconSize);
        Assert.Equal(3, GridSizePreset.Columns(1100, GridSizePreset.Maximum));
        Assert.Equal("Layout_MediumIcons", GridSizePreset.Medium.ZoomKey);
    }

    [Fact]
    public void Preference_saves_do_not_reapply_the_default_view_to_open_tabs()
    {
        var page = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Views",
            "NavigatorPage.xaml.cs"));
        var start = page.IndexOf("private void ExplorerPreferencesChanged", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var chunk = page[start..Math.Min(page.Length, start + 700)];
        Assert.Contains("RebindVisibleEntries", chunk, StringComparison.Ordinal);
        Assert.DoesNotContain("ToFileLayout(preferences.DefaultView)", chunk, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLayout(layout)", chunk, StringComparison.Ordinal);
        Assert.Contains("FileSurface.SetLayout(ToFileLayout(App.ExplorerPreferences.DefaultView))", page, StringComparison.Ordinal);
        Assert.Contains("_appliedStartupLayout", page, StringComparison.Ordinal);
        Assert.Contains("PersistFolderView(_leftVm)", page, StringComparison.Ordinal);
        Assert.Contains("PersistFolderView(_rightVm)", page, StringComparison.Ordinal);
        var customization = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.Customization.cs"));
        Assert.Contains("_viewRevisions.GetValueOrDefault(vm) != revision", customization, StringComparison.Ordinal);
        Assert.Contains("_applyingView.Contains(vm)", customization, StringComparison.Ordinal);
        Assert.DoesNotContain("SetExplorerPreferencesAsync", customization, StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_wheel_updates_are_deferred_out_of_the_pointer_event_and_snapshot_realized_tiles()
    {
        var surface = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml.cs"));

        Assert.Contains("_zoomChangePending", surface, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueuePriority.Low", surface, StringComparison.Ordinal);
        Assert.Contains("_tiles.ToArray()", surface, StringComparison.Ordinal);
        Assert.Contains("_pendingZoomGeneration", surface, StringComparison.Ordinal);
        Assert.Contains("generation != _generation", surface, StringComparison.Ordinal);
        Assert.Contains("!IsLoaded", surface, StringComparison.Ordinal);
        Assert.Contains("ResetPendingZoom", surface, StringComparison.Ordinal);
        Assert.Contains("Deferred zoom update failed", surface, StringComparison.Ordinal);
        Assert.Contains("ScheduleTileMetricsRefresh", surface, StringComparison.Ordinal);
        Assert.Contains("Deferred tile metrics update failed", surface, StringComparison.Ordinal);
    }

    [Fact]
    public void Grid_hit_test_ignores_gutters_and_the_unused_margin_after_the_last_column()
    {
        var preset = GridSizePreset.Medium;
        const int columns = 3;
        const int count = 5;
        var x0 = ContentX(preset, 0);
        var x2 = ContentX(preset, 2);
        var y0 = ContentY(preset, 0);
        var y1 = ContentY(preset, 1);

        Assert.Equal(0, GridSizePreset.IndexFromPoint(x0, y0, count, columns, preset));
        Assert.Equal(2, GridSizePreset.IndexFromPoint(x2, y0, count, columns, preset));
        Assert.Equal(-1, GridSizePreset.IndexFromPoint(1, 1, count, columns, preset));
        Assert.Equal(-1, GridSizePreset.IndexFromPoint(preset.ItemWidth + 1, y0, count, columns, preset));
        Assert.Equal(-1, GridSizePreset.IndexFromPoint((columns * (preset.ItemWidth + preset.Gutter)) + 8, y0, count, columns, preset));
        Assert.Equal(-1, GridSizePreset.IndexFromPoint(x0, preset.ItemHeight + 1, count, columns, preset));
        Assert.Equal(3, GridSizePreset.IndexFromPoint(x0, y1, count, columns, preset));
        Assert.Equal(-1, GridSizePreset.IndexFromPoint(x2, y1, count, columns, preset));
        Assert.Equal(-1, GridSizePreset.IndexFromPoint(-1, y0, count, columns, preset));
        Assert.Equal(-1, GridSizePreset.IndexFromPoint(x0, y0, 0, columns, preset));
    }

    [Fact]
    public void Marquee_selects_tiles_whose_content_intersects_the_rectangle()
    {
        var preset = GridSizePreset.Medium;
        var hits = new List<int>();
        GridSizePreset.CollectIndicesInRect(
            ContentX(preset, 0) - 4,
            ContentY(preset, 0) - 4,
            ContentX(preset, 2) + 8,
            ContentY(preset, 0) + 8,
            5,
            3,
            preset,
            hits);
        Assert.Equal([0, 1, 2], hits);

        hits.Clear();
        GridSizePreset.CollectIndicesInRect(
            preset.ItemWidth + 0.5,
            0,
            preset.ItemWidth + preset.Gutter - 0.5,
            GridSizePreset.HighlightPad + 8,
            5,
            3,
            preset,
            hits);
        Assert.Empty(hits);

        hits.Clear();
        GridSizePreset.CollectIndicesInRect(
            ContentX(preset, 0) - 4,
            ContentY(preset, 1) - 4,
            ContentX(preset, 0) + 8,
            ContentY(preset, 1) + 8,
            5,
            3,
            preset,
            hits);
        Assert.Equal([3], hits);
    }

    private static double ContentX(GridSizePreset preset, int column) =>
        (column * (preset.ItemWidth + preset.Gutter)) + preset.ChromeLeft + 8;

    private static double ContentY(GridSizePreset preset, int row) =>
        (row * (preset.ItemHeight + preset.Gutter)) + GridSizePreset.HighlightPad + 8;
}
