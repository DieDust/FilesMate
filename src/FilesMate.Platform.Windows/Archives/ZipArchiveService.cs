using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using static FilesMate.Platform.Windows.Archives.ArchivePathGuard;

namespace FilesMate.Platform.Windows.Archives;

/// <summary>
/// ZIP creation and extraction for an application-owned staging directory. Extraction never
/// merges with existing contents. The caller owns cleanup of partially extracted staging data.
/// </summary>
public static class ZipArchiveService
{
    internal const int BufferSize = 64 * 1024;
    internal const int MaxEntries = 100_000;
    internal const long MaxMetadataBytes = 64L * 1024 * 1024;
    internal const long MaxExpandedBytes = 64L * 1024 * 1024 * 1024;
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static Task CreateAsync(IReadOnlyList<string> sources, string destinationArchive,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var snapshot = sources.ToArray();
        return Task.Run(() => CreateCoreAsync(snapshot, destinationArchive, progress, cancellationToken), cancellationToken);
    }

    public static Task ExtractAsync(string archivePath, string destinationDirectory,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            try { await ExtractCoreAsync(archivePath, destinationDirectory, progress, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
            { throw Error(ArchiveErrorCode.InvalidArchive, error.Message); }
        }, cancellationToken);

    private static async Task CreateCoreAsync(IReadOnlyList<string> sources, string destinationArchive,
        IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        if (sources.Count == 0) throw Error(ArchiveErrorCode.InvalidPath);
        var destination = LocalPath(destinationArchive);
        using var destinationGuard = LockDirectory(Path.GetDirectoryName(destination)!);
        if (Path.Exists(destination)) throw Error(ArchiveErrorCode.DestinationExists, destination);
        var items = new List<SourceItem>();
        var names = new EntryNames();
        long total = 0;
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            var path = LocalPath(source);
            if (path.Length <= 3 || path.Equals(destination, StringComparison.OrdinalIgnoreCase)) throw Error(ArchiveErrorCode.InvalidPath, path);
            if (destination.StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw Error(ArchiveErrorCode.InvalidPath, destination);
            AddSource(path, Path.GetFileName(path), items, names, ref total, token);
        }
        CheckSpace(destination, total);
        var reporter = new Reporter(progress, total, items.Count);
        var buffer = new byte[BufferSize];
        using var output = CreateNew(destination);
        try
        {
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    reporter.Report(item.Path);
                    var entry = archive.CreateEntry(item.Name + (item.Directory ? "/" : ""), CompressionLevel.Optimal);
                    if (!item.Directory)
                    {
                        using var sourceGuard = LockDirectory(Path.GetDirectoryName(item.Path)!);
                        using var input = OpenRead(item.Path, out var identity);
                        if (identity != item.Identity) throw Error(ArchiveErrorCode.SourceChanged, item.Path);
                        var date = DateTimeOffset.FromFileTime(identity.LastWrite);
                        entry.LastWriteTime = date.Year is >= 1980 and <= 2107 ? date : new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using var entryStream = entry.Open();
                        await CopyAsync(input, entryStream, identity.Length, null, buffer, reporter, item.Path, token).ConfigureAwait(false);
                    }
                    reporter.Completed++;
                }
            }
            token.ThrowIfCancellationRequested();
            output.Flush(flushToDisk: true);
            reporter.Report(destination, force: true);
        }
        catch
        {
            // Exact open object, never File.Delete(path) after a race or failed CREATE_NEW.
            DeleteOwned(output);
            throw;
        }
    }

    private static void AddSource(string path, string name, List<SourceItem> items, EntryNames names, ref long total, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (items.Count >= MaxEntries) throw Error(ArchiveErrorCode.TooManyEntries);
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw Error(ArchiveErrorCode.UnsafeLink, path);
        var directory = (attributes & FileAttributes.Directory) != 0;
        names.Add(name, directory);
        if (directory)
        {
            using var guard = LockDirectory(path);
            items.Add(new(path, name, true, default));
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
                AddSource(child, name + "/" + Path.GetFileName(child), items, names, ref total, token);
        }
        else
        {
            using var guard = LockDirectory(Path.GetDirectoryName(path)!);
            using var stream = OpenRead(path, out var identity);
            total = AddSize(total, identity.Length);
            items.Add(new(path, name, false, identity));
        }
    }

    private static async Task ExtractCoreAsync(string archivePath, string destinationDirectory,
        IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        var source = LocalPath(archivePath);
        var destination = LocalPath(destinationDirectory);
        if (destination.Length <= 3) throw Error(ArchiveErrorCode.InvalidPath, destination);
        using var sourceGuard = LockDirectory(Path.GetDirectoryName(source)!);
        using var input = OpenRead(source, out _);
        var securityZone = ArchiveSecurityZone.Read(source);
        token.ThrowIfCancellationRequested();
        var expectedCount = ZipDirectoryPreflight.Validate(input, token);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count != expectedCount) throw Error(ArchiveErrorCode.InvalidArchive);
        var names = new EntryNames();
        var plan = new List<(ZipArchiveEntry Entry, string Name, bool Directory)>(expectedCount);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var directory = entry.FullName.EndsWith('/');
            var name = directory ? entry.FullName[..^1] : entry.FullName;
            var type = ((uint)entry.ExternalAttributes >> 16) & 0xF000;
            if (((uint)entry.ExternalAttributes & (uint)FileAttributes.ReparsePoint) != 0 || type == 0xA000)
                throw Error(ArchiveErrorCode.UnsafeLink, entry.FullName);
            if (type is not (0 or 0x8000 or 0x4000) || (type == 0x4000 && !directory) ||
                (!directory && (entry.ExternalAttributes & (int)FileAttributes.Directory) != 0))
                throw Error(ArchiveErrorCode.UnsupportedEntry, entry.FullName);
            names.Add(name, directory);
            if (directory && entry.Length != 0) throw Error(ArchiveErrorCode.InvalidArchive, name);
            total = AddSize(total, entry.Length);
            plan.Add((entry, name, directory));
        }
        // Preflight completes before even creating the staging root.
        using var destinationGuard = LockDirectory(destination, createLeaf: true);
        if (Directory.EnumerateFileSystemEntries(destination).Any()) throw Error(ArchiveErrorCode.DestinationNotEmpty, destination);
        destinationGuard.KeepOwnedDirectoryNonempty(destination);
        CheckSpace(destination, total);
        var reporter = new Reporter(progress, total, plan.Count);
        var buffer = new byte[BufferSize];
        foreach (var item in plan)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(destination, item.Name.Replace('/', '\\')));
            if (!target.StartsWith(destination.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw Error(ArchiveErrorCode.InvalidPath, item.Name);
            reporter.Report(item.Name);
            var relativeParent = item.Directory ? item.Name : item.Name.Contains('/') ? item.Name[..item.Name.LastIndexOf('/')] : "";
            using var parentGuard = CreateParents(destination, relativeParent);
            using var entryStream = item.Entry.Open();
            if (item.Directory)
                await CopyAsync(entryStream, Stream.Null, 0, item.Entry.Crc32, buffer, reporter, item.Name, token).ConfigureAwait(false);
            else
            {
                using var output = CreateNew(target);
                ArchiveSecurityZone.Write(target, securityZone);
                await CopyAsync(entryStream, output, item.Entry.Length, item.Entry.Crc32, buffer, reporter, item.Name, token).ConfigureAwait(false);
                output.Flush();
                SetLastWriteTime(output, item.Entry.LastWriteTime);
            }
            reporter.Completed++;
        }
        token.ThrowIfCancellationRequested();
        reporter.Report(source, force: true);
    }

    private static DirectoryLease CreateParents(string root, string relative)
    {
        var guard = new DirectoryLease();
        try
        {
            var path = root;
            foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                path = Path.Combine(path, part);
                Directory.CreateDirectory(path);
                guard.Add(OpenDirectory(path));
                guard.KeepOwnedDirectoryNonempty(path);
            }
            return guard;
        }
        catch { guard.Dispose(); throw; }
    }

    private static async Task CopyAsync(Stream source, Stream destination, long expected, uint? expectedCrc,
        byte[] buffer, Reporter reporter, string path, CancellationToken token)
    {
        long copied = 0;
        uint crc = uint.MaxValue;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) break;
            if (read > expected - copied) throw Error(ArchiveErrorCode.InvalidArchive, path);
            if (expectedCrc.HasValue)
                for (var i = 0; i < read; i++) crc = CrcTable[(byte)(crc ^ buffer[i])] ^ (crc >> 8);
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            copied += read;
            reporter.Bytes += read;
            reporter.Report(path);
        }
        if (copied != expected || (expectedCrc.HasValue && ~crc != expectedCrc.Value))
            throw Error(ArchiveErrorCode.InvalidArchive, path);
    }

    private static long AddSize(long total, long size)
    {
        if (size < 0 || size > MaxExpandedBytes - total) throw Error(ArchiveErrorCode.ArchiveTooLarge);
        return total + size;
    }

    private static void CheckSpace(string path, long bytes)
    {
        var available = new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace;
        if (bytes > Math.Max(0, available - 16 * 1024 * 1024)) throw Error(ArchiveErrorCode.InsufficientSpace);
    }

    private sealed class EntryNames
    {
        private readonly Dictionary<string, bool> _nodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _explicit = new(StringComparer.OrdinalIgnoreCase);
        private long _nameBytes;
        private long _archiveMetadataBytes;
        internal void Add(string path, bool directory)
        {
            if (path.Length is 0 or > 4096 || path.Contains('\\') || path.StartsWith('/')) throw Error(ArchiveErrorCode.InvalidPath, path);
            _archiveMetadataBytes += 74 + Encoding.UTF8.GetByteCount(path) + (directory ? 1 : 0);
            if (_archiveMetadataBytes > MaxMetadataBytes) throw Error(ArchiveErrorCode.MetadataTooLarge);
            var parts = path.Split('/');
            if (parts.Length > 128) throw Error(ArchiveErrorCode.InvalidPath, path);
            var current = "";
            for (var i = 0; i < parts.Length; i++)
            {
                ValidateSegment(parts[i]);
                current = i == 0 ? parts[i] : current + "/" + parts[i];
                var isDirectory = i < parts.Length - 1 || directory;
                var found = _nodes.TryGetValue(current, out var existing);
                if (found && (!existing || !isDirectory))
                    throw Error(ArchiveErrorCode.InvalidPath, path);
                if (!found)
                {
                    if (_nodes.Count >= MaxEntries) throw Error(ArchiveErrorCode.TooManyEntries);
                    _nameBytes += current.Length * sizeof(char);
                    if (_nameBytes > MaxMetadataBytes) throw Error(ArchiveErrorCode.MetadataTooLarge);
                }
                _nodes[current] = isDirectory;
            }
            if (!_explicit.Add(path)) throw Error(ArchiveErrorCode.InvalidPath, path);
        }
    }

    private sealed record SourceItem(string Path, string Name, bool Directory, FileIdentity Identity);
    private sealed class Reporter(IProgress<ArchiveProgress>? progress, long total, int entries)
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _reported = -100;
        internal long Bytes;
        internal int Completed;
        internal void Report(string path, bool force = false)
        {
            if (progress is null || (!force && _watch.ElapsedMilliseconds - _reported < 100)) return;
            _reported = _watch.ElapsedMilliseconds;
            progress.Report(new(path, Bytes, total, Completed, entries));
        }
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var j = 0; j < 8; j++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xEDB88320u : 0u);
            table[i] = value;
        }
        return table;
    }
}
