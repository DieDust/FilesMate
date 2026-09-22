using System.ComponentModel;
using System.Runtime.InteropServices;
using FilesMate.App.Localization;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Archives;
using FilesMate.Platform.Windows.Metadata;
using FilesMate.Platform.Windows.Operations;
using Microsoft.Win32.SafeHandles;

namespace FilesMate.App.Services;

/// <summary>Prepare ZIP output privately, then publish through the normal conflict and undo pipeline.</summary>
internal static class BuiltInArchiveTransfer
{
    internal static async Task<FileTransferResult> RunAsync(ILocalFileOperations operations,
        IReadOnlyList<string> sources, string destinationDirectory, bool compress, string? archiveName = null,
        bool createSubfolder = false, FileConflictResolver? resolveConflict = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken token = default,
        ReplacementBackupBudget? backupBudget = null, bool smartExtract = false)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(sources);
        var sourceSnapshot = sources.ToArray();
        return await Task.Run(async () =>
        {
            string? staging = null;
            string? stagingIdentity = null;
            SafeFileHandle? stagingGuard = null;
            FileStream? stagingMarker = null;
            var destinationGuards = new List<SafeFileHandle>();
            var destinationPaths = new List<string>();
            var result = new FileTransferResult([], [], false, 0, null);
            var errors = new List<string>();
            var displays = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                token.ThrowIfCancellationRequested();
                if (sourceSnapshot.Length == 0) throw ArchiveError(ArchiveErrorCode.InvalidPath);
                var destination = RequireNormalDirectory(destinationDirectory);
                var ancestors = new Stack<string>();
                for (var current = destination; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
                while (ancestors.TryPop(out var ancestor))
                {
                    destinationGuards.Add(OpenDirectoryGuard(ancestor));
                    destinationPaths.Add(ancestor);
                }
                var inputs = sourceSnapshot.Select(FullLocalPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var source in inputs)
                {
                    token.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(source);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw ArchiveError(ArchiveErrorCode.UnsafeLink);
                    RequireNormalDirectory(Path.GetDirectoryName(source) ?? source);
                    if (compress && (attributes & FileAttributes.Directory) != 0
                        && IsWithin(destination, source))
                        throw ArchiveError(ArchiveErrorCode.InvalidPath);
                }
                if (compress && inputs.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inputs.Length)
                    throw ArchiveError(ArchiveErrorCode.InvalidPath);

                staging = Path.Combine(destination, ".filesmate-archive-" + Guid.NewGuid().ToString("N"));
                new WindowsLocalFileOperations().CreateDirectory(staging, failIfExists: true);
                File.SetAttributes(staging, FileAttributes.Hidden);
                stagingGuard = OpenDirectoryGuard(staging);
                stagingIdentity = new WindowsFileIdentityProvider().Resolve(staging).StableKey;
                // An empty directory can receive a mount point through a metadata-only
                // handle even while rename is denied. Keep our private root nonempty
                // until publication and cleanup have finished.
                stagingMarker = new FileStream(Path.Combine(staging, ".owner-" + Guid.NewGuid().ToString("N")),
                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.DeleteOnClose);
                using (OpenDirectoryGuard(staging))
                    if (new WindowsFileIdentityProvider().Resolve(staging).StableKey != stagingIdentity)
                        throw ArchiveError(ArchiveErrorCode.UnsafeLink);
                var requests = new List<FilePathPair>();
                if (compress)
                {
                    var name = archiveName ?? (inputs.Length == 1
                        ? (Directory.Exists(inputs[0]) ? Path.GetFileName(inputs[0]) : Path.GetFileNameWithoutExtension(inputs[0])) + ".zip"
                        : "Archive.zip");
                    FileNameRules.Validate(name);
                    if (!string.Equals(Path.GetExtension(name), ".zip", StringComparison.OrdinalIgnoreCase))
                        throw ArchiveError(ArchiveErrorCode.UnsupportedEntry);
                    var output = Path.Combine(staging, name);
                    displays.Add(output, Path.Combine(destination, name));
                    await ZipArchiveService.CreateAsync(inputs, output, progress, token).ConfigureAwait(false);
                    requests.Add(new(output, Path.Combine(destination, name)));
                }
                else
                {
                    for (var i = 0; i < inputs.Length; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var input = inputs[i];
                        if (!string.Equals(Path.GetExtension(input), ".zip", StringComparison.OrdinalIgnoreCase))
                            throw ArchiveError(ArchiveErrorCode.UnsupportedEntry);
                        var output = Path.Combine(staging, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        Directory.CreateDirectory(output);
                        displays.Add(output, input);
                        await ZipArchiveService.ExtractAsync(input, output, progress, token).ConfigureAwait(false);
                        var entries = Directory.GetFileSystemEntries(output);
                        if (createSubfolder || (smartExtract && !(entries.Length == 1 && Directory.Exists(entries[0]))))
                        {
                            var name = FileNameRules.Validate(Path.GetFileNameWithoutExtension(input));
                            requests.Add(new(output, Path.Combine(destination, name)));
                        }
                        else
                            requests.AddRange(entries.Select(path => new FilePathPair(path, Path.Combine(destination, Path.GetFileName(path)))));
                    }
                }
                token.ThrowIfCancellationRequested();
                ValidateTree(staging, token);
                // MoveFileEx needs write sharing on the destination directory while publishing
                // children. Keep real read handles without delete sharing, so the directories
                // themselves cannot be renamed during the existing transfer pipeline.
                for (var i = 0; i < destinationGuards.Count; i++)
                {
                    var publicationGuard = OpenDirectoryGuard(destinationPaths[i], publishing: true);
                    destinationGuards[i].Dispose();
                    destinationGuards[i] = publicationGuard;
                }
                FileConflictResolver? wrappedResolver = resolveConflict is null ? null
                    : (conflict, cancellation) => resolveConflict(conflict with { Source = Display(conflict.Source) }, cancellation);
                result = await WindowsFileTransfer.RunAsync(operations, requests, move: true,
                    resolveConflict: wrappedResolver, token: token, backupBudget: backupBudget).ConfigureAwait(false);
                if (result.Undo is { } moved)
                {
                    foreach (var replacement in moved.Replacements) replacement.ConvertToCreatedOperation();
                    // A later archive can merge into a folder published by an earlier archive.
                    // Recycle that folder once, not both its root and individual children.
                    var paths = MinimalRoots(moved.Pairs.Select(pair => pair.Destination));
                    var pathSet = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var directories = moved.CreatedDirectories.Where(path => !IsCovered(path, pathSet)).ToArray();
                    var undo = FileUndoRecord.Copied(paths, directories) with { Replacements = moved.Replacements };
                    result = result with { Undo = undo };
                    result = result with { Undo = SeparateReplacementFolders(undo) };
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                result = result with { Cancelled = true };
            }
            catch (ArchiveOperationException error)
            {
                errors.Add(StringTable.Get("Archive_Error" + error.ErrorCode));
                System.Diagnostics.Trace.TraceWarning("Archive preparation failed: {0}", error);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or NotSupportedException or Win32Exception)
            {
                errors.Add(StringTable.Get("Archive_ErrorOperationFailed"));
                System.Diagnostics.Trace.TraceWarning("Archive operation failed: {0}", error);
            }
            finally
            {
                stagingGuard?.Dispose();
                if (staging is not null && stagingIdentity is not null)
                {
                    try { DeleteOwnedStaging(staging, stagingIdentity, stagingMarker); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
                    {
                        errors.Add(StringTable.Format("Archive_TemporaryFilesRemain", staging));
                        System.Diagnostics.Trace.TraceWarning("Archive staging retained at {0}: {1}", staging, error);
                    }
                }
                stagingMarker?.Dispose();
                for (var i = destinationGuards.Count - 1; i >= 0; i--) destinationGuards[i].Dispose();
            }
            return result with
            {
                Completed = result.Completed.Select(pair => pair with { Source = Display(pair.Source) }).ToArray(),
                RemovedSourceDirectories = [],
                Errors = result.Errors.Select(TransferError).Concat(errors).ToArray(),
            };

            string Display(string path)
            {
                for (var current = path; current is not null; current = Path.GetDirectoryName(current))
                    if (displays.TryGetValue(current, out var display))
                        return path.Length == current.Length ? display : Path.Combine(display, Path.GetRelativePath(current, path));
                return path;
            }
            string TransferError(string message)
            {
                System.Diagnostics.Trace.TraceWarning("Archive publication failed: {0}", message);
                // Transfer failures may include the only recovery path after rollback
                // fails. Keep that detail even when the native error is not localized.
                foreach (var (prepared, display) in displays.OrderByDescending(pair => pair.Key.Length))
                    message = message.Replace(prepared, display, StringComparison.OrdinalIgnoreCase);
                return StringTable.Get("Archive_ErrorOperationFailed") + "\n" + message;
            }
        }).ConfigureAwait(false);
    }

    private static string FullLocalPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw ArchiveError(ArchiveErrorCode.InvalidPath);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string RequireNormalDirectory(string path)
    {
        var full = FullLocalPath(path);
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                throw ArchiveError(ArchiveErrorCode.UnsafeLink);
        }
        return full;
    }

    private static bool IsWithin(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string[] MinimalRoots(IEnumerable<string> paths)
    {
        var roots = new List<string>();
        var rootSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path.Length))
            if (!IsCovered(path, rootSet)) { roots.Add(path); rootSet.Add(path); }
        return roots.ToArray();
    }

