using System.IO.Compression;
using FilesMate.App.Services;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.App.Tests.Services;

public sealed class ExternalArchiveDropConflictTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate-external-archive-" + Guid.NewGuid().ToString("N"));
    private readonly WindowsLocalFileOperations _operations = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extracted_file_copy_asks_and_replaces_only_after_the_choice(bool controlCopy)
    {
        var content = Write("archive-source/settings.ini", "new archive settings");
        var archive = Path.Combine(_root, "update.zip");
        ZipFile.CreateFromDirectory(Path.GetDirectoryName(content)!, archive);
        var extracted = Path.Combine(_root, "decompressor-temp");
        ZipFile.ExtractToDirectory(archive, extracted);
        var target = Write("destination/settings.ini", "old settings");
        var source = Path.Combine(extracted, "settings.ini");
        var asked = 0;
        var result = await FileShelfTransfer.RunAsync(_operations, [source], Path.GetDirectoryName(target)!, false,
            allowSameDirectoryCopy: controlCopy, resolveConflict: (conflict, _) =>
            {
                asked++; Assert.False(conflict.IsMove); Assert.True(conflict.CanReplace);
                Assert.Equal(source, conflict.SourcePreviewPath); Assert.Equal(target, conflict.Destination);
                Assert.Equal("old settings", File.ReadAllText(target));
                return Task.FromResult(new FileConflictChoice(FileConflictAction.Replace));
            });
        try
        {
            Assert.Equal(1, asked); Assert.Empty(result.Errors); Assert.False(result.Cancelled);
            Assert.Equal(target, Assert.Single(result.Completed).Destination);
            Assert.Equal("new archive settings", File.ReadAllText(target));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(target)!, "settings (2).ini")));
            Assert.True(File.Exists(archive)); Assert.True(File.Exists(source));
            Assert.NotNull(result.Undo); Assert.Single(result.Undo.Replacements);
        }
        finally { if (result.Undo is { } undo) foreach (var backup in undo.Replacements) backup.Dispose(); }
    }

    [Theory]
    [InlineData(FileConflictAction.Skip)]
    [InlineData(FileConflictAction.Cancel)]
    [InlineData(FileConflictAction.KeepBoth)]
    public async Task Ordinary_copy_obeys_the_users_conflict_choice(FileConflictAction action)
    {
        var source = Write("incoming/a.txt", "incoming");
        var target = Write("destination/a.txt", "existing");
        var asked = 0;
        var result = await FileShelfTransfer.RunAsync(_operations, [source], Path.GetDirectoryName(target)!, false,
            resolveConflict: (_, _) => { asked++; return Task.FromResult(new FileConflictChoice(action)); });
        Assert.Equal(1, asked); Assert.Empty(result.Errors);
        Assert.Equal("existing", File.ReadAllText(target)); Assert.Equal("incoming", File.ReadAllText(source));
        Assert.Equal(action == FileConflictAction.KeepBoth, File.Exists(Path.Combine(Path.GetDirectoryName(target)!, "a (2).txt")));
        Assert.Equal(action == FileConflictAction.Cancel, result.Cancelled);
    }

    [Fact]
    public async Task Same_directory_duplication_does_not_suppress_other_conflicts_in_the_batch()
    {
        var local = Write("destination/local.txt", "local");
        var incoming = Write("incoming/a.txt", "new");
        var target = Write("destination/a.txt", "old");
        var asked = 0;
        var result = await FileShelfTransfer.RunAsync(_operations, [local, incoming], Path.GetDirectoryName(target)!, false,
            allowSameDirectoryCopy: true, resolveConflict: (conflict, _) =>
            {
                asked++; Assert.Equal(incoming, conflict.Source); return Task.FromResult(new FileConflictChoice(FileConflictAction.Skip));
            });
        Assert.Equal(1, asked); Assert.Empty(result.Errors); Assert.Equal(1, result.Skipped);
        Assert.Equal("local (2).txt", Path.GetFileName(Assert.Single(result.Completed).Destination));
        Assert.Equal("local", File.ReadAllText(local)); Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public async Task Copied_folder_can_merge_without_creating_a_numbered_sibling()
    {
        var source = Write("incoming/Folder/new.txt", "new");
        var target = Write("destination/Folder/keep.txt", "keep");
        var asked = 0;
        var result = await FileShelfTransfer.RunAsync(_operations, [Path.GetDirectoryName(source)!], Path.Combine(_root, "destination"), false,
            resolveConflict: (conflict, _) =>
            {
                asked++; Assert.True(conflict.CanMerge); return Task.FromResult(new FileConflictChoice(FileConflictAction.Merge));
            });
        Assert.Equal(1, asked); Assert.Empty(result.Errors);
        Assert.Equal("keep", File.ReadAllText(target));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_root, "destination/Folder/new.txt")));
        Assert.False(Directory.Exists(Path.Combine(_root, "destination/Folder (2)")));
    }

    private string Write(string relative, string text)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text); return path;
    }
    public void Dispose() => Directory.Delete(_root, true);
}
