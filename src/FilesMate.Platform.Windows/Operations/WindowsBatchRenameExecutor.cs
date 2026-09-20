using FilesMate.Core.Operations;

namespace FilesMate.Platform.Windows.Operations;

public sealed record BatchRenameExecutionResult(
    IReadOnlyList<string> CompletedSources,
    IReadOnlyList<string> Errors);

public sealed class WindowsBatchRenameExecutor
{
    private readonly ILocalFileOperations _operations;

    public WindowsBatchRenameExecutor(ILocalFileOperations operations)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public Task<BatchRenameExecutionResult> ExecuteAsync(
        BatchRenamePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsValid)
        {
            throw new ArgumentException("The batch rename plan contains validation errors.", nameof(plan));
        }

        return Task.Run(() => Execute(plan, cancellationToken), cancellationToken);
    }

    private BatchRenameExecutionResult Execute(BatchRenamePlan plan, CancellationToken cancellationToken)
    {
        var movedToTemp = new List<(string Source, string Temp, string Target)>();
        var completed = new List<string>();
        try
        {
            for (var i = 0; i < plan.Entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = plan.Entries[i];
                if (!entry.RequiresRename)
                {
                    continue;
                }

                var temp = Path.Combine(Path.GetDirectoryName(entry.Source)!, ".filesmate-rename-" + Guid.NewGuid().ToString("N"));
                _operations.Rename(entry.Source, temp);
                movedToTemp.Add((entry.Source, temp, entry.Target));
            }

            foreach (var item in movedToTemp)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _operations.Rename(item.Temp, item.Target);
                completed.Add(item.Source);
            }

            return new BatchRenameExecutionResult(completed, []);
        }
        catch (Exception error)
        {
            var rollbackErrors = Rollback(movedToTemp, completed);
            return new BatchRenameExecutionResult([], [error.Message, .. rollbackErrors]);
        }
    }

    private IReadOnlyList<string> Rollback(
        IReadOnlyList<(string Source, string Temp, string Target)> movedToTemp,
        IReadOnlyList<string> completed)
    {
        var errors = new List<string>();
        var completedSet = completed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stillAtTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Vacate final destinations first: they may be another item's original name.
        foreach (var item in movedToTemp.Where(item => completedSet.Contains(item.Source)))
        {
            try { _operations.Rename(item.Target, item.Temp); }
            catch (Exception error)
            {
                stillAtTarget.Add(item.Source);
                errors.Add($"Could not restore '{item.Source}'; item remains at '{item.Target}': {error.Message}");
            }
        }
        foreach (var item in movedToTemp.Reverse())
        {
            if (stillAtTarget.Contains(item.Source)) continue;
            try
            {
                _operations.Rename(item.Temp, item.Source);
            }
            catch (Exception error)
            {
                errors.Add($"Could not restore '{item.Source}'; item remains at '{item.Temp}': {error.Message}");
            }
        }
        return errors;
    }
}
