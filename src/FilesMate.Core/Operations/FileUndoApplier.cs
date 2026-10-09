namespace FilesMate.Core.Operations;

public static class FileUndoApplier
{
    public static void Undo(ILocalFileOperations operations, FileUndoRecord record)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(record);
        record.ValidateUndo(operations);
        ApplyReplacements(record, undo: true);
        switch (record.Kind)
        {
            case FileUndoKind.Merged:
                foreach (var folder in record.Paths.OrderBy(path => path.Length)) operations.CreateDirectory(folder);
                Relocate(operations, Reverse(record.Pairs), record.AcceptUndoDirectoryState);
                RemoveEmptyDirectories(record.CreatedDirectories);
                break;
            case FileUndoKind.Copied:
                if (record.Paths.Count > 0) RecycleForHistory(operations, record, undo: true);
                RemoveEmptyDirectories(record.CreatedDirectories);
                break;
            case FileUndoKind.Grouped:
                Relocate(operations, Reverse(record.Pairs), record.AcceptUndoDirectoryState);
                // Keep anything the user added after grouping. Only remove an empty folder.
                foreach (var folder in record.Paths)
                {
                    // Non-recursive deletion also protects files created between checking and deleting.
                    try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: false); }
                    catch (IOException) when (Directory.Exists(folder)) { }
                }
                break;
            case FileUndoKind.Created:
                RecycleForHistory(operations, record, undo: true);
                break;
            case FileUndoKind.Relocated:
                Relocate(operations, Reverse(record.Pairs), record.AcceptUndoDirectoryState);
                break;
            case FileUndoKind.Recycled:
                RestoreForHistory(operations, record, undo: true);
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
                Relocate(operations, record.Pairs, record.AcceptRedoDirectoryState);
                RemoveEmptyDirectories(record.Paths);
                break;
            case FileUndoKind.Copied:
                foreach (var folder in record.CreatedDirectories.OrderBy(path => path.Length)) operations.CreateDirectory(folder);
                if (record.Paths.Count > 0) RestoreForHistory(operations, record, undo: false);
                break;
            case FileUndoKind.Grouped:
                foreach (var folder in record.Paths) operations.CreateDirectory(folder);
                Relocate(operations, record.Pairs, record.AcceptRedoDirectoryState);
                break;
            case FileUndoKind.Created:
                RestoreForHistory(operations, record, undo: false);
                break;
            case FileUndoKind.Relocated:
                Relocate(operations, record.Pairs, record.AcceptRedoDirectoryState);
                break;
            case FileUndoKind.Recycled:
                RecycleForHistory(operations, record, undo: false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(record));
        }
        // Capture the ordinary work now, then update only destinations changed by our
        // successful replacements. A later failure must not adopt an external edit.
        record.CaptureUndo();
        ApplyReplacements(record, undo: false);
        if (record.Kind == FileUndoKind.Merged) RemoveEmptyDirectories(record.Paths);
    }

    private static void ApplyReplacements(FileUndoRecord record, bool undo)
    {
        var completed = new HashSet<FileReplacement>();
        foreach (var replacement in undo ? record.Replacements.Reverse() : record.Replacements)
        {
            var wasApplied = replacement.IsApplied;
            var previousBackup = replacement.Backup;
            try
            {
                if (undo) replacement.Undo(); else replacement.Redo();
                completed.Add(replacement);
                record.AcceptReplacementState(replacement, previousBackup);
            }
            catch (Exception error)
            {
                // The rename may have committed even if a subsequent metadata read failed.
                if (replacement.IsApplied != wasApplied)
                {
                    completed.Add(replacement);
                    record.AcceptReplacementState(replacement, previousBackup);
                }
                if (completed.Count == 0 && (undo || !record.HasOrdinaryActions)) throw;
                var done = record.Replacements.Where(completed.Contains).ToArray();
                var pending = record.Replacements.Where(item => !completed.Contains(item)).ToArray();
                // Preserve chronological order for redo, and give each backup to one record.
                FileUndoRecord remaining;
                FileUndoRecord changed;
                if (undo)
                {
                    remaining = record with { Replacements = pending };
                    changed = FileUndoRecord.Copied([], []) with { Replacements = done };
                    changed.CaptureRedo();
                }
                else
                {
                    remaining = FileUndoRecord.Copied([], []) with { Replacements = pending };
                    remaining.CaptureRedo();
                    changed = record with { Replacements = done };
                }
                throw new PartialFileUndoException(remaining, changed, error);
            }
        }
    }

    private static void RecycleForHistory(ILocalFileOperations operations, FileUndoRecord record, bool undo)
    {
        var permanent = 0;
        var completed = new List<RecycleItemResult>();
        try
        {
            operations.Recycle(record.Paths, item =>
            {
                if (!item.IsRecycled) permanent++;
                else completed.Add(item);
            });
        }
        catch (Exception error) when (permanent == 0)
        {
            record.RecycledItems = completed.ToArray();
            ThrowPartial(record, completed.Select(item => item.OriginalPath).ToArray(), undo, error);
            throw;
        }
        finally
        {
            // Windows may ask to permanently delete even during undo/redo.
            if (permanent > 0) throw new IrreversibleDeletionException(permanent);
        }
        record.RecycledItems = completed.ToArray();
    }

    private static void RestoreForHistory(ILocalFileOperations operations, FileUndoRecord record, bool undo)
    {
        var receipts = new Dictionary<string, RecycleItemResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in record.RecycledItems) receipts[item.OriginalPath] = item;
        var items = record.Paths.Select(path => receipts.TryGetValue(path, out var item) ? item : new(path, true)).ToArray();
        var completed = new List<string>();
        try { operations.RestoreRecycledItems(items, completed.Add); }
        catch (Exception error)
        {
            ThrowPartial(record, completed, undo, error);
            throw;
        }
    }

    private static void ThrowPartial(FileUndoRecord record, IReadOnlyList<string> completedPaths, bool undo, Exception error)
    {
        // Copy replacements run before recycling on undo and after restoration on redo.
        // Give each retained backup to exactly one of the split records.
        var replacementsDone = undo && record.Kind == FileUndoKind.Copied;
        if (completedPaths.Count == 0 && (!replacementsDone || record.Replacements.Count == 0)) return;
        var completedNames = new HashSet<string>(completedPaths, StringComparer.OrdinalIgnoreCase);
        var remaining = record.SelectPaths(record.Paths.Where(path => !completedNames.Contains(path)).ToArray(),
            replacementsDone ? [] : record.Replacements);
        var completed = record.SelectPaths(completedPaths, replacementsDone ? record.Replacements : []);
        if (undo) completed.CaptureRedo(); else completed.CaptureUndo();
        throw new PartialFileUndoException(remaining, completed, error);
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

    private static void Relocate(ILocalFileOperations operations, IReadOnlyList<FilePathPair> pairs, Action<IEnumerable<string>> acceptRollback)
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

        var sourceNames = needed.Select(pair => Path.TrimEndingDirectorySeparator(pair.Source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var movedToTemp = new List<(string Source, string Temp, string Target)>(needed.Count);
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // A child's staging name must not travel with an ancestor staged later.
            // Stage descendants outside the outermost moving source, then their parents.
            foreach (var pair in needed.OrderByDescending(pair => PathDepth(pair.Source)))
            {
                var outermost = pair.Source;
                for (var parent = Path.GetDirectoryName(pair.Source); parent is not null; parent = Path.GetDirectoryName(parent))
                    if (sourceNames.Contains(parent)) outermost = parent;
                var temp = Path.Combine(Path.GetDirectoryName(outermost)!, ".filesmate-undo-" + Guid.NewGuid().ToString("N"));
                operations.Rename(pair.Source, temp);
                movedToTemp.Add((pair.Source, temp, pair.Destination));
            }

            foreach (var item in movedToTemp.OrderBy(item => PathDepth(item.Target)))
            {
                EnsureParent(operations, item.Target);
                operations.Rename(item.Temp, item.Target);
                applied.Add(item.Temp);
            }
        }
        catch (Exception error)
        {
            var recoveryErrors = new List<string>();
            var touchedDirectories = movedToTemp.Select(item => Path.GetDirectoryName(item.Source)!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in movedToTemp.Where(item => applied.Contains(item.Temp)))
            {
                var parent = Path.GetDirectoryName(item.Target)!;
                touchedDirectories.Add(parent);
                // A published ancestor can receive a child, then carry the changed
                // directory metadata back to its source during recovery.
                foreach (var owner in movedToTemp.Where(owner => owner.Temp != item.Temp && IsWithin(parent, owner.Target)))
                    touchedDirectories.Add(Path.GetFullPath(Path.Combine(owner.Source, Path.GetRelativePath(owner.Target, parent))));
            }
            // Vacate all published targets first. In a cycle, a target is another
            // item's original name, so restoring originals in one pass is unsafe.
            foreach (var item in movedToTemp.OrderByDescending(item => PathDepth(item.Target)))
            {
                if (!applied.Contains(item.Temp)) continue;
                if (movedToTemp.Any(child => child.Temp != item.Temp && applied.Contains(child.Temp)
                    && IsWithin(child.Target, item.Target)))
                {
                    recoveryErrors.Add($"{item.Target}: contains a destination that could not be recovered; retained in place.");
                    continue;
                }
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
            var restored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in movedToTemp.OrderBy(item => PathDepth(item.Source)))
            {
                if (applied.Contains(item.Temp)) continue;
                if (movedToTemp.Any(parent => parent.Temp != item.Temp && IsWithin(item.Source, parent.Source)
                    && !restored.Contains(parent.Temp)))
                {
                    recoveryErrors.Add($"{item.Temp} → {item.Source}: parent could not be restored; item retained at its temporary path.");
                    continue;
                }
                try
                {
                    operations.Rename(item.Temp, item.Source);
                    restored.Add(item.Temp);
                }
                catch (Exception recovery)
                {
                    recoveryErrors.Add($"{item.Temp} → {item.Source}: {recovery.Message}");
                }
            }
            if (recoveryErrors.Count > 0)
                throw new IOException(error.Message + Environment.NewLine + string.Join(Environment.NewLine, recoveryErrors), error);
            // A fully rolled-back nested move still touched containing-directory timestamps.
            // Keep original content snapshots, so unrelated edits are never adopted on retry.
            acceptRollback(touchedDirectories);
            throw;
        }
    }

    private static int PathDepth(string path) => Path.GetFullPath(path).Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);

    private static bool IsWithin(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void EnsureParent(ILocalFileOperations operations, string path)
    {
        var parent = Path.GetDirectoryName(path.TrimEnd('\\', '/'));
        if (!string.IsNullOrEmpty(parent))
        {
            operations.CreateDirectory(parent);
        }
    }
}
