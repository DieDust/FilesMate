using FilesMate.App.Services;

namespace FilesMate.App.Navigation;

public sealed record ClosedPaneState(string Path, FolderViewSettings View, double ScrollOffset,
    string[]? SelectedNames = null, string FilterQuery = "", NavigationHistoryState? History = null);
public sealed record ClosedTabState(ClosedPaneState Left, ClosedPaneState? Right = null, bool RightActive = false, bool PreviewVisible = false);

public sealed class ClosedTabHistory
{
    public const int Limit = 16;
    private readonly List<ClosedTabState> _items = [];
    public int Count => _items.Count;
    public void Push(ClosedTabState state)
    {
        _items.Add(state);
        if (_items.Count > Limit) _items.RemoveAt(0);
    }
    public ClosedTabState? Pop()
    {
        if (_items.Count == 0) return null;
        var item = _items[^1];
        _items.RemoveAt(_items.Count - 1);
        return item;
    }
}
