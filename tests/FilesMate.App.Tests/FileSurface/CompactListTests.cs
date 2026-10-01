using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Services;
using FilesMate.Core.Entries;
using System.Text.Json;

namespace FilesMate.App.Tests.FileSurface;

public sealed class CompactListTests
{
    [Fact]
    public void Names_expand_the_column_until_the_reference_explorer_limit()
    {
        Assert.True(CompactListMetrics.WidthForName(400) > 400);
        Assert.Equal(800, CompactListMetrics.WidthForName(2_000));
        Assert.Equal(1_280, CompactListMetrics.WidthForName(2_000, 1.6));
        Assert.Equal(96, CompactListMetrics.WidthForName(0));
        var narrowLetters = CompactListMetrics.WidthForName(180);
        var wideLetters = CompactListMetrics.WidthForName(360);
        Assert.True(wideLetters > narrowLetters);
    }

    [Fact]
    public void Each_column_uses_its_own_width_for_positions_hit_testing_and_marquee()
    {
        var geometry = new CompactListGeometry(10, 23, [180, 800, 240]);
        Assert.Equal(180, geometry.LeftAt(1));
        Assert.Equal(980, geometry.LeftAt(2));
        Assert.Equal(1_220, geometry.ExtentWidth);
        Assert.Equal(0, geometry.ColumnAt(179.9));
        Assert.Equal(1, geometry.ColumnAt(180));
        Assert.Equal(2, geometry.ColumnAt(980));
        Assert.Equal(-1, geometry.ColumnAt(1_220));
        Assert.Equal(10, CompactListMetrics.IndexAt(192, 10, geometry, 28));
        Assert.Equal(22, CompactListMetrics.IndexAt(992, 60, geometry, 28));
        Assert.Equal(-1, CompactListMetrics.IndexAt(992, 90, geometry, 28));
        Assert.Equal(-1, CompactListMetrics.IndexAt(978, 10, geometry, 28));
        var hits = new List<int>();
        CompactListMetrics.Collect(195, 29, 1_100, 140, geometry, hits, 28);
        Assert.Equal([11, 12, 13, 14, 21, 22], hits);
    }

    [Fact]
    public void Variable_width_reveal_and_visible_range_use_accumulated_positions()
    {
        var geometry = new CompactListGeometry(10, 23, [180, 800, 240]);
        Assert.Equal(180, geometry.RevealOffset(10, 0, 500));
        Assert.Equal(980, geometry.RevealOffset(22, 0, 500));
        Assert.Equal(0, geometry.RevealOffset(0, 500, 500));
        Assert.Equal(new VisibleRange(1, 2, 0, 2), geometry.VisibleRange(200, 800));
    }

    [Fact]
    public void High_resolution_wheel_accumulates_whole_notches_without_partial_scrolling()
    {
        var geometry = new CompactListGeometry(10, 40, [180, 800, 240, 160]);
        var motion = new ListScrollState();
        for (var i = 0; i < 3; i++) Assert.False(motion.AddWheel(-30, false, geometry, 100.25, 1_220));
        Assert.False(motion.HasTarget);
        Assert.True(motion.AddWheel(-30, false, geometry, 100.25, 1_220));
        Assert.Equal(180, motion.Target);
        for (var i = 0; i < 3; i++) Assert.False(motion.AddWheel(-30, false, geometry, 100.25, 1_220));
        Assert.True(motion.AddWheel(-30, false, geometry, 100.25, 1_220));
        Assert.Equal(980, motion.Target);
    }

