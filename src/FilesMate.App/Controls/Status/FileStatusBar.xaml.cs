using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Status;

public sealed partial class FileStatusBar : UserControl
{
    public FileStatusBar()
    {
        InitializeComponent();
    }

    public void Apply(string? status, string? selection, string? zoom, string? size = null)
    {
        LeftBlock.Text = status ?? string.Empty;
        ToolTipService.SetToolTip(LeftBlock, string.IsNullOrEmpty(status) ? null : status);

        var hasSelection = !string.IsNullOrWhiteSpace(selection);
        SelectionBlock.Text = hasSelection ? selection : string.Empty;
        SelectionBlock.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(SelectionBlock, hasSelection ? selection : null);

        var hasSize = !string.IsNullOrWhiteSpace(size);
        SizeBlock.Text = hasSize ? size : string.Empty;
        SizeBlock.Visibility = hasSize ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(SizeBlock, hasSize ? size : null);

        var hasZoom = !string.IsNullOrWhiteSpace(zoom);
        ZoomBlock.Text = hasZoom ? zoom : string.Empty;
        ZoomBlock.Visibility = hasZoom ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(ZoomBlock, hasZoom ? zoom : null);
    }
}
