using FilesMate.Core.Entries;

namespace FilesMate.App.Navigation;

public sealed partial class PaneViewModel
{
    public EntryMetadataFilter MetadataFilter => _filter.Metadata;
    public bool HasInlineFilter => !string.IsNullOrEmpty(FilterQuery) || MetadataFilter.IsActive;

    public void SetMetadataFilter(EntryMetadataFilter filter)
    {
        AssertUi();
        ArgumentNullException.ThrowIfNull(filter);
        if (_filter.Metadata == filter) return;
        _filter = _filter with { Metadata = filter };
        var revision = CancelIndexBuild();
        if (_disposed || revision != Volatile.Read(ref _indexBuildVersion)) return;
        OnPropertyChanged(nameof(MetadataFilter));
        if (_disposed || revision != Volatile.Read(ref _indexBuildVersion)) return;
        RebuildIndex();
    }

    public void ClearInlineSearchAndFilters()
    {
        SetMetadataFilter(EntryMetadataFilter.None);
        SetFilterQuery(string.Empty);
    }
}
