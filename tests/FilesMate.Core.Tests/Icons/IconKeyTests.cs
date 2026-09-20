using FilesMate.Core.Entries;
using FilesMate.Core.Icons;

namespace FilesMate.Core.Tests.Icons;

public sealed class IconKeyTests
{
    [Fact]
    public void Directories_share_one_key_per_size()
    {
        var a = IconKey.From(Dir("Documents"), null, 32);
        var b = IconKey.From(Dir("Pictures"), @"C:\Users\a\Pictures", 32);
        Assert.Equal(a, b);
        Assert.NotEqual(a, IconKey.From(Dir("Documents"), null, 16));
    }

    [Fact]
    public void Ordinary_files_key_by_normalized_extension()
    {
        var txt = IconKey.From(File("Readme.TXT"), @"D:\Readme.TXT", 16);
        var other = IconKey.From(File("notes.txt"), @"D:\notes.txt", 16);
        Assert.Equal(txt, other);
        Assert.NotEqual(txt, IconKey.From(File("notes.pdf"), @"D:\notes.pdf", 16));
    }

    [Fact]
    public void Executables_and_shortcuts_key_by_path()
    {
        var left = IconKey.From(File("app.exe"), @"C:\Tools\App.EXE", 32);
        var right = IconKey.From(File("app.exe"), @"C:\Other\app.exe", 32);
        Assert.NotEqual(left, right);
        Assert.Equal(left, IconKey.From(File("app.exe"), @"c:\tools\app.exe", 32));
    }

    private static FileEntryCore File(string name) =>
        new(1, name, 1, 1, 1, FileAttributes.Normal, EntryKind.File);

    private static FileEntryCore Dir(string name) =>
        new(1, name, 0, 1, 1, FileAttributes.Directory, EntryKind.Directory);
}
