using FilesMate.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Omnibar;

public sealed partial class Omnibar
{
    private string? _selectionDisplayPath, _recursiveRoot;
    private bool _selectionDisplayIsFile, _selectionCrossDirectories;
    private int _selectionDisplayCount;
    private string DisplayPath => _selectionDisplayPath ?? _session.Path;

    public void SetSelectionLocation(string? path, bool isFile, int count, bool crossDirectories, string? recursiveRoot)
    {
        if (_selectionDisplayPath == path && _selectionDisplayIsFile == isFile && _selectionDisplayCount == count
            && _selectionCrossDirectories == crossDirectories && _recursiveRoot == recursiveRoot) return;
        var fileChanged = _selectionDisplayIsFile != isFile;
        _selectionDisplayPath = path;
        _selectionDisplayIsFile = isFile;
        _selectionDisplayCount = count;
        _selectionCrossDirectories = crossDirectories;
        _recursiveRoot = recursiveRoot;
        if (fileChanged) _crumbPath = null;
        // The draft is owned by the editor. Selection can change underneath it.
        if (!IsEditing) ApplySelectionDisplay();
    }

    private void ApplySelectionDisplay()
    {
        var recursive = _recursiveRoot is not null;
        SelectionLocationLabel.Visibility = recursive ? Visibility.Visible : Visibility.Collapsed;
        SelectionLocationLabel.Text = StringTable.Get(_selectionDisplayPath is null ? "Address_RecursiveRoot" : "Address_SelectedLocation");
        var summary = _selectionDisplayCount > 1 ? StringTable.Format("Status_Selected", _selectionDisplayCount) + " · " : "";
        if (_selectionCrossDirectories) summary += StringTable.Get("Address_CrossDirectories") + " · ";
        var rootContext = summary + (recursive ? StringTable.Format("Address_RootCue", _recursiveRoot) : "");
        // At the recursive root, the breadcrumb already identifies that root.
        // A selected descendant keeps a compact root cue rather than repeating
        // a second truncated folder name beside the primary navigation path.
        var distinctRoot = recursive && _selectionDisplayPath is not null
            && !string.Equals(DisplayPath, _recursiveRoot, StringComparison.Ordinal);
        RecursiveRootCue.Visibility = distinctRoot ? Visibility.Visible : Visibility.Collapsed;
        RecursiveRootCue.Text = recursive ? StringTable.Get("Address_RootBadge") : "";
        ToolTipService.SetToolTip(RecursiveRootCue, rootContext);
        AutomationProperties.SetName(RecursiveRootCue, rootContext);
        ToolTipService.SetToolTip(SelectionLocationLabel, rootContext);
        AutomationProperties.SetHelpText(SelectionLocationLabel, rootContext);
        PathBox.Text = DisplayPath;
        RebuildCrumbs(DisplayPath);
        LayoutCrumbs();
        ToolTipService.SetToolTip(PathHost, DisplayPath);
    }

    private bool IsFileCrumb(string path) => _selectionDisplayIsFile && string.Equals(path, _selectionDisplayPath, StringComparison.Ordinal);
}
