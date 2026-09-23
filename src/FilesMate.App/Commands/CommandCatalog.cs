using FilesMate.App.Localization;
using FilesMate.App.Navigation;

namespace FilesMate.App.Commands;

public static class CommandCatalog
{
    public static readonly AppCommandId[] All =
    [
        AppCommandId.AddToFavorites,
        AppCommandId.AddToShelf,
        AppCommandId.ShowShelf,
        AppCommandId.ChooseFolderCover,
        AppCommandId.ResetFolderCover,
        AppCommandId.ResetFolderView,
        AppCommandId.NewFolder,
        AppCommandId.NewFile,
        AppCommandId.Cut,
        AppCommandId.Copy,
        AppCommandId.Paste,
        AppCommandId.Rename,
        AppCommandId.Share,
        AppCommandId.Recycle,
        AppCommandId.PermanentDelete,
        AppCommandId.Open,
        AppCommandId.OpenInNewTab,
        AppCommandId.OpenWith,
        AppCommandId.OpenInTerminal,
        AppCommandId.Properties,
        AppCommandId.PinToSidebar,
        AppCommandId.UnpinFromSidebar,
        AppCommandId.CopyPath,
        AppCommandId.SelectAll,
        AppCommandId.Refresh,
        AppCommandId.Sort,
        AppCommandId.ChangeLayout,
    ];

    public static readonly AppCommandId[] Implemented =
    [
        AppCommandId.AddToFavorites,
        AppCommandId.AddToShelf,
        AppCommandId.ShowShelf,
        AppCommandId.ChooseFolderCover,
        AppCommandId.ResetFolderCover,
        AppCommandId.ResetFolderView,
        AppCommandId.NewFolder,
        AppCommandId.NewFile,
        AppCommandId.Cut,
        AppCommandId.Copy,
        AppCommandId.Paste,
        AppCommandId.Rename,
        AppCommandId.Share,
        AppCommandId.Recycle,
        AppCommandId.PermanentDelete,
        AppCommandId.Open,
        AppCommandId.OpenInNewTab,
        AppCommandId.OpenInNewWindow,
        AppCommandId.OpenWith,
        AppCommandId.OpenInTerminal,
        AppCommandId.OpenInCompactMate,
        AppCommandId.Properties,
        AppCommandId.WhoLocks,
        AppCommandId.PinToSidebar,
        AppCommandId.UnpinFromSidebar,
        AppCommandId.CopyPath,
        AppCommandId.CopyPathQuoted,
        AppCommandId.CreateShortcut,
        AppCommandId.Compress,
        AppCommandId.CompressZip,
        AppCommandId.Compress7z,
        AppCommandId.CompressNew,
        AppCommandId.Extract,
        AppCommandId.ExtractHere,
        AppCommandId.ExtractToFolder,
        AppCommandId.ExtractToOther,
        AppCommandId.SmartExtract,
        AppCommandId.SelectAll,
        AppCommandId.Refresh,
        AppCommandId.Sort,
        AppCommandId.ChangeLayout,
    ];

    public static readonly AppCommandId[] MenuOrder =
    [
        AppCommandId.Open,
        AppCommandId.OpenInNewTab,
        AppCommandId.OpenInNewWindow,
        AppCommandId.OpenWith,
        AppCommandId.OpenInCompactMate,
        AppCommandId.PinToSidebar,
        AppCommandId.UnpinFromSidebar,
        AppCommandId.SelectAll,
        AppCommandId.NewFolder,
        AppCommandId.NewFile,
        AppCommandId.Cut,
        AppCommandId.Copy,
        AppCommandId.Paste,
        AppCommandId.Rename,
        AppCommandId.BatchRename,
        AppCommandId.NewFolderWithSelection,
        AppCommandId.SelectSameType,
        AppCommandId.InvertSelection,
        AppCommandId.CopyToOtherPane,
        AppCommandId.MoveToOtherPane,
        AppCommandId.Share,
        AppCommandId.Recycle,
        AppCommandId.PermanentDelete,
        AppCommandId.CopyPath,
        AppCommandId.CopyPathQuoted,
        AppCommandId.CreateShortcut,
        AppCommandId.AddTags,
        AppCommandId.ManageTags,
        AppCommandId.AddToFavorites,
        AppCommandId.AddToShelf,
        AppCommandId.ShowShelf,
        AppCommandId.ChooseFolderCover,
        AppCommandId.ResetFolderCover,
        AppCommandId.ResetFolderView,
        AppCommandId.Compress,
        AppCommandId.Extract,
        AppCommandId.Sort,
        AppCommandId.ChangeLayout,
        AppCommandId.Refresh,
        AppCommandId.OpenInTerminal,
        AppCommandId.SearchCommands,
        AppCommandId.WhoLocks,
        AppCommandId.Properties,
    ];

