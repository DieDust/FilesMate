using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FilesMate.Platform.Windows.Archives;
using Xunit.Abstractions;

namespace FilesMate.Platform.Windows.Tests.Archives;

[Collection("ZIP streaming allocation")]
public sealed class ZipArchiveServiceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = MakeRoot();
    private string At(string name) => Path.Combine(_root, name);

    [Fact]
    public async Task Round_trip_preserves_chinese_names_empty_folders_and_file_contents()
    {
        var source = Directory.CreateDirectory(At("项目")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "空文件夹"));
        await File.WriteAllTextAsync(Path.Combine(source, "测试.txt"), "你好，世界 🌍");
        await File.WriteAllBytesAsync(Path.Combine(source, "empty.bin"), []);
        await File.WriteAllTextAsync(At("other.txt"), "another selection");
        var reports = new List<ArchiveProgress>();
        await ZipArchiveService.CreateAsync([source, At("other.txt")], At("result.zip"), new InlineProgress(reports.Add));
        await ZipArchiveService.ExtractAsync(At("result.zip"), At("stage"), new InlineProgress(reports.Add));
        Assert.True(Directory.Exists(At("stage/项目/空文件夹")));
        Assert.Equal("你好，世界 🌍", await File.ReadAllTextAsync(At("stage/项目/测试.txt")));
        Assert.Equal(0, new FileInfo(At("stage/项目/empty.bin")).Length);
        Assert.Equal("another selection", await File.ReadAllTextAsync(At("stage/other.txt")));
        Assert.Equal(reports[^1].TotalBytes, reports[^1].ProcessedBytes);
        Assert.Equal(reports[^1].TotalEntries, reports[^1].CompletedEntries);
    }

    [Fact]
    public async Task Large_file_uses_streaming_copy_and_bounded_progress()
    {
        var path = At("large.bin");
        var buffer = new byte[64 * 1024];
        new Random(127).NextBytes(buffer);
        using (var file = File.Create(path)) for (var i = 0; i < 512; i++) file.Write(buffer);
        var watch = Stopwatch.StartNew();
        var before = GC.GetTotalAllocatedBytes(true);
        var events = 0;
        await ZipArchiveService.CreateAsync([path], At("large.zip"), new InlineProgress(_ => events++));
        await ZipArchiveService.ExtractAsync(At("large.zip"), At("large-stage"), new InlineProgress(_ => events++));
        var allocated = GC.GetTotalAllocatedBytes(true) - before;
        using var original = File.OpenRead(path);
        using var result = File.OpenRead(At("large-stage/large.bin"));
        Assert.Equal(await SHA256.HashDataAsync(original), await SHA256.HashDataAsync(result));
        Assert.InRange(events, 2, (int)(watch.ElapsedMilliseconds / 100) + 6);
        // Two 32 MiB file payloads are never materialized as managed arrays.
        Assert.True(allocated < 24L * 1024 * 1024, $"Unexpected managed allocation: {allocated:N0} bytes.");
        output.WriteLine($"32 MiB round trip: {watch.ElapsedMilliseconds} ms, cumulative managed allocation {allocated:N0} bytes, progress events {events}.");
    }

    [Fact]
    public async Task Cancel_create_removes_only_its_own_output_and_preserves_sources()
    {
        await File.WriteAllTextAsync(At("source.txt"), "unchanged");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ZipArchiveService.CreateAsync([At("source.txt")], At("cancel.zip"),
            new InlineProgress(_ => cancellation.Cancel()), cancellation.Token));
        Assert.False(File.Exists(At("cancel.zip")));
        Assert.Equal("unchanged", await File.ReadAllTextAsync(At("source.txt")));
    }

    [Fact]
    public async Task Cancel_extract_leaves_partial_staging_for_caller_cleanup()
    {
        MakeZip("cancel.zip", [("a.txt", "payload")]);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ZipArchiveService.ExtractAsync(At("cancel.zip"), At("stage"),
            new InlineProgress(_ => cancellation.Cancel()), cancellation.Token));
        Assert.True(File.Exists(At("cancel.zip")));
        Assert.True(Directory.Exists(At("stage")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("safe/../../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("C:relative.txt")]
    [InlineData("a\\b.txt")]
    [InlineData("\\\\server\\share\\file")]
    [InlineData("x.txt:stream")]
    [InlineData("CON")]
    [InlineData("aux.txt")]
    [InlineData("LPT1.log")]
    [InlineData("COM²")]
    [InlineData("CONIN$")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("a//b")]
    [InlineData("a/./b")]
    [InlineData("a/../b")]
    [InlineData("a/ ")]
    [InlineData("a\0b")]
    public async Task Unsafe_names_are_rejected_before_any_payload_is_written(string name)
    {
        MakeZip("bad.zip", [("good.txt", "good"), (name, "bad")]);
        Directory.CreateDirectory(At("stage"));
        await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("bad.zip"), At("stage")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(At("stage")));
        Assert.False(File.Exists(At("escape.txt")));
    }

    [Theory]
    [InlineData("a.txt", "A.TXT")]
    [InlineData("a", "a/b.txt")]
    [InlineData("a/b.txt", "A")]
    [InlineData("a/", "a/")]
    [InlineData("a/", "a")]
    public async Task Duplicate_and_file_directory_conflicts_are_preflighted(string first, string second)
    {
        MakeZip("bad.zip", [(first, first.EndsWith('/') ? "" : "first"), (second, second.EndsWith('/') ? "" : "second")]);
        await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("bad.zip"), At("stage")));
        Assert.False(Directory.Exists(At("stage")));
    }

    [Fact]
    public async Task Explicit_directory_after_child_is_valid()
    {
        MakeZip("valid.zip", [("a/b.txt", "data"), ("a/", "")]);
        await ZipArchiveService.ExtractAsync(At("valid.zip"), At("stage"));
        Assert.Equal("data", await File.ReadAllTextAsync(At("stage/a/b.txt")));
    }

    [Fact]
    public async Task Existing_archive_and_nonempty_destination_are_never_overwritten()
    {
        await File.WriteAllTextAsync(At("source.txt"), "source");
        await File.WriteAllTextAsync(At("existing.zip"), "keep original");
        var create = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.CreateAsync([At("source.txt")], At("existing.zip")));
        Assert.Equal(ArchiveErrorCode.DestinationExists, create.ErrorCode);
        Assert.Equal("keep original", await File.ReadAllTextAsync(At("existing.zip")));
        MakeZip("valid.zip", [("keep.txt", "overwrite attempt")]);
        Directory.CreateDirectory(At("stage"));
        await File.WriteAllTextAsync(At("stage/keep.txt"), "keep original");
        var extract = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("valid.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.DestinationNotEmpty, extract.ErrorCode);
        Assert.Equal("keep original", await File.ReadAllTextAsync(At("stage/keep.txt")));
    }

    [Fact]
    public async Task File_appearing_after_preflight_is_not_overwritten()
    {
        MakeZip("valid.zip", [("a.txt", "archive version")]);
        var progress = new InlineProgress(_ => File.WriteAllText(At("stage/a.txt"), "external version"));
        var failure = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("valid.zip"), At("stage"), progress));
        Assert.Equal(ArchiveErrorCode.DestinationExists, failure.ErrorCode);
        Assert.Equal("external version", await File.ReadAllTextAsync(At("stage/a.txt")));
    }

    [Theory]
    [InlineData(unchecked((int)0xA1FF0000), ArchiveErrorCode.UnsafeLink)]
    [InlineData((int)FileAttributes.ReparsePoint, ArchiveErrorCode.UnsafeLink)]
    [InlineData(0x10000000, ArchiveErrorCode.UnsupportedEntry)]
    public async Task Link_and_special_entries_are_rejected(int attributes, ArchiveErrorCode code)
    {
        using (var archive = ZipFile.Open(At("link.zip"), ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("link");
            entry.ExternalAttributes = attributes;
            using var writer = new StreamWriter(entry.Open());
            writer.Write("../outside");
        }
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("link.zip"), At("stage")));
        Assert.Equal(code, error.ErrorCode);
        Assert.False(Directory.Exists(At("stage")));
    }

    [Fact]
    public async Task Real_directory_junction_is_rejected_in_sources_and_destination_ancestors()
    {
        Directory.CreateDirectory(At("actual"));
        await File.WriteAllTextAsync(At("actual/keep.txt"), "original");
        var link = At("junction");
        CreateJunction(link, At("actual"));
        try
        {
            var create = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.CreateAsync([link], At("linked.zip")));
            Assert.Equal(ArchiveErrorCode.UnsafeLink, create.ErrorCode);
            MakeZip("valid.zip", [("keep.txt", "changed")]);
            var extract = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("valid.zip"), Path.Combine(link, "stage")));
            Assert.Equal(ArchiveErrorCode.UnsafeLink, extract.ErrorCode);
            Assert.Equal("original", await File.ReadAllTextAsync(At("actual/keep.txt")));
            Assert.False(Directory.Exists(At("actual/stage")));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task Real_hard_link_source_is_rejected()
    {
        await File.WriteAllTextAsync(At("actual.txt"), "original");
        Assert.True(CreateHardLinkW(At("linked.txt"), At("actual.txt"), 0));
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.CreateAsync([At("linked.txt")], At("result.zip")));
        Assert.Equal(ArchiveErrorCode.UnsafeLink, error.ErrorCode);
        Assert.False(File.Exists(At("result.zip")));
    }

    [Fact]
    public async Task Output_within_selected_directory_is_rejected_before_creation()
    {
        var folder = Directory.CreateDirectory(At("source")).FullName;
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.CreateAsync([folder], Path.Combine(folder, "recursive.zip")));
        Assert.Equal(ArchiveErrorCode.InvalidPath, error.ErrorCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }

    [Fact]
    public async Task Corrupt_stored_payload_fails_crc_validation()
    {
        MakeZip("corrupt.zip", [("a.txt", "original")]);
        var data = await File.ReadAllBytesAsync(At("corrupt.zip"));
        var content = 30 + Read16(data, 26) + Read16(data, 28);
        data[content] ^= 0x10;
        await File.WriteAllBytesAsync(At("corrupt.zip"), data);
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("corrupt.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.InvalidArchive, error.ErrorCode);
    }

    [Fact]
    public async Task Actual_size_above_declared_length_fails_without_writing_excess_bytes()
    {
        MakeZip("oversize.zip", [("a.txt", "12345678")]);
        var data = await File.ReadAllBytesAsync(At("oversize.zip"));
        var central = FindSignature(data, 0x02014b50);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(central + 24), 3);
        await File.WriteAllBytesAsync(At("oversize.zip"), data);
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("oversize.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.InvalidArchive, error.ErrorCode);
        Assert.True(new FileInfo(At("stage/a.txt")).Length <= 3);
    }

    [Fact]
    public async Task Metadata_limit_is_enforced_before_entries_are_materialized()
    {
        MakeZip("large-metadata.zip", []);
        var data = await File.ReadAllBytesAsync(At("large-metadata.zip"));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)ZipArchiveService.MaxMetadataBytes + 1);
        await File.WriteAllBytesAsync(At("large-metadata.zip"), data);
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("large-metadata.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.MetadataTooLarge, error.ErrorCode);
        Assert.False(Directory.Exists(At("stage")));
    }

    [Fact]
    public async Task Too_many_real_entries_are_rejected_with_zip64_count()
    {
        using (var zip = ZipFile.Open(At("many.zip"), ZipArchiveMode.Create))
            for (var i = 0; i <= ZipArchiveService.MaxEntries; i++) zip.CreateEntry($"{i}.txt");
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("many.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.TooManyEntries, error.ErrorCode);
        Assert.False(Directory.Exists(At("stage")));
    }

    [Fact]
    public async Task Forged_entry_count_cannot_bypass_central_directory_validation()
    {
        MakeZip("bad-count.zip", [("a", "one"), ("b", "two")]);
        var data = await File.ReadAllBytesAsync(At("bad-count.zip"));
        var end = FindSignature(data, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(end + 8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(end + 10), 1);
        await File.WriteAllBytesAsync(At("bad-count.zip"), data);
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("bad-count.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.InvalidArchive, error.ErrorCode);
    }

    [Fact]
    public async Task Declared_total_above_64_gib_is_rejected_before_payload_creation()
    {
        using (var zip = ZipFile.Open(At("bomb.zip"), ZipArchiveMode.Create))
            for (var i = 0; i < 18; i++) zip.CreateEntry($"{i}.txt");
        var data = await File.ReadAllBytesAsync(At("bomb.zip"));
        for (var i = 0; i < data.Length - 46; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i)) == 0x02014b50)
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i + 24), uint.MaxValue - 1);
        await File.WriteAllBytesAsync(At("bomb.zip"), data);
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("bomb.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.ArchiveTooLarge, error.ErrorCode);
        Assert.False(Directory.Exists(At("stage")));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Internet_security_zone_is_preserved_without_copying_arbitrary_metadata(int zone)
    {
        MakeZip("internet.zip", [("nested/download.exe", "not executable"), ("inner.zip", "nested archive bytes")]);
        await File.WriteAllTextAsync(At("internet.zip") + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId={zone}\r\nHostUrl=https://private.example/secret\r\nReferrerUrl=private\r\n");
        await ZipArchiveService.ExtractAsync(At("internet.zip"), At("stage"));
        foreach (var name in new[] { "nested/download.exe", "inner.zip" })
            Assert.Equal($"[ZoneTransfer]\r\nZoneId={zone}\r\n", await File.ReadAllTextAsync(At("stage/" + name) + ":Zone.Identifier"));
    }

    [Fact]
    public async Task Local_archive_without_security_zone_does_not_gain_an_internet_mark()
    {
        MakeZip("local.zip", [("a.txt", "text")]);
        await ZipArchiveService.ExtractAsync(At("local.zip"), At("stage"));
        Assert.Throws<FileNotFoundException>(() => File.ReadAllText(At("stage/a.txt") + ":Zone.Identifier"));
    }

    [Theory]
    [InlineData("[ZoneTransfer]\nZoneId=3\nZoneId=0")]
    [InlineData("[ZoneTransfer]\nZoneId=not-a-zone")]
    [InlineData("[ZoneTransfer]\nHostUrl=private")]
    public async Task Invalid_security_metadata_fails_before_writing_files(string metadata)
    {
        MakeZip("internet.zip", [("a.txt", "text")]);
        await File.WriteAllTextAsync(At("internet.zip") + ":Zone.Identifier", metadata);
        var failure = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("internet.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.SecurityMetadataUnavailable, failure.ErrorCode);
        Assert.False(Directory.Exists(At("stage")));
    }

    [Fact]
    public async Task Excessive_security_metadata_is_bounded()
    {
        MakeZip("internet.zip", [("a.txt", "text")]);
        await File.WriteAllTextAsync(At("internet.zip") + ":Zone.Identifier", "[ZoneTransfer]\nZoneId=3\n" + new string('x', 16384));
        var failure = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("internet.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.SecurityMetadataUnavailable, failure.ErrorCode);
    }

    [Fact]
    public async Task Extraction_restores_file_last_write_time()
    {
        var timestamp = new DateTimeOffset(2020, 2, 3, 4, 5, 6, TimeSpan.Zero);
        using (var archive = ZipFile.Open(At("dated.zip"), ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("dated.txt");
            entry.LastWriteTime = timestamp.ToLocalTime();
            using var writer = new StreamWriter(entry.Open());
            writer.Write("timestamp");
        }
        await ZipArchiveService.ExtractAsync(At("dated.zip"), At("stage"));
        Assert.Equal(timestamp.UtcDateTime, File.GetLastWriteTimeUtc(At("stage/dated.txt")));
    }

    [Fact]
    public async Task Cancel_during_large_entry_stops_streaming_and_removes_new_archive()
    {
        var block = new byte[64 * 1024];
        new Random(43).NextBytes(block);
        using (var file = File.Create(At("large.bin"))) for (var i = 0; i < 1024; i++) file.Write(block);
        using var cancellation = new CancellationTokenSource();
        long completed = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ZipArchiveService.CreateAsync([At("large.bin")], At("cancel.zip"),
            new InlineProgress(p => { completed = p.ProcessedBytes; if (completed > 0) cancellation.Cancel(); }), cancellation.Token));
        Assert.InRange(completed, 1, 64L * 1024 * 1024 - 1);
        Assert.False(File.Exists(At("cancel.zip")));
        Assert.Equal(64L * 1024 * 1024, new FileInfo(At("large.bin")).Length);
    }

    [Fact]
    public async Task Staging_root_cannot_be_renamed_or_opened_for_reparse_writes_during_extraction()
    {
        MakeZip("valid.zip", [("a.txt", "data")]);
        var attempted = false;
        var progress = new InlineProgress(_ =>
        {
            attempted = true;
            Assert.Throws<IOException>(() => Directory.Move(At("stage"), At("renamed")));
            using var handle = CreateFileW(At("stage"), 0x40000000, FileShare.ReadWrite | FileShare.Delete, 0, 3, 0x02200000, 0);
            Assert.True(handle.IsInvalid);
        });
        await ZipArchiveService.ExtractAsync(At("valid.zip"), At("stage"), progress);
        Assert.True(attempted);
        Assert.Equal("data", await File.ReadAllTextAsync(At("stage/a.txt")));
        Assert.Single(Directory.EnumerateFileSystemEntries(At("stage")));
    }

    [Fact]
    public async Task Valid_zip64_trailers_are_supported_and_invalid_offsets_rejected()
    {
        MakeZip("zip64.zip", [("small.txt", "valid ZIP64")]);
        var original = await File.ReadAllBytesAsync(At("zip64.zip"));
        var end = FindSignature(original, 0x06054b50);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(end + 12));
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(end + 16));
        using (var stream = File.Create(At("zip64.zip")))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(original.AsSpan(0, end));
            writer.Write(0x06064b50u); writer.Write(44ul); writer.Write((ushort)45); writer.Write((ushort)45);
            writer.Write(0u); writer.Write(0u); writer.Write(1ul); writer.Write(1ul); writer.Write((ulong)size); writer.Write((ulong)offset);
            writer.Write(0x07064b50u); writer.Write(0u); writer.Write((ulong)end); writer.Write(1u);
            writer.Write(0x06054b50u); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(ushort.MaxValue); writer.Write(ushort.MaxValue); writer.Write(uint.MaxValue); writer.Write(uint.MaxValue); writer.Write((ushort)0);
        }
        await ZipArchiveService.ExtractAsync(At("zip64.zip"), At("stage"));
        Assert.Equal("valid ZIP64", await File.ReadAllTextAsync(At("stage/small.txt")));
        var data = await File.ReadAllBytesAsync(At("zip64.zip"));
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(end + 56 + 8), ulong.MaxValue);
        await File.WriteAllBytesAsync(At("zip64.zip"), data);
        var failure = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("zip64.zip"), At("second-stage")));
        Assert.Equal(ArchiveErrorCode.InvalidArchive, failure.ErrorCode);
        Assert.False(Directory.Exists(At("second-stage")));
    }

    [Fact]
    public async Task Shrunk_central_directory_cannot_hide_additional_metadata()
    {
        MakeZip("shrunk.zip", [("a", "one"), ("b", "two")]);
        var data = await File.ReadAllBytesAsync(At("shrunk.zip"));
        var end = FindSignature(data, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(end + 8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(end + 10), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(end + 12), 47);
        await File.WriteAllBytesAsync(At("shrunk.zip"), data);
        var failure = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("shrunk.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.InvalidArchive, failure.ErrorCode);
        Assert.False(Directory.Exists(At("stage")));
    }

    [Fact]
    public async Task Malformed_zip_yields_a_localizable_error()
    {
        await File.WriteAllBytesAsync(At("broken.zip"), new byte[128]);
        var failure = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("broken.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.InvalidArchive, failure.ErrorCode);
    }

    [Fact]
    public async Task Implicit_parent_directory_count_is_bounded_before_writing()
    {
        using (var archive = ZipFile.Open(At("many-parents.zip"), ZipArchiveMode.Create))
            for (var i = 0; i < 1001; i++)
                archive.CreateEntry(i + "/" + string.Join('/', Enumerable.Repeat("d", 100)) + "/file");
        var failure = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("many-parents.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.TooManyEntries, failure.ErrorCode);
        Assert.False(Directory.Exists(At("stage")));
    }

    [Fact]
    public async Task Staging_subdirectory_inserted_as_junction_is_never_followed()
    {
        MakeZip("valid.zip", [("nested/keep.txt", "overwritten")]);
        Directory.CreateDirectory(At("outside"));
        await File.WriteAllTextAsync(At("outside/keep.txt"), "original");
        var progress = new InlineProgress(_ => CreateJunction(At("stage/nested"), At("outside")));
        try
        {
            var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("valid.zip"), At("stage"), progress));
            Assert.Equal(ArchiveErrorCode.UnsafeLink, error.ErrorCode);
            Assert.Equal("original", await File.ReadAllTextAsync(At("outside/keep.txt")));
        }
        finally { if (Directory.Exists(At("stage/nested"))) Directory.Delete(At("stage/nested")); }
    }

    [Fact]
    public async Task Changed_source_after_preflight_fails_without_publishing_archive()
    {
        await File.WriteAllTextAsync(At("source.txt"), "original");
        var failure = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.CreateAsync([At("source.txt")], At("changed.zip"),
            new InlineProgress(_ => File.WriteAllText(At("source.txt"), "externally replaced source"))));
        Assert.Equal(ArchiveErrorCode.SourceChanged, failure.ErrorCode);
        Assert.False(File.Exists(At("changed.zip")));
        Assert.Equal("externally replaced source", await File.ReadAllTextAsync(At("source.txt")));
    }

    [Fact]
    public async Task Interrupted_extraction_never_produces_a_completed_entry()
    {
        using (var archive = ZipFile.Open(At("large.zip"), ZipArchiveMode.Create))
        using (var entry = archive.CreateEntry("large.bin", CompressionLevel.NoCompression).Open())
        {
            var block = new byte[64 * 1024];
            new Random(41).NextBytes(block);
            for (var i = 0; i < 1024; i++) entry.Write(block);
        }
        using var cancellation = new CancellationTokenSource();
        ArchiveProgress? observed = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ZipArchiveService.ExtractAsync(At("large.zip"), At("stage"),
            new InlineProgress(p => { if (p.ProcessedBytes > 0) { observed = p; cancellation.Cancel(); } }), cancellation.Token));
        Assert.NotNull(observed);
        Assert.Equal(0, observed.CompletedEntries);
        Assert.InRange(new FileInfo(At("stage/large.bin")).Length, 1, 64L * 1024 * 1024 - 1);
    }

    [Fact]
    public async Task Fake_end_header_in_comment_cannot_select_a_different_unchecked_directory()
    {
        MakeZip("comment.zip", [("a.txt", "data")]);
        var original = await File.ReadAllBytesAsync(At("comment.zip"));
        var end = FindSignature(original, 0x06054b50);
        var data = new byte[original.Length + 40];
        original.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(end + 20), 40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(original.Length + 2), 0x06054b50);
        await File.WriteAllBytesAsync(At("comment.zip"), data);
        var failure = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("comment.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.InvalidArchive, failure.ErrorCode);
        Assert.False(Directory.Exists(At("stage")));
    }

    [Fact]
    public async Task Compressed_data_corruption_is_reported_as_invalid_archive()
    {
        using (var archive = ZipFile.Open(At("deflated.zip"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("a.txt").Open())) writer.Write(new string('x', 10_000));
        var bytes = await File.ReadAllBytesAsync(At("deflated.zip"));
        var start = 30 + Read16(bytes, 26) + Read16(bytes, 28);
        bytes[start] = 0x07; // Reserved DEFLATE block type.
        await File.WriteAllBytesAsync(At("deflated.zip"), bytes);
        var failure = await Assert.ThrowsAsync<ArchiveOperationException>(() => ZipArchiveService.ExtractAsync(At("deflated.zip"), At("stage")));
        Assert.Equal(ArchiveErrorCode.InvalidArchive, failure.ErrorCode);
    }

    private void MakeZip(string name, (string Name, string Content)[] entries)
    {
        using var zip = ZipFile.Open(At(name), ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var item = zip.CreateEntry(entry.Name, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(item.Open());
            writer.Write(entry.Content);
        }
    }

    private static int FindSignature(byte[] data, uint signature)
    {
        for (var i = 0; i <= data.Length - 4; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i)) == signature) return i;
        throw new InvalidOperationException("Fixture signature missing.");
    }
    private static int Read16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    private static string MakeRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "filesmate-zip-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static void CreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(Path.GetFullPath(path)); start.ArgumentList.Add(Path.GetFullPath(target));
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
    public void Dispose()
    {
        var resolved = Path.GetFullPath(_root);
        var expected = Path.Combine(Path.GetTempPath(), "filesmate-zip-test-");
        if (!resolved.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected fixture cleanup path.");
        Directory.Delete(resolved, recursive: true);
    }
    private sealed class InlineProgress(Action<ArchiveProgress> action) : IProgress<ArchiveProgress> { public void Report(ArchiveProgress value) => action(value); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, nint security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path, uint access, FileShare share, nint security, uint mode, uint flags, nint template);

    [Fact]
    public void Nonempty_locked_directory_rejects_actual_mountpoint_fsctl_for_metadata_and_write_handles()
    {
        Directory.CreateDirectory(At("guarded"));
        Directory.CreateDirectory(At("outside"));
        using var marker = new FileStream(At("guarded/.owner"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        using var guard = ArchivePathGuard.LockDirectory(At("guarded"));
        foreach (var access in new uint[] { 0x80000000, 0x40000000, 0x80, 0x100 })
        {
            using var handle = CreateFileW(At("guarded"), access, FileShare.ReadWrite | FileShare.Delete, 0, 3, 0x02200000, 0);
            if (handle.IsInvalid) continue;
            var data = MountPointData(At("outside"));
            var changed = DeviceIoControl(handle, 0x000900A4, data, (uint)data.Length, 0, 0, out _, 0);
            var error = Marshal.GetLastWin32Error();
            Assert.False(changed, $"Access mask {access:X} unexpectedly changed a locked directory to a junction; error={error}.");
        }
        Assert.Equal((FileAttributes)0, File.GetAttributes(At("guarded")) & FileAttributes.ReparsePoint);
    }

    private static byte[] MountPointData(string target)
    {
        var path = Path.GetFullPath(target);
        var substitute = System.Text.Encoding.Unicode.GetBytes(@"\??\" + path);
        var print = System.Text.Encoding.Unicode.GetBytes(path);
        var bytes = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0xA0000003);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)(bytes.Length - 8));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), (ushort)(substitute.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), (ushort)print.Length);
        substitute.CopyTo(bytes, 16);
        print.CopyTo(bytes, 16 + substitute.Length + 2);
        return bytes;
    }

    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint code,
        byte[] input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);
}

[CollectionDefinition("ZIP streaming allocation", DisableParallelization = true)]
public sealed class ZipStreamingAllocationCollection;
