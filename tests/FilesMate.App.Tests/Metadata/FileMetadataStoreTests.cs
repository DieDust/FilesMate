using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Core.Metadata;

namespace FilesMate.App.Tests.Metadata;

public sealed class FileMetadataStoreTests
{
    [Fact]
    public async Task Tags_are_created_sorted_and_case_insensitively_unique()
    {
        using var fixture = new StoreFixture();
        var work = await fixture.Store.CreateTagAsync("Work", "#2F80ED");
        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.CreateTagAsync("work", "#FF0000"));

        Assert.Contains("already exists", duplicate.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(work, Assert.Single(await fixture.Store.ListTagsAsync()));
    }

    [Fact]
    public async Task Tags_can_be_renamed_recolored_and_deleted()
    {
        using var fixture = new StoreFixture();
        var created = await fixture.Store.CreateTagAsync("Work", "#2F80ED");

        var updated = await fixture.Store.UpdateTagAsync(created.Id, "Focus", "#FF9F0A");

        Assert.Equal("Focus", updated.Name);
        Assert.Equal("#FF9F0A", updated.Color);
        Assert.Equal(updated, Assert.Single(await fixture.Store.ListTagsAsync()));

        await fixture.Store.DeleteTagAsync(updated.Id);
        Assert.Empty(await fixture.Store.ListTagsAsync());
    }

    [Fact]
    public async Task Tags_can_be_reordered()
    {
        using var fixture = new StoreFixture();
        var first = await fixture.Store.CreateTagAsync("Alpha", "#2F80ED");
        var second = await fixture.Store.CreateTagAsync("Beta", "#EB5757");

        await fixture.Store.ReorderTagsAsync([second.Id, first.Id]);

        Assert.Equal(["Beta", "Alpha"], (await fixture.Store.ListTagsAsync()).Select(tag => tag.Name));
    }

    [Fact]
    public async Task File_identity_and_assignments_round_trip_in_one_transaction()
    {
        using var fixture = new StoreFixture();
        var urgent = await fixture.Store.CreateTagAsync("Urgent", "#FF453A");
        var later = await fixture.Store.CreateTagAsync("Later", "#8E8E93");
        var identity = FileIdentity.FromStable(4, 55, @"C:\Work\todo.txt");

        await fixture.Store.UpsertFileIdentityAsync(identity);
        await fixture.Store.SetTagsAsync(identity, [urgent.Id, later.Id]);

        var tags = await fixture.Store.GetTagsAsync(identity);
        Assert.Equal([urgent, later], tags);
        Assert.Equal([@"C:\Work\todo.txt"], await fixture.Store.ListPathsForTagAsync(urgent.Id));

        var moved = identity with { NormalizedPath = @"C:\Archive\todo.txt" };
        await fixture.Store.UpsertFileIdentityAsync(moved);
        Assert.Equal([urgent, later], await fixture.Store.GetTagsAsync(moved));
        Assert.Equal(@"C:\Archive\todo.txt", (await fixture.Store.GetIdentityAsync(identity.StableKey))!.Value.NormalizedPath);
        Assert.Equal([@"C:\Archive\todo.txt"], await fixture.Store.ListPathsForTagAsync(urgent.Id));
    }

    [Fact]
    public async Task Path_fallback_identity_survives_restart_and_can_clear_assignments()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "metadata.db");
        var identity = FileIdentity.FromNormalizedPath(@"C:\Network\Share\Report.docx");
        try
        {
            await using (var first = new SqliteFileMetadataStore(path))
            {
                var tag = await first.CreateTagAsync("Review", "#FF9F0A");
                await first.SetTagsAsync(identity, [tag.Id]);
            }

            await using var second = new SqliteFileMetadataStore(path);
            Assert.Equal([@"path:c:\network\share\report.docx"], [identity.StableKey]);
            Assert.Single(await second.GetTagsAsync(identity));
            await second.SetTagsAsync(identity, []);
            Assert.Empty(await second.GetTagsAsync(identity));
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public async Task Corrupt_database_is_preserved_and_recreated_with_wal()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "metadata.db");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "not a sqlite database");
        try
        {
            await using var store = new SqliteFileMetadataStore(path);
            var tag = await store.CreateTagAsync("Recovered", "#30D158");
            Assert.Equal("Recovered", tag.Name);
            Assert.True(Directory.EnumerateFiles(directory, "metadata.db.corrupt-*").Any());
            Assert.Equal("wal", await store.GetJournalModeAsync());
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private sealed class StoreFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public StoreFixture()
        {
            Directory.CreateDirectory(_directory);
            Store = new SqliteFileMetadataStore(Path.Combine(_directory, "metadata.db"));
        }

        public SqliteFileMetadataStore Store { get; }

        public void Dispose()
        {
            Store.DisposeAsync().AsTask().GetAwaiter().GetResult();
            TryDeleteDirectory(_directory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
