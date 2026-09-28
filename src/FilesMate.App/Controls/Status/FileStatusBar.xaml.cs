using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Status;

public sealed partial class FileStatusBar : UserControl
{
    public FileStatusBar() => InitializeComponent();

    public void Apply(string? status, string? selection, string? capacity, string? size = null, string? path = null)
    {
        var summary = string.Join(" · ", new[] { status, selection, size, capacity }
            .Where(text => !string.IsNullOrWhiteSpace(text)));
        SummaryBlock.Text = summary;
        AutomationProperties.SetName(SummaryBlock, summary);
        ToolTipService.SetToolTip(SummaryBlock, string.IsNullOrEmpty(path) ? summary : path + "\n" + summary);
    }
}
