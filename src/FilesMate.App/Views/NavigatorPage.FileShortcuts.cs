using FilesMate.App.Commands;
using FilesMate.App.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    internal bool InvokeWindowNavigationShortcut(VirtualKey key)
    {
        if (_disposed || !IsLoaded || FileAcceleratorsBlocked()) return false;
        switch (key)
        {
            case VirtualKey.Back:
            case VirtualKey.Up: ScheduleNavigation(ViewModel.Up); break;
            case VirtualKey.Left: ScheduleNavigation(ViewModel.Back); break;
            case VirtualKey.Right: ScheduleNavigation(ViewModel.Forward); break;
            case VirtualKey.S: ToggleDualPane(); break;
            default: return false;
        }
        return true;
    }

    internal bool InvokeWindowShortcut(ShortcutAction action)
    {
        if (_disposed || !IsLoaded || _ownerWindow.FileShortcutRoutingBlocked) return false;
        switch (action)
        {
            case ShortcutAction.EditAddress: Omni.BeginPathEdit(); return true;
            case ShortcutAction.FilterFolder: Omni.BeginSearch(); return true;
            case ShortcutAction.CommandPalette: _ = ShowCommandPaletteAsync(); return true;
        }
        if (FileAcceleratorsBlocked()) return false;
        switch (action)
        {
            case ShortcutAction.Refresh: RefreshCurrentFolder(); return true;
            case ShortcutAction.CopyPath: CopySelectedPaths(); return true;
            case ShortcutAction.Undo: _fileActions.Undo(); return true;
            case ShortcutAction.Redo: _fileActions.Redo(); return true;
            case ShortcutAction.Rename:
                RunFileCommand(ActiveSurface.Selection.Count > 1 ? AppCommandId.BatchRename : AppCommandId.Rename);
                return true;
            case ShortcutAction.Preview: SetPreviewVisible(!_previewVisible); return true;
            case ShortcutAction.OpenTerminal: OpenTerminalAt(ViewModel.AddressText); return true;
        }
        AppCommandId? command = action switch
        {
            ShortcutAction.Cut => AppCommandId.Cut,
            ShortcutAction.Copy => AppCommandId.Copy,
            ShortcutAction.Paste => AppCommandId.Paste,
            ShortcutAction.NewFolder => AppCommandId.NewFolder,
            ShortcutAction.Recycle => AppCommandId.Recycle,
            ShortcutAction.PermanentDelete => AppCommandId.PermanentDelete,
            ShortcutAction.Properties => AppCommandId.Properties,
            _ => null
        };
        if (command is not { } id) return false;
        _ = _fileActions.RunAsync(id);
        return true;
    }

    internal bool SelectAllFromWindow()
    {
        if (_disposed || FileAcceleratorsBlocked()) return false;
        return ActiveSurface.SelectAll();
    }

    internal bool HandleEscapeFromWindow()
    {
        if (_disposed || _ownerWindow.FileShortcutRoutingBlocked) return false;
        if (ShelfCard.Visibility == Visibility.Visible) { _shelfPanel?.RequestClose(); return true; }
        if (LockOverlay.Visibility == Visibility.Visible) { HideLockOverlay(); return true; }
        if (Omni.CancelMode()) return true;
        return !FileAcceleratorsBlocked() && ActiveSurface.ClearSelection();
    }
}
