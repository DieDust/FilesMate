namespace FilesMate.Core.Recursive;

/// <summary>Pure capture helpers. Never discover extra files or mutate the selection after confirmation.</summary>
public static class RecursiveOperationSelection
{
    public static IReadOnlyList<string> ResolvePaths(RecursiveProjection projection, IEnumerable<int> selectedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(selectedIds);
        var selected = new HashSet<int>();
        var ordered = new List<int>();
        foreach (var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (projection.IsSelectable(id) && selected.Add(id)) ordered.Add(id);
        }
        var paths = new List<string>(ordered.Count);
        foreach (var id in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parent = projection.GetNode(id).ParentId;
            var covered = false;
            while (parent != 0)
            {
                if (selected.Contains(parent)) { covered = true; break; }
                parent = projection.GetNode(parent).ParentId;
            }
            if (!covered) paths.Add(projection.ResolvePath(id));
        }
        return paths.AsReadOnly();
    }

    /// <summary>
    /// Path-based boundary check for operation planners that already hold captured paths. A directory
    /// covers its descendants, never similarly prefixed siblings such as A and AB. No filesystem I/O.
    /// </summary>
    public static bool IsDescendantPath(string directoryPath, string candidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        return RecursivePath.IsDescendant(directoryPath, candidatePath);
    }
}
