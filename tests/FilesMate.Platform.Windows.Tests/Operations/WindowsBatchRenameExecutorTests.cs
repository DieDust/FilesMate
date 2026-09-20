using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class WindowsBatchRenameExecutorTests
{
    [Fact]
    public async Task Two_phase_execution_supports_a_b_swaps()
    {
        var operations = new FakeOperations();
        var root = @"C:\Temp";
        var a = root + "\\a.txt";
        var b = root + "\\b.txt";
        operations.Paths.Add(a);
        operations.Paths.Add(b);
        var plan = new BatchRenamePlan(
        [
            new BatchRenameEntry(a, b),
            new BatchRenameEntry(b, a),
        ]);

        var result = await new WindowsBatchRenameExecutor(operations).ExecuteAsync(plan);

        Assert.Empty(result.Errors);
        Assert.Contains(a, operations.Paths);
        Assert.Contains(b, operations.Paths);
        Assert.DoesNotContain(operations.Paths, path => path.Contains("filesmate-rename", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_swap_restores_both_original_names()
    {
        var operations = new FakeOperations { FailOnCall = 4 };
        var a = @"C:\Temp\a.txt";
        var b = @"C:\Temp\b.txt";
        operations.Paths.UnionWith([a, b]);
        var result = await new WindowsBatchRenameExecutor(operations).ExecuteAsync(new BatchRenamePlan(
            [new BatchRenameEntry(a, b), new BatchRenameEntry(b, a)]));
        Assert.Single(result.Errors);
        Assert.Empty(result.CompletedSources);
        Assert.Equal(2, operations.Paths.Count);
        Assert.Contains(a, operations.Paths);
        Assert.Contains(b, operations.Paths);
    }

    [Fact]
    public async Task Case_only_rename_is_performed_using_temporary_name()
    {
        var operations = new FakeOperations();
        var a = @"C:\Temp\Report.TXT";
        var b = @"C:\Temp\report.TXT";
        operations.Paths.Add(a);
        var result = await new WindowsBatchRenameExecutor(operations).ExecuteAsync(new BatchRenamePlan([new BatchRenameEntry(a, b)]));
        Assert.Empty(result.Errors);
        Assert.Equal(b, Assert.Single(operations.Paths));
        Assert.Equal(2, operations.Calls);
    }

    private sealed class FakeOperations : ILocalFileOperations
    {
        public int FailOnCall { get; init; }
        public int Calls { get; private set; }
        public HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Rename(string source, string destinationPath)
        {
            if (++Calls == FailOnCall) throw new IOException("Injected move failure");
            Assert.True(Paths.Remove(source), $"Missing source {source}");
            Assert.DoesNotContain(destinationPath, Paths);
            Paths.Add(destinationPath);
        }

        public void CreateDirectory(string path, bool failIfExists = false) => throw new NotSupportedException();
        public void CreateEmptyFile(string path) => throw new NotSupportedException();
        public IReadOnlyList<string> Copy(IReadOnlyList<string> sources, string destinationDirectory) => throw new NotSupportedException();
        public IReadOnlyList<string> Move(IReadOnlyList<string> sources, string destinationDirectory) => throw new NotSupportedException();
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null) => throw new NotSupportedException();
        public void RestoreRecycled(IReadOnlyList<string> originalPaths) => throw new NotSupportedException();
        public void PermanentDelete(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void ShowProperties(string path) => throw new NotSupportedException();
    }
}
