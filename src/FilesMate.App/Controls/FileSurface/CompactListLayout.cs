using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

internal sealed class CompactListLayout : VirtualizingLayout
{
    public CompactListGeometry Geometry { get; private set; } = CompactListGeometry.Uniform(1, 0, CompactListMetrics.ColumnWidth);
    public int Rows => Geometry.Rows;
    public double RowHeight { get; private set; } = FileColumnLayout.RowHeight;
    private double _viewportWidth;
    private double _rasterizationScale = 1;
    public void Configure(CompactListGeometry geometry, double rowHeight, double viewportWidth, double rasterizationScale = 1)
    {
        if (ReferenceEquals(Geometry, geometry) && RowHeight == rowHeight && _viewportWidth == viewportWidth
            && _rasterizationScale == rasterizationScale) return;
        Geometry = geometry; RowHeight = rowHeight; _viewportWidth = viewportWidth;
        _rasterizationScale = rasterizationScale > 0 ? rasterizationScale : 1;
        InvalidateMeasure();
    }
    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var rect = context.RealizationRect;
        var columns = Geometry.ColumnCount;
        var startColumn = Geometry.ClampedColumnAt(rect.X);
        var visibleWidth = double.IsFinite(rect.Width) ? rect.Width : CompactListMetrics.ColumnWidth * 4;
        var endColumn = Math.Min(columns, Geometry.ClampedColumnAt(Math.Max(0, rect.X) + visibleWidth) + 2);
        var first = Math.Min(context.ItemCount, startColumn * Rows);
        var end = Math.Max(first, Math.Min(context.ItemCount, endColumn * Rows));
        var state = context.LayoutState as RealizedColumns ?? new();
        context.LayoutState = state;
        state.First = first;
        state.Count = end - first;
        state.Geometry = Geometry;
        state.Height = RowHeight;
        if (state.Elements.Length < state.Count) state.Elements = new UIElement[state.Count];
        for (var i = 0; i < state.Count; i++)
        {
            var element = context.GetOrCreateElementAt(first + i);
            state.Elements[i] = element;
            element.Measure(new Size(Geometry.WidthAt((first + i) / Rows) - CompactListMetrics.ColumnGap, RowHeight));
        }
        Array.Clear(state.Elements, state.Count, state.Elements.Length - state.Count);
        var extent = Geometry.ExtentForViewport(_viewportWidth);
        // WinUI rounds the extent and viewport separately. Keep one physical pixel
        // beyond the final column edge so the scroll limit cannot clamp it short.
        if (extent > Geometry.ExtentWidth) extent += 1 / _rasterizationScale;
        return new Size(extent, Math.Min(Rows, context.ItemCount) * RowHeight);
    }
    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        if (context.LayoutState is RealizedColumns state)
            for (var i = 0; i < state.Count; i++)
            {
                var index = state.First + i;
                var geometry = state.Geometry;
                state.Elements[i].Arrange(new Rect(geometry.LeftAt(index / geometry.Rows), index % geometry.Rows * state.Height,
                    geometry.WidthAt(index / geometry.Rows) - CompactListMetrics.ColumnGap, state.Height));
            }
        return finalSize;
    }

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
    {
        if (context.LayoutState is RealizedColumns state) Array.Clear(state.Elements);
        context.LayoutState = null;
    }

    private sealed class RealizedColumns
    {
        internal int First, Count;
        internal double Height;
        internal CompactListGeometry Geometry = CompactListGeometry.Uniform(1, 0, CompactListMetrics.ColumnWidth);
        internal UIElement[] Elements = [];
    }
}
