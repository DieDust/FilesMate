namespace FilesMate.App.Models;

/// <summary>
/// A private editing session. Changes are never written to preferences until the
/// caller explicitly saves a snapshot; dropping the draft is Cancel/light dismiss.
/// </summary>
public sealed class ToolbarCustomizationDraft
{
    private List<ToolbarTool> _order;
    private readonly HashSet<ToolbarTool> _hidden;
    private readonly ToolbarTool[] _initialOrder;
    private readonly HashSet<ToolbarTool> _initialHidden;

    public ToolbarCustomizationDraft(IEnumerable<ToolbarTool>? order, IEnumerable<ToolbarTool>? hidden)
    {
        _order = ToolbarToolCatalog.NormalizeOrder(order).ToList();
        // Keep legacy hidden IDs too. Editing an unrelated command must not erase
        // preferences retained for older versions of the toolbar.
        _hidden = (hidden ?? []).ToHashSet();
        _initialOrder = _order.ToArray();
        _initialHidden = _hidden.ToHashSet();
    }

    public IReadOnlyList<ToolbarToolDefinition> Visible(ToolbarZone zone, bool searchMode) =>
        ToolbarToolCatalog.Ordered(_order, zone, searchMode).Where(d => !_hidden.Contains(d.Id)).ToArray();

    public IReadOnlyList<ToolbarToolDefinition> Available(bool searchMode) =>
        _order.Select(ToolbarToolCatalog.Get).Where(d => _hidden.Contains(d.Id) && (!searchMode || d.AvailableInSearch)).ToArray();

    public bool SetVisible(ToolbarTool id, bool visible)
    {
        if (!ToolbarToolCatalog.All.Any(d => d.Id == id)) return false;
        return visible ? _hidden.Remove(id) : _hidden.Add(id);
    }

    /// <summary>Accept only a permutation of this zone's currently visible tools.</summary>
    public bool SetVisibleOrder(ToolbarZone zone, bool searchMode, IEnumerable<ToolbarTool> values)
    {
        var next = values.ToArray();
        var visible = Visible(zone, searchMode).Select(d => d.Id).ToHashSet();
        if (next.Length != visible.Count || next.Distinct().Count() != next.Length || !visible.SetEquals(next)) return false;
        var position = 0;
        for (var i = 0; i < _order.Count; i++)
            if (visible.Contains(_order[i])) _order[i] = next[position++];
        return true;
    }

    public bool Move(ToolbarTool id, int direction, bool searchMode)
    {
        if (direction is not (-1 or 1) || !ToolbarToolCatalog.All.Any(d => d.Id == id)) return false;
        var definition = ToolbarToolCatalog.Get(id);
        var visible = Visible(definition.Zone, searchMode).Select(d => d.Id).ToList();
        var old = visible.IndexOf(id);
        var next = old + direction;
        if (old < 0 || next < 0 || next >= visible.Count) return false;
        (visible[old], visible[next]) = (visible[next], visible[old]);
        return SetVisibleOrder(definition.Zone, searchMode, visible);
    }

    public void Reset()
    {
        _order = ToolbarToolCatalog.NormalizeOrder(null).ToList();
        _hidden.Clear();
    }

    public bool HasOrderChanges => !_order.SequenceEqual(_initialOrder);
    public bool HasVisibilityChanges => !_hidden.SetEquals(_initialHidden);
    public bool HasChanges => HasOrderChanges || HasVisibilityChanges;

    /// <summary>
    /// Preserve newer fields this editor did not change. If two editors changed
    /// the same field, reject the stale save rather than silently overwrite it.
    /// </summary>
    public bool TryMergeForSave(IEnumerable<ToolbarTool>? currentOrder, IEnumerable<ToolbarTool>? currentHidden,
        out ToolbarCustomizationSnapshot snapshot)
    {
        var latestOrder = currentOrder?.ToArray();
        var latestHidden = currentHidden?.ToArray();
        snapshot = Snapshot();
        var normalizedLatestOrder = ToolbarToolCatalog.NormalizeOrder(latestOrder);
        if (HasOrderChanges && !normalizedLatestOrder.SequenceEqual(_initialOrder) && !normalizedLatestOrder.SequenceEqual(_order)
            || HasVisibilityChanges && !_initialHidden.SetEquals(latestHidden ?? []) && !_hidden.SetEquals(latestHidden ?? [])) return false;
        snapshot = new(HasOrderChanges ? snapshot.Order : latestOrder,
            HasVisibilityChanges ? snapshot.Hidden : latestHidden);
        return true;
    }

    // Copy on every call: neither preferences nor a previous snapshot shares the
    // mutable draft collections. Keep the established null/default storage form.
    public ToolbarCustomizationSnapshot Snapshot() => new(
        _order.SequenceEqual(ToolbarToolCatalog.NormalizeOrder(null)) ? null : _order.ToArray(),
        _hidden.Count == 0 ? null : _hidden.OrderBy(id => (int)id).ToArray());
}

public sealed record ToolbarCustomizationSnapshot(IReadOnlyList<ToolbarTool>? Order, IReadOnlyList<ToolbarTool>? Hidden);
