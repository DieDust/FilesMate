namespace FilesMate.TestDataGenerator;

internal static class PathGuard
{
    public static string ValidateNewRoot(string root) => Validate(root, mustExist: false);

    public static string ValidateExistingRoot(string root) => Validate(root, mustExist: true);

    private static string Validate(string root, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Root path is empty.", nameof(root));
        }

        if (root.Contains('\0'))
        {
            throw new ArgumentException("Root path contains a null character.", nameof(root));
        }

        if (!Path.IsPathRooted(root))
        {
            throw new ArgumentException($"Root must be an absolute path: '{root}'.", nameof(root));
        }

        var full = Path.GetFullPath(root);
        var driveRoot = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(driveRoot) && PathsEqual(full, driveRoot))
        {
            throw new ArgumentException($"Refusing to use drive root '{full}'.", nameof(root));
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile) && PathsEqual(full, userProfile))
        {
            throw new ArgumentException($"Refusing to use the user profile root '{full}'.", nameof(root));
        }

        var workspace = TryFindWorkspaceRoot();
        if (workspace is not null)
        {
            if (PathsEqual(full, workspace))
            {
                throw new ArgumentException($"Refusing to use the FilesMate workspace root '{full}'.", nameof(root));
            }

            if (IsStrictAncestorOf(full, workspace))
            {
                throw new ArgumentException(
                    $"Refusing to use '{full}' because it contains the FilesMate workspace.",
                    nameof(root));
            }
        }

        if (mustExist && !Directory.Exists(DatasetGenerator.ToExtendedPath(full)))
        {
            throw new DirectoryNotFoundException($"Dataset root '{full}' does not exist.");
        }

        return full;
    }

    internal static string? TryFindWorkspaceRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "FilesMate.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static bool PathsEqual(string left, string right)
    {
        var a = TrimSlash(Path.GetFullPath(left));
        var b = TrimSlash(Path.GetFullPath(right));
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStrictAncestorOf(string ancestor, string descendant)
    {
        var prefix = TrimSlash(Path.GetFullPath(ancestor)) + Path.DirectorySeparatorChar;
        var child = TrimSlash(Path.GetFullPath(descendant)) + Path.DirectorySeparatorChar;
        return child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               !PathsEqual(ancestor, descendant);
    }

    private static string TrimSlash(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
