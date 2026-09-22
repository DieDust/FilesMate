namespace FilesMate.Core.Operations;

public static class FileUndoApplier
{
    public static void Undo(ILocalFileOperations operations, FileUndoRecord record)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(record);
        record.ValidateUndo(operations);
        foreach (var replacement in record.Replacements.Reverse()) replacement.Undo();
        switch (record.Kind)
        {
            case FileUndoKind.Merged:
                foreach (var folder in record.Paths.OrderBy(path => path.Length)) operations.CreateDirectory(folder);
                Relocate(operations, Reverse(record.Pairs));
                RemoveEmptyDirectories(record.CreatedDirectories);
                break;
            case FileUndoKind.Copied:
                if (record.Paths.Count > 0) RecycleForHistory(operations, record.Paths);
                RemoveEmptyDirectories(record.CreatedDirectories);
                break;
            case FileUndoKind.Grouped:
                Relocate(operations, Reverse(record.Pairs));
                // Keep anything the user added after grouping. Only remove an empty folder.
                foreach (var folder in record.Paths)
                {
                    // Non-recursive deletion also protects files created between checking and deleting.
                    try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: false); }
                    catch (IOException) when (Directory.Exists(folder)) { }
                }
                break;
            case FileUndoKind.Created:
                RecycleForHistory(operations, record.Paths);
                break;
            case FileUndoKind.Relocated:
                Relocate(operations, Reverse(record.Pairs));
                break;
            case FileUndoKind.Recycled:
                operations.RestoreRecycled(record.Paths);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(record));
        }
        record.CaptureRedo();
    }

    public static void Redo(ILocalFileOperations operations, FileUndoRecord record)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(record);
        record.ValidateRedo(operations);
        switch (record.Kind)
        {
            case FileUndoKind.Merged:
                foreach (var folder in record.CreatedDirectories.OrderBy(path => path.Length)) operations.CreateDirectory(folder);
                Relocate(operations, record.Pairs);
                RemoveEmptyDirectories(record.Paths);
                break;
            case FileUndoKind.Copied:
                foreach (var folder in record.CreatedDirectories.OrderBy(path => path.Length)) operations.CreateDirectory(folder);
                if (record.Paths.Count > 0) operations.RestoreRecycled(record.Paths);
                break;
            case FileUndoKind.Grouped:
                foreach (var folder in record.Paths) operations.CreateDirectory(folder);
                Relocate(operations, record.Pairs);
                break;
            case FileUndoKind.Created:
                operations.RestoreRecycled(record.Paths);
                break;
            case FileUndoKind.Relocated:
                Relocate(operations, record.Pairs);
                break;
            case FileUndoKind.Recycled:
                RecycleForHistory(operations, record.Paths);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(record));
        }
        foreach (var replacement in record.Replacements) replacement.Redo();
        if (record.Kind == FileUndoKind.Merged) RemoveEmptyDirectories(record.Paths);
        record.CaptureUndo();
    }

    private static void RecycleForHistory(ILocalFileOperations operations, IReadOnlyList<string> paths)
    {
        var permanent = 0;
        try { operations.Recycle(paths, item => { if (!item.IsRecycled) permanent++; }); }
        finally
        {
            // Windows may ask to permanently delete even during undo/redo.
            if (permanent > 0) throw new IrreversibleDeletionException(permanent);
        }
    }

    private static void RemoveEmptyDirectories(IReadOnlyList<string> directories)
    {
        foreach (var folder in directories.OrderByDescending(path => path.Length))
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: false); }
            catch (IOException) when (Directory.Exists(folder)) { }
        }
    }

    private static IReadOnlyList<FilePathPair> Reverse(IReadOnlyList<FilePathPair> pairs)
    {
        var reversed = new FilePathPair[pairs.Count];
        for (var i = 0; i < pairs.Count; i++)
        {
            reversed[i] = pairs[i].Reverse();
        }

        return reversed;
    }

    private static void Relocate(ILocalFileOperations operations, IReadOnlyList<FilePathPair> pairs)
    {
        var needed = new List<FilePathPair>(pairs.Count);
        foreach (var pair in pairs)
        {
            if (!string.Equals(pair.Source, pair.Destination, StringComparison.Ordinal))
            {
                needed.Add(pair);
            }
        }

        if (needed.Count == 0)
        {
            return;
        }

        if (needed.Count == 1 && !string.Equals(needed[0].Source, needed[0].Destination, StringComparison.OrdinalIgnoreCase))
        {
            EnsureParent(operations, needed[0].Destination);
            operations.Rename(needed[0].Source, needed[0].Destination);
            return;
        }

        var movedToTemp = new List<(string Source, string Temp, string Target)>(needed.Count);
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (var i = 0; i < needed.Count; i++)
            {
                var pair = needed[i];
                var temp = Path.Combine(Path.GetDirectoryName(pair.Source)!, ".filesmate-undo-" + Guid.NewGuid().ToString("N"));
                operations.Rename(pair.Source, temp);
                movedToTemp.Add((pair.Source, temp, pair.Destination));
            }

            foreach (var item in movedToTemp)
            {
                EnsureParent(operations, item.Target);
                operations.Rename(item.Temp, item.Target);
                applied.Add(item.Temp);
            }
        }
        catch (Exception error)
        {
            var recoveryErrors = new List<string>();
            // Vacate all published targets first. In a cycle, a target is another
            // item's original name, so restoring originals in one pass is unsafe.
            foreach (var item in movedToTemp.AsEnumerable().Reverse())
            {
                if (!applied.Contains(item.Temp)) continue;
                try
                {
                    operations.Rename(item.Target, item.Temp);
                    applied.Remove(item.Temp);
                }
                catch (Exception recovery)
                {
                    recoveryErrors.Add($"{item.Target}: {recovery.Message}");
                }
            }
            foreach (var item in movedToTemp.AsEnumerable().Reverse())
            {
                if (applied.Contains(item.Temp)) continue;
                try
                {
                    operations.Rename(item.Temp, item.Source);
                }
                catch (Exception recovery)
                {
                    recoveryErrors.Add($"{item.Temp} → {item.Source}: {recovery.Message}");
                }
            }
            if (recoveryErrors.Count > 0)
                throw new IOException(error.Message + Environment.NewLine + string.Join(Environment.NewLine, recoveryErrors), error);
            throw;
        }
    }

    private static void EnsureParent(ILocalFileOperations operations, string path)
    {
        var parent = Path.GetDirectoryName(path.TrimEnd('\\', '/'));
        if (!string.IsNullOrEmpty(parent))
        {
            operations.CreateDirectory(parent);
        }
    }
}
