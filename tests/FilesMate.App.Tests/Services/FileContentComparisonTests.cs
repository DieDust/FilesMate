using System.Text;
using FilesMate.App.Services;
using FilesMate.Platform.Windows.Operations;
using FilesMate.Core.Operations;

namespace FilesMate.App.Tests.Services;

public sealed class FileContentComparisonTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate-comparison-" + Guid.NewGuid().ToString("N"));
    public FileContentComparisonTests() => Directory.CreateDirectory(_root);
    private string Write(string name, string text) { var path = Path.Combine(_root, name); File.WriteAllText(path, text); return path; }

    [Fact]
    public async Task Matching_size_and_date_are_not_treated_as_identical_content()
    {
        var a = Write("a.txt", "one"); var b = Write("b.txt", "two");
        File.SetLastWriteTimeUtc(b, File.GetLastWriteTimeUtc(a));
        var result = await FileContentComparison.ReadTextAsync(a, b);
        Assert.NotNull(result); Assert.False(result.SameBytes); Assert.False(result.IsPartial);
        Assert.Equal(1, result.Difference.AddedLines); Assert.False(await FileContentComparison.EqualBytesAsync(a, b));
        Assert.Equal("one", File.ReadAllText(a)); Assert.Equal("two", File.ReadAllText(b));
    }

    [Fact]
    public async Task Identical_visible_text_with_different_encoding_or_newlines_is_not_equal_bytes()
    {
        var a = Write("a.txt", "hello\nworld"); var b = Write("b.txt", "hello\r\nworld");
        var result = await FileContentComparison.ReadTextAsync(a, b);
        Assert.NotNull(result); Assert.False(result.SameBytes); Assert.Equal(0, result.Difference.AddedLines);
        File.WriteAllText(b, "hello\nworld", Encoding.Unicode);
        result = await FileContentComparison.ReadTextAsync(a, b);
        Assert.NotNull(result); Assert.False(result.SameBytes); Assert.Equal(0, result.Difference.AddedLines);
    }

    [Fact]
    public async Task A_change_past_the_preview_limit_cannot_be_reported_as_identical()
    {
        var prefix = string.Concat(Enumerable.Repeat("a line\n", FileContentComparison.TextByteLimit / 7 + 1));
        var a = Write("a.txt", prefix + "one"); var b = Write("b.txt", prefix + "two");
        var result = await FileContentComparison.ReadTextAsync(a, b);
        Assert.NotNull(result); Assert.True(result.IsPartial); Assert.Null(result.SameBytes);
        Assert.False(await FileContentComparison.EqualBytesAsync(a, b));
    }

    [Fact]
    public async Task Comparison_is_read_only_and_releases_handles_before_transfer()
    {
        var a = Write("a.txt", "same"); var b = Write("b.txt", "same");
        var result = await FileContentComparison.ReadTextAsync(a, b);
        Assert.True(result!.SameBytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileContentComparison.EqualBytesAsync(a, b, new(true)));
        File.Move(a, Path.Combine(_root, "moved.txt"));
        File.WriteAllText(b, "writable");
    }

    [Fact]
    public async Task Binary_and_unknown_types_use_previews_instead_of_a_fake_text_diff()
    {
        var a = Write("a.png", "PNG"); var b = Write("b.png", "PNG");
        Assert.Null(await FileContentComparison.ReadTextAsync(a, b));
        Assert.True(await FileContentComparison.EqualBytesAsync(a, b));
        a = Write("a.txt", "\0binary"); b = Write("b.txt", "\0binary");
        Assert.Null(await FileContentComparison.ReadTextAsync(a, b));
    }

    [Fact]
    public async Task Application_copy_and_move_both_ask_before_resolving_another_files_conflict()
    {
        var source = Write("a.txt", "incoming"); var folder = Path.Combine(_root, "target"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "existing"); var asked = 0;
        Task<FileConflictChoice> Resolve(FileConflict _, CancellationToken __)
        { asked++; return Task.FromResult(new FileConflictChoice(FileConflictAction.Skip)); }
        var copy = await FileShelfTransfer.RunAsync(new WindowsLocalFileOperations(), [source], folder, false, resolveConflict: Resolve);
        Assert.Empty(copy.Errors); Assert.Equal(1, asked); Assert.Empty(copy.Completed); Assert.Equal(1, copy.Skipped);
        Assert.False(File.Exists(Path.Combine(folder, "a (2).txt")));
        var move = await FileShelfTransfer.RunAsync(new WindowsLocalFileOperations(), [source], folder, true, resolveConflict: Resolve);
        Assert.Empty(move.Errors); Assert.Equal(2, asked); Assert.Equal(1, move.Skipped); Assert.True(File.Exists(source));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
