namespace FilesMate.Search;

// Windows target identity ignores case; arguments can contain case-sensitive
// URLs or other values. The existing serialized identity format stays intact.
internal sealed class ApplicationIdentityComparer : IEqualityComparer<string>
{
    public static ApplicationIdentityComparer Instance { get; } = new();
    public bool Equals(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        var separator = left.IndexOf('\n');
        var otherSeparator = right.IndexOf('\n');
        return separator < 0 || otherSeparator < 0
            ? StringComparer.OrdinalIgnoreCase.Equals(left, right)
            : left.AsSpan(0, separator).Equals(right.AsSpan(0, otherSeparator), StringComparison.OrdinalIgnoreCase)
                && left.AsSpan(separator + 1).Equals(right.AsSpan(otherSeparator + 1), StringComparison.Ordinal);
    }
    public int GetHashCode(string identity)
    {
        var separator = identity.IndexOf('\n');
        return separator < 0 ? StringComparer.OrdinalIgnoreCase.GetHashCode(identity)
            : HashCode.Combine(string.GetHashCode(identity.AsSpan(0, separator), StringComparison.OrdinalIgnoreCase),
                string.GetHashCode(identity.AsSpan(separator + 1), StringComparison.Ordinal));
    }
}
