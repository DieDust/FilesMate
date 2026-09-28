using FilesMate.Core.Archives;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using static FilesMate.Platform.Windows.Archives.ArchivePathGuard;
using static FilesMate.Platform.Windows.Archives.ZipArchiveService;

namespace FilesMate.Platform.Windows.Archives;

/// <summary>Bundled archive engine. All output stays in caller-owned, empty staging directories.</summary>
public static class ManagedArchiveService
{
    public static Task ExtractAsync(ArchiveSet set, string destination, string? password = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken token = default) =>
        Task.Run(() => Extract(set, destination, password, progress, token), token);

    private static void Extract(ArchiveSet set, string destination, string? password,
        IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        var leases = new List<IDisposable>();
        var encrypted = false;
        try
        {
            var streams = new List<Stream>();
            foreach (var path in set.Volumes)
            {
                token.ThrowIfCancellationRequested();
                var local = LocalPath(path);
                leases.Add(LockDirectory(Path.GetDirectoryName(local)!));
                var input = OpenRead(local, out _);
                leases.Add(input);
                streams.Add(input);
            }
            var zone = ArchiveSecurityZone.Read(set.PrimaryPath);
            if (streams.Count == 1 && streams[0] is FileStream zipInput && zipInput.Length >= 4)
            {
                Span<byte> header = stackalloc byte[4];
                zipInput.ReadExactly(header);
                zipInput.Position = 0;
                var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header);
                if (magic is 0x04034b50 or 0x06054b50)
                    ZipDirectoryPreflight.Validate(zipInput, token, allowAdditionalMethods: true);
            }
            destination = LocalPath(destination);
            using var destinationGuard = LockDirectory(destination, createLeaf: true);
            if (Directory.EnumerateFileSystemEntries(destination).Any()) throw Error(ArchiveErrorCode.DestinationNotEmpty);
            destinationGuard.KeepOwnedDirectoryNonempty(destination);
            var options = new ReaderOptions { Password = password, LeaveStreamOpen = true,
                ExtensionHint = Path.GetExtension(ArchiveFileName.CanonicalName(set.PrimaryPath)).TrimStart('.') };
            var name = ArchiveFileName.CanonicalName(set.PrimaryPath).ToLowerInvariant();
            var streaming = name.EndsWith(".tar.gz") || name.EndsWith(".tgz") || name.EndsWith(".tar.bz2")
                || name.EndsWith(".tbz2") || name.EndsWith(".tar.xz") || name.EndsWith(".txz")
                || name.EndsWith(".tar.zst") || name.EndsWith(".tzst");
            var names = new EntryNames();
            long total = 0;
            long written = 0;
            var count = 0;
            if (streaming)
            {
                if (streams.Count != 1) throw Error(ArchiveErrorCode.UnsupportedEntry);
                using var reader = ReaderFactory.OpenReader(streams[0], options);
                using var cancellation = token.Register(reader.Cancel);
                try
                {
                    while (reader.MoveToNextEntry())
                    {
                        Validate(reader.Entry);
                        count--;
                        Write(reader.Entry, output => CopyReader(reader, output), 0, 0);
                    }
                }
                catch { reader.Cancel(); throw; }
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(streams, options);
                var entries = new List<IArchiveEntry>();
                foreach (var entry in archive.Entries) { Validate(entry); entries.Add(entry); }
                CheckSpace(destination, total);
                count = 0;
                if (archive.Type == ArchiveType.SevenZip || (archive.Type == ArchiveType.Rar && archive.IsSolid))
                {
                    using var reader = archive.ExtractAllEntries();
                    using var cancellation = token.Register(reader.Cancel);
                    try
                    {
                        while (reader.MoveToNextEntry())
                        {
                            if (archive.Type == ArchiveType.SevenZip && reader.Entry.Crc == 0 && !reader.Entry.IsDirectory)
                            {
                                // IEntry cannot distinguish an absent 7z CRC from a zero CRC.
                                // The archive API retains that distinction and validates when present.
                                var entry = entries.First(e => e.Key == reader.Entry.Key);
                                Write(entry, output => entry.WriteTo(output, new ExtractionOptions { CheckCrc = true }), total, entries.Count);
                            }
                            else Write(reader.Entry, output => CopyReader(reader, output), total, entries.Count,
                                archive.Type == ArchiveType.SevenZip ? (uint?)reader.Entry.Crc : null);
                        }
                    }
                    catch { reader.Cancel(); throw; }
                    if (count != entries.Count) throw Error(ArchiveErrorCode.InvalidArchive);
                }
                else
                    foreach (var entry in entries)
                        Write(entry, output => entry.WriteTo(output, new ExtractionOptions { CheckCrc = true }), total, entries.Count);
            }
            token.ThrowIfCancellationRequested();
            progress?.Report(new(set.PrimaryPath, written, written, count, count));

            void Validate(IEntry entry)
            {
                token.ThrowIfCancellationRequested();
                if (++count > MaxEntries) throw Error(ArchiveErrorCode.TooManyEntries);
                // TAR and compression wrappers expose links through LinkTarget, but their
                // Attrib property is deliberately unimplemented in SharpCompress.
                var attributes = entry is SharpCompress.Common.Zip.ZipEntry or SharpCompress.Common.Rar.RarEntry
                    or SharpCompress.Common.SevenZip.SevenZipEntry ? (uint)(entry.Attrib ?? 0) : 0;
                if (!string.IsNullOrEmpty(entry.LinkTarget) || entry is SharpCompress.Common.Rar.RarEntry { IsRedir: true }
                    || (attributes & (uint)FileAttributes.ReparsePoint) != 0
                    || (attributes >> 16 & 0xF000) == 0xA000 || (attributes & 0xF000) == 0xA000)
                    throw Error(ArchiveErrorCode.UnsafeLink);
                var key = EntryName(entry);
                if (key.Length != 0 || !entry.IsDirectory) names.Add(key, entry.IsDirectory);
                total = AddSize(total, entry.Size);
                encrypted |= entry.IsEncrypted;
                if (entry.IsEncrypted && password is null) throw Error(ArchiveErrorCode.PasswordRequired);
            }

            void Write(IEntry entry, Action<Stream> copy, long totalBytes, int totalEntries, uint? crc = null)
            {
                token.ThrowIfCancellationRequested();
                var key = EntryName(entry);
                var parent = entry.IsDirectory ? key : key.Contains('/') ? key[..key.LastIndexOf('/')] : "";
                using var guard = CreateParents(destination, parent);
                if (!entry.IsDirectory)
                {
                    var target = LocalPath(Path.Combine(destination, key.Replace('/', '\\')));
                    using var output = CreateNew(target);
                    ArchiveSecurityZone.Write(target, zone);
                    using var bounded = new ProgressStream(output, token, bytes =>
                    {
                        written = AddSize(written, bytes);
                        progress?.Report(new(key, written, totalBytes, count, totalEntries));
                    }, reading: false, maximum: entry.Size, checkCrc: crc.HasValue);
                    copy(bounded);
                    if (bounded.Transferred != entry.Size || (crc.HasValue && bounded.Crc != crc.Value))
                        throw Error(ArchiveErrorCode.InvalidArchive);
                    output.Flush();
                    if (entry.LastModifiedTime is { } date) SetLastWriteTime(output, new DateTimeOffset(date));
                }
                count++;
            }
        }
        catch (SharpCompress.Common.CryptographicException) { throw Error(ArchiveErrorCode.PasswordRequired); }
        catch (ArchiveOperationException error) when (encrypted && error.ErrorCode == ArchiveErrorCode.InvalidArchive)
        { throw Error(ArchiveErrorCode.PasswordRequired); }
        catch (SharpCompress.Common.IncompleteArchiveException) { throw Error(ArchiveErrorCode.MissingVolume); }
        catch (Exception error) when (error is SharpCompressException or InvalidDataException or EndOfStreamException)
        {
            var code = encrypted ? ArchiveErrorCode.PasswordRequired : ArchiveErrorCode.InvalidArchive;
            throw new ArchiveOperationException(code, code.ToString(), error);
        }
        catch (NotSupportedException) { throw Error(ArchiveErrorCode.UnsupportedEntry); }
        finally { for (var i = leases.Count - 1; i >= 0; i--) leases[i].Dispose(); }
    }

    private static string EntryName(IEntry entry)
    {
        var key = (entry.Key ?? "").Replace('\\', '/');
        while (key.StartsWith("./", StringComparison.Ordinal)) key = key[2..];
        if (entry.IsDirectory && key == ".") return "";
        return entry.IsDirectory && key.EndsWith('/') ? key[..^1] : key;
    }

    private static void CopyReader(IReader reader, Stream output)
    {
        using var input = reader.OpenEntryStream();
        try { input.CopyTo(output, BufferSize); }
        // Cancel before disposing EntryStream: otherwise disposal drains the remaining
        // solid entry after cancellation or a size-limit failure.
        catch { reader.Cancel(); throw; }
    }

    public static Task Create7zAsync(IReadOnlyList<string> sources, string destination,
        IProgress<ArchiveProgress>? progress = null, CancellationToken token = default) => Task.Run(() =>
    {
        destination = LocalPath(destination);
        using var guard = LockDirectory(Path.GetDirectoryName(destination)!);
        var items = new List<SourceItem>();
        var names = new EntryNames();
        long total = 0;
        foreach (var source in sources)
        {
            var path = LocalPath(source);
            if (path.Length <= 3 || destination.StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw Error(ArchiveErrorCode.InvalidPath);
            AddSource(path, Path.GetFileName(path), items, names, ref total, token);
        }
        if (items.Count == 0) throw Error(ArchiveErrorCode.InvalidPath);
        CheckSpace(destination, total);
        using var output = CreateNew(destination);
        var reporter = new Reporter(progress, total, items.Count);
        try
        {
            using (var writer = WriterFactory.OpenWriter(output, ArchiveType.SevenZip,
                new SevenZipWriterOptions(CompressionType.LZMA2) { LeaveStreamOpen = true }))
            {
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    if (item.Directory) writer.WriteDirectory(item.Name, null);
                    else
                    {
                        using var sourceGuard = LockDirectory(Path.GetDirectoryName(item.Path)!);
                        using var input = OpenRead(item.Path, out var identity);
                        if (identity != item.Identity) throw Error(ArchiveErrorCode.SourceChanged);
                        using var tracked = new ProgressStream(input, token, bytes =>
                        { reporter.Bytes += bytes; reporter.Report(item.Path); }, reading: true, maximum: identity.Length);
                        writer.Write(item.Name, tracked, DateTime.FromFileTimeUtc(identity.LastWrite));
                        if (tracked.Transferred != identity.Length) throw Error(ArchiveErrorCode.SourceChanged);
                    }
                    reporter.Completed++;
                }
            }
            token.ThrowIfCancellationRequested();
            output.Flush(flushToDisk: true);
            reporter.Report(destination, force: true);
        }
        catch { DeleteOwned(output); throw; }
    }, token);

    private sealed class ProgressStream(Stream inner, CancellationToken token, Action<int> progress, bool reading, long maximum, bool checkCrc = false) : Stream
    {
        private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
        { var c = (uint)n; for (var i = 0; i < 8; i++) c = (c >> 1) ^ ((c & 1) == 0 ? 0 : 0xEDB88320u); return c; }).ToArray();
        private uint _crc = uint.MaxValue;
        public uint Crc => ~_crc;
        public long Transferred { get; private set; }
        private long _lastReport = Environment.TickCount64;
        private int _pending;
        private void Advance(int bytes)
        {
            if (bytes > maximum - Transferred) throw Error(ArchiveErrorCode.InvalidArchive);
            Transferred += bytes;
            _pending += bytes;
            if (Environment.TickCount64 - _lastReport < 100 && Transferred != maximum && _pending < 4 * 1024 * 1024) return;
            progress(_pending); _pending = 0; _lastReport = Environment.TickCount64;
        }
        public override int Read(byte[] buffer, int offset, int count)
        { token.ThrowIfCancellationRequested(); var read = inner.Read(buffer, offset, count); Advance(read); return read; }
        public override void Write(byte[] buffer, int offset, int count)
        {
            token.ThrowIfCancellationRequested(); Advance(count);
            if (checkCrc) for (var i = offset; i < offset + count; i++) _crc = CrcTable[(byte)(_crc ^ buffer[i])] ^ (_crc >> 8);
            inner.Write(buffer, offset, count);
        }
        public override bool CanRead => reading;
        public override bool CanWrite => !reading;
        public override bool CanSeek => reading && inner.CanSeek;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() => inner.Flush();
        protected override void Dispose(bool disposing) { if (_pending > 0) { progress(_pending); _pending = 0; } base.Dispose(disposing); }
    }
}
