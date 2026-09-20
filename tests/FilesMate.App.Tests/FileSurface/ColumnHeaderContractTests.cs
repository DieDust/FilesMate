using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.FileSurface;

public sealed class ColumnHeaderContractTests
{
    [Fact]
    public void Header_highlight_is_inset_and_uses_the_shared_rounding()
    {
        var document = ThemeXaml.Load("Themes/FileSurfaceStyles.xaml");
        var style = document.Descendants().Single(element =>
            (string?)element.Attribute(ThemeXaml.Xaml + "Key") == "ColumnHeaderButtonStyle");
        var setters = style.Elements().ToDictionary(
            element => element.Attribute("Property")!.Value,
            element => element.Attribute("Value")!.Value);

        Assert.Equal("{StaticResource FilesMate.Corner.Small}", setters["CornerRadius"]);
        Assert.Equal("2,2", setters["Margin"]);
        Assert.Equal("24", setters["Height"]);
    }

    [Fact]
    public void Resize_targets_are_wide_and_not_covered_by_the_header_rule()
    {
        var document = ThemeXaml.Load("Controls/FileSurface/FileDetailsSurface.xaml");
        foreach (var name in new[] { "NameResize", "ModifiedResize", "TypeResize", "SizeResize" })
        {
            var grip = document.Descendants().Single(element =>
                (string?)element.Attribute(ThemeXaml.Xaml + "Name") == name);
            Assert.Equal("12", (string?)grip.Attribute("Width"));
            Assert.Equal("None", (string?)grip.Attribute("ManipulationMode"));
            Assert.Equal("ColumnResize_PointerCaptureLost", (string?)grip.Attribute("PointerCaptureLost"));
        }

        var header = document.Descendants().Single(element =>
            (string?)element.Attribute(ThemeXaml.Xaml + "Name") == "DetailsHeader");
        Assert.Equal("False", (string?)header.Elements().Last().Attribute("IsHitTestVisible"));
        Assert.Equal("HeaderScroller", (string?)header.Parent!.Attribute(ThemeXaml.Xaml + "Name"));
    }

    [Fact]
    public void Widths_and_hit_testing_share_the_horizontal_scroll_extent()
    {
        var code = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot, "Controls", "FileSurface", "FileDetailsSurface.xaml.cs"))
            + File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", "FileDetailsSurface.Columns.cs"));
        Assert.DoesNotContain("DisplayNameWidth", code, StringComparison.Ordinal);
        Assert.Contains("new GridLength(column.Visible ? column.Width : 0)", code, StringComparison.Ordinal);
        Assert.Contains("row.ApplyColumns(_detailColumns)", code, StringComparison.Ordinal);
        Assert.Contains("Repeater.MinWidth = _layout == FileLayoutKind.Details ? contentWidth : 0", code, StringComparison.Ordinal);
        Assert.Contains("HeaderScroller.ChangeView(Scroller.HorizontalOffset", code, StringComparison.Ordinal);
        Assert.Contains("x + Scroller.HorizontalOffset", code, StringComparison.Ordinal);
        Assert.Contains("point.Position.Y < 0", code, StringComparison.Ordinal);
        Assert.Contains("!((UIElement)sender).CapturePointer(e.Pointer)", code, StringComparison.Ordinal);
        Assert.Contains("_resizePointerId != e.Pointer.PointerId", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(96, 64, 64, 64)]
    [InlineData(560, 252, 132, 246)]
    [InlineData(560, 560, 560, 560)]
    public void Wide_rows_keep_every_column_reachable(double name, double modified, double type, double size)
    {
        var width = FileColumnLayout.RowWidth(name, modified, type, size);
        var lastColumnX = width - FileColumnLayout.ContentRight - 8;
        const double offset = 200;
        var screenX = lastColumnX - offset;
        Assert.Equal(0, FileColumnLayout.IndexFromPoint(
            screenX + offset, 10, 2, name, modified, type, size));
        Assert.Equal(-1, FileColumnLayout.IndexFromPoint(
            width, 10, 2, name, modified, type, size));
    }
}
