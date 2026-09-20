using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Tests.DesignSystem;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.FileSurface;

public sealed class FileTileVisualStateTests
{
    [Fact]
    public void Tile_template_matches_the_row_interaction_states()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileTile.xaml"));
        foreach (var state in FileRowVisualStates.All)
        {
            Assert.Contains($"x:Name=\"{state}\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("FilesMate.Item.HoverBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Item.SelectedBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Selection.AccentBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IconHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IconImage\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"Placeholder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxLines=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Corner.Card", xaml, StringComparison.Ordinal);
        Assert.Contains("FileTileNameStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TagHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("VerticalAlignment=\"Bottom\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesMate.Item.HoverShadow", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyChrome", xaml, StringComparison.Ordinal);

        var styles = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "FileSurfaceStyles.xaml"));
        Assert.Contains("x:Key=\"FileTileNameStyle\"", styles, StringComparison.Ordinal);
        Assert.Contains("LineStackingStrategy", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptionTextBlockStyle", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Recycled_tiles_drop_icon_thumbnail_and_request_id()
    {
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileTile.xaml.cs"));
        Assert.Contains("GoToState", code, StringComparison.Ordinal);
        Assert.Contains("ResetVisual", code, StringComparison.Ordinal);
        var dragging = code.IndexOf("public void SetDragging", StringComparison.Ordinal);
        var dropTarget = code.IndexOf("public void SetDropTarget", dragging, StringComparison.Ordinal);
        Assert.Contains("_pressed = false", code[dragging..dropTarget], StringComparison.Ordinal);
        Assert.Contains("showNames: false", code, StringComparison.Ordinal);
        Assert.Contains("NameText.Width", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Placeholder.Visibility", code, StringComparison.Ordinal);
        Assert.Contains("UniformToFill", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyChrome", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Root.Background", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SubtleFillColor", code, StringComparison.Ordinal);

        var surface = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml.cs"));
        Assert.Contains("tile.ResetVisual()", surface, StringComparison.Ordinal);
        Assert.Contains("ApplyMetrics", surface, StringComparison.Ordinal);
    }

    [Fact]
    public void Photos_are_detected_without_opening_the_file()
    {
        Assert.True(FileRowFormatter.IsImage(new FileEntryCore(1, "shot.jpg", 1, 1, 1, FileAttributes.Normal, EntryKind.File)));
        Assert.False(FileRowFormatter.IsImage(new FileEntryCore(2, "notes.txt", 1, 1, 1, FileAttributes.Normal, EntryKind.File)));
        Assert.False(FileRowFormatter.IsImage(new FileEntryCore(3, "Photos", 0, 1, 1, FileAttributes.Directory, EntryKind.Directory)));
    }
}
