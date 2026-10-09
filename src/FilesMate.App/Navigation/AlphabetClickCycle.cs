using FilesMate.Core.Entries;

namespace FilesMate.App.Navigation;

/// <summary>Direct alphabet clicks alternate kinds independently of the viewport.</summary>
public sealed class AlphabetClickCycle
{
    private EntryViewIndex? _index;
    private string? _label;
    private bool _lastWasDirectory;
    private EntryViewIndex? _targetIndex;
    private string? _targetLabel;
    private int _folderRow = -1;
    private int _fileRow = -1;

    /// <summary>Invalidates an in-flight jump when another click or reset reenters it.</summary>
    public long Revision { get; private set; }

    public int Next(EntryViewIndex index, string label)
    {
        ArgumentNullException.ThrowIfNull(index);
        FindTargets(index, label);
        if (_folderRow < 0 && _fileRow < 0)
        {
            Reset();
            return -1;
        }

        var continuing = ReferenceEquals(_index, index)
            && string.Equals(_label, label, StringComparison.Ordinal);
        var preferFolders = !continuing || !_lastWasDirectory;
        var row = preferFolders ? _folderRow : _fileRow;
        if (row < 0) row = preferFolders ? _fileRow : _folderRow;
        _index = index;
        _label = label;
        _lastWasDirectory = row == _folderRow;
        unchecked { Revision++; }
        return row;
    }

    public int Select(EntryViewIndex index, string label, bool isDirectory)
    {
        ArgumentNullException.ThrowIfNull(index);
        // An explicit kind choice is independent of the direct-click sequence.
        Reset();
        FindTargets(index, label);
        return isDirectory ? _folderRow : _fileRow;
    }

    public bool HasBothKinds(EntryViewIndex index, string label)
    {
        ArgumentNullException.ThrowIfNull(index);
        FindTargets(index, label);
        return _folderRow >= 0 && _fileRow >= 0;
    }

    public void Reset()
    {
        _index = null;
        _label = null;
        _lastWasDirectory = false;
        _targetIndex = null;
        _targetLabel = null;
        _folderRow = _fileRow = -1;
        unchecked { Revision++; }
    }

    private void FindTargets(EntryViewIndex index, string label)
    {
        if (ReferenceEquals(_targetIndex, index) && string.Equals(_targetLabel, label, StringComparison.Ordinal)) return;
        _targetIndex = index;
        _targetLabel = label;
        _folderRow = _fileRow = -1;
        if (index.Sort.Column != EntrySortColumn.Name) return;
        var sections = index.NameSections;
        for (var section = 0; section < sections.Count; section++)
        {
            if (!string.Equals(sections[section].Label, label, StringComparison.Ordinal)) continue;
            var end = section + 1 < sections.Count ? sections[section + 1].FirstIndex : index.Count;
            for (var row = sections[section].FirstIndex; row < end; row++)
            {
                // Mixed sections intentionally hide their kind. Read only this
                // letter's immutable rows, never the live store or the viewport.
                if (index.Snapshot[index[row]].Kind == EntryKind.Directory)
                {
                    if (_folderRow < 0) _folderRow = row;
                }
                else if (_fileRow < 0) _fileRow = row;
                if (_folderRow >= 0 && _fileRow >= 0) return;
                // Grouped name sections contain one kind, so their first row
                // establishes the target without scanning the remaining run.
                if (index.Sort.EffectiveGrouping != EntryGrouping.Mixed) break;
            }
        }
    }
}
