using System.IO;
using System.Reflection;

using FilesMate.Core.Caching;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;
using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Contracts;

public sealed class CoreContractTests
{
    [Fact]
    public void FileEntryCore_equality_uses_name_not_a_full_path()
    {
        Assert.Null(typeof(FileEntryCore).GetProperty("FullPath", BindingFlags.Public | BindingFlags.Instance));

        var left = new FileEntryCore(
            Id: 1,
            Name: "readme.txt",
            Size: 12,
            ModifiedUtcTicks: 100,
            CreatedUtcTicks: 50,
            Attributes: FileAttributes.Archive,
            Kind: EntryKind.File);

        var right = new FileEntryCore(
            Id: 1,
            Name: "readme.txt",
            Size: 12,
            ModifiedUtcTicks: 100,
            CreatedUtcTicks: 50,
            Attributes: FileAttributes.Archive,
            Kind: EntryKind.File);

        Assert.Equal(left, right);
        Assert.Equal("readme.txt", left.Name);
        Assert.DoesNotContain('\\', left.Name);
        Assert.DoesNotContain('/', left.Name);
    }

    [Fact]
    public void DirectoryBatch_rejects_mixed_pane_generation_or_path_identity()
    {
        var pane = PaneId.New();
        var first = DirectoryBatch.Create(
            pane,
            generation: 2,
            directoryPath: @"D:\data",
            entries: [SampleFile(1, "a.txt")],
            isFinal: false,
            error: null);

        var otherPane = DirectoryBatch.Create(
            PaneId.New(),
            generation: 2,
            directoryPath: @"D:\data",
            entries: [SampleFile(2, "b.txt")],
            isFinal: false,
            error: null);

        var otherGeneration = DirectoryBatch.Create(
            pane,
            generation: 3,
            directoryPath: @"D:\data",
            entries: [SampleFile(2, "b.txt")],
            isFinal: false,
            error: null);

        var otherPath = DirectoryBatch.Create(
            pane,
            generation: 2,
            directoryPath: @"D:\other",
            entries: [SampleFile(2, "b.txt")],
            isFinal: false,
            error: null);

        Assert.Throws<InvalidOperationException>(() => first.Concat(otherPane));
        Assert.Throws<InvalidOperationException>(() => first.Concat(otherGeneration));
        Assert.Throws<InvalidOperationException>(() => first.Concat(otherPath));

        var merged = first.Concat(DirectoryBatch.Create(
            pane,
            generation: 2,
            directoryPath: @"D:\data",
            entries: [SampleFile(2, "b.txt")],
            isFinal: true,
            error: null));

        Assert.Equal(2, merged.Entries.Count);
        Assert.True(merged.IsFinal);
        Assert.Equal("a.txt", merged.Entries[0].Name);
        Assert.Equal("b.txt", merged.Entries[1].Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000)]
    public void DirectoryReadOptions_rejects_invalid_batch_size(int batchSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DirectoryReadOptions.Default with { BatchSize = batchSize });
    }

    [Fact]
    public void DirectoryRequest_rejects_empty_or_whitespace_paths()
    {
        var options = DirectoryReadOptions.Default;
        Assert.Throws<ArgumentException>(() => new DirectoryRequest(PaneId.New(), 1, "", options));
        Assert.Throws<ArgumentException>(() => new DirectoryRequest(PaneId.New(), 1, "   ", options));
        Assert.Throws<ArgumentException>(() => DirectoryBatch.Create(PaneId.New(), 1, "", [], true, null));
        Assert.Throws<ArgumentException>(() => NavigationTarget.FromPath(""));
    }

    [Fact]
    public void FileOperationRequest_preserves_source_order_and_normalizes_destination()
    {
        var sources = new List<string> { @"D:\a\one.txt", @"D:\a\two.txt", @"D:\a\three.txt" };
        var request = FileOperationRequest.Create(
            Guid.NewGuid(),
            FileOperationKind.Copy,
            sources,
            @"D:\dest\");

        sources[0] = @"D:\mutated.txt";
        sources.Add(@"D:\extra.txt");

        Assert.Equal(
            [@"D:\a\one.txt", @"D:\a\two.txt", @"D:\a\three.txt"],
            request.Sources);
        Assert.Equal(@"D:\dest", request.Destination);
    }

    [Fact]
    public void CacheBudget_rejects_negative_weights_and_exposes_bytes()
    {
        var budget = new CacheBudget(maxBytes: 1024);
        Assert.Equal(0, budget.CurrentBytes);
        Assert.Equal(1024, budget.MaxBytes);

        budget.Add(128);
        Assert.Equal(128, budget.CurrentBytes);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CacheBudget(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Add(-8));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.TryReserve(-1));
    }

    private static FileEntryCore SampleFile(int id, string name) => new(
        id,
        name,
        Size: 1,
        ModifiedUtcTicks: 0,
        CreatedUtcTicks: 0,
        Attributes: FileAttributes.Normal,
        Kind: EntryKind.File);
}