    private static bool IsCovered(string path, HashSet<string> roots)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (roots.Contains(current)) return true;
        return false;
    }

    private static FileUndoRecord SeparateReplacementFolders(FileUndoRecord record)
    {
        if (record.Replacements.Count == 0) return record;
        var files = new List<string>();
        var directories = new HashSet<string>(record.CreatedDirectories, StringComparer.OrdinalIgnoreCase);
        var backups = record.Replacements.Select(item => Path.GetDirectoryName(item.Backup)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replacementAncestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var replacement in record.Replacements)
            for (var parent = Path.GetDirectoryName(replacement.Destination); parent is not null; parent = Path.GetDirectoryName(parent))
                replacementAncestors.Add(parent);
        foreach (var root in record.Paths)
        {
            if (!replacementAncestors.Contains(root) || !Directory.Exists(root))
            { files.Add(root); continue; }
            // Backups can reside inside a folder published by an earlier ZIP in this batch.
            // Keep those objects at their stable paths when recycling the created contents.
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                directories.Add(directory);
                using var guard = OpenDirectoryGuard(directory);
                foreach (var child in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (backups.Contains(child)) continue;
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0)
                        pending.Push(child);
                    else files.Add(child);
                }
            }
        }
        return FileUndoRecord.Copied(files, directories.ToArray()) with { Replacements = record.Replacements };
    }

    private static void ValidateTree(string path, CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            using var guard = OpenDirectoryGuard(directory);
            foreach (var child in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw ArchiveError(ArchiveErrorCode.UnsafeLink);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(child);
            }
        }
    }

    private static void DeleteOwnedStaging(string path, string identity, FileStream? marker)
    {
        if (!Directory.Exists(path)) return;
        RequireNormalDirectory(Path.GetDirectoryName(path)!);
        using (var guard = OpenDirectoryGuard(path))
        {
            if (new WindowsFileIdentityProvider().Resolve(path).StableKey != identity)
                throw new IOException("The temporary archive directory changed; it was left untouched.");
            DeleteChildren(path, marker?.Name);
            marker?.Dispose();
        }
        Directory.Delete(path, recursive: false);
    }

    private static void DeleteChildren(string path, string? markerPath = null)
    {
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
        {
            if (string.Equals(child, markerPath, StringComparison.OrdinalIgnoreCase)) continue;
            var attributes = File.GetAttributes(child);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                // Delete a junction itself without traversing its target.
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                    using (var guard = OpenDirectoryGuard(child)) DeleteChildren(child);
                Directory.Delete(child, recursive: false);
            }
            else File.Delete(child);
        }
    }

    private static SafeFileHandle OpenDirectoryGuard(string path, bool publishing = false)
    {
        var handle = CreateFileW(path, 0x80000000, publishing ? FileShare.ReadWrite : FileShare.Read, 0, 3, 0x02000000 | 0x00200000, 0);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        if (!GetFileInformationByHandle(handle, out var info))
        { handle.Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        if ((info.Attributes & FileAttributes.Directory) == 0 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        { handle.Dispose(); throw new IOException("The temporary archive path is no longer a normal directory."); }
        return handle;
    }

    private static ArchiveOperationException ArchiveError(ArchiveErrorCode code) => new(code, code.ToString());

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, nint security,
        uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public FileAttributes Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
}
