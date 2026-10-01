using System.Diagnostics;
using FilesMate.App.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private readonly CompactListLayout _listLayout = new();
    private readonly ListScrollState _listWheel = new();
    private ScrollContentPresenter? _listWheelPresenter;
    private TextBlock? _listNameMeasure;
    private DetailsColumn[]? _listColumns;
    private CompactListGeometry _listGeometry = CompactListGeometry.Uniform(1, 0, CompactListMetrics.ColumnWidth);
    private double[] _listPendingWidths = [];
    private int _listMeasureRows;
    private int _listMeasureIndex;
    private long _listMeasureVersion;
    private bool _listMeasurePending;
    private bool _updatingColumnScroller;
    public int ListZoomPercent { get; private set; } = 100;
    private double ListScale => _layout == FileLayoutKind.List ? ListZoomPercent / 100d : 1;
    private AppearanceSettings RowTypography => _layout == FileLayoutKind.List ? _typography with
    { FileNameFontSize = _typography.FileNameFontSize * ListScale, FileDetailsFontSize = _typography.FileDetailsFontSize * ListScale } : _typography;

    public void SetListZoom(int percent)
    {
        percent = CompactListMetrics.ClampZoom(percent);
        if (ListZoomPercent == percent) return;
        StopListWheel();
        var anchor = _realized.Where(row => row.IsLoaded).Select(row => row.ViewIndex).DefaultIfEmpty(0).Min();
        ListZoomPercent = percent;
        if (_layout == FileLayoutKind.List)
        {
            UpdateListMetrics();
            ScheduleListNameMeasurement();
            Repeater.InvalidateMeasure();
            Scroller.ChangeView(ListGeometry.LeftAt(Math.Max(0, anchor / ListRows)), 0, null, true);
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
    private CompactListGeometry ListGeometry => _listLayout.Geometry;
    private double ActiveScrollOffset => _layout == FileLayoutKind.List ? Scroller.HorizontalOffset : Scroller.VerticalOffset;
    private double ActiveScrollableExtent => _layout == FileLayoutKind.List ? Scroller.ScrollableWidth : Scroller.ScrollableHeight;
    private void ChangeScrollOffset(double value)
    {
        StopListWheel();
        if (_layout == FileLayoutKind.List)
            Scroller.ChangeView(ListGeometry.LeftAt(ListGeometry.ClampedColumnAt(value)), 0, null, true);
        else Scroller.ChangeView(null, value, null, true);
    }
    private void UpdateListMetrics()
    {
        if (_layout != FileLayoutKind.List) return;
        var rows = CompactListMetrics.Rows(Scroller.ActualHeight, RowHeight);
        var regrouped = _listGeometry.Rows != rows || _listGeometry.Count != _items.Count;
        if (regrouped)
            _listGeometry = CompactListGeometry.Uniform(rows, _items.Count, CompactListMetrics.ColumnWidth * ListScale);
        _listLayout.Configure(_listGeometry, RowHeight, Scroller.ActualWidth, XamlRoot?.RasterizationScale ?? 1);
        UpdateListColumnScroller();
        foreach (var row in _realized.ToArray()) ApplyRowColumns(row);
        if (regrouped) ScheduleListNameMeasurement();
    }
    private void ApplyRowColumns(FileRow row, int? viewIndex = null)
    {
        row.SetCompact(_layout == FileLayoutKind.List);
        row.SetIconScale(ListScale);
        if (_layout != FileLayoutKind.List)
        {
            row.ApplyColumns(_detailColumns);
            row.MinWidth = 0;
            row.ApplyTypography(RowTypography, XamlRoot?.RasterizationScale ?? 1);
            row.SetIconScale(1);
            return;
        }
        row.MinWidth = 0;
        var width = ListGeometry.WidthAt(Math.Max(0, viewIndex ?? row.ViewIndex) / ListRows);
        var nameWidth = width - CompactListMetrics.ColumnGap - FileColumnLayout.ContentLeft - FileColumnLayout.ContentRight
            - Math.Max(FileColumnLayout.GlyphWidth, FileColumnLayout.DetailsIconSize * ListScale + 4) - FileColumnLayout.AccentWidth;
        if (_listColumns is null || _listColumns[0].Width != nameWidth)
            _listColumns = DetailsColumn.Defaults().Select(c => c with
            { Visible = c.Id == DetailsColumnId.Name, Width = c.Id == DetailsColumnId.Name ? nameWidth : c.Width }).ToArray();
        row.ApplyColumns(_listColumns);
        row.ApplyTypography(RowTypography, XamlRoot?.RasterizationScale ?? 1);
        row.SetIconScale(ListScale);
    }

    private void ScheduleListNameMeasurement()
    {
        var version = ++_listMeasureVersion;
        _listMeasurePending = false;
        if (_layout != FileLayoutKind.List || _resourcesReleased) return;
        _listNameMeasure ??= new TextBlock
        {
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            TextWrapping = TextWrapping.NoWrap,
        };
        _listNameMeasure.XamlRoot = XamlRoot;
        FileTypography.Apply(_listNameMeasure, RowTypography, RowTypography.FileNameFontSize);
        if (RowTypography.FileFontFamily is null) _listNameMeasure.FontFamily = FontFamily;
        _listMeasureIndex = 0;
        _listMeasureRows = CompactListMetrics.Rows(Scroller.ActualHeight, RowHeight);
        _listPendingWidths = new double[(int)Math.Ceiling(_items.Count / (double)_listMeasureRows)];
        Array.Fill(_listPendingWidths, CompactListMetrics.WidthForName(0, ListScale));
        _listMeasurePending = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => MeasureListNames(version));
    }

    private void MeasureListNames(long version)
    {
        if (version != _listMeasureVersion || !_listMeasurePending) return;
        if (_layout != FileLayoutKind.List || !IsLoaded || _resourcesReleased) { _listMeasurePending = false; return; }
        // Measure the whole view, including offscreen names, without blocking input
        // or keeping a width/cache object per file in large folders.
        var watch = Stopwatch.StartNew();
        while (_listMeasureIndex < _items.Count)
        {
            var column = _listMeasureIndex / _listMeasureRows;
            if (_items.TryGetEntry(_listMeasureIndex++, out var entry))
            {
                _listNameMeasure!.Text = entry.Name;
                _listNameMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var width = CompactListMetrics.WidthForName(_listNameMeasure.DesiredSize.Width + 1 / (XamlRoot?.RasterizationScale ?? 1), ListScale);
                _listPendingWidths[column] = Math.Max(_listPendingWidths[column], width);
            }
            if (_listPendingWidths[column] >= CompactListMetrics.MaximumColumnWidth * ListScale)
                _listMeasureIndex = Math.Min(_items.Count, (column + 1) * _listMeasureRows);
            if (watch.Elapsed.TotalMilliseconds >= 4)
            {
                _listMeasurePending = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => MeasureListNames(version));
                return;
            }
        }
        _listMeasurePending = false;
        _listGeometry = new CompactListGeometry(_listMeasureRows, _items.Count, _listPendingWidths);
        _listPendingWidths = [];
        _listNameMeasure!.Text = string.Empty;
        UpdateListMetrics();
        SchedulePendingReveal();
    }

    private void CancelListNameMeasurement()
    {
        ++_listMeasureVersion;
        _listMeasurePending = false;
        _listPendingWidths = [];
        if (_listNameMeasure is not null) _listNameMeasure.Text = string.Empty;
    }

    private void InitializeListScrolling()
    {
        AttachRepeaterWheelHandler(Repeater);
        Scroller.Loaded += (_, _) => AttachListWheelHandler();
        Scroller.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => StopListWheel()), true);
    }

    private void AttachRepeaterWheelHandler(ItemsRepeater repeater) =>
        repeater.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(ListContent_PointerWheelChanged), true);

    private void AttachListWheelHandler()
    {
        var presenter = FindPresenter(Scroller);
        if (ReferenceEquals(presenter, _listWheelPresenter)) return;
        if (_listWheelPresenter is not null) _listWheelPresenter.PointerWheelChanged -= ListContent_PointerWheelChanged;
        _listWheelPresenter = presenter;
        if (presenter is not null) presenter.PointerWheelChanged += ListContent_PointerWheelChanged;

        static ScrollContentPresenter? FindPresenter(DependencyObject parent)
        {
            if (parent is ScrollContentPresenter found) return found;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (FindPresenter(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
            return null;
        }
    }

    private void ListContent_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // The repeater handles wheel input before ScrollContentPresenter/ScrollViewer
        // can start a pixel-based manipulation; the presenter covers blank content.
        if (_layout == FileLayoutKind.List && !IsModifier(Windows.System.VirtualKey.Control) && !e.Handled)
            OnPointerWheelChanged(sender, e);
    }

    private void ScrollListWheel(int delta, bool horizontal)
    {
        Scroller.CancelDirectManipulations();
        if (!SystemParametersInfoW(horizontal ? 0x006Cu : 0x0068u, 0, out var units, 0)) units = 3;
        if (units == 0) { StopListWheel(); return; }
        if (_listWheel.AddWheel(delta, horizontal, ListGeometry, Scroller.HorizontalOffset, Scroller.ScrollableWidth))
            Scroller.ChangeView(_listWheel.Target, 0, null, disableAnimation: true);
    }

    internal bool HandleListWheelFromWindow(UIElement root, Point position, int delta, bool horizontal)
    {
        if (_layout != FileLayoutKind.List || _resourcesReleased || !IsLoaded) return false;
        CancelScrollRestore();
        _marqueePointer = root.TransformToVisual(Scroller).TransformPoint(position);
        ScrollListWheel(delta, horizontal);
        return true;
    }

    private void StopListWheel()
    {
        _listWheel.Reset();
    }

    private void UpdateListColumnScroller()
    {
        if (_layout != FileLayoutKind.List) return;
        _updatingColumnScroller = true;
        try
        {
            ListColumnScroller.Maximum = ListGeometry.ClampedColumnAt(Scroller.ScrollableWidth);
            ListColumnScroller.ViewportSize = Math.Max(1,
                ListGeometry.ClampedColumnAt(Scroller.HorizontalOffset + Scroller.ViewportWidth)
                - ListGeometry.ClampedColumnAt(Scroller.HorizontalOffset));
            ListColumnScroller.Value = ListGeometry.ClampedColumnAt(Scroller.HorizontalOffset);
        }
        finally { _updatingColumnScroller = false; }
    }

    private void ListColumnScroller_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_updatingColumnScroller || _layout != FileLayoutKind.List) return;
        var column = (int)Math.Round(args.NewValue, MidpointRounding.AwayFromZero);
        _updatingColumnScroller = true;
        try { ListColumnScroller.Value = column; }
        finally { _updatingColumnScroller = false; }
        Scroller.CancelDirectManipulations();
        ChangeScrollOffset(ListGeometry.LeftAt(column));
    }
}
