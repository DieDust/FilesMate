using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Status;

public sealed partial class OperationNotice : UserControl
{
    public OperationNotice() { InitializeComponent(); Theming.AppTypography.Track(this); }
}
