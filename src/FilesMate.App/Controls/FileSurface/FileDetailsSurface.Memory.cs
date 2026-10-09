using FilesMate.App.Models;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    // A presentation-only host owns the data and activation callbacks. It can
    // never turn a logical sample identity into a filesystem operation.
    private Func<bool, ItemOpeningMode>? _memoryOpeningMode;
    private bool IsMemoryPresentation => _memoryOpeningMode is not null;
    internal void UseMemoryPresentation(Func<bool, ItemOpeningMode> openingMode)
    {
        if (_items.Store is not null) throw new InvalidOperationException("Configure before binding.");
        _memoryOpeningMode = openingMode;
        AllowDrop = false;
        IsFolderWritable = false;
        CanRefresh = false;
    }
    private ItemOpeningMode OpeningMode(bool folder) =>
        _memoryOpeningMode?.Invoke(folder) ?? App.ExplorerPreferences.OpeningMode(folder);
    internal void RefreshMemoryOpeningMode() => ApplyOpeningPreferences();
    internal void FocusMemorySelection() => FocusPrimary();
    internal void FocusMemoryEntry(string name)
    {
        SelectMemoryEntry(name);
        FocusPrimary();
    }
    internal void SelectMemoryEntry(string name)
    {
        if (!IsMemoryPresentation) throw new InvalidOperationException("Memory selection requires a memory host.");
        if (!_items.TryFindEntry(entry => string.Equals(entry.Name, name, StringComparison.Ordinal), out var found)) return;
        _selection.SelectOnly(found.Id);
        RefreshRealizedSelection();
        ScrollPrimaryIntoView();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
