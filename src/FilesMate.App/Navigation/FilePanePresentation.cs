using System.Text.RegularExpressions;

using FilesMate.App.Localization;

namespace FilesMate.App.Navigation;

public enum FilePaneKind
{
    Content,
    LoadingDelayed,
    Empty,
    AccessDenied,
    Offline,
    NotFound,
    UnknownError
}

public readonly record struct FilePaneSnapshot(
    long Generation,
    bool IsLoading,
    int ItemCount,
    string? ErrorText,
    string? FilterQuery,
    bool CanGoUp,
    TimeSpan Elapsed);

public sealed class FilePanePresentation
{
    public const int LoadingDelayMilliseconds = 150;

    public FilePaneKind Kind { get; private set; } = FilePaneKind.Content;

    public long Generation { get; private set; }

    public bool ShowLoadingIndicator { get; private set; }

    public bool ShowList { get; private set; } = true;

    public bool ShowInfo { get; private set; }

    public string? Title { get; private set; }

    public string? Body { get; private set; }

    public string? PrimaryAction { get; private set; }

    public string? SecondaryAction { get; private set; }

    public string? Glyph { get; private set; }

    public void Apply(FilePaneSnapshot snapshot)
    {
        Generation = snapshot.Generation;
        Title = null;
        Body = null;
        PrimaryAction = null;
        SecondaryAction = null;
        Glyph = null;

        if (!string.IsNullOrWhiteSpace(snapshot.ErrorText))
        {
            Kind = Classify(snapshot.ErrorText);
            ShowLoadingIndicator = false;
            ShowList = false;
            ShowInfo = true;
            ApplyError(Kind, Sanitize(snapshot.ErrorText), snapshot.CanGoUp);
            return;
        }

        if (snapshot.IsLoading && snapshot.ItemCount == 0)
        {
            if (snapshot.Elapsed.TotalMilliseconds < LoadingDelayMilliseconds)
            {
                Kind = FilePaneKind.Content;
                ShowLoadingIndicator = false;
                ShowList = true;
                ShowInfo = false;
                return;
            }

            Kind = FilePaneKind.LoadingDelayed;
            ShowLoadingIndicator = true;
            ShowList = true;
            ShowInfo = false;
            return;
        }

        if (snapshot.ItemCount == 0)
        {
            Kind = FilePaneKind.Empty;
            ShowLoadingIndicator = false;
            ShowList = false;
            ShowInfo = true;
            var filtered = !string.IsNullOrWhiteSpace(snapshot.FilterQuery);
            Title = filtered ? StringTable.Get("Empty_FilteredTitle") : StringTable.Get("Empty_Title");
            Body = filtered ? StringTable.Get("Empty_FilteredBody") : StringTable.Get("Empty_Body");
            Glyph = "\uE8B7";
            return;
        }

        Kind = FilePaneKind.Content;
        ShowLoadingIndicator = snapshot.IsLoading && snapshot.Elapsed.TotalMilliseconds >= LoadingDelayMilliseconds;
        ShowList = true;
        ShowInfo = false;
    }

    public static FilePaneKind Classify(string error)
    {
        var text = error.ToLowerInvariant();
        if (text.Contains("denied", StringComparison.Ordinal)
            || text.Contains("unauthorized", StringComparison.Ordinal)
            || text.Contains("access", StringComparison.Ordinal))
        {
            return FilePaneKind.AccessDenied;
        }

        if (text.Contains("offline", StringComparison.Ordinal)
            || text.Contains("network", StringComparison.Ordinal))
        {
            return FilePaneKind.Offline;
        }

        if (text.Contains("not found", StringComparison.Ordinal)
            || text.Contains("missing", StringComparison.Ordinal)
            || text.Contains("cannot find", StringComparison.Ordinal))
        {
            return FilePaneKind.NotFound;
        }

        return FilePaneKind.UnknownError;
    }

    public static string Sanitize(string message)
    {
        var trimmed = Hresult.Replace(message, string.Empty);
        trimmed = HexCode.Replace(trimmed, string.Empty);
        return string.Join(' ', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private void ApplyError(FilePaneKind kind, string detail, bool canGoUp)
    {
        switch (kind)
        {
            case FilePaneKind.AccessDenied:
                Title = StringTable.Get("AccessDenied_Title");
                Body = StringTable.Get("AccessDenied_Body");
                PrimaryAction = "Retry";
                SecondaryAction = canGoUp ? "Go up" : null;
                Glyph = "\uE72E";
                break;
            case FilePaneKind.Offline:
                Title = StringTable.Get("Offline_Title");
                Body = StringTable.Get("Offline_Body");
                PrimaryAction = "Retry";
                Glyph = "\uE704";
                break;
            case FilePaneKind.NotFound:
                Title = StringTable.Get("NotFound_Title");
                Body = StringTable.Get("NotFound_Body");
                PrimaryAction = canGoUp ? "Go up" : "Retry";
                Glyph = "\uE7BA";
                break;
            default:
                Title = StringTable.Get("OpenFailed_Title");
                Body = string.IsNullOrWhiteSpace(detail) ? StringTable.Get("OpenFailed_Body") : detail;
                PrimaryAction = "Retry";
                SecondaryAction = canGoUp ? "Go up" : null;
                Glyph = "\uEA39";
                break;
        }
    }

    private static readonly Regex Hresult = new(@"HRESULT[:\s]*-?\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HexCode = new(@"0x[0-9A-Fa-f]+", RegexOptions.Compiled);
}
