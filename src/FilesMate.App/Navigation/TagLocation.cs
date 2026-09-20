using System.Globalization;

namespace FilesMate.App.Navigation;

public static class TagLocation
{
    public const string Prefix = "filesmate:tag:";

    public static string Uri(long id) => Prefix + id.ToString(CultureInfo.InvariantCulture);

    public static bool IsTag(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? path, out long id)
    {
        id = 0;
        if (!IsTag(path))
        {
            return false;
        }

        return long.TryParse(path.AsSpan(Prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
            && id > 0;
    }
}
