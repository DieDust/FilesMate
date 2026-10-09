using FilesMate.App.Input;
using FilesMate.App.Models;
using FilesMate.Core.Entries;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private readonly NameRenameClickTracker _nameRenameClicks = new();
    private DispatcherQueueTimer? _nameRenameTimer;
    private UIElement? _nameRenameInputRoot;
    private NameRenameTarget? _renamePressTarget;
    private bool _renamePressWasOnlySelected;

    private NameRenameTarget? RenameTarget(FileEntryCore entry) => ResolvePath?.Invoke(entry) is { Length: > 0 } path
        ? new(_generation, entry.Id, path) : null;

    private void CancelNameClickRename(bool retainPrevious = false)
    {
        _nameRenameTimer?.Stop();
        _nameRenameInputRoot?.RemoveHandler(PointerPressedEvent, new PointerEventHandler(NameRenameRoot_PointerPressed));
        _nameRenameInputRoot = null;
        _nameRenameClicks.Cancel(retainPrevious);
    }

    private void ValidateNameClickRenameSelection()
    {
        _nameRenameClicks.ValidateSelection(_selection.Revision);
        if (!_nameRenameClicks.HasPending) CancelNameClickRename(retainPrevious: true);
    }

    private void ScheduleNameClickRename(FileEntryCore entry, ItemOpeningMode mode, bool onName, bool opened)
    {
        RecordNameOpening("ScheduleName", onName: onName, opened: opened);
        var target = RenameTarget(entry);
        if (_renamePressTarget is not { } pressed || target is not { } current
            || !NameRenameClickTracker.SameTarget(pressed, current)
            || _selection.Count != 1 || _selection.PrimaryId != entry.Id
            || !IsFolderWritable || IsPortableDevice || RenameRequested is null)
        {
            CancelNameClickRename();
            return;
        }
        var scheduled = _nameRenameClicks.CompleteClick(current, _selection.Revision, _renamePressWasOnlySelected,
            mode, onName, opened, Environment.TickCount64, (int)ItemActivation.DoubleClickInterval.TotalMilliseconds);
        RecordNameOpening("ScheduleNameResult", onName: onName, opened: opened);
        if (!scheduled) return;
        if (_nameRenameTimer is null)
        {
            _nameRenameTimer = DispatcherQueue.CreateTimer();
            _nameRenameTimer.IsRepeating = false;
            _nameRenameTimer.Tick += NameRenameTimer_Tick;
        }
        _nameRenameTimer.Interval = TimeSpan.FromMilliseconds(_nameRenameClicks.RemainingDelay(Environment.TickCount64));
        _nameRenameInputRoot = XamlRoot?.Content;
        _nameRenameInputRoot?.AddHandler(PointerPressedEvent, new PointerEventHandler(NameRenameRoot_PointerPressed), true);
        _nameRenameTimer.Start();
    }

    private void NameRenameRoot_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        CancelNameClickRename(retainPrevious: true);

    private void NameRenameTimer_Tick(DispatcherQueueTimer timer, object args)
    {
        timer.Stop();
        var eligible = IsLoaded && !IsRenaming && !_pointerDown && !_externalDragStarted
            && _selection.Count == 1 && IsFolderWritable && !IsPortableDevice && RenameRequested is not null
            && TryGetPrimary(out var entry) && OpeningMode(entry.Kind == EntryKind.Directory) == ItemOpeningMode.DoubleClick;
        RecordNameOpening("NameTimer");
        var target = eligible && TryGetPrimary(out var current) ? RenameTarget(current) : null;
        if (target is not { } identity)
        {
            CancelNameClickRename();
            return;
        }
        var openingWindow = _nameRenameClicks.PendingOpeningWindow;
        if (_nameRenameClicks.TryConsume(identity, _selection.Revision, eligible, Environment.TickCount64, out _))
            BeginInlineRenameCore(openingWindow);
        else if (_nameRenameClicks.HasPending)
        {
            timer.Interval = TimeSpan.FromMilliseconds(_nameRenameClicks.RemainingDelay(Environment.TickCount64));
            timer.Start();
        }
        else CancelNameClickRename();
    }
}
