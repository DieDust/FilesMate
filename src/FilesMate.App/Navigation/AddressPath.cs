namespace FilesMate.App.Navigation;

/// <summary>Resolves Windows address input lexically, relative to the pane, never the process directory.</summary>
public static class AddressPath
{
    public static string Normalize(string draft, string currentFolder) =>
        Normalize(draft, currentFolder, Environment.ExpandEnvironmentVariables,
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    // Keep Windows path rules independent of the host running the pure tests.
    // These callbacks expand text only; normalization never probes the filesystem.
    internal static string Normalize(string draft, string currentFolder, Func<string, string> expandEnvironment, Func<string> userProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draft);
        RejectControls(draft);
        var text = draft.Trim();
        if (text.StartsWith('"') && text.EndsWith('"') && text.Length >= 2) text = text[1..^1].Trim();
        if (text.Contains('"')) throw new ArgumentException("Unpaired quotes are not a path.");
        text = expandEnvironment(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        RejectControls(text);
        if (text.Contains('"')) throw new ArgumentException("Enter one path on one line.");
        // Uri can promote C:relative to a drive-rooted file URI. Reject that
        // ambiguous syntax before URI parsing; navigation must never use cwd.
        RejectDriveRelative(text);
        if (FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(text, out var device)) return device.Uri;
        var paths = new WindowsPathService();
        if (HomeLocation.IsHome(text) || TagLocation.IsTag(text)) return paths.Normalize(text);

        // Parse only explicit URIs. A literal Windows filename may contain # or %,
        // and must not be interpreted as an escaped URI or a URI fragment.
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !uri.IsFile
                || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 || !uri.IsDefaultPort)
                throw new ArgumentException("Enter a file address without a query or fragment.");
            var path = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped).Replace('/', '\\');
            var unc = uri.IsUnc && !(string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                && IsDrivePrefix(path.TrimStart('\\')));
            text = unc ? @"\\" + uri.Host + "\\" + path.TrimStart('\\') : path;
            if (!unc && text.Length >= 3 && text[0] == '\\' && IsDrivePrefix(text.AsSpan(1))) text = text[1..];
            if (!IsFullyQualified(text)) throw new ArgumentException("A file address must contain an absolute Windows path.");
        }
        else if (!IsFullyQualified(text) && Uri.TryCreate(text, UriKind.Absolute, out _))
            throw new ArgumentException("Enter a folder or file path.");

        if (text == "~" || text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith(@"~\", StringComparison.Ordinal))
        {
            var home = userProfile();
            if (!IsFullyQualified(home)) throw new ArgumentException("The user folder is unavailable.");
            text = text.Length <= 2 ? home : home.TrimEnd('\\', '/') + "\\" + text[2..];
        }
        text = text.Replace('/', '\\');
        RejectControls(text);
        RejectDriveRelative(text);
        var ordinaryText = text.StartsWith(@"\\?\", StringComparison.Ordinal) ? text[4..] : text;
        if (ordinaryText.IndexOfAny(['<', '>', '|', '*', '?', '"']) >= 0
            || ordinaryText.AsSpan(IsDrivePrefix(ordinaryText) ? 2 : 0).Contains(':')
            || text.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new ArgumentException("This path contains an invalid character.");
        // Newlines/quoted lists are rejected above. Also reject an unquoted UNC
        // list rather than silently interpreting its second path as child names.
        for (var i = 1; i + 1 < text.Length; i++)
            if (char.IsWhiteSpace(text[i - 1]) && text[i] == '\\' && text[i + 1] == '\\')
                throw new ArgumentException("Enter one path on one line.");
        if (IsFullyQualified(text)) return paths.Normalize(text);
        if (text.StartsWith('\\')) throw new ArgumentException("Enter an absolute path including its drive or share.");
        if (HomeLocation.IsHome(currentFolder) || TagLocation.IsTag(currentFolder)
            || FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(currentFolder, out _)
            || !IsFullyQualified(currentFolder))
            throw new ArgumentException("Enter an absolute path from this location.", nameof(draft));
        return paths.Normalize(paths.Normalize(currentFolder).TrimEnd('\\') + "\\" + text);
    }

    public static bool TryNormalizeForPaste(string? text, string currentFolder, out string normalized) =>
        TryNormalizeForPaste(text, currentFolder, Environment.ExpandEnvironmentVariables,
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), out normalized);

    internal static bool TryNormalizeForPaste(string? text, string currentFolder, Func<string, string> expandEnvironment,
        Func<string> userProfile, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var path = Normalize(text, currentFolder, expandEnvironment, userProfile);
            if (!IsFullyQualified(path)) return false;
            normalized = path;
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static bool IsDrivePrefix(ReadOnlySpan<char> text) =>
        text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':';

    private static bool IsFullyQualified(string text) =>
        text.Length >= 3 && IsDrivePrefix(text) && text[2] is '\\' or '/'
        || text.Length >= 2 && text[0] is '\\' or '/' && text[1] is '\\' or '/';

    private static void RejectDriveRelative(string text)
    {
        var ordinary = text.StartsWith(@"\\?\", StringComparison.Ordinal) ? text.AsSpan(4) : text.AsSpan();
        if (IsDrivePrefix(ordinary) && (ordinary.Length == 2 || ordinary[2] is not ('\\' or '/')))
            throw new ArgumentException("Drive paths must include their root separator.");
    }

    private static void RejectControls(string text)
    {
        foreach (var character in text)
            if (char.IsControl(character)) throw new ArgumentException("Enter one path on one line.");
    }
}
