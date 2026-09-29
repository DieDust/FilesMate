#if FILESMATE_UI_TEST
using FilesMate.App.Models;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    internal async Task<IReadOnlyList<object>> RunGapSelectionSmokeAsync()
    {
        var samples = new List<object>();
        var store = new EntryStore();
        store.Append(Enumerable.Range(0, 120).Select(i => new FileEntryCore(i,
            i < 4 ? new[] { "项目文档", "设计素材", "软件工具", "归档资料" }[i] : $"项目文件-{i:D3}.txt",
            0, 0, 0, i < 4 ? FileAttributes.Directory : FileAttributes.Normal,
            i < 4 ? EntryKind.Directory : EntryKind.File)).ToArray());
        Bind(store, EntryViewIndex.InSourceOrder(store, 1), 1);
        ResolveTags = _ => [];
        try
        {
            foreach (var font in new[] { 13d, 20d })
            {
                await App.AppearanceViewModel!.SetFileTypographyAsync(null, font, 12);
                SetLayout(FileLayoutKind.Grid);
                foreach (var size in GridSizePreset.All)
                {
                    SetGridSize(size);
                    Scroller.ChangeView(0, 0, null, true);
                    await Task.Delay(120);
                    var a = Part(0, "Root"); var b = Part(1, "Root");
                    var target = new Point(b.X + b.Width / 2, a.Y + 12);
                    Drag(new(a.X + 12, a.Bottom + 3), target, $"Grid/{size.IconSize}/{font}/name gap");
                    Drag(new(a.X + 12, a.Bottom + GridSizePreset.TileTagGap + 4), target, $"Grid/{size.IconSize}/{font}/empty tags");
                    PreparePointerSelection(new(a.X + 12, a.Y + 12), 1);
                    Require(_dragCandidate && AdvanceSelectionPointer(new(a.X + 30, a.Y + 30)), "File icon no longer starts a file drag");
                    CancelMarquee();
                    Scroller.ChangeView(0, (EffectiveGridPreset.ItemHeight + size.Gutter) * 2, null, true);
                    await Task.Delay(120);
                    Require(Scroller.VerticalOffset > 0, "Grid fixture did not scroll");
                    a = Part(Columns() * 2, "Root"); b = Part(Columns() * 2 + 1, "Root");
                    Drag(new(a.X + 12, a.Bottom + 3), new(b.X + b.Width / 2, a.Y + 12), $"Grid/{size.IconSize}/{font}/scrolled gap");
                }
                SetLayout(FileLayoutKind.List);
                foreach (var zoom in new[] { 80, 100, 160 })
                {
                    SetListZoom(zoom);
                    Scroller.ChangeView(0, 0, null, true);
                    await Task.Delay(120);
                    var a = Part(0, "Root"); var b = Part(1, "Root"); var c = Part(2, "Root");
                    Drag(new(a.X + 12, (a.Bottom + b.Top) / 2), new(c.X + c.Width / 2, c.Y + c.Height / 2), $"List/{zoom}/{font}/row gap");
                    var next = Part(ListRows, "Root"); var nextRow = Part(ListRows + 1, "Root");
                    Drag(new((a.Right + next.Left) / 2, a.Y + 3), new(nextRow.X + 45, nextRow.Y + nextRow.Height / 2), $"List/{zoom}/{font}/column gap");
                    Scroller.ChangeView(ListWidth * 2, 0, null, true);
                    await Task.Delay(120);
                    Require(Scroller.HorizontalOffset > 0, "List fixture did not scroll");
                    a = Part(ListRows * 2, "Root"); b = Part(ListRows * 2 + 1, "Root"); c = Part(ListRows * 2 + 2, "Root");
                    Drag(new(a.X + 12, (a.Bottom + b.Top) / 2), new(c.X + c.Width / 2, c.Y + c.Height / 2), $"List/{zoom}/{font}/scrolled gap");
                }
            }
            await App.AppearanceViewModel!.SetFileTypographyAsync(null, 13, 12);
            SetLayout(FileLayoutKind.Grid); SetGridSize(GridSizePreset.Large);
            Scroller.ChangeView(0, 0, null, true);
            ResolveTags = entry => entry.Id == 0 ? [new TagDefinition(1, "项目", "#4F786C", 0)] : [];
            RebindVisibleEntries(); await Task.Delay(140);
            var tag = Part(0, "TagHost");
            PreparePointerSelection(new(tag.X + tag.Width / 2, tag.Y + tag.Height / 2), 1);
            Require(_dragCandidate && _selection.Contains(0), "Visible label is no longer selectable");
            CancelMarquee();
            var root = Part(0, "Root"); var other = Part(1, "Root");
            Drag(new(root.X + 12, root.Bottom + 3), new(other.X + 35, other.Y + 12), "Grid/visible tag gap");
            ResolveTags = _ => []; RebindVisibleEntries();
            RestoreSelectedNames(["项目文档", "设计素材", "归档资料"]);
            return samples;
        }
        finally { CancelMarquee(); }

        Rect Part(int index, string name)
        {
            var item = (FrameworkElement)Repeater.TryGetElement(index);
            var part = (FrameworkElement)item.FindName(name);
            return part.TransformToVisual(Scroller).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
        }
        void Drag(Point start, Point end, string scenario)
        {
            Require(start.Y >= 0 && end.Y >= 0 && start.Y < Scroller.ViewportHeight && end.Y < Scroller.ViewportHeight,
                scenario + ": fixture points are outside the visible viewport");
            PreparePointerSelection(start, 1);
            Require(_pressViewIndex < 0 && !_dragCandidate, scenario + ": empty space selected a file");
            Require(!AdvanceSelectionPointer(end) && _dragging && !_externalDragStarted, scenario + ": did not start marquee");
            Require(_selection.Count == 2, scenario + $": expected two items, got {_selection.Count}; start={start}; end={end}; offset={Scroller.HorizontalOffset},{Scroller.VerticalOffset}; anchor={_marqueeStart}; current={ToContentPoint(end)}; columns={Columns()}; preset={EffectiveGridPreset}");
            samples.Add(new { Scenario = scenario, Selected = _selection.Ids.ToArray(), StartsInGap = true, ExternalDrag = false });
            CancelMarquee();
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
#endif
