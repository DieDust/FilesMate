using System.Globalization;

using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Tests.DesignSystem;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.FileSurface;

public sealed class FileRowVisualStateTests
{
    [Fact]
    public void Row_template_declares_the_full_interaction_state_set()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileRow.xaml"));
        foreach (var state in FileRowVisualStates.All)
        {
            Assert.Contains($"x:Name=\"{state}\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("FilesMate.Item.HoverBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Item.SelectedBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Selection.AccentBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Text.SecondaryBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Control.Height.Row", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Column.Modified", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WidthStates", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CompactColumns", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyChrome", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Code_behind_computes_state_names_and_does_not_assign_brushes()
    {
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileRow.xaml.cs"));
        Assert.Contains("GoToState", code, StringComparison.Ordinal);
        Assert.Contains("ResetVisual", code, StringComparison.Ordinal);
        Assert.Contains("FileRowVisualStates.Resolve", code, StringComparison.Ordinal);
        var dragging = code.IndexOf("public void SetDragging", StringComparison.Ordinal);
        var dropTarget = code.IndexOf("public void SetDropTarget", dragging, StringComparison.Ordinal);
        Assert.Contains("_pressed = false", code[dragging..dropTarget], StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyChrome", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Root.Background", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AccentBar.Background", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SubtleFillColor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AccentFillColor", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_prefers_drop_drag_then_selected_pointer_and_focus()
    {
        Assert.Equal(FileRowVisualStates.DropTarget, FileRowVisualStates.Resolve(true, true, true, true, true, true));
        Assert.Equal(FileRowVisualStates.Dragging, FileRowVisualStates.Resolve(true, true, true, true, true, false));
        Assert.Equal(FileRowVisualStates.SelectedPressed, FileRowVisualStates.Resolve(true, true, true, true, false, false));
        Assert.Equal(FileRowVisualStates.SelectedPointerOver, FileRowVisualStates.Resolve(true, true, false, true, false, false));
        Assert.Equal(FileRowVisualStates.Selected, FileRowVisualStates.Resolve(true, false, false, true, false, false));
        Assert.Equal(FileRowVisualStates.Pressed, FileRowVisualStates.Resolve(false, true, true, true, false, false));
        Assert.Equal(FileRowVisualStates.PointerOver, FileRowVisualStates.Resolve(false, true, false, true, false, false));
        Assert.Equal(FileRowVisualStates.Focused, FileRowVisualStates.Resolve(false, false, false, true, false, false));
        Assert.Equal(FileRowVisualStates.Normal, FileRowVisualStates.Resolve(false, false, false, false, false, false));
    }

    [Fact]
    public void Hidden_entries_are_marked_without_losing_the_name()
    {
        var hidden = FileRowFormatter.Format(
            new FileEntryCore(1, "secret.txt", 1, 1, 1, FileAttributes.Hidden, EntryKind.File),
            selected: false);
        var visible = FileRowFormatter.Format(
            new FileEntryCore(2, "open.txt", 1, 1, 1, FileAttributes.Normal, EntryKind.File),
            selected: true);

        Assert.True(hidden.IsHidden);
        Assert.Equal("secret.txt", hidden.Name);
        Assert.False(visible.IsHidden);
        Assert.True(visible.IsSelected);
        Assert.True(FileRowFormatter.IsGhosted(FileAttributes.System));
        Assert.True(FileRowFormatter.IsGhosted(FileAttributes.Hidden | FileAttributes.System));
        Assert.False(FileRowFormatter.IsGhosted(FileAttributes.Normal));

        var stem = FileRowFormatter.DisplayName(
            new FileEntryCore(1, "secret.txt", 1, 1, 1, FileAttributes.Hidden, EntryKind.File),
            showFileExtensions: false);
        Assert.Equal("secret", stem);
        Assert.Equal(
            ".gitignore",
            FileRowFormatter.DisplayName(
                new FileEntryCore(3, ".gitignore", 1, 1, 1, FileAttributes.Normal, EntryKind.File),
                showFileExtensions: false));
        Assert.Equal(
            "notes.txt",
            FileRowFormatter.DisplayName(
                new FileEntryCore(4, @"C:\Tagged\notes.txt", 1, 1, 1, FileAttributes.Normal, EntryKind.File),
                showFileExtensions: true));
    }

    [Fact]
    public void Details_rows_keep_modified_type_and_size_columns()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileRow.xaml"));
        Assert.Contains("x:Name=\"ModifiedText\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"HiddenMark\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Typography.NumeralAlignment=\"Tabular\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TypeText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SizeText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"12,1,16,1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Icon.Size.Medium", xaml, StringComparison.Ordinal);
        Assert.Contains("Padding=\"4,0,8,0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ModifiedText.Visibility", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AdaptiveTrigger", xaml, StringComparison.Ordinal);

        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileRow.xaml.cs"));
        Assert.Contains("ShowFolderSizes", code, StringComparison.Ordinal);
        Assert.Contains("FolderSizeCache", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueuePriority.Low", code, StringComparison.Ordinal);
        Assert.Equal(
            string.Empty,
            FileRowFormatter.FormatSize(new FileEntryCore(9, "Photos", 0, 1, 1, FileAttributes.Directory, EntryKind.Directory)));
        Assert.Equal(
            string.Empty,
            FileRowFormatter.FormatSize(new FileEntryCore(10, "Photos", 4096, 1, 1, FileAttributes.Directory, EntryKind.Directory)));
    }

    [Fact]
    public void Header_and_recycle_path_stay_aligned_with_the_row_contract()
    {
        var header = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileDetailsSurface.xaml"));
        Assert.Contains("HeaderRow\" Height=\"0\"", header, StringComparison.Ordinal);
        Assert.Contains("DetailsHeader", header, StringComparison.Ordinal);
        Assert.Contains("Padding=\"12,0,16,0\"", header, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NameResize\"", header, StringComparison.Ordinal);
        Assert.Contains("ColumnResize_PointerPressed", header, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", header, StringComparison.Ordinal);
        Assert.Equal(28, FileColumnLayout.RowHeight);
        Assert.Equal(0, FileColumnLayout.RowGap);
        Assert.Equal(28, FileColumnLayout.RowStride);
        Assert.Equal(24, FileColumnLayout.GlyphWidth);
        Assert.Equal(4, FileColumnLayout.NameCellPad);
        Assert.Equal(20, FileColumnLayout.DetailsIconSize);
        Assert.Equal(31, FileColumnLayout.NameHeaderPad);

        var row = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileRow.xaml"));
        Assert.Contains("x:Name=\"IconFrame\"", row, StringComparison.Ordinal);
        Assert.Equal(-1, FileColumnLayout.IndexFromPoint(4, 10, 10, 240, 148, 100, 88));
        Assert.Equal(0, FileColumnLayout.IndexFromPoint(20, 10, 10, 240, 148, 100, 88));
        Assert.Equal(-1, FileColumnLayout.IndexFromPoint(900, 10, 10, 240, 148, 100, 88));
        Assert.Equal(-1, FileColumnLayout.IndexFromPoint(20, 27, 10, 240, 148, 100, 88));
        Assert.Equal(1, FileColumnLayout.IndexFromPoint(20, 32, 10, 240, 148, 100, 88));
        Assert.Contains("FilesMate.Column.Modified", header, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ModifiedHeader\"", header, StringComparison.Ordinal);
        Assert.DoesNotContain("WidthStates", header, StringComparison.Ordinal);
        Assert.DoesNotContain("CompactColumns", header, StringComparison.Ordinal);
        Assert.DoesNotContain("ModifiedHeader.Visibility", header, StringComparison.Ordinal);
        Assert.Contains("ElementClearing", header, StringComparison.Ordinal);

        var surface = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileDetailsSurface.xaml.cs"));
        Assert.Contains("row.ResetVisual()", surface, StringComparison.Ordinal);
        Assert.Contains("MotionDurations.Fast", surface, StringComparison.Ordinal);
        Assert.Contains("ScheduleVisibleRange", surface, StringComparison.Ordinal);
        Assert.Contains("ResetFolderSizeWalks", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("Scroller.SizeChanged += (_, _) => UpdateVisibleRange()", surface, StringComparison.Ordinal);

        var tokens = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "DesignTokens.xaml"));
        Assert.Contains("FilesMate.Column.Modified", tokens, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Item.HiddenOpacity", tokens, StringComparison.Ordinal);
        Assert.Contains("0.48", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void Whole_row_stays_hit_testable_across_blank_column_stretches()
    {
        // WinUI only hit-tests panels that paint a Background. Without one, the empty left part
        // of the size column and the accent gutter fall through to the surface and the row
        // loses PointerOver the moment the cursor leaves a text cell.
        var row = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileRow.xaml"));
        var name = row.IndexOf("x:Name=\"Root\"", StringComparison.Ordinal);
        Assert.True(name >= 0);
        var open = row.LastIndexOf('<', name);
        var close = row.IndexOf('>', name);
        Assert.True(open >= 0 && close > open);
        var tag = row[open..close];
        Assert.StartsWith("<Grid", tag, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", tag, StringComparison.Ordinal);
        Assert.DoesNotContain("IsHitTestVisible=\"False\"", tag, StringComparison.Ordinal);
    }

    [Fact]
    public void Details_icon_host_does_not_circular_clip_the_glyph()
    {
        var row = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileRow.xaml"));
        var name = row.IndexOf("x:Name=\"IconFrame\"", StringComparison.Ordinal);
        Assert.True(name >= 0);
        var open = row.LastIndexOf('<', name);
        var close = row.IndexOf('>', name);
        Assert.True(open >= 0 && close > open);
        var tag = row[open..close];
        Assert.DoesNotContain("CornerRadius", tag, StringComparison.Ordinal);
    }

    [Fact]
    public void Modified_timestamps_keep_fixed_width_numeric_fields()
    {
        Assert.Equal("yyyy/MM/dd HH:mm", FileRowFormatter.PadDateTimePattern("yyyy/M/d H:mm"));
        Assert.Equal("MM/dd/yyyy hh:mm tt", FileRowFormatter.PadDateTimePattern("M/d/yyyy h:mm tt"));
        var utc = new DateTime(2026, 6, 5, 0, 49, 0, DateTimeKind.Local).ToUniversalTime().Ticks;
        Assert.Equal("2026-06-05 00:49", FileRowFormatter.FormatModified(utc, DateFormatKind.Iso));
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        try
        {
            Assert.Equal("2026/06/05 00:49", FileRowFormatter.FormatModified(utc, DateFormatKind.System));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
