namespace FilesMate.Core.Operations;

public sealed record BatchRenameEntry(
    string Source,
    string Target,
    string? Error = null)
{
    public bool RequiresRename => !string.Equals(Source, Target, StringComparison.Ordinal);
}

public sealed record BatchRenamePlan(IReadOnlyList<BatchRenameEntry> Entries)
{
    public bool IsValid => Entries.Count > 0 && Entries.All(entry => entry.Error is null);

    public IReadOnlyList<string> Errors => Entries
        .Where(entry => entry.Error is not null)
        .Select(entry => entry.Error!)
        .ToArray();
}
