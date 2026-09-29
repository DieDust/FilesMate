using FilesMate.App.Localization;
using FilesMate.Core.Operations;

namespace FilesMate.App.Models;

public sealed record OperationHistoryEntry(FileUndoRecord Record, bool Undone)
{
    public string Title => Record.Kind switch
    {
        FileUndoKind.Recycled => StringTable.Format("Files_RecycledCount", Count),
        FileUndoKind.Relocated => StringTable.Format("Files_RenamedMovedCount", Count),
        FileUndoKind.Grouped => StringTable.Format("Files_GroupedCount", Count),
        FileUndoKind.Merged => StringTable.Get("Files_Merged"),
        _ => StringTable.Format("Files_CreatedCopiedCount", Count)
    };
    public string Detail => Record.RecordedAt.ToLocalTime().ToString("HH:mm") + " · "
        + StringTable.Get(Undone ? "History_Undone" : "History_Completed");
    public string FileSummary => string.Join(" · ", Paths.Take(2).Select(path =>
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }));
    private int Count => (Record.Pairs.Count > 0 ? Record.Pairs.Count : Record.Paths.Count + Record.CreatedDirectories.Count) + Record.Replacements.Count;
    public IReadOnlyList<string> Paths => Record.Pairs.Select(pair => Undone ? pair.Source : pair.Destination)
        .Concat(Record.Paths).Concat(Record.CreatedDirectories).Concat(Record.Replacements.Select(r => r.Destination))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static IReadOnlyList<OperationHistoryEntry> Snapshot(FileUndoStack stack) => stack.UndoRecords.Select(r => new OperationHistoryEntry(r, false))
        .Concat(stack.RedoRecords.Select(r => new OperationHistoryEntry(r, true))).OrderByDescending(r => r.Record.RecordedAt).ToArray();
}
