using System.Diagnostics;
using FilesMate.App.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private bool _autoFitNameColumn;
    private double _manualNameWidth = FileColumnLayout.NameWidth;
    private TextBlock? _autoNameMeasure;
    private long _autoNameMeasureVersion;
    private bool _autoNameMeasurePending;
    private int _autoNameMeasureIndex;
    private double _autoNameMeasuredWidth;
    // The name header includes the icon column; its complete width shares the list limit.
    private const double AutoNameMaximum = CompactListMetrics.MaximumColumnWidth - FileColumnLayout.GlyphWidth;

    internal DetailsColumn[] GetPresentationColumns() => _detailColumns.Select(column =>
        _autoFitNameColumn && column.Id == DetailsColumnId.Name ? column with { Width = _manualNameWidth } : column).ToArray();

    private void RememberManualNameWidth() =>
        _manualNameWidth = _detailColumns.FirstOrDefault(c => c.Id == DetailsColumnId.Name)?.Width ?? FileColumnLayout.NameWidth;

    private void ApplyAutoNamePreference(ExplorerPreferences preferences)
    {
        if (_autoFitNameColumn == preferences.AutoFitNameColumn) return;
        _autoFitNameColumn = preferences.AutoFitNameColumn;
        CancelAutoNameMeasurement();
        if (!_autoFitNameColumn)
        {
            SetMeasuredNameWidth(_manualNameWidth);
            return;
        }
        ScheduleAutoNameMeasurement();
    }

    private void ScheduleAutoNameMeasurement()
    {
        CancelAutoNameMeasurement();
        if (!_autoFitNameColumn || _layout != FileLayoutKind.Details || !IsLoaded || _resourcesReleased || _resizeColumn == "Name") return;
        _autoNameMeasure ??= new TextBlock
        {
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            TextWrapping = TextWrapping.NoWrap,
        };
        _autoNameMeasure.XamlRoot = XamlRoot;
        FileTypography.Apply(_autoNameMeasure, _typography, _typography.FileNameFontSize);
        if (_typography.FileFontFamily is null) _autoNameMeasure.FontFamily = FontFamily;
        _autoNameMeasure.Text = _detailColumns.First(c => c.Id == DetailsColumnId.Name).Title;
        _autoNameMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _autoNameMeasuredWidth = Math.Max(FileColumnLayout.MinNameWidth, _autoNameMeasure.DesiredSize.Width + 30);
        _autoNameMeasureIndex = 0;
        var version = _autoNameMeasureVersion;
        _autoNameMeasurePending = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => MeasureAutomaticNameWidth(version));
    }

    private void MeasureAutomaticNameWidth(long version)
    {
        if (version != _autoNameMeasureVersion || !_autoNameMeasurePending) return;
        if (!_autoFitNameColumn || _layout != FileLayoutKind.Details || !IsLoaded || _resourcesReleased)
        { CancelAutoNameMeasurement(); return; }
        var watch = Stopwatch.StartNew();
        // One reusable text element and one maximum, including names outside the viewport.
        while (_autoNameMeasureIndex < _items.Count && _autoNameMeasuredWidth < AutoNameMaximum)
        {
            if (_items.TryGetEntry(_autoNameMeasureIndex++, out var entry))
            {
                _autoNameMeasure!.Text = FileRowFormatter.DisplayName(entry, App.ExplorerPreferences.ShowFileExtensions);
                _autoNameMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _autoNameMeasuredWidth = Math.Max(_autoNameMeasuredWidth, _autoNameMeasure.DesiredSize.Width + 30);
            }
            if (watch.Elapsed.TotalMilliseconds >= 4)
            {
                _autoNameMeasurePending = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => MeasureAutomaticNameWidth(version));
                return;
            }
        }
        _autoNameMeasurePending = false;
        _autoNameMeasure!.Text = string.Empty;
        SetMeasuredNameWidth(Math.Clamp(Math.Ceiling(_autoNameMeasuredWidth), FileColumnLayout.MinNameWidth, AutoNameMaximum));
    }

    private void SetMeasuredNameWidth(double width)
    {
        if (_detailColumns.First(c => c.Id == DetailsColumnId.Name).Width == width) return;
        _detailColumns = _detailColumns.Select(c => c.Id == DetailsColumnId.Name ? c with { Width = width } : c).ToArray();
        // Automatic measurements are transient; folder/session settings keep the manual width.
        ApplyDetailsColumns(false);
        Repeater.InvalidateMeasure();
    }

    private void CancelAutoNameMeasurement()
    {
        ++_autoNameMeasureVersion;
        _autoNameMeasurePending = false;
        if (_autoNameMeasure is not null) _autoNameMeasure.Text = string.Empty;
    }
}
