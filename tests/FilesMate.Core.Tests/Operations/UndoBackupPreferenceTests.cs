using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class UndoBackupPreferenceTests
{
    private static string Fixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "filesmate-undo-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task DisabledPreferenceSurvivesReloadAndDefaultRemainsEnabled()
    {
        var file = Path.Combine(Fixture(), "explorer.json");
        var first = new ExplorerPreferencesService(file);
        Assert.True(first.Load().EnableUndoBackups);
        await first.SaveAsync(ExplorerPreferences.Default with { EnableUndoBackups = false });
        Assert.False(new ExplorerPreferencesService(file).Load().EnableUndoBackups);
        await first.SaveAsync(ExplorerPreferences.Default with { EnableUndoBackups = true });
        Assert.True(new ExplorerPreferencesService(file).Load().EnableUndoBackups);
    }

    [Fact]
    public void DisablingBudgetDoesNotReleaseExistingVersionsOrCreateAnotherDirectory()
    {
        var root = Fixture(); var budget = new ReplacementBackupBudget();
        var old = Path.Combine(root, ".filesmate-history-" + Guid.NewGuid().ToString("N"));
        using var lease = budget.Reserve(old, 4, () => Directory.CreateDirectory(old));
        File.WriteAllText(Path.Combine(old, "old.txt"), "keep");
        budget.Enabled = false;
        Assert.False(budget.CanReserve(4));
        var called = false;
        Assert.Throws<IOException>(() => budget.Reserve(Path.Combine(root, "unexpected"), 4, () => called = true));
        Assert.False(called); Assert.Single(budget.Entries); Assert.Equal(4, budget.UsedBytes);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(old, "old.txt")));
    }

    [Fact]
    public void GroupedEntriesReloadAlongsideLegacyEntriesWithoutMovingThem()
    {
        var root = Fixture(); var journal = Path.Combine(root, "journal");
        var budget = new ReplacementBackupBudget(); budget.Initialize(journal);
        var legacy = Path.Combine(root, ".filesmate-history-" + Guid.NewGuid().ToString("N"));
        using var oldLease = budget.Reserve(legacy, 2, () => Directory.CreateDirectory(legacy));
        var batch = Guid.NewGuid();
        var one = ReplacementBackupLayout.EntryDirectory(root, batch, Guid.NewGuid());
        var two = ReplacementBackupLayout.EntryDirectory(root, batch, Guid.NewGuid());
        using var first = budget.Reserve(one, 3, () => ReplacementBackupLayout.CreateEntryDirectory(one));
        using var second = budget.Reserve(two, 4, () => ReplacementBackupLayout.CreateEntryDirectory(two));
        Assert.Equal(Path.GetDirectoryName(one), Path.GetDirectoryName(two));
        Assert.Single(Directory.GetDirectories(Path.Combine(root, ReplacementBackupLayout.RootName)));
        var restarted = new ReplacementBackupBudget(); restarted.Initialize(journal);
        Assert.Equal(3, restarted.Entries.Count); Assert.Equal(9, restarted.UsedBytes);
        Assert.Contains(restarted.Entries, e => e.Directory == legacy);
        Assert.True(Directory.Exists(legacy));
    }

    [Fact]
    public void ExistingUnownedRootIsRejectedAndItsFilesRemainIntact()
    {
        var root = Fixture(); var existing = Path.Combine(root, ReplacementBackupLayout.RootName);
        Directory.CreateDirectory(existing); File.WriteAllText(Path.Combine(existing, "user.txt"), "preserve");
        var entry = ReplacementBackupLayout.EntryDirectory(root, Guid.NewGuid(), Guid.NewGuid());
        Assert.ThrowsAny<IOException>(() => ReplacementBackupLayout.CreateEntryDirectory(entry));
        Assert.False(Directory.Exists(entry)); Assert.Equal("preserve", File.ReadAllText(Path.Combine(existing, "user.txt")));
    }

    [Fact]
    public void EmptyBatchCleanupKeepsOtherOperationsAndFixedRoot()
    {
        var root = Fixture();
        var first = ReplacementBackupLayout.EntryDirectory(root, Guid.NewGuid(), Guid.NewGuid());
        var second = ReplacementBackupLayout.EntryDirectory(root, Guid.NewGuid(), Guid.NewGuid());
        ReplacementBackupLayout.CreateEntryDirectory(first); ReplacementBackupLayout.CreateEntryDirectory(second);
        Directory.Delete(first, recursive: false); ReplacementBackupLayout.RemoveEmptyParents(first);
        Assert.False(Directory.Exists(Path.GetDirectoryName(first))); Assert.True(Directory.Exists(second));
        Assert.True(Directory.Exists(Path.Combine(root, ReplacementBackupLayout.RootName)));
    }
}