    public static AppCommand Resolve(AppCommandId id, CommandContext context)
    {
        var (label, glyph, shortcut, group) = Describe(id);
        var implemented = IsImplemented(id);
        return new AppCommand
        {
            Id = id,
            Label = label,
            Tooltip = TooltipFor(id, context, label, shortcut),
            Glyph = glyph,
            Shortcut = shortcut,
            Group = group,
            Implemented = implemented,
            Visible = implemented && IsVisible(id, context),
            Enabled = implemented && CanExecute(id, context),
        };
    }

    public static IReadOnlyList<AppCommand> VisibleCommands(CommandContext context)
    {
        var source = context.Surface == CommandSurface.Menu ? MenuOrder : All;
        var commands = new List<AppCommand>(source.Length);
        foreach (var id in source)
        {
            var command = Resolve(id, context);
            if (command.Visible)
            {
                commands.Add(command);
            }
        }

        return commands;
    }

    public static bool CanExecute(AppCommandId id, CommandContext context) =>
        (!context.IsPortableDevice || DeviceCommandSupported(id)) && CanExecuteCore(id, context);

    // Device objects are not filesystem paths. Only expose commands implemented by
    // the Shell provider; never feed an opaque identity to a local mutation API.
    public static bool DeviceCommandSupported(AppCommandId id) => id is
        AppCommandId.Open or AppCommandId.OpenInNewTab or AppCommandId.OpenInNewWindow
        or AppCommandId.Copy or AppCommandId.Paste or AppCommandId.CopyToOtherPane
        or AppCommandId.SelectAll or AppCommandId.SelectSameType or AppCommandId.InvertSelection
        or AppCommandId.Refresh or AppCommandId.Sort or AppCommandId.ChangeLayout or AppCommandId.SearchCommands;

