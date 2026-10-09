using FilesMate.App.Models;

using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

namespace FilesMate.App.Controls.Settings;

/// <summary>Arranges compact accent wells on fixed tracks inside their palette card.</summary>
public sealed class AccentSwatchPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return new Size(0, 0);
        // Use the incoming constraint, not ActualWidth from a previous layout pass.
        var columns = Math.Min(AccentSwatchLayout.ColumnCount(availableSize.Width), Children.Count);
        var size = AccentSwatchLayout.SwatchSize;
        var gap = AccentSwatchLayout.Gap;
        foreach (var child in Children)
        {
            child.Measure(new Size(size, size));
        }
        var rows = (Children.Count + columns - 1) / columns;
        var width = columns * size + (columns - 1) * gap;
        return new Size(width, rows * size + (rows - 1) * gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Min(AccentSwatchLayout.ColumnCount(finalSize.Width), Children.Count);
        if (columns == 0) return finalSize;
        var size = AccentSwatchLayout.SwatchSize;
        var stride = size + AccentSwatchLayout.Gap;
        for (var index = 0; index < Children.Count; index++)
        {
            var row = index / columns;
            // The palette stays compact; every row uses the same fixed tracks.
            Children[index].Arrange(new Rect((index % columns) * stride,
                row * (size + AccentSwatchLayout.Gap), size, size));
        }
        return finalSize;
    }
}
