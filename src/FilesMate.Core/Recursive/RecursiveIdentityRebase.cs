namespace FilesMate.Core.Recursive;

/// <summary>Exact path and birth-time mapping between two captures of one root.</summary>
public sealed class RecursiveIdentityRebase
{
    private readonly RecursiveSnapshot _before, _after;
    private readonly int[] _ids;
    private RecursiveIdentityRebase(RecursiveSnapshot before, RecursiveSnapshot after, int[] ids)
    { _before = before; _after = after; _ids = ids; }
    public bool Matches(RecursiveSnapshot before, RecursiveSnapshot after) =>
        ReferenceEquals(before, _before) && ReferenceEquals(after, _after);
    public int Map(int id) => (uint)id < (uint)_ids.Length ? _ids[id] : -1;
    public static RecursiveIdentityRebase? Create(RecursiveSnapshot before, RecursiveSnapshot after, CancellationToken cancellation)
    {
        if (!string.Equals(before.RootPath, after.RootPath, StringComparison.Ordinal)) return null;
        var edges = new Dictionary<(int Parent, string Name), int>(before.Count);
        var ids = new int[before.Count + 1];
        Array.Fill(ids, -1); ids[0] = 0;
        for (int id = 1; id <= before.Count; id++)
        {
            cancellation.ThrowIfCancellationRequested();
            edges.Add((before.GetNode(id).ParentId, before.GetEntry(id).Name), id);
        }
        var parents = new int[after.Count + 1];
        parents[0] = 1;
        for (int id = 1; id <= after.Count; id++)
        {
            cancellation.ThrowIfCancellationRequested();
            var node = after.GetNode(id);
            var parent = parents[node.ParentId] - 1;
            if (parent < 0 || !edges.TryGetValue((parent, after.GetEntry(id).Name), out var oldId)) continue;
            if (before.GetNode(oldId).IsDirectory != node.IsDirectory) continue;
            if (node.IsDirectory) parents[id] = oldId + 1;
            var birth = before.GetEntry(oldId).CreatedUtcTicks;
            if (birth != 0 && birth == after.GetEntry(id).CreatedUtcTicks) ids[oldId] = id;
        }
        return new(before, after, ids);
    }
}
