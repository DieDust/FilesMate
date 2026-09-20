namespace FilesMate.App.Navigation;

public static class HomeLocation
{
    public const string Uri = "filesmate:home";

    public static bool IsHome(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(path.Trim(), Uri, StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? text, out string uri)
    {
        uri = Uri;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim().Trim('"');
        if (IsHome(trimmed))
        {
            return true;
        }

        return string.Equals(trimmed, Localization.StringTable.Get("Home"), StringComparison.OrdinalIgnoreCase);
    }
}
