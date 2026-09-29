using FilesMate.App.Models;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private readonly CompactListLayout _listLayout = new();
    public int ListZoomPercent { get; private set; } = 100;
    private double ListScale => _layout == FileLayoutKind.List ? ListZoomPercent / 100d : 1;
    private AppearanceSettings RowTypography => _layout == FileLayoutKind.List ? _typography with
    { FileNameFontSize = _typography.FileNameFontSize * ListScale, FileDetailsFontSize = _typography.FileDetailsFontSize * ListScale } : _typography;

    public void SetListZoom(int percent)
    {
        percent = CompactListMetrics.ClampZoom(percent);
        if (ListZoomPercent == percent) return;
        var anchor = _realized.Where(row => row.IsLoaded).Select(row => row.ViewIndex).DefaultIfEmpty(0).Min();
        ListZoomPercent = percent;
        if (_layout == FileLayoutKind.List)
        {
            UpdateListMetrics();
            Repeater.InvalidateMeasure();
            Scroller.ChangeView(Math.Max(0, anchor / ListRows) * ListWidth, 0, null, true);
        }
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }
    private Layout CurrentItemLayout => _layout switch
    {
        FileLayoutKind.Grid => FileGridLayout,
        FileLayoutKind.List => _listLayout,
        _ => _stackLayout
    };
    private int ListRows => _listLayout.Rows;
    private double ListWidth => _listLayout.ColumnWidth;
    private double ActiveScrollOffset => _layout == FileLayoutKind.List ? Scroller.HorizontalOffset : Scroller.VerticalOffset;
    private double ActiveScrollableExtent => _layout == FileLayoutKind.List ? Scroller.ScrollableWidth : Scroller.ScrollableHeight;
    private void ChangeScrollOffset(double value)
    {
        if (_layout == FileLayoutKind.List) Scroller.ChangeView(value, 0, null, true);
        else Scroller.ChangeView(null, value, null, true);
    }
    private void UpdateListMetrics()
    {
        if (_layout != FileLayoutKind.List) return;
        _listLayout.Configure(CompactListMetrics.Rows(Scroller.ActualHeight, RowHeight), Math.Clamp(ViewportWidth() - 8, 160, CompactListMetrics.ColumnWidth * ListScale), RowHeight);
        foreach (var row in _realized) ApplyRowColumns(row);
    }
    private void ApplyRowColumns(FileRow row)
    {
        row.SetCompact(_layout == FileLayoutKind.List);
        row.SetIconScale(ListScale);
        if (_layout != FileLayoutKind.List)
        {
            row.ApplyColumns(_detailColumns);
            row.MinWidth = ViewportWidth();
            row.ApplyTypography(RowTypography, XamlRoot?.RasterizationScale ?? 1);
            row.SetIconScale(1);
            return;
        }
        row.MinWidth = 0;
        row.ApplyColumns(DetailsColumn.Defaults().Select(c => c with
        {
            Visible = c.Id == DetailsColumnId.Name,
            Width = c.Id == DetailsColumnId.Name ? ListWidth - 8 - FileColumnLayout.ContentLeft - FileColumnLayout.ContentRight
                - Math.Max(FileColumnLayout.GlyphWidth, FileColumnLayout.DetailsIconSize * ListScale + 4) - FileColumnLayout.AccentWidth : c.Width
        }).ToArray());
        row.ApplyTypography(RowTypography, XamlRoot?.RasterizationScale ?? 1);
        row.SetIconScale(ListScale);
    }
}
