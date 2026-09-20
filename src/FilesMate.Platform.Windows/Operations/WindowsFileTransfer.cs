using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Metadata;

namespace FilesMate.Platform.Windows.Operations;

public sealed record FileTransferResult(IReadOnlyList<FilePathPair> Completed, IReadOnlyList<string> Errors,
    bool Cancelled, int Skipped, FileUndoRecord? Undo)
{
    public int WithoutUndo { get; init; }
}

/// <summary>One operation-scoped conflict policy, including conflicts inside merged folders.</summary>
public static class WindowsFileTransfer
{
    public static Task<FileTransferResult> RunAsync(ILocalFileOperations operations, IReadOnlyList<FilePathPair> requests,
        bool move, FileConflictResolver? resolveConflict = null, IProgress<int>? progress = null, CancellationToken token = default,
        ReplacementBackupBudget? backupBudget = null) =>
        Task.Run(async () =>
        {
            var completed = new List<FilePathPair>();
            var ordinary = new List<FilePathPair>();
            var replacements = new List<FileReplacement>();
            var createdDirectories = new List<string>();
            var removedDirectories = new List<string>();
            var errors = new List<string>();
            var remembered = new Dictionary<(bool Merge, bool Replace, bool SameItem), FileConflictAction>();
            var skipped = 0;
            var cancelled = false;
            var withoutUndo = 0;
            var budget = backupBudget ?? ReplacementBackupBudget.Shared;
            foreach (var request in requests)
            {
                if (token.IsCancellationRequested || cancelled) break;
                try
                {
                    var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Source));
                    var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
                    var parent = Path.GetDirectoryName(target) ?? throw new IOException("Missing destination directory.");
                    if (Directory.Exists(source))
                    {
                        WindowsLocalFileOperations.EnsureCopyDestination(source, parent);
                        if (source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Cannot merge a folder into its ancestor.");
                    }
                    if (move && string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                    operations.CreateDirectory(parent);
                    await Transfer(source, target, 0).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; break; }
                catch (Exception error) when (IsOperationError(error)) { errors.Add($"{request.Source}: {error.Message}"); }
            }
            FileUndoRecord? undo = null;
            if (ordinary.Count + replacements.Count + createdDirectories.Count + removedDirectories.Count > 0)
                undo = move
                    ? removedDirectories.Count + createdDirectories.Count == 0
                        ? ordinary.Count > 0 ? FileUndoRecord.Relocated(ordinary) : new(FileUndoKind.Relocated, [], [])
                        : FileUndoRecord.Merged(removedDirectories, ordinary) with { CreatedDirectories = createdDirectories.ToArray() }
                    : FileUndoRecord.Copied(ordinary.Select(p => p.Destination).ToArray(), createdDirectories);
            if (undo is not null) undo = undo with { Replacements = replacements.ToArray() };
            if (withoutUndo > 0)
            {
                // The explicit destructive choice invalidates this whole batch's
                // undo, including earlier writes to a destination replaced again.
                foreach (var replacement in replacements) replacement.Dispose();
                undo = null;
            }
            return new FileTransferResult(completed, errors, cancelled || token.IsCancellationRequested, skipped, undo) { WithoutUndo = withoutUndo };

            async Task Transfer(string source, string originalTarget, int depth)
            {
                if (cancelled || token.IsCancellationRequested) return;
                if (depth >= 256) throw new IOException("The folder tree is too deep.");
                var attributes = File.GetAttributes(source);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked items cannot be transferred by this operation.");
                var directory = (attributes & FileAttributes.Directory) != 0;
                var target = originalTarget;
                var numberOnCollision = false;
                for (var attempt = 0; attempt < 10_000; attempt++)
                {
                    if (cancelled || token.IsCancellationRequested) return;
                    var merge = false;
                    if (Path.Exists(target))
                    {
                        if (!numberOnCollision)
                        {
                            var identity = new WindowsFileIdentityProvider();
                            var sourceIdentity = identity.Resolve(source).StableKey;
                            var targetIdentity = identity.Resolve(target).StableKey;
                            var sameItem = string.Equals(source, target, StringComparison.OrdinalIgnoreCase) || sourceIdentity == targetIdentity;
                            var destinationIsLink = (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0;
                            var canMerge = directory && WindowsFolderMerge.IsNormalDirectory(target) && !sameItem;
                            var canReplace = !directory && IsNormalFile(target)
                                && !sameItem;
                            var incoming = !directory ? FileConflictDetails.Read(source) : null;
                            var existing = IsNormalFile(target) ? FileConflictDetails.Read(target) : null;
                            var existingIdentity = canReplace ? targetIdentity : null;
                            var backupBytes = canReplace ? Math.Max(FileStreamSize.Read(source), FileStreamSize.Read(target)) : 0;
                            var backupUnavailable = canReplace && !budget.CanReserve(backupBytes);
                            var kind = (canMerge, canReplace, sameItem);
                            FileConflictChoice choice;
                            if (remembered.TryGetValue(kind, out var action) && !(action == FileConflictAction.Replace && backupUnavailable)) choice = new(action);
                            else
                            {
                                var suggestion = NumberedPath(originalTarget, directory);
                                choice = resolveConflict is null ? new(FileConflictAction.Cancel)
                                    : await resolveConflict(new(source, target, canMerge, Path.GetFileName(suggestion))
                                    { CanReplace = canReplace, IsSameItem = sameItem, DestinationIsLink = destinationIsLink,
                                        Incoming = incoming, Existing = existing, BackupBytes = backupBytes,
                                        BackupUsedBytes = budget.UsedBytes, BackupUnavailable = backupUnavailable }, token).ConfigureAwait(false);
                                if (choice.ApplyToAll && choice.Action is not (FileConflictAction.Cancel or FileConflictAction.ReplaceWithoutUndo)
                                    && (choice.Action != FileConflictAction.Merge || canMerge)
                                    && (choice.Action != FileConflictAction.Replace || canReplace)) remembered[kind] = choice.Action;
                            }
                            if (choice.Action == FileConflictAction.Cancel) { cancelled = true; return; }
                            if (choice.Action == FileConflictAction.Skip) { skipped++; return; }
                            if (choice.Action is FileConflictAction.Replace or FileConflictAction.ReplaceWithoutUndo && canReplace)
                            {
                                token.ThrowIfCancellationRequested();
                                if (FileConflictDetails.Read(target) != existing || FileConflictDetails.Read(source) != incoming
                                    || identity.Resolve(target).StableKey != existingIdentity || identity.Resolve(source).StableKey != sourceIdentity)
                                {
                                    remembered.Remove(kind); // A changed comparison needs a fresh decision.
                                    continue;
                                }
                                var replacement = await ReplaceFileAsync(source, target, move, incoming!, existing!, sourceIdentity!, existingIdentity!, token,
                                    budget, retainUndo: choice.Action == FileConflictAction.Replace).ConfigureAwait(false);
                                if (replacement is not null) replacements.Add(replacement); else withoutUndo++;
                                completed.Add(new(source, target)); progress?.Report(completed.Count); return;
                            }
                            merge = choice.Action == FileConflictAction.Merge && canMerge;
                            numberOnCollision = choice.Action == FileConflictAction.KeepBoth;
                            if (!merge && !numberOnCollision) { cancelled = true; return; }
                        }
                        if (numberOnCollision) target = NumberedPath(originalTarget, directory);
                    }
                    var targetAcquired = false;
                    try
                    {
                        if (directory)
                        {
                            if (!merge && move && string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    operations.Rename(source, target);
                                    targetAcquired = true;
                                    completed.Add(new(source, target)); ordinary.Add(new(source, target)); progress?.Report(completed.Count); return;
                                }
                                catch (IOException error) when ((error.HResult & 0xFFFF) == 17) { /* Mounted volume: transfer children. */ }
                            }
                            if (merge)
                            {
                                if (!WindowsFolderMerge.IsNormalDirectory(target)) throw new IOException("The destination folder changed.");
                            }
                            else
                            {
                                operations.CreateDirectory(target, failIfExists: true);
                                createdDirectories.Add(target);
                            }
                            targetAcquired = true;
                            await TransferChildren(source, target, depth).ConfigureAwait(false);
                            return;
                        }
                        if (move) operations.Rename(source, target);
                        else await CopyFileAsync(source, target, token).ConfigureAwait(false);
                        targetAcquired = true;
                        completed.Add(new(source, target));
                        ordinary.Add(new(source, target));
                        progress?.Report(completed.Count);
                        return;
                    }
                    catch (IOException) when (!targetAcquired && !merge && Path.Exists(target))
                    {
                        // A name taken after the check is a conflict too; never overwrite it.
                        // CopyFileAsync publishes only a fully copied file with an exclusive rename.
                    }
                }
                throw new IOException("Could not allocate an available destination name.");
            }

            async Task TransferChildren(string source, string target, int depth)
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(source))
                {
                    if (cancelled || token.IsCancellationRequested) break;
                    try { await Transfer(child, Path.Combine(target, Path.GetFileName(child)), depth + 1).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; break; }
                    catch (Exception error) when (IsOperationError(error)) { errors.Add($"{child}: {error.Message}"); }
                }
                if (move && !cancelled && !token.IsCancellationRequested && !Directory.EnumerateFileSystemEntries(source).Any())
                {
                    if (!WindowsFolderMerge.IsNormalDirectory(source)) throw new IOException("The source folder changed.");
                    Directory.Delete(source, recursive: false);
                    removedDirectories.Add(source);
                }
            }
        });

    private static bool IsOperationError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException
        or NotSupportedException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException;

    private static string NumberedPath(string path, bool directory) =>
        UniquePath.CombineAvailable(Path.GetDirectoryName(path)!, Path.GetFileName(path), Path.Exists, directory);

    private static bool IsNormalFile(string path) => File.Exists(path)
        && (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;

    private static async Task<FileReplacement?> ReplaceFileAsync(string source, string target, bool move,
        FileConflictDetails incoming, FileConflictDetails existing, string sourceIdentity, string targetIdentity, CancellationToken token,
        ReplacementBackupBudget budget, bool retainUndo)
    {
        var parent = Path.GetDirectoryName(target)!;
        var staged = Path.Combine(parent, ".filesmate-copy-" + Guid.NewGuid().ToString("N"));
        var backupDirectory = Path.Combine(parent, ".filesmate-history-" + Guid.NewGuid().ToString("N"));
        // Copying a replacement needs its incoming bytes free even without undo.
        // Leave a reserve for the filesystem and other work; never start by removing the old version.
        var space = new DriveInfo(Path.GetPathRoot(parent)!);
        var incomingBytes = FileStreamSize.Read(source);
        var backupBytes = Math.Max(incomingBytes, FileStreamSize.Read(target));
        var sameVolumeMove = move && string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);
        var required = sameVolumeMove ? 0 : incomingBytes;
        if (space.AvailableFreeSpace < required + 128L * 1024 * 1024)
            throw new IOException("Insufficient free space to safely prepare the replacement (128 MiB reserve required).");
        IDisposable? lease = null;
        if (retainUndo)
        {
            lease = budget.Reserve(backupDirectory, backupBytes,
                () => new WindowsLocalFileOperations().CreateDirectory(backupDirectory, failIfExists: true));
        }
        var backup = retainUndo ? Path.Combine(backupDirectory, Path.GetFileName(target)) : null;
        var leaseTransferred = false;
        var stagedOwned = false;
        try
        {
            if (retainUndo) File.SetAttributes(backupDirectory, FileAttributes.Hidden);
            // Deny concurrent writers while preparing the full replacement. Delete sharing
            // permits the subsequent filesystem rename; no file is ever opened for truncation.
            using var sourceGuard = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var targetGuard = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var identities = new WindowsFileIdentityProvider();
            if (!IsNormalFile(source) || !IsNormalFile(target)
                || identities.Resolve(source).StableKey != sourceIdentity || identities.Resolve(target).StableKey != targetIdentity
                || FileConflictDetails.Read(source) != incoming || FileConflictDetails.Read(target) != existing)
                throw new IOException("The compared file changed before replacement. Please try again.");
            if (FileStreamSize.Read(source) > incomingBytes || FileStreamSize.Read(target) > backupBytes)
                throw new IOException("The file grew before replacement. Please choose again.");
            if (move)
            {
                File.Move(source, staged, overwrite: false);
                sourceGuard.Dispose(); // ReplaceFile must open the moved staging file for metadata writes.
            }
            else await CopyFileAsync(source, staged, token).ConfigureAwait(false);
            stagedOwned = true;
            token.ThrowIfCancellationRequested();
            if (!IsNormalFile(target) || FileConflictDetails.Read(target) != existing || identities.Resolve(target).StableKey != targetIdentity)
                throw new IOException("The destination changed before replacement. Please try again.");
            // Both names are on the destination volume. Windows replaces the directory entry
            // and retains the old file; failures do not turn into a destructive copy fallback.
            File.Replace(staged, target, backup);
            stagedOwned = false;
            if (!retainUndo) return null;
            var retained = new FileReplacement(source, target, backup!, move, lease);
            leaseTransferred = true;
            return retained;
        }
        catch (Exception error) when (IsOperationError(error))
        {
            // Some filesystems can report a partially completed native replacement.
            // Restore the previous name only if free, and always keep the recoverable data.
            if (File.Exists(backup))
            {
                if (!Path.Exists(target))
                {
                    try { File.Move(backup, target, overwrite: false); }
                    catch (Exception restoreError) when (IsOperationError(restoreError))
                    { throw new IOException($"{error.Message} Previous version retained at: {backup}", restoreError); }
                }
                else throw new IOException($"{error.Message} Previous version retained at: {backup}", error);
            }
            throw;
        }
        finally
        {
            if (stagedOwned && File.Exists(staged))
            {
                if (move)
                {
                    try { File.Move(staged, source, overwrite: false); }
                    catch (Exception error) when (IsOperationError(error))
                    { throw new IOException($"Could not restore the source name. Incoming file retained at: {staged}", error); }
                }
                else { File.SetAttributes(staged, FileAttributes.Normal); File.Delete(staged); }
            }
            // A successful replacement keeps its previous version until its undo record expires.
            try
            {
                if (retainUndo && !File.Exists(backup) && Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, recursive: false);
            }
            finally { if (!leaseTransferred) lease?.Dispose(); }
        }
    }

    private static Task CopyFileAsync(string source, string target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var staging = Path.Combine(Path.GetDirectoryName(target)!, ".filesmate-copy-" + Guid.NewGuid().ToString("N"));
        // Reserve a private sibling and disallow replacement while Windows copies data,
        // timestamps, attributes and alternate streams. A byte-stream copy loses metadata.
        var reservation = new FileStream(staging, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
        try
        {
            using (reservation)
            {
                File.Copy(source, staging, overwrite: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(staging, target, overwrite: false);
        }
        finally
        {
            if (File.Exists(staging))
            {
                File.SetAttributes(staging, FileAttributes.Normal);
                File.Delete(staging);
            }
        }
        return Task.CompletedTask;
    }
}