    private static bool CanExecuteCore(AppCommandId id, CommandContext context) => id switch
    {
        AppCommandId.AddToFavorites => context.SelectionCount > 0,
        AppCommandId.AddToShelf => context.SelectionCount > 0,
        AppCommandId.ShowShelf => true,
        AppCommandId.ChooseFolderCover or AppCommandId.ResetFolderCover =>
            (context.IsBackground && HasFolderPath(context))
            || (context.SelectionCount == 1 && context.PrimaryIsDirectory),
        AppCommandId.ResetFolderView => HasFolderPath(context),
        AppCommandId.NewFolder or AppCommandId.NewFile => context.IsFolderWritable,
        AppCommandId.Cut or AppCommandId.Copy => HasItemTarget(context),
        AppCommandId.Share => context.ShareAvailable && HasItemTarget(context),
        AppCommandId.Paste => context.IsFolderWritable && context.ClipboardHasFiles,
        AppCommandId.Rename => context.IsFolderWritable && HasSingleTarget(context),
        AppCommandId.BatchRename => context.IsFolderWritable && context.SelectionCount > 1,
        AppCommandId.Recycle or AppCommandId.PermanentDelete =>
            context.IsFolderWritable && context.SelectionCount > 0,
        AppCommandId.Open => context.SelectionCount == 1,
        AppCommandId.OpenInNewTab or AppCommandId.OpenInNewWindow =>
            context.SelectionCount == 1 && context.PrimaryIsDirectory,
        AppCommandId.OpenWith => context.SelectionCount == 1 && !context.PrimaryIsDirectory,
        AppCommandId.OpenInCompactMate => context.SelectionCount == 1 && context.PrimaryIsArchive,
        AppCommandId.OpenInTerminal => TerminalTarget(context) is not null,
        AppCommandId.Properties => context.SelectionCount > 0 || HasFolderPath(context),
        AppCommandId.WhoLocks => HasItemTarget(context),
        AppCommandId.AddTags => context.SelectionCount > 0,
        AppCommandId.ManageTags => true,
        AppCommandId.PinToSidebar => context.SelectionCount == 1
            && context.PrimaryIsDirectory
            && !string.IsNullOrWhiteSpace(context.PrimaryPath)
            && !context.PrimaryIsPinned,
        AppCommandId.UnpinFromSidebar => context.SelectionCount == 1
            && context.PrimaryIsDirectory
            && !string.IsNullOrWhiteSpace(context.PrimaryPath)
            && context.PrimaryIsPinned,
        AppCommandId.CopyPath or AppCommandId.CopyPathQuoted =>
            context.SelectionCount > 0 || HasFolderPath(context),
        AppCommandId.CreateShortcut => context.IsFolderWritable && context.SelectionCount == 1,
        AppCommandId.Compress or
        AppCommandId.CompressZip or
        AppCommandId.Compress7z or
        AppCommandId.CompressNew => context.SelectionCount > 0,
        AppCommandId.Extract or
        AppCommandId.ExtractHere or
        AppCommandId.ExtractToFolder or
        AppCommandId.ExtractToOther or
        AppCommandId.SmartExtract => context.SelectionIsArchive && context.SelectionCount > 0,
        AppCommandId.SelectAll => true,
        AppCommandId.NewFolderWithSelection => context.IsFolderWritable && HasFolderPath(context) && context.SelectionCount > 0,
        AppCommandId.SelectSameType => context.SelectionCount > 0,
        AppCommandId.InvertSelection or AppCommandId.SearchCommands => true,
        AppCommandId.CopyToOtherPane or AppCommandId.MoveToOtherPane => context.SelectionCount > 0 && context.OtherPanePath is not null,
        AppCommandId.Refresh => context.CanRefresh,
        AppCommandId.Sort or AppCommandId.ChangeLayout => true,
        _ => false,
    };

    private static bool IsVisible(AppCommandId id, CommandContext context) =>
        (!context.IsPortableDevice || context.Surface == CommandSurface.Toolbar || DeviceCommandSupported(id))
        && IsVisibleCore(id, context);

