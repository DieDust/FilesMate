namespace FilesMate.App.Navigation;

/// <summary>
/// Small boundary between the app shell and the navigation model. Keeping the
/// provider here lets PaneViewModel stay usable in the platform-neutral tests.
/// </summary>
internal static class ExplorerPreferenceBridge
{
    public static Func<bool> ShowHiddenFiles { get; set; } = static () => false;

    public static Func<bool> ShowFolderSizes { get; set; } = static () => false;

    public static FolderSizeTryGet TryFolderSize { get; set; } = DefaultFolderSize;
    public static Action<string> InvalidateFolderSizes { get; set; } = static _ => { };

    public static event Action? FolderSizesChanged;

    public static void NotifyFolderSizesChanged() => FolderSizesChanged?.Invoke();

    public delegate bool FolderSizeTryGet(string path, out ulong size);

    private static bool DefaultFolderSize(string path, out ulong size)
    {
        _ = path;
        size = 0;
        return false;
    }
}
