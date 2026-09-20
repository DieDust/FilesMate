using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class ReplacementBackupBudgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate-budget-" + Guid.NewGuid().ToString("N"));
    public ReplacementBackupBudgetTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void UnavailableJournalDisablesBackupsInsteadOfSilentlyUsingMemory()
    {
        var journal = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(journal, "occupied");
        var budget = new ReplacementBackupBudget();
        Assert.Throws<IOException>(() => budget.Initialize(journal));
        Assert.False(budget.CanReserve(1));
        var created = false;
        Assert.ThrowsAny<IOException>(() => budget.Reserve(Path.Combine(_root, ".filesmate-history-test"), 1, () => created = true));
        Assert.False(created);
    }

    [Fact]
    public void SeparateInstancesAndRestartsCountExistingBackupsWithoutDeletingThem()
    {
        var journal = Path.Combine(_root, "journal");
        var first = new ReplacementBackupBudget(10, 10); first.Initialize(journal);
        var folder = Path.Combine(_root, ".filesmate-history-" + Guid.NewGuid().ToString("N"));
        using var lease = first.Reserve(folder, 8, () => Directory.CreateDirectory(folder));
        var backup = Path.Combine(folder, "original"); File.WriteAllText(backup, "original");
        var restarted = new ReplacementBackupBudget(10, 10); restarted.Initialize(journal);
        Assert.Equal(8, restarted.UsedBytes); Assert.False(restarted.CanReserve(3));
        var other = Path.Combine(_root, ".filesmate-history-other");
        Assert.Throws<IOException>(() => restarted.Reserve(other, 3, () => Directory.CreateDirectory(other)));
        Assert.False(Directory.Exists(other));
        lease.Dispose(); Assert.Equal(8, first.UsedBytes); Assert.Equal("original", File.ReadAllText(backup));
        File.Delete(backup); Directory.Delete(folder);
        restarted.ForgetMissing(); Assert.Equal(0, restarted.UsedBytes); Assert.True(first.CanReserve(10));
    }

    [Fact]
    public void FileLimitAndTotalLimitAreIndependent()
    {
        var budget = new ReplacementBackupBudget(100, 20);
        Assert.False(budget.CanReserve(21)); Assert.True(budget.CanReserve(20));
        for (var i = 0; i < 5; i++)
        {
            var folder = Path.Combine(_root, ".filesmate-history-" + i);
            budget.Reserve(folder, 20, () => Directory.CreateDirectory(folder));
        }
        Assert.False(budget.CanReserve(1)); Assert.Equal(100, budget.UsedBytes);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
