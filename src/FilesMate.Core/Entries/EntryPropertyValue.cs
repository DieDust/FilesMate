namespace FilesMate.Core.Entries;

/// <summary>A detached value; comparers never invoke shell property handlers.</summary>
public sealed record EntryPropertyValue(string Text, decimal? Number = null, long? Timestamp = null)
{
    public static int Compare(EntryPropertyValue? left, EntryPropertyValue? right, IComparer<string> names)
    {
        if (left is null || right is null) return left is null ? right is null ? 0 : 1 : -1;
        if (left.Number is { } a && right.Number is { } b) return a.CompareTo(b);
        if (left.Timestamp is { } x && right.Timestamp is { } y) return x.CompareTo(y);
        return names.Compare(left.Text, right.Text);
    }
}
