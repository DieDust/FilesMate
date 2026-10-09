using FilesMate.App.Input;
using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Models;
using FilesMate.Core.Entries;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private TextBox? _renameEditor;
    private TextBlock? _renameLabel;
    private FrameworkElement? _renameRow;
    private string? _renamePath;
    private int _renameSequence;
    private UIElement? _renameInputRoot;
    private double _renameLabelOpacity;
    private bool _renameLabelHitTestVisible;
    private bool _renameStarting;
    internal bool OwnsTextEditing => IsRenaming || _renameStarting;
    public bool IsRenaming => _renameEditor is not null;
    public Func<string, string, Task<string?>>? RenameRequested { get; set; }

    private NameRenameOpeningWindow? _renameClickOpening;
    private uint? _renameClickPointer;
    private Point _renameClickPointerPosition;

    public void BeginInlineRename() => BeginInlineRenameCore(null);

    private async void BeginInlineRenameCore(NameRenameOpeningWindow? openingWindow)
    {
        CancelNameClickRename();
        if (IsPortableDevice || OwnsTextEditing || RenameRequested is null || !IsFolderWritable || _selection.Count != 1) return;
        if (!ValidateRecursiveSelection()) return;
        NotifyUserInteraction();
        StopListWheel();
        CancelPendingReveal();
        var sequence = ++_renameSequence;
        var targetId = _selection.PrimaryId;
        var generation = _generation;
        var selectionRevision = _selection.Revision;
        var interaction = UserInteractionVersion;
        if (!TryGetPrimary(out var targetEntry) || RenameTarget(targetEntry) is not { } target) return;
        _renameStarting = true;
        try
        {
        for (var attempt = 0; attempt < 20 && IsLoaded && sequence == _renameSequence; attempt++)
        {
            if (attempt > 0) await Task.Delay(30);
            if (!IsLoaded || sequence != _renameSequence || generation != _generation
                || targetId != _selection.PrimaryId || _selection.Count != 1
                || selectionRevision != _selection.Revision || interaction != UserInteractionVersion) return;
            if (_selection.PrimaryId is not int id || !TryGetPrimary(out var entry)) return;
            if (RenameTarget(entry) is not { } current || !NameRenameClickTracker.SameTarget(target, current)) return;
            if (_items.Store is null || _items.Index is null) return;
            var view = _selection.ViewIndexOfPrimary(_items.Store, _items.Index);
            if (view < 0) return;
            var row = (FrameworkElement?)_realized.FirstOrDefault(r => r.EntryId == id)
                ?? _tiles.FirstOrDefault(t => t.EntryId == id);
            var label = row?.FindName("NameText") as TextBlock;
            if (!EnsureRenameTargetVisible(view, label) || label?.Parent is not Grid parent) continue;
            _renamePath = current.Path;
            _renameLabel = label;
            _renameRow = row;
            _renameLabelOpacity = label.Opacity;
            _renameLabelHitTestVisible = label.IsHitTestVisible;
            var editor = new TextBox { Text = entry.Name, MinWidth = 0, MinHeight = 0,
                Padding = new Thickness(2, 0, 2, 0), FontSize = label.FontSize, FontFamily = label.FontFamily,
                MaxWidth = Math.Max(1, parent.ActualWidth), MaxHeight = Math.Max(label.ActualHeight, label.FontSize + 4),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(editor, Loc.Get("FileName"));
            Grid.SetColumn(editor, Grid.GetColumn(label));
            Grid.SetRow(editor, Grid.GetRow(label));
            Canvas.SetZIndex(editor, 10);
            _renameEditor = editor;
            // Keep the measured name cell as the scroll/layout anchor.
            label.Opacity = 0;
            label.IsHitTestVisible = false;
            parent.Children.Add(editor);
            if (!ReferenceEquals(editor, _renameEditor) || sequence != _renameSequence) return;
            _renameClickOpening = openingWindow;
            RecordNameOpening("EditorCreated");
            editor.AddHandler(PointerPressedEvent, new PointerEventHandler(RenameEditor_PointerPressed), true);
            editor.AddHandler(PointerReleasedEvent, new PointerEventHandler(RenameEditor_PointerReleased), true);
            editor.PointerCaptureLost += RenameEditor_PointerCaptureLost;
            editor.TextChanged += RenameEditor_TextChanged;
            editor.PreviewKeyDown += RenameEditor_KeyDown;
            editor.LostFocus += RenameEditor_LostFocus;
            editor.BringIntoViewRequested += RenameEditor_BringIntoViewRequested;
            _renameInputRoot = XamlRoot?.Content;
            _renameInputRoot?.AddHandler(PointerPressedEvent, new PointerEventHandler(RenameRoot_PointerPressed), true);
            editor.Focus(FocusState.Programmatic);
            editor.Select(0, FileNameRules.StemLength(entry.Name, entry.Kind == EntryKind.Directory));
            return;
        }
        }
        finally { _renameStarting = false; }
    }

    private bool EnsureRenameTargetVisible(int view, TextBlock? label)
    {
        var horizontal = Scroller.HorizontalOffset;
        var vertical = Scroller.VerticalOffset;
        var desiredX = horizontal;
        var desiredY = vertical;
        var measured = label is { ActualWidth: > 0, ActualHeight: > 0 };
        if (measured)
        {
            var bounds = label!.TransformToVisual(Scroller).TransformBounds(new Rect(0, 0, label.ActualWidth, label.ActualHeight));
            desiredX = InlineRenameViewport.MinimumOffset(bounds.X + horizontal, bounds.Width,
                horizontal, Scroller.ViewportWidth, Scroller.ScrollableWidth);
            desiredY = InlineRenameViewport.MinimumOffset(bounds.Y + vertical, bounds.Height,
                vertical, Scroller.ViewportHeight, Scroller.ScrollableHeight);
        }
        else if (_layout == FileLayoutKind.List)
        {
            var column = view / ListRows;
            desiredX = InlineRenameViewport.MinimumOffset(ListGeometry.LeftAt(column), ListGeometry.WidthAt(column),
                horizontal, Scroller.ViewportWidth, Scroller.ScrollableWidth);
        }
        else
        {
            var start = _layout == FileLayoutKind.Grid ? (view / Columns()) * ItemHeight() : view * ItemHeight();
            var height = _layout == FileLayoutKind.Grid ? EffectiveGridPreset.ItemHeight : RowHeight;
            desiredY = InlineRenameViewport.MinimumOffset(start, height, vertical, Scroller.ViewportHeight, Scroller.ScrollableHeight);
        }
        if (Math.Abs(desiredX - horizontal) > .01 || Math.Abs(desiredY - vertical) > .01)
        {
            Scroller.ChangeView(desiredX, desiredY, null, disableAnimation: true);
            return false;
        }
        return measured && Scroller.ViewportWidth > 0 && Scroller.ViewportHeight > 0;
    }

    private void RenameEditor_BringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        // Visibility is established above. Focus must not realign the parent's
        // viewport; the TextBox's own internal caret scrolling still runs first.
        // If it later leaves the viewport, allow accessibility to reveal it.
        if (!ReferenceEquals(sender, _renameEditor) || _renameLabel is not { } label) return;
        var bounds = label.TransformToVisual(Scroller).TransformBounds(new Rect(0, 0, label.ActualWidth, label.ActualHeight));
        if (bounds.Width > 0 && bounds.Height > 0 && bounds.Right > 0 && bounds.X < Scroller.ViewportWidth
            && bounds.Bottom > 0 && bounds.Y < Scroller.ViewportHeight) args.Handled = true;
    }

    private bool CanFinishNameDoubleClick(out FileEntryCore entry)
    {
        entry = default;
        return _renameClickOpening is { } window && _renameEditor is { } editor
            && !ItemActivation.HasSelectionModifier && _selection.Count == 1
            && TryGetPrimary(out entry) && RenameTarget(entry) is { } current
            && OpeningMode(entry.Kind == EntryKind.Directory) == ItemOpeningMode.DoubleClick
            && NameRenameClickTracker.CanCompleteOpening(window, current, _selection.Revision,
                Environment.TickCount64, string.Equals(editor.Text, entry.Name, StringComparison.Ordinal));
    }

    private void RenameEditor_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        RecordNameOpening("EditorPressed", e);
        if (!ReferenceEquals(sender, _renameEditor) || !CanFinishNameDoubleClick(out _)) return;
        var point = e.GetCurrentPoint(XamlRoot.Content);
        if (point.Properties.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonPressed) return;
        // TextBox's inner view may lose capture synchronously as its parent
        // takes it. Publish this press only after that transfer completes.
        if (!_renameEditor!.CapturePointer(e.Pointer)) return;
        _renameClickPointer = e.Pointer.PointerId;
        _renameClickPointerPosition = point.Position;
        RecordNameOpening("CapturedEditorPress", e);
        e.Handled = true;
    }

    private void RenameEditor_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        RecordNameOpening("EditorReleased", e);
        if (!ReferenceEquals(sender, _renameEditor) || _renameClickPointer != e.Pointer.PointerId) return;
        _renameClickPointer = null;
        var position = e.GetCurrentPoint(XamlRoot.Content).Position;
        var editor = _renameEditor!;
        editor.ReleasePointerCapture(e.Pointer);
        if (!CanFinishNameDoubleClick(out var entry)
            || Hypot(position.X - _renameClickPointerPosition.X, position.Y - _renameClickPointerPosition.Y) > 4
            || !ItemActivation.ShouldOpen(this, _renamePath!, position, ItemOpeningMode.DoubleClick, onName: true)) return;
        e.Handled = true;
        RecordNameOpening("FastThirdOpened", e);
        CancelInlineRename();
        ActivateEntry(entry);
    }

    private void RenameEditor_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        RecordNameOpening("EditorCaptureLost", e);
        // Native TextBox releases capture before bubbling PointerReleased on
        // mouse-up. Keep that completed press for the matching release handler;
        // capture transfer while the button is still down cancels it.
        if (ReferenceEquals(e.OriginalSource, _renameEditor)
            && e.GetCurrentPoint(XamlRoot.Content).Properties.IsLeftButtonPressed)
            _renameClickPointer = null;
    }

    private void RenameEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        // The initial Text notification can arrive after parenting/focus.
        // Only an actual edit revokes the opening window, irreversibly.
        if (_renameEditor is not { } editor || !TryGetPrimary(out var entry)
            || !string.Equals(editor.Text, entry.Name, StringComparison.Ordinal))
            _renameClickOpening = null;
        RecordNameOpening("EditorTextChanged");
    }

    private int _nameOpeningAuditCount;
    private void RecordNameOpening(string stage, PointerRoutedEventArgs? args = null, bool? onName = null, bool? opened = null)
    {
        // Explicit isolated acceptance marker only; ordinary/performance profiles
        // perform no IO and retain no diagnostic history.
        if (_presentationAudit is null || _nameOpeningAuditCount++ >= 128) return;
        try
        {
            var now = Environment.TickCount64;
            var nativeName = _pressViewIndex >= 0
                ? (Repeater.TryGetElement(_pressViewIndex) as FrameworkElement)?.FindName("NameText") as TextBlock : null;
            var nameBounds = nativeName is null ? (Rect?)null : nativeName.TransformToVisual(Scroller)
                .TransformBounds(new Rect(0, 0, nativeName.ActualWidth, nativeName.ActualHeight));
            var pointerPosition = args?.GetCurrentPoint(Scroller).Position;
            var data = new
            {
                Utc = DateTimeOffset.UtcNow, Stage = stage, Generation = _generation,
                Layout = _layout.ToString(), PressViewIndex = _pressViewIndex,
                PressPointer = _pressPointerId, PointerDown = _pointerDown, PressOnName = _pressOnName,
                PressWasOnlySelected = _renamePressWasOnlySelected, PressTargetId = _renamePressTarget?.EntryId,
                PendingRename = _nameRenameClicks.HasPending, RenameDelay = _nameRenameClicks.RemainingDelay(now),
                OnName = onName, Opened = opened, UserInteraction = UserInteractionVersion,
                WindowPresent = _renameClickOpening is not null,
                WindowRemainingMs = _renameClickOpening is { } window ? window.Until - now : 0,
                WindowRevision = _renameClickOpening?.Revision, SelectionRevision = _selection.Revision,
                SelectionCount = _selection.Count, PrimaryId = _selection.PrimaryId,
                EditorPresent = _renameEditor is not null, Pointer = _renameClickPointer,
                OriginalSource = args?.OriginalSource?.GetType().Name,
                OriginalSourceName = (args?.OriginalSource as FrameworkElement)?.Name,
                RoutedNameHit = nativeName is not null && IsInside(args?.OriginalSource as DependencyObject, nativeName),
                NameBounds = nameBounds, PointerPosition = pointerPosition,
                LeftButtonPressed = args?.GetCurrentPoint(XamlRoot.Content).Properties.IsLeftButtonPressed,
                SameText = _renameEditor is { } editor && TryGetPrimary(out var entry)
                    && string.Equals(editor.Text, entry.Name, StringComparison.Ordinal),
            };
            File.AppendAllText(Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath,
                "rename-opening.jsonl"), System.Text.Json.JsonSerializer.Serialize(data) + Environment.NewLine);
        }
        catch (Exception error) { App.AppendCrashRecord("NameOpeningAudit", error); }
    }
    private void RenameEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        _renameClickOpening = null;
        NotifyUserInteraction();
        if (e.Key == VirtualKey.Escape) { CancelInlineRename(); Focus(FocusState.Programmatic); e.Handled = true; }
        else if (e.Key is VirtualKey.Enter or VirtualKey.Tab)
        {
            e.Handled = true;
            _ = CommitInlineRenameAsync(e.Key == VirtualKey.Tab ? (IsModifier(VirtualKey.Shift) ? -1 : 1) : 0);
        }
    }
    private void RenameRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        CommitRenameOutside(e.OriginalSource as DependencyObject);
    }

    internal void CommitRenameOutside(DependencyObject? source)
    {
        if (_renameEditor is not { } editor || IsInside(source, editor)) return;
        _ = CompleteInlineRenameAsync(0, restoreFocus: false);
    }

    private void RenameEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        var editor = _renameEditor;
        // A TextBox's context menu may temporarily own focus. Pointer dismissal is
        // handled at the root; keyboard focus leaving the editor also commits.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (editor is null || !ReferenceEquals(editor, _renameEditor) || XamlRoot is null) return;
            if (IsInside(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject, editor)) return;
            if (VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Count > 0) return;
            _ = CompleteInlineRenameAsync(0, restoreFocus: false);
        });
    }

    private static bool IsInside(DependencyObject? element, DependencyObject ancestor)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, ancestor)) return true;
        return false;
    }

    private Task CommitInlineRenameAsync(int direction) => CompleteInlineRenameAsync(direction, restoreFocus: true);

    private async Task CompleteInlineRenameAsync(int direction, bool restoreFocus)
    {
        if (_renameEditor is not { } editor || _renamePath is not { } path || RenameRequested is null) return;
        var name = editor.Text;
        var folder = ResolveFolder?.Invoke();
        var primaryId = _selection.PrimaryId;
        var recursiveSession = _items.Binding?.Recursive?.SessionId;
        string? nextName = null;
        if (recursiveSession is null && direction != 0 && _items.Store is not null && _items.Index is not null)
        {
            var next = _selection.ViewIndexOfPrimary(_items.Store, _items.Index) + direction;
            if (next >= 0 && next < _items.Count && _items.TryGetEntry(next, out var entry)) nextName = entry.Name;
        }
        CancelInlineRename();
        var sequence = _renameSequence;
        if (recursiveSession is not null && (primaryId is not int operationId || !ValidateRecursiveEntry(operationId))) return;
        var renamedPath = await RenameRequested(path, name);
        var succeeded = renamedPath is not null;
        if (!IsLoaded || sequence != _renameSequence || !string.Equals(folder, ResolveFolder?.Invoke(), StringComparison.OrdinalIgnoreCase)) return;
        // A click may select another item while the filesystem operation is pending.
        // Keep that selection and the new control's focus when finishing on blur.
        if (!restoreFocus && _selection.PrimaryId is { } selectedId && selectedId != primaryId) return;
        if (recursiveSession is not null)
        {
            // Leaf names are not identities in a tree. Never chain Tab rename
            // or select a same-named row in a different parent after an operation.
            if (_items.Binding?.Recursive?.SessionId != recursiveSession) return;
            if (succeeded) ClearSelection();
            if (restoreFocus) Focus(FocusState.Programmatic);
            return;
        }
        var target = succeeded ? nextName ?? Path.GetFileName(renamedPath!) : Path.GetFileName(path);
        for (var attempt = 0; attempt < 60 && IsLoaded && sequence == _renameSequence; attempt++)
        {
            if (!string.Equals(folder, ResolveFolder?.Invoke(), StringComparison.OrdinalIgnoreCase)) return;
            if (TrySelectByName(target))
            {
                if (succeeded && nextName is not null) BeginInlineRename();
                else if (restoreFocus) Focus(FocusState.Programmatic);
                return;
            }
            await Task.Delay(50);
        }
    }

    private void CancelInlineRename()
    {
        CancelNameClickRename();
        ++_renameSequence;
        _renameInputRoot?.RemoveHandler(PointerPressedEvent, new PointerEventHandler(RenameRoot_PointerPressed));
        _renameInputRoot = null;
        if (_renameEditor is { } editor)
        {
            editor.RemoveHandler(PointerPressedEvent, new PointerEventHandler(RenameEditor_PointerPressed));
            editor.RemoveHandler(PointerReleasedEvent, new PointerEventHandler(RenameEditor_PointerReleased));
            editor.PointerCaptureLost -= RenameEditor_PointerCaptureLost;
            editor.TextChanged -= RenameEditor_TextChanged;
            editor.PreviewKeyDown -= RenameEditor_KeyDown;
            editor.LostFocus -= RenameEditor_LostFocus;
            editor.BringIntoViewRequested -= RenameEditor_BringIntoViewRequested;
            _renameEditor = null;
            if (editor.Parent is Panel parent) parent.Children.Remove(editor);
        }
        if (_renameLabel is { } label)
        {
            label.Opacity = _renameLabelOpacity;
            label.IsHitTestVisible = _renameLabelHitTestVisible;
        }
        _renameClickOpening = null;
        _renameClickPointer = null;
        _renameLabel = null;
        _renameRow = null;
        _renamePath = null;
    }
}
