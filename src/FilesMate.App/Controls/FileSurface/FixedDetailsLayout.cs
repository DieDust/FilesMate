using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

// Details presenters have an explicit, uniform typography-derived height.
// Their exact extent and visible indices do not need StackLayout's estimated
// anchor or repeated measurement when jumping across a very large directory.
internal sealed class FixedDetailsLayout : VirtualizingLayout
{
    private double _height = FileColumnLayout.RowHeight;
    private double _width;

    internal void Configure(double height, double width)
    {
        if (_height == height && _width == width) return;
        _height = height;
        _width = width;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var rect = context.RealizationRect;
        var first = (int)Math.Clamp(Math.Floor(Math.Max(0, rect.Y) / _height), 0, context.ItemCount);
        var bottom = double.IsFinite(rect.Height) ? rect.Y + rect.Height : rect.Y + _height * 32;
        var end = (int)Math.Clamp(Math.Ceiling(Math.Max(0, bottom) / _height) + 1, first, context.ItemCount);
        var state = context.LayoutState as Range ?? new Range();
        context.LayoutState = state;
        state.First = first;
        state.Count = end - first;
        state.Height = _height;
        state.Width = _width;
        if (state.Elements.Length < state.Count) state.Elements = new UIElement[state.Count];
        for (var i = 0; i < state.Count; i++)
        {
            var element = context.GetOrCreateElementAt(first + i);
            state.Elements[i] = element;
            element.Measure(new Size(double.PositiveInfinity, _height));
            state.Width = Math.Max(state.Width, element.DesiredSize.Width);
        }
        Array.Clear(state.Elements, state.Count, state.Elements.Length - state.Count);
        return new Size(state.Width, context.ItemCount * _height);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        if (context.LayoutState is Range state)
            for (var i = 0; i < state.Count; i++)
                state.Elements[i].Arrange(new Rect(0, (state.First + i) * state.Height,
                    Math.Max(state.Width, finalSize.Width), state.Height));
        return finalSize;
    }

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
    {
        if (context.LayoutState is Range state) Array.Clear(state.Elements);
        context.LayoutState = null;
    }

    private sealed class Range
    {
        internal int First, Count;
        internal double Height, Width;
        internal UIElement[] Elements = [];
    }
}
