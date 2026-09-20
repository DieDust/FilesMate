using FilesMate.Core.Directories;
using FilesMate.Platform.Windows.Directories;

namespace FilesMate.Platform.Windows.Tests.Directories;

public sealed class LiveDirectoryEntryTests
{
    [Fact]
    public void TryRead_returns_a_file_snapshot()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-live-entry-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "pkg.exe"), "ok");
            Assert.True(LiveDirectoryEntry.TryRead(
                root.FullName,
                "pkg.exe",
                DirectoryReadOptions.Default,
                out var entry));
            Assert.Equal("pkg.exe", entry.Name);
            Assert.True(entry.Size > 0);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void TryRead_skips_hidden_when_options_exclude_them()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-live-hidden-");
        try
        {
            var path = Path.Combine(root.FullName, "secret.txt");
            File.WriteAllText(path, "ok");
            File.SetAttributes(path, FileAttributes.Hidden);
            Assert.False(LiveDirectoryEntry.TryRead(
                root.FullName,
                "secret.txt",
                DirectoryReadOptions.Default with { IncludeHidden = false },
                out _));
        }
        finally
        {
            root.Delete(true);
        }
    }
}
