using FilesMate.App.Localization;

namespace FilesMate.App.Services;

public static class TransferFeedback
{
    public static bool NeedsAttention(ShelfTransferResult result) =>
        result.Cancelled || result.Skipped > 0 || result.Errors.Count > 0 || result.WithoutUndo > 0;

    public static string Summary(ShelfTransferResult result)
    {
        var summary = StringTable.Format("Transfer_ResultCounts", result.Completed.Count, result.Skipped, result.Errors.Count);
        if (result.Cancelled) summary = StringTable.Get("Files_OperationCancelled") + " · " + summary;
        if (result.WithoutUndo > 0) summary += " · " + StringTable.Format("Backup_UnprotectedCount", result.WithoutUndo);
        return summary;
    }

    public static string Format(ShelfTransferResult result) => result.Errors.Count == 0 ? Summary(result)
        : Summary(result) + Environment.NewLine + string.Join(Environment.NewLine, result.Errors);
}
