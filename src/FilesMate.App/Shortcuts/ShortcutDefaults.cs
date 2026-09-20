namespace FilesMate.App.Shortcuts;

public sealed record ShortcutDefinition(
    ShortcutAction Action,
    string LabelKey,
    ShortcutGesture Gesture);

public static class ShortcutDefaults
{
    public static IReadOnlyList<ShortcutDefinition> Definitions { get; } =
    [
        Define(ShortcutAction.NewTab, "NewTab", ShortcutKey.T, ShortcutModifiers.Control),
        Define(ShortcutAction.CloseTab, "Shortcut_CloseTab", ShortcutKey.W, ShortcutModifiers.Control),
        Define(ShortcutAction.ReopenTab, "Tab_Reopen", ShortcutKey.T, ShortcutModifiers.Control | ShortcutModifiers.Shift),
        Define(ShortcutAction.CommandPalette, "CommandPalette", ShortcutKey.P, ShortcutModifiers.Control | ShortcutModifiers.Shift),
        Define(ShortcutAction.EditAddress, "Shortcut_EditAddress", ShortcutKey.L, ShortcutModifiers.Control),
        Define(ShortcutAction.FilterFolder, "Shortcut_FilterFolder", ShortcutKey.F, ShortcutModifiers.Control),
        Define(ShortcutAction.Refresh, "Command_Refresh", ShortcutKey.F5),
        Define(ShortcutAction.Cut, "Command_Cut", ShortcutKey.X, ShortcutModifiers.Control),
        Define(ShortcutAction.Copy, "Command_Copy", ShortcutKey.C, ShortcutModifiers.Control),
        Define(ShortcutAction.CopyPath, "Command_CopyPath", ShortcutKey.C, ShortcutModifiers.Control | ShortcutModifiers.Shift),
        Define(ShortcutAction.Paste, "Command_Paste", ShortcutKey.V, ShortcutModifiers.Control),
        Define(ShortcutAction.Undo, "Shortcut_Undo", ShortcutKey.Z, ShortcutModifiers.Control),
        Define(ShortcutAction.Redo, "Shortcut_Redo", ShortcutKey.Y, ShortcutModifiers.Control),
        Define(ShortcutAction.NewFolder, "Command_NewFolder", ShortcutKey.N, ShortcutModifiers.Control | ShortcutModifiers.Shift),
        Define(ShortcutAction.Rename, "Command_Rename", ShortcutKey.F2),
        Define(ShortcutAction.Recycle, "Command_Recycle", ShortcutKey.Delete),
        Define(ShortcutAction.PermanentDelete, "Command_PermanentDelete", ShortcutKey.Delete, ShortcutModifiers.Shift),
        Define(ShortcutAction.Properties, "Command_Properties", ShortcutKey.Enter, ShortcutModifiers.Menu),
        Define(ShortcutAction.OpenTerminal, "Command_OpenInTerminal", ShortcutKey.T, ShortcutModifiers.Control | ShortcutModifiers.Menu),
        Define(ShortcutAction.Preview, "Shortcut_Preview", ShortcutKey.P, ShortcutModifiers.Menu),
    ];

    public static ShortcutDefinition For(ShortcutAction action) =>
        Definitions.First(item => item.Action == action);

    private static ShortcutDefinition Define(
        ShortcutAction action,
        string labelKey,
        ShortcutKey key,
        ShortcutModifiers modifiers = ShortcutModifiers.None) =>
        new(action, labelKey, new ShortcutGesture(key, modifiers));
}
