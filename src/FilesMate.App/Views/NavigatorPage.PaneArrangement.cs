using FilesMate.App.Models;
using FilesMate.App.Workspace;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private PaneArrangement _paneArrangement = App.ExplorerPreferences.PaneArrangement;

    private void Commands_PaneArrangementChanged(object? sender, PaneArrangement arrangement)
    {
        if (_paneCount == 3 && _paneArrangement == arrangement)
        {
            SetPaneCount(1, persist: true);
            return;
        }
        _paneArrangement = arrangement;
        SetPaneCount(3, persist: false);
        ApplyPaneArrangement(resetRatio: true);
        _ = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with
        { DualPane = true, PaneCount = 3, PaneArrangement = arrangement });
    }

    private void ApplyPaneArrangement(bool resetRatio)
    {
        var triple = _paneCount == 3;
        var verticalStack = _paneArrangement is PaneArrangement.Rows or PaneArrangement.TopFocus or PaneArrangement.BottomFocus;
        WorkspaceSplit.Layout = !_dualPane ? WorkspaceLayoutKind.Single
            : triple && verticalStack ? WorkspaceLayoutKind.Horizontal : WorkspaceLayoutKind.Vertical;
        WorkspaceSplit.Reverse = triple && _paneArrangement is PaneArrangement.RightFocus or PaneArrangement.BottomFocus;
        if (_trailingSplit is not null)
        {
            _trailingSplit.Layout = !triple ? WorkspaceLayoutKind.Single
                : _paneArrangement is PaneArrangement.Rows or PaneArrangement.LeftFocus or PaneArrangement.RightFocus
                    ? WorkspaceLayoutKind.Horizontal : WorkspaceLayoutKind.Vertical;
            if (resetRatio) _trailingSplit.SplitRatio = .5;
        }
        if (resetRatio) WorkspaceSplit.SplitRatio = triple && _paneArrangement is PaneArrangement.Columns or PaneArrangement.Rows ? 1d / 3 : .5;
        Commands.SetPaneArrangement(_paneArrangement);
    }
}
