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
    private readonly Dictionary<double, DetailsColumn[]> _listColumnsByWidth = [];
    private AppearanceSettings? _listTypographySource;
    private AppearanceSettings? _listTypography;
    private double _listTypographyScale;
    private CompactListGeometry _listGeometry = CompactListGeometry.Uniform(1, 0, CompactListMetrics.ColumnWidth);
    private double[] _listPendingWidths = [];
    private int _listMeasureRows;
    private int _listMeasureIndex;
    private int[] _listMeasuredEntries = [];
    private DispatcherQueueTimer? _listMeasureTimer;
    private long _listMeasureQuietUntil;
    private long _listMeasureVersion;
    private bool _listMeasurePending;
    private bool _updatingColumnScroller;
    private bool _listColumnThumbDragging;
    public int ListZoomPercent { get; private set; } = 100;
    private double ListScale => _layout == FileLayoutKind.List ? ListZoomPercent / 100d : 1;
    private AppearanceSettings RowTypography
    {
        get
        {
            if (_layout != FileLayoutKind.List) return _typography;
            var scale = ListScale;
            if (!ReferenceEquals(_listTypographySource, _typography) || _listTypographyScale != scale)
            {
                _listTypographySource = _typography;
                _listTypographyScale = scale;
                _listTypography = _typography with
                { FileNameFontSize = _typography.FileNameFontSize * scale, FileDetailsFontSize = _typography.FileDetailsFontSize * scale };
            }
            return _listTypography!;
        }
    }

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
            return;
        }
        row.MinWidth = 0;
        var width = ListGeometry.WidthAt(Math.Max(0, viewIndex ?? row.ViewIndex) / ListRows);
        var nameWidth = width - CompactListMetrics.ColumnGap - FileColumnLayout.ContentLeft - FileColumnLayout.ContentRight
            - Math.Max(FileColumnLayout.GlyphWidth, FileColumnLayout.DetailsIconSize * ListScale + 4) - FileColumnLayout.AccentWidth;
        if (!_listColumnsByWidth.TryGetValue(nameWidth, out var columns))
        {
            // Realized columns repeatedly reuse these immutable descriptions.
            // Retention is bounded independently of directory size or zooms.
            if (_listColumnsByWidth.Count >= 32) _listColumnsByWidth.Clear();
            columns = DetailsColumn.Defaults().Select(c => c with
            { Visible = c.Id == DetailsColumnId.Name, Width = c.Id == DetailsColumnId.Name ? nameWidth : c.Width }).ToArray();
            _listColumnsByWidth.Add(nameWidth, columns);
        }
        row.ApplyColumns(columns);
        row.ApplyTypography(RowTypography, XamlRoot?.RasterizationScale ?? 1);
    }

    private void ScheduleListNameMeasurement()
    {
        ++_listMeasureVersion;
        _listMeasurePending = false;
        _listMeasureTimer?.Stop();
        if (_layout != FileLayoutKind.List || _resourcesReleased) return;
        _listNameMeasure ??= new TextBlock
        {
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            TextWrapping = TextWrapping.NoWrap,
        };
        _listNameMeasure.XamlRoot = XamlRoot;
        var typography = RowTypography;
        FileTypography.Apply(_listNameMeasure, typography, typography.FileNameFontSize);
        if (typography.FileFontFamily is null) _listNameMeasure.FontFamily = FontFamily;
        _listMeasureIndex = 0;
        _listMeasureRows = CompactListMetrics.Rows(Scroller.ActualHeight, RowHeight);
        _listPendingWidths = new double[(int)Math.Ceiling(_items.Count / (double)_listMeasureRows)];
        Array.Fill(_listPendingWidths, CompactListMetrics.WidthForName(0, ListScale));
        _listMeasuredEntries = new int[_listPendingWidths.Length];
        _listMeasurePending = true;
        _listMeasureTimer ??= CreateListMeasureTimer();
        _listMeasureTimer.Start();
    }

    private DispatcherQueueTimer CreateListMeasureTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = TimeSpan.FromMilliseconds(32);
        timer.Tick += (_, _) => MeasureListNames(_listMeasureVersion);
        return timer;
    }

    private void MeasureListNames(long version)
    {
        if (version != _listMeasureVersion || !_listMeasurePending) return;
        if (_layout != FileLayoutKind.List || !IsLoaded || _resourcesReleased) { _listMeasurePending = false; return; }
        // Yield during movement. Continuous low-priority dispatcher callbacks
        // otherwise occupy a core until every name in a large folder is shaped.
        if (_inputClock.ElapsedMilliseconds < _listMeasureQuietUntil
            || _listWheel.HasTarget || _thumbnailDecision.Fast || _listColumnThumbDragging || _scrollbarDragging || IsMemoryReclamationBusy)
        { _listMeasureTimer?.Start(); return; }
        var watch = Stopwatch.StartNew();
        var first = ListGeometry.ClampedColumnAt(Scroller.HorizontalOffset);
        var last = ListGeometry.ClampedColumnAt(Scroller.HorizontalOffset + Scroller.ViewportWidth) + 1;
        last = Math.Min(last, _listPendingWidths.Length - 1);
        var visibleChanged = false;
        while (_listMeasureIndex < _listPendingWidths.Length)
        {
            // Finish visible columns first, then fill the rest in bounded idle
            // slices. Only per-column progress/width is retained, never a layout
            // or measured string object for each file.
            var column = -1;
            for (var c = first; c <= last; c++)
                if (_listMeasuredEntries[c] < Math.Min(_listMeasureRows, _items.Count - c * _listMeasureRows))
                { column = c; break; }
            if (column < 0)
            {
                while (_listMeasureIndex < _listPendingWidths.Length
                    && _listMeasuredEntries[_listMeasureIndex] >= Math.Min(_listMeasureRows, _items.Count - _listMeasureIndex * _listMeasureRows))
                    ++_listMeasureIndex;
                if (_listMeasureIndex == _listPendingWidths.Length) break;
                column = _listMeasureIndex;
            }
            var index = column * _listMeasureRows + _listMeasuredEntries[column]++;
            if (_items.TryGetEntry(index, out var entry))
            {
                _listNameMeasure!.Text = entry.Name;
                _listNameMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (version != _listMeasureVersion || !_listMeasurePending || _layout != FileLayoutKind.List) return;
                var width = CompactListMetrics.WidthForName(_listNameMeasure.DesiredSize.Width + 1 / (XamlRoot?.RasterizationScale ?? 1), ListScale);
                _listPendingWidths[column] = Math.Max(_listPendingWidths[column], width);
            }
            if (_listPendingWidths[column] >= CompactListMetrics.MaximumColumnWidth * ListScale)
                _listMeasuredEntries[column] = Math.Min(_listMeasureRows, _items.Count - column * _listMeasureRows);
            if (column >= first && column <= last
                && _listMeasuredEntries[column] == Math.Min(_listMeasureRows, _items.Count - column * _listMeasureRows))
                visibleChanged = true;
            if (watch.Elapsed.TotalMilliseconds >= .75)
            {
                PublishListMeasuredWidths(first, last, all: false);
                _listMeasureTimer?.Start();
                return;
            }
        }
        if (visibleChanged) PublishListMeasuredWidths(first, last, all: false);
        _listMeasurePending = false;
        PublishListMeasuredWidths(first, last, all: true);
        _listPendingWidths = [];
        _listMeasuredEntries = [];
        _listNameMeasure!.Text = string.Empty;
        SchedulePendingReveal();
    }

    private void PublishListMeasuredWidths(int first, int last, bool all)
    {
        if (_listGeometry.Rows != _listMeasureRows || _listGeometry.Count != _items.Count) return;
        var geometry = _listGeometry.WithMeasuredWidths(_listPendingWidths, _listMeasuredEntries,
            all ? 0 : first, all ? _listPendingWidths.Length - 1 : last);
        if (ReferenceEquals(geometry, _listGeometry)) return;
        var anchor = _listGeometry.ClampedColumnAt(Scroller.HorizontalOffset);
        var within = Scroller.HorizontalOffset - _listGeometry.LeftAt(anchor);
        _listGeometry = geometry;
        UpdateListMetrics();
        // Preserve the same first file while widths before it change.
        Scroller.ChangeView(_listGeometry.LeftAt(anchor) + Math.Min(within, _listGeometry.WidthAt(anchor)), 0, null, true);
    }

    private void CancelListNameMeasurement()
    {
        ++_listMeasureVersion;
        _listMeasurePending = false;
        _listMeasureTimer?.Stop();
        _listPendingWidths = [];
        _listMeasuredEntries = [];
        if (_listNameMeasure is not null) _listNameMeasure.Text = string.Empty;
    }

    private void DeferListNameMeasurementForInput() =>
        _listMeasureQuietUntil = _inputClock.ElapsedMilliseconds + 250;

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
        NotifyUserInteraction();
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
            // ChangeView may be applied after this sample. While the thumb owns
            // the requested column, stale viewport feedback must not move it back.
            if (!_listColumnThumbDragging)
                ListColumnScroller.Value = ListGeometry.ClampedColumnAt(Scroller.HorizontalOffset);
        }
        finally { _updatingColumnScroller = false; }
    }

    private void ListColumnScroller_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_updatingColumnScroller || _layout != FileLayoutKind.List) return;
        DeferListNameMeasurementForInput();
        var column = (int)Math.Round(args.NewValue, MidpointRounding.AwayFromZero);
        _updatingColumnScroller = true;
        try { ListColumnScroller.Value = column; }
        finally { _updatingColumnScroller = false; }
        // Publish requested columns during the gesture. Thumbnail/metadata
        // admission still follows the common viewport speed/settle policy.
        Scroller.CancelDirectManipulations();
        ChangeScrollOffset(ListGeometry.LeftAt(column));
    }
}
