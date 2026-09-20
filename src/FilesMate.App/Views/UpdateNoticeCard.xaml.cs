using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class UpdateNoticeCard : UserControl
{
    public UpdateNoticeCard() => InitializeComponent();
    public event EventHandler? UpdateRequested;
    public event EventHandler? DismissRequested;
    public void SetVersion(string version) => VersionLabel.Text = version;
    public void ShowDemoLabel() => DemoLabel.Visibility = Visibility.Visible;
    private void Install_Click(object sender, RoutedEventArgs e) => UpdateRequested?.Invoke(this, EventArgs.Empty);
    private void Dismiss_Click(object sender, RoutedEventArgs e) => DismissRequested?.Invoke(this, EventArgs.Empty);
}
