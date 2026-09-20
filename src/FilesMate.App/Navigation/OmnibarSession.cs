namespace FilesMate.App.Navigation;

public enum OmnibarMode
{
    PathDisplay,
    PathEdit
}

public readonly record struct OmnibarOverflow(
    bool ShowForward,
    bool ShowUp,
    bool ShowRefresh,
    bool ShowOverflow)
{
    public static OmnibarOverflow ForWidth(double width, bool forceCompact)
    {
        if (forceCompact || width < 360)
        {
            return new(false, false, false, true);
        }

        if (width < 400)
        {
            return new(true, false, true, true);
        }

        return new(true, true, true, false);
    }
}

public sealed class OmnibarSession
{
    public OmnibarMode Mode { get; private set; } = OmnibarMode.PathDisplay;

    public string Path { get; private set; } = string.Empty;

    public string Draft { get; private set; } = string.Empty;

    public string Filter { get; private set; } = string.Empty;

    public string? PathError { get; private set; }

    public void SetPath(string path)
    {
        Path = path ?? string.Empty;
        if (Mode != OmnibarMode.PathEdit)
        {
            Draft = Path;
            PathError = null;
        }
    }

    public void BeginPathEdit()
    {
        Mode = OmnibarMode.PathEdit;
        Draft = Path;
        PathError = null;
    }

    public void BeginSearch()
    {
        PathError = null;
    }

    public void SetDraft(string draft) => Draft = draft ?? string.Empty;

    public void SetFilter(string query) => Filter = query ?? string.Empty;

    public bool Cancel()
    {
        if (Mode == OmnibarMode.PathDisplay)
        {
            return false;
        }

        Mode = OmnibarMode.PathDisplay;
        Draft = Path;
        PathError = null;
        return true;
    }

    public bool SubmitPath(Func<string, string> commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        try
        {
            var next = commit(Draft);
            ArgumentException.ThrowIfNullOrWhiteSpace(next);
            Path = next;
            Draft = next;
            PathError = null;
            Mode = OmnibarMode.PathDisplay;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException or IOException)
        {
            PathError = ex.Message;
            Mode = OmnibarMode.PathEdit;
            return false;
        }
    }

    public void RejectPath(string message)
    {
        PathError = message;
        Mode = OmnibarMode.PathEdit;
    }
}
