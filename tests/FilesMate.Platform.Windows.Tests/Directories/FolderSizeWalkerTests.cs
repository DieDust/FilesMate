using FilesMate.Platform.Windows.Directories;

namespace FilesMate.Platform.Windows.Tests.Directories;

public sealed class FolderSizeWalkerTests
{
    [Fact]
    public void Measure_sums_nested_files_and_skips_empty_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), "filesmate-folder-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[100]);
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(Path.Combine(nested, "b.bin"), new byte[40]);
            Directory.CreateDirectory(Path.Combine(root, "empty"));

            Assert.Equal(140UL, FolderSizeWalker.Measure(root, CancellationToken.None));
            Assert.Equal(0UL, FolderSizeWalker.Measure(Path.Combine(root, "empty"), CancellationToken.None));
            Assert.Throws<OperationCanceledException>(() =>
                FolderSizeWalker.Measure(root, new CancellationToken(canceled: true)));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
