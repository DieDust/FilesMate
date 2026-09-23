namespace FilesMate.App.Commands;

public readonly record struct CommandContext(
    CommandSurface Surface,
    int SelectionCount,
    bool PrimaryIsDirectory,
    bool IsBackground,
    bool CanRefresh,
    bool IsFolderWritable = true,
    bool ClipboardHasFiles = false,
    string? PrimaryPath = null,
    bool PrimaryIsPinned = false,
    bool TagsAvailable = false,
    bool BatchRenameAvailable = false,
    bool ShareAvailable = false,
    string? FolderPath = null,
    bool PrimaryIsArchive = false,
    bool SelectionIsArchive = false,
    string? OtherPanePath = null,
    bool IsPortableDevice = false)
{
    public static CommandContext ForMenu(
        int selectionCount,
        bool primaryIsDirectory = false,
        bool isBackground = false,
        bool canRefresh = true,
        bool isFolderWritable = true,
        bool clipboardHasFiles = false,
        string? primaryPath = null,
        bool primaryIsPinned = false) =>
        new(
            CommandSurface.Menu,
            selectionCount,
            primaryIsDirectory,
            isBackground,
            canRefresh,
            isFolderWritable,
            clipboardHasFiles,
            primaryPath,
            primaryIsPinned,
            TagsAvailable: false,
            BatchRenameAvailable: false,
            ShareAvailable: false,
            FolderPath: null,
            PrimaryIsArchive: false,
            SelectionIsArchive: false);

    public static CommandContext ForToolbar(
        int selectionCount,
        bool primaryIsDirectory = false,
        bool isFolderWritable = true,
        bool clipboardHasFiles = false,
        bool canRefresh = true,
        string? primaryPath = null,
        bool primaryIsPinned = false,
        bool shareAvailable = false) =>
        new(
            CommandSurface.Toolbar,
            selectionCount,
            primaryIsDirectory,
            IsBackground: false,
            canRefresh,
            isFolderWritable,
            clipboardHasFiles,
            primaryPath,
            primaryIsPinned,
            TagsAvailable: false,
            BatchRenameAvailable: false,
            ShareAvailable: shareAvailable);

    public static CommandContext None { get; } = ForMenu(0);

    public static CommandContext Blank { get; } = ForMenu(0, isBackground: true);

    public static CommandContext SingleFile { get; } = ForMenu(1);

    public static CommandContext SingleDirectory { get; } = ForMenu(1, primaryIsDirectory: true);

    public static CommandContext Multi { get; } = ForMenu(3);
}
