using FilesMate.App.Controls.FileSurface;

namespace FilesMate.App.Tests.FileSurface;

public sealed class DragPreviewSelectionTests
{
    [Fact]
    public void First_three_items_in_display_order_form_the_preview()
    {
        string[] selection = [@"D:\folder", @"D:\script.ps1", @"D:\program.exe", @"D:\archive.zip"];
        var original = selection.ToArray();
        var preview = DragPreviewSelection.Paths(selection);
        Assert.Equal(selection.Take(3), preview);
        Assert.All(preview, path => Assert.Contains(path, selection));
        Assert.Equal(3, preview.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(original, selection);
    }

    [Fact]
    public void Repeated_paths_do_not_consume_another_layer()
    {
        Assert.Equal(new[] { @"D:\image.png", @"D:\a.zip", @"D:\b.txt" },
            DragPreviewSelection.Paths([@"D:\image.png", @"d:\IMAGE.PNG", @"D:\a.zip", @"D:\b.txt", @"D:\folder"]));
    }

    [Fact]
    public void Fewer_than_three_items_only_draw_existing_items()
    {
        Assert.Equal(new[] { @"D:\file.txt" }, DragPreviewSelection.Paths([@"D:\file.txt"]));
        Assert.Equal(new[] { @"D:\file.txt", @"D:\a.zip" }, DragPreviewSelection.Paths([@"D:\file.txt", @"D:\a.zip"]));
        Assert.Empty(DragPreviewSelection.Paths([]));
    }
}
