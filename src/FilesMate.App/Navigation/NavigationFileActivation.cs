namespace FilesMate.App.Navigation;

/// <summary>Only an explicitly file-capable navigation may raise file activation.</summary>
internal static class NavigationFileActivation
{
    internal static bool TryOpen(string path, bool allowFileActivation, Func<string, bool> fileExists,
        Func<string, bool> directoryExists, Func<string, string?> getParent, Action<string> openFile,
        out string parent)
    {
        parent = path;
        // This is an operation policy, not an existence check: even if a probed
        // directory becomes an executable before navigation, it cannot activate.
        if (!allowFileActivation || HomeLocation.IsHome(path) || TagLocation.IsTag(path)
            || !fileExists(path) || directoryExists(path)) return false;
        openFile(path);
        var folder = getParent(path);
        if (string.IsNullOrEmpty(folder)) return false;
        parent = folder;
        return true;
    }
}
