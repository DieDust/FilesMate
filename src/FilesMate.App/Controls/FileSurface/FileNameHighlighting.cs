using FilesMate.App.Models;
using FilesMate.App.Theming;
using FilesMate.Core.Entries;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

/// <summary>Rounded glyph backplates for realized names only; no list rebuild or background work.</summary>
internal sealed class FileNameHighlighting
{
    private readonly TextBlock _text;
    private readonly Canvas _backplates;
    private readonly List<Border> _pool = [];
    private readonly List<Rect> _fragments = [];
    private IReadOnlyList<NameMatchRange> _ranges = [];
    private string _query = "";
    private string? _renderedName, _renderedQuery;
    private bool _subscribed, _queued, _waitingLayout;
    private int _layoutRetries;

    public FileNameHighlighting(TextBlock text, Canvas backplates)
    {
        _text = text;
        _backplates = backplates;
        text.Loaded += (_, _) => { Subscribe(); QueueGeometry(); };
        text.Unloaded += (_, _) => { Unsubscribe(); StopWaitingLayout(); Hide(); };
        text.SizeChanged += (_, _) => { _layoutRetries = 0; QueueGeometry(); };
        text.ActualThemeChanged += (_, _) => RefreshBrushes();
    }

    public void SetQuery(string query) { _query = query; Refresh(); }

    public void Refresh()
    {
        if (_renderedName == _text.Text && _renderedQuery == _query) return;
        _renderedName = _text.Text;
        _renderedQuery = _query;
        _ranges = NameMatchRanges.Find(_text.Text, _query);
        _layoutRetries = 0;
        // Recycled text must not show the previous name's geometry before layout.
        Hide();
        QueueGeometry();
    }