    private static bool IsVisibleCore(AppCommandId id, CommandContext context) => context.Surface switch
    {
        CommandSurface.Toolbar => id switch
        {
            AppCommandId.NewFolder or
            AppCommandId.Cut or
            AppCommandId.Copy or
            AppCommandId.Paste or
            AppCommandId.Rename or
            AppCommandId.Recycle or
            AppCommandId.Sort or
            AppCommandId.ChangeLayout => true,
            AppCommandId.Share => context.ShareAvailable && context.SelectionCount > 0,
            AppCommandId.CopyPath => context.SelectionCount > 0,
            AppCommandId.AddTags or AppCommandId.ManageTags => false,
            AppCommandId.BatchRename => false,
            AppCommandId.PinToSidebar or AppCommandId.UnpinFromSidebar => false,
            _ => false,
        },
        CommandSurface.Shortcut => true,
        CommandSurface.Menu when context.IsBackground => id switch
        {
            AppCommandId.ShowShelf => true,
            AppCommandId.NewFolderWithSelection => HasFolderPath(context) && context.SelectionCount > 0,
            AppCommandId.InvertSelection or AppCommandId.SearchCommands => true,
            AppCommandId.ChooseFolderCover or AppCommandId.ResetFolderCover
                or AppCommandId.ResetFolderView => HasFolderPath(context),
            AppCommandId.NewFolder or
            AppCommandId.NewFile or
            AppCommandId.Paste => HasFolderPath(context),
            AppCommandId.SelectAll or
            AppCommandId.Refresh or
            AppCommandId.Sort or
            AppCommandId.ChangeLayout => true,
            AppCommandId.Cut or
            AppCommandId.Copy or
            AppCommandId.Rename or
            AppCommandId.Recycle => HasFolderPath(context),
            AppCommandId.Share => context.ShareAvailable && HasFolderPath(context),
            AppCommandId.CopyPath or
            AppCommandId.Properties or
            AppCommandId.WhoLocks or
            AppCommandId.OpenInTerminal => HasFolderPath(context),
            _ => false,
        },
        CommandSurface.Menu => id switch
        {
            AppCommandId.AddToFavorites => context.SelectionCount > 0,
            AppCommandId.NewFolderWithSelection => HasFolderPath(context) && context.SelectionCount > 0,
            AppCommandId.SelectSameType or AppCommandId.InvertSelection => context.SelectionCount > 0,
            AppCommandId.CopyToOtherPane or AppCommandId.MoveToOtherPane => context.SelectionCount > 0 && context.OtherPanePath is not null,
            AppCommandId.AddToShelf => context.SelectionCount > 0,
            AppCommandId.ChooseFolderCover or AppCommandId.ResetFolderCover =>
                context.SelectionCount == 1 && context.PrimaryIsDirectory,
            AppCommandId.Open => context.SelectionCount == 1,
            AppCommandId.OpenInNewTab or AppCommandId.OpenInNewWindow =>
                context.SelectionCount == 1 && context.PrimaryIsDirectory,
            AppCommandId.OpenWith => context.SelectionCount == 1 && !context.PrimaryIsDirectory,
            AppCommandId.OpenInCompactMate => context.SelectionCount == 1 && context.PrimaryIsArchive,
            AppCommandId.OpenInTerminal => TerminalTarget(context) is not null,
            AppCommandId.Cut or AppCommandId.Copy => context.SelectionCount > 0,
            AppCommandId.Share => context.ShareAvailable && context.SelectionCount > 0,
            AppCommandId.Paste => false,
            AppCommandId.Rename => context.SelectionCount == 1,
            AppCommandId.BatchRename => context.BatchRenameAvailable && context.SelectionCount > 1,
            AppCommandId.Recycle => context.SelectionCount > 0,
            AppCommandId.CopyPath => context.SelectionCount > 0,
            AppCommandId.CreateShortcut => context.SelectionCount == 1,
            AppCommandId.Compress => context.SelectionCount > 0,
            AppCommandId.Extract => context.SelectionIsArchive && context.SelectionCount > 0,
            AppCommandId.Properties => context.SelectionCount > 0,
            AppCommandId.WhoLocks => context.SelectionCount > 0,
            AppCommandId.AddTags => context.TagsAvailable && context.SelectionCount > 0,
            AppCommandId.ManageTags => false,
            AppCommandId.PinToSidebar => context.SelectionCount == 1
                && context.PrimaryIsDirectory
                && !string.IsNullOrWhiteSpace(context.PrimaryPath)
                && !context.PrimaryIsPinned,
            AppCommandId.UnpinFromSidebar => context.SelectionCount == 1
                && context.PrimaryIsDirectory
                && !string.IsNullOrWhiteSpace(context.PrimaryPath)
                && context.PrimaryIsPinned,
            AppCommandId.Refresh or AppCommandId.SelectAll => false,
            _ => false,
        },
        _ => false,
    };

    private static bool IsImplemented(AppCommandId id) => true;

