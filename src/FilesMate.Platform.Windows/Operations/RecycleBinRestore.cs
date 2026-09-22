using System.Text;
using FilesMate.Core.Operations;

namespace FilesMate.Platform.Windows.Operations;

public static class RecycleBinRestore
{
    public static void RestoreItems(IReadOnlyList<RecycleItemResult> items, Action<string>? completed = null)
    {
        // Validate the whole batch before publishing any entry. Do not substitute a newer
        // recycle item if another program has since deleted a file with the same original name.
        foreach (var item in items)
        {
            var receipt = item.Receipt ?? throw new UndoStateChangedException();
            if (!item.IsRecycled || !string.Equals(Canonical(item.OriginalPath), Canonical(receipt.OriginalPath), StringComparison.OrdinalIgnoreCase)
                || Path.Exists(item.OriginalPath)
                || !TryReadOriginalPath(receipt.InfoPath, out var original, out _)
                || !string.Equals(original, Canonical(item.OriginalPath), StringComparison.OrdinalIgnoreCase))
                throw new UndoStateChangedException();
            receipt.Validate();
        }
        foreach (var item in items)
        {
            var receipt = item.Receipt!;
            receipt.Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(item.OriginalPath)!);
            if (Directory.Exists(receipt.DataPath)) Directory.Move(receipt.DataPath, item.OriginalPath);
            else File.Move(receipt.DataPath, item.OriginalPath, overwrite: false);
            // Report immediately after the file moves, including a later metadata failure.
            completed?.Invoke(item.OriginalPath);
            File.SetAttributes(item.OriginalPath, receipt.OriginalAttributes);
            try { File.Delete(receipt.InfoPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.TraceWarning("Restored recycle metadata retained at {0}: {1}", receipt.InfoPath, error.Message); }
        }
    }

    public static bool TryReadOriginalPath(string infoFile, out string originalPath, out long deletedUtcTicks)
    {
        originalPath = "";
        deletedUtcTicks = 0;
        try
        {
            using var stream = new FileStream(infoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // Both known formats fit a Windows path. Never allocate from an
            // unchecked file length or silently resolve a relative restore path.
            if (stream.Length < 28 || stream.Length > 28 + 32768 * 2)
            {
                return false;
            }

            using var reader = new BinaryReader(stream);
            var version = reader.ReadInt64();
            reader.ReadInt64();
            var deleted = reader.ReadInt64();
            string? path;
            if (version == 1)
            {
                if (stream.Length != 544) return false;
                var bytes = reader.ReadBytes(520);
                path = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            }
            else if (version == 2)
            {
                var count = reader.ReadInt32();
                if (count < 2 || count > 32768 || stream.Length - stream.Position != count * 2L) return false;
                var value = Encoding.Unicode.GetString(reader.ReadBytes(count * 2));
                if (value[^1] != '\0') return false;
                path = value[..^1];
            }
            else return false;

            if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || !Path.IsPathFullyQualified(path))
            {
                return false;
            }

            originalPath = Canonical(path);
            deletedUtcTicks = deleted;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string Canonical(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
