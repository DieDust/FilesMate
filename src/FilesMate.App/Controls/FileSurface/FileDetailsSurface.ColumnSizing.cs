using FilesMate.App.Models;
using FilesMate.App.Views;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private string? ColumnAt(double? x)
    {
        if (x is null) return _detailColumns.FirstOrDefault(c => c.Visible)?.Key;
        var offset = FileColumnLayout.AccentWidth;
        foreach (var column in _detailColumns.Where(c => c.Visible))
        {
            var width = column.Width + (column.Id == DetailsColumnId.Name ? FileColumnLayout.GlyphWidth : 0);
            if (x >= offset && x < offset + width) return column.Key;
            offset += width;
        }
        return null;
    }
    internal void FitColumn(string? key)
    {
        var measure = new TextBlock();
        _detailColumns = _detailColumns.Select(column =>
        {
            if (!column.Visible || key is not null && column.Key != key) return column;
            FileTypography.Apply(measure, _typography, column.Id == DetailsColumnId.Name ? _typography.FileNameFontSize : _typography.FileDetailsFontSize);
            var texts = _realized.Select(row => row.ColumnText(column)).Prepend(column.Title);
            var width = texts.Select(text =>
            {
                measure.Text = text;
                measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return measure.DesiredSize.Width;
            }).DefaultIfEmpty(64).Max() + 30;
            return column with { Width = Math.Clamp(Math.Ceiling(width), column.Id == DetailsColumnId.Name ? 96 : 64, 1200) };
        }).ToArray();
        if (key is null or "Name") RememberManualNameWidth();
        ApplyDetailsColumns(true);
        ScheduleAutoNameMeasurement();
    }
    private async Task ChooseColumnsAsync()
    {
        try
        {
            var columns = await DetailsColumnPicker.ShowAsync(this, _detailColumns);
            if (columns is null || !IsLoaded) return;
            _detailColumns = columns;
            RememberManualNameWidth();
            ApplyDetailsColumns(true);
            ScheduleAutoNameMeasurement();
            UpdateExtraSortGlyphs();
        }
        catch (Exception error) { App.LogFailure("ChooseColumns", error); }
    }
}
