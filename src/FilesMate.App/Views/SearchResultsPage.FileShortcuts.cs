using FilesMate.App.Commands;
using FilesMate.App.Shortcuts;
using Microsoft.UI.Xaml;
using Windows.System;

namespace FilesMate.App.Views;

public sealed partial class SearchResultsPage
{
    internal bool InvokeWindowNavigationShortcut(VirtualKey key)
    {
        var window = App.WindowForElement(this);
        if (_disposed || window is null || window.FileShortcutRoutingBlocked || window.TextInputFocused
            || Results.IsRenaming || _shelfPanel?.ContainsFocus() == true) return false;
        if (key is not (VirtualKey.Back or VirtualKey.Up)) return false;
        RevealSelected();
        return true;
    }

    internal bool InvokeWindowShortcut(ShortcutAction action)
    {
        var window = App.WindowForElement(this);
        if (_disposed || window is null || window.FileShortcutRoutingBlocked) return false;
        if (action is ShortcutAction.FilterFolder or ShortcutAction.EditAddress)
        { QueryBox.Focus(FocusState.Keyboard); QueryBox.SelectAll(); return true; }
        if (window.TextInputFocused || Results.IsRenaming || _shelfPanel?.ContainsFocus() == true) return false;
        switch (action)
        {
            case ShortcutAction.Refresh: _ = SearchAsync(false); return true;
            case ShortcutAction.Undo: _fileActions.Undo(); return true;
            case ShortcutAction.Redo: _fileActions.Redo(); return true;
            case ShortcutAction.Preview: ToggleQuickPreview(); return true;
        }
        AppCommandId? command = action switch
        {
            ShortcutAction.Copy => AppCommandId.Copy,
            ShortcutAction.Cut => AppCommandId.Cut,
            ShortcutAction.CopyPath => AppCommandId.CopyPath,
            ShortcutAction.Paste => AppCommandId.Paste,
            ShortcutAction.Rename => Results.Selection.Count > 1 ? AppCommandId.BatchRename : AppCommandId.Rename,
            ShortcutAction.Recycle => AppCommandId.Recycle,
            ShortcutAction.PermanentDelete => AppCommandId.PermanentDelete,
            ShortcutAction.Properties => AppCommandId.Properties,
            ShortcutAction.OpenTerminal => AppCommandId.OpenInTerminal,
            _ => null
        };
        if (command is not { } id) return false;
        _ = RunActionAsync(id);
        return true;
    }

    internal bool HandleEscapeFromWindow()
    {
        var window = App.WindowForElement(this);
        if (_disposed || window is null || window.FileShortcutRoutingBlocked || window.TextInputFocused || Results.IsRenaming) return false;
        if (IsSearching) { Stop_Click(this, new()); return true; }
        return Results.ClearSelection();
    }

    internal bool SelectAllFromWindow()
    {
        var window = App.WindowForElement(this);
        return !_disposed && window is not null && !window.FileShortcutRoutingBlocked
            && !window.TextInputFocused && !Results.IsRenaming && _shelfPanel?.ContainsFocus() != true && Results.SelectAll();
    }
}
