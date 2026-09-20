namespace FilesMate.App.Commands;

public enum AppCommandId
{
    NewFolder,
    NewFile,
    Cut,
    Copy,
    Paste,
    Rename,
    BatchRename,
    Share,
    Recycle,
    PermanentDelete,
    CopyPath,
    Open,
    OpenInNewTab,
    OpenInNewWindow,
    OpenWith,
    OpenInTerminal,
    OpenInCompactMate,
    Properties,
    WhoLocks,
    AddTags,
    ManageTags,
    PinToSidebar,
    UnpinFromSidebar,
    SelectAll,
    Refresh,
    Sort,
    ChangeLayout,
    CreateShortcut,
    CopyPathQuoted,
    Compress,
    CompressZip,
    Compress7z,
    CompressNew,
    Extract,
    ExtractHere,
    ExtractToFolder,
    ExtractToOther,
    SmartExtract,
    AddToShelf,
    ShowShelf,
    ChooseFolderCover,
    ResetFolderCover,
    ResetFolderView,
    AddToFavorites,
    NewFolderWithSelection,
    SelectSameType,
    InvertSelection,
    CopyToOtherPane,
    MoveToOtherPane,
    SearchCommands,
}

public enum CommandSurface
{
    Toolbar,
    Menu,
    Shortcut,
}

public enum CommandGroup
{
    Create,
    Clipboard,
    Organize,
    Open,
    Path,
    Folder,
    View,
    Archive,
}

public sealed class AppCommand
{
    public required AppCommandId Id { get; init; }

    public required string Label { get; init; }

    public required string Tooltip { get; init; }

    public required string Glyph { get; init; }

    public string? Shortcut { get; init; }

    public required CommandGroup Group { get; init; }

    public required bool Implemented { get; init; }

    public required bool Visible { get; init; }

    public required bool Enabled { get; init; }
}