    [Fact]
    public void Rapid_notches_use_pending_targets_and_each_columns_actual_width()
    {
        var geometry = new CompactListGeometry(10, 40, [180, 800, 240, 160]);
        var motion = new ListScrollState();
        Assert.True(motion.AddWheel(-120, false, geometry, 0, 1_220));
        Assert.Equal(180, motion.Target);
        Assert.True(motion.AddWheel(-120, false, geometry, 0, 1_220));
        Assert.Equal(980, motion.Target);
        Assert.True(motion.AddWheel(-120, false, geometry, 180, 1_220));
        Assert.Equal(1_220, motion.Target);
        Assert.True(motion.AddWheel(-120, true, geometry, 180, 1_220));
        Assert.Equal(980, motion.Target);
        motion.ObserveOffset(180);
        Assert.True(motion.HasTarget);
        motion.ObserveOffset(980);
        Assert.False(motion.HasTarget);
        Assert.True(motion.AddWheel(120, true, geometry, 980, 1_220));
        Assert.Equal(1_220, motion.Target);
        Assert.True(motion.AddWheel(-120, false, geometry, 1_220, 1_220));
        Assert.False(motion.HasTarget);
        Assert.Equal(1_220, motion.Target);
    }

    [Fact]
    public void Scrollbar_positions_align_in_the_requested_direction_on_the_next_notch()
    {
        var geometry = new CompactListGeometry(10, 40, [180, 800, 240, 160]);
        Assert.Equal(180, geometry.WheelOffset(100.25, 1, 1_220));
        Assert.Equal(0, geometry.WheelOffset(100.25, -1, 1_220));
        Assert.Equal(180, geometry.WheelOffset(500.25, -1, 1_220));
        Assert.Equal(980, geometry.WheelOffset(500.25, 1, 1_220));
        Assert.Equal(0, geometry.WheelOffset(180, -1, 1_220));
        Assert.Equal(980, geometry.WheelOffset(179.9, 1, 1_220));
        Assert.Equal(0, geometry.WheelOffset(179.9, -1, 1_220));
        var motion = new ListScrollState();
        motion.AddWheel(-150, false, geometry, 0, 1_220);
        motion.Reset();
        Assert.False(motion.HasTarget);
        Assert.False(motion.AddWheel(-90, false, geometry, 500.25, 1_220));
        Assert.True(motion.AddWheel(-30, false, geometry, 500.25, 1_220));
        Assert.Equal(980, motion.Target);
    }

    [Fact]
    public void Reversing_partial_input_and_regrouping_discard_old_remainders()
    {
        var geometry = new CompactListGeometry(10, 40, [180, 800, 240, 160]);
        var motion = new ListScrollState();
        Assert.False(motion.AddWheel(-60, false, geometry, 980, 1_220));
        Assert.False(motion.AddWheel(60, false, geometry, 980, 1_220));
        Assert.True(motion.AddWheel(60, false, geometry, 980, 1_220));
        Assert.Equal(180, motion.Target);
        var regrouped = CompactListGeometry.Uniform(5, 40, 200);
        Assert.False(motion.AddWheel(-60, false, regrouped, 200, 1_400));
        Assert.True(motion.AddWheel(-60, false, regrouped, 200, 1_400));
        Assert.Equal(400, motion.Target);
    }

    [Fact]
    public void Last_page_has_an_aligned_scroll_limit_without_cutting_off_the_tail()
    {
        var geometry = new CompactListGeometry(10, 23, [180, 800, 240]);
        var extent = geometry.ExtentForViewport(500);
        Assert.Equal(1_480, extent);
        Assert.Equal(980, extent - 500);
        Assert.Equal(980, geometry.WheelOffset(180, 100, extent - 500));
        Assert.Equal(180, geometry.WheelOffset(980, -1, extent - 500));
        Assert.Equal(geometry.ExtentWidth, geometry.ExtentForViewport(1_400));
        Assert.Equal(1_200, CompactListGeometry.Uniform(10, 30, 400).ExtentForViewport(800));
        Assert.Equal(geometry.ExtentWidth, geometry.ExtentForViewport(200));
    }

    [Fact]
    public void Empty_and_oversized_columns_do_not_reverse_forward_wheel_input()
    {
        var empty = CompactListGeometry.Uniform(1, 0, 240);
        Assert.False(new ListScrollState().AddWheel(-120, false, empty, 0, 0));
        Assert.Equal(0, empty.ExtentForViewport(800));
        var geometry = new CompactListGeometry(10, 20, [180, 800]);
        Assert.Equal(500.25, geometry.WheelOffset(500.25, 1, 780));
        Assert.Equal(180, geometry.WheelOffset(500.25, -1, 780));
    }

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
