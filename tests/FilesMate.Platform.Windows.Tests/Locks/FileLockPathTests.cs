using FilesMate.Platform.Windows.Locks;

namespace FilesMate.Platform.Windows.Tests.Locks;

public sealed class FileLockPathTests
{
    [Fact]
    public void Normalize_strips_extended_prefixes_and_trailing_slashes()
    {
        Assert.Equal(@"C:\Work\file.txt", FileLockPath.Normalize(@"\\?\C:\Work\file.txt"));
        Assert.Equal(@"\\server\share\a", FileLockPath.Normalize(@"\\?\UNC\server\share\a\"));
        Assert.Equal(@"E:", FileLockPath.Normalize(@"E:\"));
        Assert.Equal(@"D:\Projects", FileLockPath.Normalize(@"d:\Projects\"));
    }

    [Fact]
    public void File_match_is_exact_after_normalize()
    {
        Assert.True(FileLockPath.Matches(@"\\?\C:\a\b.txt", @"C:\a\b.txt", directory: false));
        Assert.False(FileLockPath.Matches(@"C:\a\b.txt", @"C:\a", directory: false));
        Assert.False(FileLockPath.Matches(@"C:\a\b.txt.bak", @"C:\a\b.txt", directory: false));
    }

    [Fact]
    public void Directory_match_includes_the_folder_and_its_children()
    {
        Assert.True(FileLockPath.Matches(@"E:\", @"E:\", directory: true));
        Assert.True(FileLockPath.Matches(@"E:\payload.zip", @"E:\", directory: true));
        Assert.True(FileLockPath.Matches(@"C:\Work\src\a.cs", @"C:\Work", directory: true));
        Assert.False(FileLockPath.Matches(@"C:\Worked\a.cs", @"C:\Work", directory: true));
        Assert.False(FileLockPath.Matches(@"D:\payload.zip", @"E:\", directory: true));
    }
}
