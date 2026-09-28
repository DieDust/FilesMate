using System.IO.Compression;
using System.Formats.Tar;
using FilesMate.Platform.Windows.Archives;

namespace FilesMate.Platform.Windows.Tests.Archives;

public sealed class ManagedArchiveServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "FilesMate-managed-tests-" + Guid.NewGuid().ToString("N"))).FullName;
    private string At(string name) => Path.Combine(_root, name);
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Archives", "Fixtures", name);

    [Theory]
    [InlineData("7Zip.LZMA2.Aes.7z", "testpassword")]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Zip.deflate.WinzipAES.zip", "test")]
    [InlineData("Zip.bzip2.pkware.zip", "test")]
    public async Task Encrypted_archives_request_password_then_validate_it(string filename, string password)
    {
        var set = Assert.Single(ArchiveSetResolver.Resolve([Fixture(filename)]));
        var missing = await Assert.ThrowsAsync<ArchiveOperationException>(() => ManagedArchiveService.ExtractAsync(set, At("missing")));
        Assert.Equal(ArchiveErrorCode.PasswordRequired, missing.ErrorCode);
        var wrong = await Assert.ThrowsAsync<ArchiveOperationException>(() => ManagedArchiveService.ExtractAsync(set, At("wrong"), "incorrect"));
        Assert.Equal(ArchiveErrorCode.PasswordRequired, wrong.ErrorCode);
        await ManagedArchiveService.ExtractAsync(set, At("correct"), password);
        Assert.NotEmpty(Directory.GetFiles(At("correct"), "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("Rar5.solid.rar")]
    [InlineData("Infozip.nocomp.multi.zip")]
    public async Task Solid_rar_and_legacy_split_zip_extract_real_fixtures(string filename)
    {
        var set = Assert.Single(ArchiveSetResolver.Resolve([Fixture(filename)]));
        await ManagedArchiveService.ExtractAsync(set, At("output"));
        Assert.Equal(3, Directory.GetFiles(At("output"), "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Renamed_rar5_volumes_resolve_from_any_part_and_extract_once()
    {
        var paths = Enumerable.Range(1, 6).Select(number => At($"分卷.part{number:D2}.rar删删删")).ToArray();
        for (var i = 0; i < paths.Length; i++) File.Copy(Fixture($"Rar5.multi.part{i + 1:D2}.rar"), paths[i]);
        var set = Assert.Single(ArchiveSetResolver.Resolve(paths.Reverse().ToArray()));
        Assert.Equal(6, set.Volumes.Count);
        await ManagedArchiveService.ExtractAsync(set, At("rar-output"));
        Assert.Equal(3, Directory.GetFiles(At("rar-output"), "*", SearchOption.AllDirectories).Length);
        Assert.All(paths, path => Assert.True(File.Exists(path)));
    }

    [Theory]
    [InlineData(".zip删删删")]
    [InlineData(".7z")]
    public async Task Round_trip_preserves_unicode_empty_directories_and_payload(string suffix)
    {
        var source = Directory.CreateDirectory(At("中文 空格")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        await File.WriteAllTextAsync(Path.Combine(source, "hello.txt"), "Hello 世界");
        if (suffix == ".7z") await ManagedArchiveService.Create7zAsync([source], At("test" + suffix));
        else await ZipArchiveService.CreateAsync([source], At("test" + suffix));
        var set = Assert.Single(ArchiveSetResolver.Resolve([At("test" + suffix)]));
        await ManagedArchiveService.ExtractAsync(set, At("output"));
        Assert.Equal("Hello 世界", await File.ReadAllTextAsync(At("output/中文 空格/hello.txt")));
        Assert.True(Directory.Exists(At("output/中文 空格/empty")));
    }

    [Fact]
    public async Task Numeric_split_selection_is_deduplicated_and_does_not_rename_sources()
    {
        await File.WriteAllTextAsync(At("hello.txt"), "split payload");
        await ManagedArchiveService.Create7zAsync([At("hello.txt")], At("archive.7z"));
        var bytes = await File.ReadAllBytesAsync(At("archive.7z"));
        var first = At("archive.7z.001删删删");
        var second = At("archive.7z.002删删删");
        await File.WriteAllBytesAsync(first, bytes[..(bytes.Length / 2)]);
        await File.WriteAllBytesAsync(second, bytes[(bytes.Length / 2)..]);
        var set = Assert.Single(ArchiveSetResolver.Resolve([second, first]));
        Assert.Equal(first, set.PrimaryPath);
        Assert.True(set.HasDecoratedNames);
        await ManagedArchiveService.ExtractAsync(set, At("output"));
        Assert.Equal("split payload", await File.ReadAllTextAsync(At("output/hello.txt")));
        Assert.True(File.Exists(second));
        Assert.False(File.Exists(At("archive.7z.001")));
    }

    [Fact]
    public async Task Tar_gzip_preserves_relative_paths_and_rejects_symbolic_links()
    {
        using (var file = File.Create(At("test.tar.gz")))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./"));
            using var data = new MemoryStream("tar payload"u8.ToArray());
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./folder/hello.txt") { DataStream = data });
        }
        await ManagedArchiveService.ExtractAsync(Assert.Single(ArchiveSetResolver.Resolve([At("test.tar.gz")])), At("tar-output"));
        Assert.Equal("tar payload", File.ReadAllText(At("tar-output/folder/hello.txt")));
        using (var file = File.Create(At("link.tar")))
        using (var tar = new TarWriter(file))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "../outside.txt" });
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ManagedArchiveService.ExtractAsync(
            Assert.Single(ArchiveSetResolver.Resolve([At("link.tar")])), At("link-output")));
        Assert.Equal(ArchiveErrorCode.UnsafeLink, error.ErrorCode);
    }

    [Fact]
    public async Task Corrupted_payload_does_not_pass_checksum_validation()
    {
        using (var zip = ZipFile.Open(At("bad-crc.zip"), ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("hello.txt", CompressionLevel.NoCompression).Open()) entry.Write("payload"u8);
        var bytes = File.ReadAllBytes(At("bad-crc.zip"));
        var dataOffset = 30 + System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26))
            + System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28));
        bytes[dataOffset] ^= 0x08;
        File.WriteAllBytes(At("bad-crc.zip"), bytes);
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ManagedArchiveService.ExtractAsync(
            Assert.Single(ArchiveSetResolver.Resolve([At("bad-crc.zip")])), At("output")));
        Assert.Equal(ArchiveErrorCode.InvalidArchive, error.ErrorCode);
    }

    [Fact]
    public void Standalone_original_and_decorated_zip_are_independent_archives()
    {
        File.WriteAllText(At("test.zip"), "first");
        File.WriteAllText(At("test.zip删删删"), "second");
        var sets = ArchiveSetResolver.Resolve([At("test.zip"), At("test.zip删删删")]);
        Assert.Equal(2, sets.Count);
        Assert.All(sets, set => Assert.Single(set.Volumes));
    }

    [Fact]
    public void Missing_volume_and_ambiguous_decorated_alias_are_rejected()
    {
        File.WriteAllText(At("test.7z.002删"), "not needed");
        Assert.Equal(ArchiveErrorCode.MissingVolume, Assert.Throws<ArchiveOperationException>(() =>
            ArchiveSetResolver.Resolve([At("test.7z.002删")])).ErrorCode);
        File.WriteAllText(At("test.7z.001删"), "not needed");
        File.WriteAllText(At("test.7z.001"), "not needed");
        Assert.Equal(ArchiveErrorCode.InvalidArchive, Assert.Throws<ArchiveOperationException>(() =>
            ArchiveSetResolver.Resolve([At("test.7z.002删")])).ErrorCode);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("C:/escape.txt")]
    [InlineData("dir/../../escape.txt")]
    [InlineData("file.txt:stream")]
    public async Task Unsafe_names_never_escape_private_staging(string entryName)
    {
        using (var zip = ZipFile.Open(At("bad.zip"), ZipArchiveMode.Create))
        using (var entry = new StreamWriter(zip.CreateEntry(entryName).Open())) entry.Write("bad");
        var error = await Assert.ThrowsAsync<ArchiveOperationException>(() => ManagedArchiveService.ExtractAsync(
            Assert.Single(ArchiveSetResolver.Resolve([At("bad.zip")])), At("output")));
        Assert.Equal(ArchiveErrorCode.InvalidPath, error.ErrorCode);
        Assert.False(File.Exists(At("escape.txt")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