    private void QueueGeometry()
    {
        if (_queued || !_text.IsLoaded || _ranges.Count == 0) return;
        _queued = _text.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _queued = false;
            if (!_text.IsLoaded || _ranges.Count == 0) return;
            Draw();
        });
    }

    private void Draw()
    {
        if (_text.ActualWidth <= 0 || _text.ActualHeight <= 0 || _backplates.ActualWidth <= 0) return;
        // Obtain the owner's public text pointer first. A deferred Run on the
        // TextBlock.Text fast path does not initialize the hit-test text view.
        // The owner pointer does, without enabling text selection or adding a second layout.
        var contentStart = _text.ContentStart;
        var start = _text.Inlines.OfType<Run>().FirstOrDefault()?.ContentStart ?? contentStart;
        if (start is null) return;
        var transform = _text.TransformToVisual(_backplates);
        var used = 0;
        var available = false;
        // The native fast layout maps omitted characters to the ellipsis caret pair.
        // Obtain that pair once from the omitted logical suffix; do not paint the
        // ellipsis as if it were the matching text, or paint only part of a keyword.
        Rect? omittedFirst = null, omittedLast = null;
        if (_text.IsTextTrimmed && _text.Text.Length > 0)
        {
            omittedFirst = start.GetPositionAtOffset(_text.Text.Length - 1, LogicalDirection.Forward)
                ?.GetCharacterRect(LogicalDirection.Forward);
            omittedLast = start.GetPositionAtOffset(_text.Text.Length, LogicalDirection.Backward)
                ?.GetCharacterRect(LogicalDirection.Backward);
        }
        foreach (var range in _ranges)
        {
            var fragments = _fragments;
            fragments.Clear();
            var omitted = false;
            var end = Math.Min(_text.Text.Length, range.StartIndex + range.Length);
            for (var index = range.StartIndex; index < end;)
            {
                var length = char.IsHighSurrogate(_text.Text[index]) && index + 1 < end
                    && char.IsLowSurrogate(_text.Text[index + 1]) ? 2 : 1;
                var first = start.GetPositionAtOffset(index, LogicalDirection.Forward);
                var last = start.GetPositionAtOffset(index + length, LogicalDirection.Backward);
                index += length;
                if (first is null || last is null) continue;
                // The two text positions preserve advances, kerning, surrogate glyphs and RTL order.
                var a = first.GetCharacterRect(LogicalDirection.Forward);
                var b = last.GetCharacterRect(LogicalDirection.Backward);
                if (a.IsEmpty || b.IsEmpty || a.Height <= 0 || b.Height <= 0) continue;
                available = true;
                if (omittedFirst is { } hiddenFirst && omittedLast is { } hiddenLast
                    && a.Equals(hiddenFirst) && b.Equals(hiddenLast))
                {
                    omitted = true;
                    break;
                }
                if (Math.Abs(a.Y - b.Y) > Math.Max(a.Height, b.Height) / 2) continue;
                var left = Math.Min(a.Left, b.Left);
                var right = Math.Max(a.Right, b.Right);
                if (right <= left) continue;
                fragments.Add(new Rect(left, Math.Min(a.Top, b.Top), right - left, Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Top, b.Top)));
            }
            if (omitted) continue;
            // Keep separately laid-out fragments separate rather than painting over other RTL text.
            fragments.Sort((a, b) => { var line = a.Top.CompareTo(b.Top); return line != 0 ? line : a.Left.CompareTo(b.Left); });
            for (var index = 0; index < fragments.Count; index++)
            {
                var rect = fragments[index];
                while (index + 1 < fragments.Count && Math.Abs(fragments[index + 1].Top - rect.Top) < .75
                    && fragments[index + 1].Left <= rect.Right + .75)
                {
                    var next = fragments[++index];
                    rect = new Rect(Math.Min(rect.Left, next.Left), Math.Min(rect.Top, next.Top),
                        Math.Max(rect.Right, next.Right) - Math.Min(rect.Left, next.Left),
                        Math.Max(rect.Bottom, next.Bottom) - Math.Min(rect.Top, next.Top));
                }
                var position = transform.TransformPoint(new Point(rect.X, rect.Y));
                var x = Math.Max(0, position.X - 1.5);
                var y = Math.Max(0, position.Y - 1);
                var right = Math.Min(_backplates.ActualWidth, position.X + rect.Width + 1.5);
                var bottom = Math.Min(_backplates.ActualHeight, position.Y + rect.Height + 1);
                if (right <= x || bottom <= y) continue;
                var plate = Plate(used++);
                Canvas.SetLeft(plate, x); Canvas.SetTop(plate, y);
                plate.Width = right - x; plate.Height = bottom - y;
                plate.Visibility = Visibility.Visible;
            }
        }
        for (var index = used; index < _pool.Count; index++) _pool[index].Visibility = Visibility.Collapsed;
        RefreshBrushes();
        if (!available && !_waitingLayout && _layoutRetries == 0)
        {
            // Character rectangles are unavailable before a valid text layout. One-shot retry only.
            _waitingLayout = true;
            _layoutRetries++;
            _text.LayoutUpdated += LayoutReady;
        }
    }

    private Border Plate(int index)
    {
        if (index < _pool.Count) return _pool[index];
        var plate = new Border { CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
        _backplates.Children.Add(plate); _pool.Add(plate);
        return plate;
    }

    private void LayoutReady(object? sender, object e) { StopWaitingLayout(); QueueGeometry(); }
    private void StopWaitingLayout()
    {
        if (!_waitingLayout) return;
        _text.LayoutUpdated -= LayoutReady;
        _waitingLayout = false;
    }
    private void Hide() { foreach (var plate in _pool) plate.Visibility = Visibility.Collapsed; }
    private void RefreshBrushes()
    {
        if (ThemeResources.Resolve(_text, "FilesMate.NameMatch.BackgroundBrush") is Brush background)
            foreach (var plate in _pool) plate.Background = background;
    }
    private void Subscribe()
    {
        if (_subscribed) return;
        App.AppearanceChanged += AppearanceChanged;
        _subscribed = true;
    }
    private void Unsubscribe()
    {
        if (!_subscribed) return;
        App.AppearanceChanged -= AppearanceChanged;
        _subscribed = false;
    }
    private void AppearanceChanged(object? sender, AppearanceSettings settings) { RefreshBrushes(); QueueGeometry(); }
}
