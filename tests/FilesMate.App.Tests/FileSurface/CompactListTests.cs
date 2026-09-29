using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Services;
using FilesMate.Core.Entries;
using System.Text.Json;

namespace FilesMate.App.Tests.FileSurface;

public sealed class CompactListTests
{
    [Fact]
    public void List_zoom_clamps_and_round_trips_without_changing_view_mode()
    {
        Assert.Equal(120, CompactListMetrics.StepZoom(100, 1));
        Assert.Equal(80, CompactListMetrics.StepZoom(80, -1));
        Assert.Equal(160, CompactListMetrics.StepZoom(160, 1));
        var view = new FolderViewSettings(false, 120, EntrySort.Name, List: true, ListZoomPercent: 140);
        var restored = JsonSerializer.Deserialize<FolderViewSettings>(JsonSerializer.Serialize(view))!;
        Assert.True(restored.List);
        Assert.Equal(140, restored.ListZoomPercent);
        Assert.Equal(100, JsonSerializer.Deserialize<FolderViewSettings>("{\"List\":true}")!.ListZoomPercent);
    }
    [Fact]
    public void Hit_testing_fills_each_column_top_to_bottom_and_excludes_empty_cells()
    {
        var rows = CompactListMetrics.Rows(288);
        Assert.Equal(10, rows);
        Assert.Equal(0, CompactListMetrics.IndexAt(12, 10, 23, rows, 260));
        Assert.Equal(9, CompactListMetrics.IndexAt(12, 261, 23, rows, 260));
        Assert.Equal(10, CompactListMetrics.IndexAt(272, 10, 23, rows, 260));
        Assert.Equal(22, CompactListMetrics.IndexAt(532, 60, 23, rows, 260));
        Assert.Equal(-1, CompactListMetrics.IndexAt(532, 90, 23, rows, 260));
        Assert.Equal(-1, CompactListMetrics.IndexAt(257, 10, 23, rows, 260));
        Assert.Equal(-1, CompactListMetrics.IndexAt(12, 281, 23, rows, 260));
        Assert.Equal(1, CompactListMetrics.Rows(1));
    }
    [Fact]
    public void Marquee_can_select_across_scrolled_columns_without_selecting_empty_tail()
    {
        var selected = new List<int>();
        CompactListMetrics.Collect(270, 29, 780, 140, 23, 10, 260, selected);
        Assert.Equal([11, 12, 13, 14, 21, 22], selected);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(36)]
    [InlineData(44)]
    public void Row_and_column_gaps_are_blank_for_both_press_and_marquee(double height)
    {
        const double width = 260;
        Assert.Equal(-1, CompactListMetrics.IndexAt(60, height, 30, 10, width, height));
        Assert.Equal(-1, CompactListMetrics.IndexAt(width - 12, 10, 30, 10, width, height));
        Assert.Equal(-1, CompactListMetrics.IndexAt(width + 6, 10, 30, 10, width, height));
        var hits = new List<int>();
        CompactListMetrics.Collect(40, height - 1, 140, height + 1, 30, 10, width, hits, height);
        Assert.Empty(hits);
        CompactListMetrics.Collect(width - 12, 3, width + 6, height * 3, 30, 10, width, hits, height);
        Assert.Empty(hits);
        CompactListMetrics.Collect(40, height, width - 12, height * 3, 30, 10, width, hits, height);
        Assert.Equal([1, 2], hits);
    }
    [Fact]
    public void List_view_round_trips_and_older_folder_view_configs_keep_their_layout()
    {
        var current = new FolderViewSettings(false, 120, EntrySort.Name, List: true);
        Assert.True(JsonSerializer.Deserialize<FolderViewSettings>(JsonSerializer.Serialize(current))!.List);
        Assert.False(JsonSerializer.Deserialize<FolderViewSettings>("{\"Details\":true,\"GridSlot\":120}")!.List);
    }
}
