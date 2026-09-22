using System.Text;

using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class RecycleBinRestoreTests
{
    [Fact]
    public void ReadsLegacyFixedPathAndRejectsOversizedRecord()
    {
        var path = Path.Combine(Path.GetTempPath(), "FilesMate-info-" + Guid.NewGuid().ToString("N"));
        try
        {
            var original = @"C:\archive\old.txt";
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(1L); writer.Write(0L); writer.Write(DateTime.UtcNow.ToFileTimeUtc());
                writer.Write(Encoding.Unicode.GetBytes(original.PadRight(260, '\0')));
            }
            Assert.True(RecycleBinRestore.TryReadOriginalPath(path, out var restored, out _));
            Assert.Equal(original, restored);
            using (var stream = File.OpenWrite(path)) stream.SetLength(1_000_000);
            Assert.False(RecycleBinRestore.TryReadOriginalPath(path, out _, out _));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(3, 4, "C:\\x\0")]
    [InlineData(2, int.MaxValue, "C:\\x\0")]
    [InlineData(2, 2, "C:\\x\0")]
    [InlineData(2, 5, "C:\\x")]
    [InlineData(2, 4, "x\0y\0")]
    [InlineData(2, 4, "abc\0")]
    public void RejectsMalformedInfoWithoutInventingARestorePath(long version, int count, string payload)
    {
        var path = Path.Combine(Path.GetTempPath(), "FilesMate-info-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(version); writer.Write(0L); writer.Write(DateTime.UtcNow.ToFileTimeUtc());
                writer.Write(count); writer.Write(Encoding.Unicode.GetBytes(payload));
            }
            Assert.False(RecycleBinRestore.TryReadOriginalPath(path, out var restored, out _));
            Assert.Empty(restored);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reads_windows10_info_files()
    {
        var path = Path.Combine(Path.GetTempPath(), "FilesMateUndo", Guid.NewGuid().ToString("N") + ".bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var original = @"C:\Users\demo\Documents\notes.txt";
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write((long)2);
                writer.Write((long)12);
                writer.Write(DateTime.UtcNow.ToFileTimeUtc());
                var chars = original + '\0';
                writer.Write(chars.Length);
                writer.Write(Encoding.Unicode.GetBytes(chars));
            }

            Assert.True(RecycleBinRestore.TryReadOriginalPath(path, out var restored, out _));
            Assert.Equal(Path.GetFullPath(original), restored);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Recycle_then_restore_puts_the_file_back()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMateUndo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ops = new WindowsLocalFileOperations();
        var file = Path.Combine(root, "keep.txt");
        try
        {
            File.WriteAllText(file, "undo-me");
            var completed = new List<FilesMate.Core.Operations.RecycleItemResult>();
            ops.Recycle([file], completed.Add);
            var result = Assert.Single(completed);
            Assert.Equal(file, result.OriginalPath); Assert.True(result.IsRecycled); Assert.NotNull(result.Receipt);
            Assert.False(File.Exists(file));

            ops.RestoreRecycledItems(completed);
            Assert.True(File.Exists(file));
            Assert.Equal("undo-me", File.ReadAllText(file));
        }
        finally
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }

                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception)
            {
                // ponytail: leftover temp files are less important than the assertion above.
            }
        }
    }
}
