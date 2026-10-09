namespace FilesMate.App.Models;

/// <summary>Opening an editor must retain the stored color, including custom colors.</summary>
public static class TagColorChoices
{
    public static IReadOnlyList<string> IncludeCurrent(IReadOnlyList<string> palette, string current)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentException.ThrowIfNullOrWhiteSpace(current);
        return new[] { current }.Concat(palette.Where(color =>
            !string.Equals(color, current, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}
