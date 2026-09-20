namespace FilesMate.App.Navigation;

/// <summary>Resolves address-bar input relative to the pane, never the process directory.</summary>
public static class AddressPath
{
    public static string Normalize(string draft, string currentFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draft);
        var text = Environment.ExpandEnvironmentVariables(draft.Trim().Trim('"'));
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            text = uri.LocalPath;
        }

        var paths = new WindowsPathService();
        if (HomeLocation.IsHome(text) || TagLocation.IsTag(text)
            || Path.IsPathRooted(text) || text == "~" || text.StartsWith("~/", StringComparison.Ordinal)
            || text.StartsWith(@"~\", StringComparison.Ordinal))
        {
            return paths.Normalize(text);
        }

        if (HomeLocation.IsHome(currentFolder) || TagLocation.IsTag(currentFolder))
        {
            throw new ArgumentException("Enter an absolute path from this location.", nameof(draft));
        }

        return paths.Normalize(Path.Combine(paths.Normalize(currentFolder), text));
    }
}
