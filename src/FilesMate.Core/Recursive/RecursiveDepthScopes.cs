namespace FilesMate.Core.Recursive;

/// <summary>Normalizes selected directory scopes without visiting unrelated snapshot rows.</summary>
public static class RecursiveDepthScopes
{
    public static IReadOnlyList<int> Outermost(RecursiveSnapshot snapshot, IReadOnlySet<int> selected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        var scopes = new List<int>(selected.Count);
        foreach (var id in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (id < 0 || id > snapshot.Count) throw new ArgumentOutOfRangeException(nameof(selected));
            if (id != 0 && !snapshot.GetNode(id).IsDirectory) throw new ArgumentException("Depth scope must be a directory.", nameof(selected));
            if (id == 0) { scopes.Add(0); continue; }
            var parent = snapshot.GetNode(id).ParentId;
            var covered = selected.Contains(0);
            while (parent != 0 && !covered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                covered = selected.Contains(parent); parent = snapshot.GetNode(parent).ParentId;
            }
            if (!covered) scopes.Add(id);
        }
        scopes.Sort(); return scopes.ToArray();
    }
}
