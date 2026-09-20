#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private bool _rowHoverSmokeStarted;

    private async Task RunRowHoverSmokeAsync()
    {
        var checks = 0;
        var hitProbes = 0;
        var largestShift = 0d;
        try
        {
            await Task.Delay(1200);
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            foreach (var width in new[] { 160d, 300d })
            {
                RequestedTheme = theme;
                var columns = DetailsColumn.Defaults().Select(c => c.Id == DetailsColumnId.Name ? c with { Width = width } : c).ToArray();
                FileSurface.SetColumns(columns);
                UpdateLayout();
                await Task.Delay(100);
                var row = Descendants(FileSurface).OfType<FileRow>().First(r => r.EntryId >= 0);
                var parts = new[] { "IconFrame", "NameText", "ModifiedText", "TypeText", "SizeText" }
                    .Select(name => (FrameworkElement)row.FindName(name)).ToArray();
                VisualStateManager.GoToState(row, "Normal", false);
                row.UpdateLayout();
                var baseline = parts.Select(p => Bounds(p, row)).ToArray();
                var rowWidth = row.ActualWidth;
                foreach (var state in FileRowVisualStates.All)
                {
                    if (!VisualStateManager.GoToState(row, state, false)) throw new InvalidOperationException("Missing state " + state);
                    row.UpdateLayout();
                    for (var i = 0; i < parts.Length; i++)
                    {
                        var actual = Bounds(parts[i], row);
                        var expected = baseline[i];
                        var shift = new[] { Math.Abs(actual.X - expected.X), Math.Abs(actual.Y - expected.Y),
                            Math.Abs(actual.Width - expected.Width), Math.Abs(actual.Height - expected.Height) }.Max();
                        largestShift = Math.Max(largestShift, shift);
                        if (shift > 0.05) throw new InvalidOperationException($"{theme} {width} {state}: {parts[i].Name} shifted {shift} DIP");
                    }
                    var fill = Bounds((FrameworkElement)row.FindName("Fill"), row);
                    var root = Bounds((FrameworkElement)row.FindName("Root"), row);
                    if (fill.Right > root.Right + 0.05 || fill.Bottom > root.Bottom + 0.05
                        || root.Right > rowWidth || Math.Abs(row.ActualWidth - rowWidth) > 0.05)
                        throw new InvalidOperationException("Row chrome exceeds its allocated bounds.");
                    checks++;
                }
                VisualStateManager.GoToState(row, "Normal", false);
            }
            RequestedTheme = ElementTheme.Light;
            FileSurface.SetColumns(DetailsColumn.Defaults());
            UpdateLayout();
            await Task.Delay(100);

            // Hover feedback must cover the whole row, including stretches that hold no text
            // (the empty left part of the size column, the accent gutter). Probe the real hit
            // test rather than the visual states: a panel without a Background is not
            // hit-testable, so pointer events over such gaps used to fall through to the surface.
            var probe = Descendants(FileSurface).OfType<FileRow>().First(r => r.EntryId >= 0);
            VisualStateManager.GoToState(probe, "Normal", false);
            probe.UpdateLayout();
            var rootBounds = Bounds((FrameworkElement)probe.FindName("Root"), probe);
            var typeBounds = Bounds((FrameworkElement)probe.FindName("TypeText"), probe);
            var sizeBounds = Bounds((FrameworkElement)probe.FindName("SizeText"), probe);
            var midY = rootBounds.Y + rootBounds.Height / 2;
            var blankProbes = new (string Name, Point Point)[]
            {
                ("SizeColumnBlank", new Point((typeBounds.Right + sizeBounds.X) / 2, midY)),
                ("AccentGutter", new Point(rootBounds.X + 1.5, midY)),
                ("RowBottomEdge", new Point((typeBounds.Right + sizeBounds.X) / 2, rootBounds.Bottom - 0.5)),
            };
            if (sizeBounds.X - typeBounds.Right < 4)
                throw new InvalidOperationException($"Fixture leaves no blank stretch in the size column ({sizeBounds.X - typeBounds.Right:0.##} DIP).");
            foreach (var (name, point) in blankProbes)
            {
                if (!HitsRow(probe, point))
                    throw new InvalidOperationException($"Blank stretch '{name}' at ({point.X:0.##},{point.Y:0.##}) does not hit the row.");
                hitProbes++;
            }
            // Negative control: the 1 DIP margin above the row chrome belongs to no row, so the
            // probe is not trivially reporting the row for every point.
            var marginPoint = new Point(rootBounds.X + rootBounds.Width / 2, rootBounds.Y - 0.5);
            if (HitsRow(probe, marginPoint))
                throw new InvalidOperationException("Hit test reports the row for its own outer margin; the probe is not discriminating.");

            var example = Descendants(FileSurface).OfType<FileRow>().First(r => r.EntryId >= 0);
            VisualStateManager.GoToState(example, "PointerOver", false);
            example.UpdateLayout();
            Save(new { Passed = true, Checks = checks, LargestShiftDip = largestShift, BorderContained = true, BlankHitProbes = hitProbes });
        }
        catch (Exception error) { Save(new { Passed = false, Checks = checks, LargestShiftDip = largestShift, BlankHitProbes = hitProbes, Error = error.ToString() }); }

        void Save(object result) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "row-hover-smoke.json"), JsonSerializer.Serialize(result));
        static Rect Bounds(FrameworkElement part, UIElement relativeTo) => part.TransformToVisual(relativeTo)
            .TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
        bool HitsRow(FileRow row, Point rowPoint)
        {
            var host = XamlRoot.Content;
            var hostPoint = row.TransformToVisual(host).TransformPoint(rowPoint);
            foreach (var hit in Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(hostPoint, FileSurface))
            {
                for (DependencyObject? node = hit; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
                {
                    if (ReferenceEquals(node, row)) return true;
                }
            }
            return false;
        }
    }
}
#endif
