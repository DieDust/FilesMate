using FilesMate.App.Localization;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Toolbar;

public sealed partial class AdaptiveCommandToolbar
{
    private ToggleMenuFlyoutItem? _recursiveViewItem;
    private bool _recursiveViewEnabled;
    public event EventHandler<bool>? RecursiveViewRequested;

    private void AddRecursiveViewItem(MenuFlyout menu)
    {

        _recursiveViewItem = new ToggleMenuFlyoutItem { Text = StringTable.Get("Recursive_Toggle") };
        ApplyCompactViewItem(_recursiveViewItem);
        ToolTipService.SetToolTip(_recursiveViewItem, StringTable.Get("Recursive_Hint") + "\n" + StringTable.Get("Recursive_TransferHint"));
        _recursiveViewItem.Click += (_, _) => RecursiveViewRequested?.Invoke(this, _recursiveViewItem.IsChecked);
        menu.Items.Add(_recursiveViewItem);
    }

    public void SetRecursiveState(bool enabled, bool available)
    {
        if (_recursiveViewItem is null) return;
        _recursiveViewEnabled = enabled;
        _recursiveViewItem.IsChecked = enabled;
        _recursiveViewItem.IsEnabled = enabled || available;
        SetLayout(_viewKind);
    }
}