    private static (string Label, string Glyph, string? Shortcut, CommandGroup Group) Describe(AppCommandId id) =>
        id switch
        {
            AppCommandId.AddToFavorites => (StringTable.Get("Favorites_Add"), "\uE734", null, CommandGroup.Organize),
            AppCommandId.AddToShelf => (StringTable.Get("Shelf_Add"), "\uE710", null, CommandGroup.Organize),
            AppCommandId.ShowShelf => (StringTable.Get("Shelf_Title"), "\uE7B8", null, CommandGroup.Organize),
            AppCommandId.ChooseFolderCover => (StringTable.Get("Cover_Choose"), "\uEB9F", null, CommandGroup.View),
            AppCommandId.ResetFolderCover => (StringTable.Get("Cover_Reset"), "\uE777", null, CommandGroup.View),
            AppCommandId.ResetFolderView => (StringTable.Get("FolderView_Reset"), "\uE777", null, CommandGroup.View),
            AppCommandId.NewFolder => (StringTable.Get("Command_NewFolder"), "\uE8F4", "Ctrl+Shift+N", CommandGroup.Create),
            AppCommandId.NewFile => (StringTable.Get("Command_NewFile"), "\uE7C3", null, CommandGroup.Create),
            AppCommandId.Cut => (StringTable.Get("Command_Cut"), "\uE8C6", "Ctrl+X", CommandGroup.Clipboard),
            AppCommandId.Copy => (StringTable.Get("Command_Copy"), "\uE8C8", "Ctrl+C", CommandGroup.Clipboard),
            AppCommandId.Paste => (StringTable.Get("Command_Paste"), "\uE77F", "Ctrl+V", CommandGroup.Clipboard),
            AppCommandId.Rename => (StringTable.Get("Command_Rename"), "\uE8AC", "F2", CommandGroup.Organize),
            AppCommandId.BatchRename => (StringTable.Get("Command_BatchRename"), "\uE8AC", "F2", CommandGroup.Organize),
            AppCommandId.Share => (StringTable.Get("Command_Share"), "\uE72D", null, CommandGroup.Organize),
            AppCommandId.Recycle => (StringTable.Get("Command_Recycle"), "\uE74D", "Delete", CommandGroup.Organize),
            AppCommandId.PermanentDelete => (StringTable.Get("Command_PermanentDelete"), "\uE74D", "Shift+Delete", CommandGroup.Organize),
            AppCommandId.Open => (StringTable.Get("Command_Open"), "\uE8E5", "Enter", CommandGroup.Open),
            AppCommandId.OpenInNewTab => (StringTable.Get("Command_OpenInNewTab"), "\uE8A7", "Ctrl+Enter", CommandGroup.Open),
            AppCommandId.OpenInNewWindow => (StringTable.Get("Command_OpenInNewWindow"), "\uE8A7", null, CommandGroup.Open),
            AppCommandId.OpenWith => (StringTable.Get("Command_OpenWith"), "\uE7AC", null, CommandGroup.Open),
            AppCommandId.OpenInCompactMate => (StringTable.Get("Command_OpenInCompactMate"), "\uE8B7", null, CommandGroup.Open),
            AppCommandId.OpenInTerminal => (StringTable.Get("Command_OpenInTerminal"), "\uE756", null, CommandGroup.Folder),
            AppCommandId.Properties => (StringTable.Get("Command_Properties"), "\uE946", "Alt+Enter", CommandGroup.Folder),
            AppCommandId.WhoLocks => (StringTable.Get("Command_WhoLocks"), "\uE72E", null, CommandGroup.Folder),
            AppCommandId.AddTags => (StringTable.Get("Command_AddTags"), "\uE8EC", null, CommandGroup.Organize),
            AppCommandId.ManageTags => (StringTable.Get("Command_ManageTags"), "\uE90F", null, CommandGroup.Organize),
            AppCommandId.PinToSidebar => (StringTable.Get("Command_PinToSidebar"), "\uE718", null, CommandGroup.Organize),
            AppCommandId.UnpinFromSidebar => (StringTable.Get("Command_UnpinFromSidebar"), "\uE77A", null, CommandGroup.Organize),
            AppCommandId.CopyPath => (StringTable.Get("Command_CopyPath"), "\uE71B", "Ctrl+Shift+C", CommandGroup.Folder),
            AppCommandId.NewFolderWithSelection => (StringTable.Get("GroupSelection"), "\uE8F4", null, CommandGroup.Organize),
            AppCommandId.SelectSameType => (StringTable.Get("SelectSameType"), "\uE8B3", null, CommandGroup.View),
            AppCommandId.InvertSelection => (StringTable.Get("InvertSelection"), "\uE8B3", null, CommandGroup.View),
            AppCommandId.CopyToOtherPane => (StringTable.Get("CopyToOtherPane"), "\uE8C8", null, CommandGroup.Clipboard),
            AppCommandId.MoveToOtherPane => (StringTable.Get("MoveToOtherPane"), "\uE8DE", null, CommandGroup.Clipboard),
            AppCommandId.SearchCommands => (StringTable.Get("CommandPalette"), "\uE721", "Ctrl+Shift+P", CommandGroup.View),
            AppCommandId.CopyPathQuoted => (StringTable.Get("Command_CopyPathQuoted"), "\uE8C8", null, CommandGroup.Folder),
            AppCommandId.CreateShortcut => (StringTable.Get("Command_CreateShortcut"), "\uE71B", null, CommandGroup.Folder),
            AppCommandId.Compress => (StringTable.Get("Command_Compress"), "\uE8DE", null, CommandGroup.Archive),
            AppCommandId.CompressZip => (StringTable.Get("Command_CompressZip"), "\uE8DE", null, CommandGroup.Archive),
            AppCommandId.Compress7z => (StringTable.Get("Command_Compress7z"), "\uE8DE", null, CommandGroup.Archive),
            AppCommandId.CompressNew => (StringTable.Get("Command_CompressNew"), "\uE8DE", null, CommandGroup.Archive),
            AppCommandId.Extract => (StringTable.Get("Command_Extract"), "\uE8E5", null, CommandGroup.Archive),
            AppCommandId.ExtractHere => (StringTable.Get("Command_ExtractHere"), "\uE8E5", null, CommandGroup.Archive),
            AppCommandId.ExtractToFolder => (StringTable.Get("Command_ExtractToFolder"), "\uE8B7", null, CommandGroup.Archive),
            AppCommandId.ExtractToOther => (StringTable.Get("Command_ExtractToOther"), "\uE8B7", null, CommandGroup.Archive),
            AppCommandId.SmartExtract => (StringTable.Get("Command_SmartExtract"), "\uE8E5", null, CommandGroup.Archive),
            AppCommandId.SelectAll => (StringTable.Get("Command_SelectAll"), "\uE8B3", "Ctrl+A", CommandGroup.Folder),
            AppCommandId.Refresh => (StringTable.Get("Command_Refresh"), "\uE72C", "F5", CommandGroup.Folder),
            AppCommandId.Sort => (StringTable.Get("Sort"), "\uE8CB", null, CommandGroup.View),
            AppCommandId.ChangeLayout => (StringTable.Get("Command_ChangeLayout"), "\uF0E2", null, CommandGroup.View),
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };

    private static string TooltipFor(
        AppCommandId id,
        CommandContext context,
        string label,
        string? shortcut)
    {
        if (id == AppCommandId.Rename && context.SelectionCount > 1)
        {
            return StringTable.Get("CannotRenameMultiple");
        }

        return shortcut is null ? label : $"{label} ({shortcut})";
    }

    private static bool HasFolderPath(CommandContext context) =>
        !string.IsNullOrWhiteSpace(context.FolderPath)
        && !HomeLocation.IsHome(context.FolderPath)
        && !TagLocation.TryParse(context.FolderPath, out _);

    private static bool HasItemTarget(CommandContext context) =>
        context.SelectionCount > 0 || (context.IsBackground && HasFolderPath(context));

    private static bool HasSingleTarget(CommandContext context) =>
        context.SelectionCount == 1
        || (context.IsBackground && context.SelectionCount == 0 && HasFolderPath(context));

    public static string? TerminalTarget(CommandContext context)
    {
        if (!context.IsBackground && context.SelectionCount == 1
            && context.PrimaryIsDirectory
            && !string.IsNullOrWhiteSpace(context.PrimaryPath))
        {
            return context.PrimaryPath;
        }

        return HasFolderPath(context) ? context.FolderPath : null;
    }
}
