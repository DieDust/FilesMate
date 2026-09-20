using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class UpdateProgressView : UserControl
{
    private long _size;
    public UpdateProgressView() => InitializeComponent();
    public event EventHandler? CancelRequested;
    public void SetRelease(UpdateRelease release)
    {
        _size = release.Size;
        VersionLabel.Text = release.DisplayVersion;
        var notes = release.GetNotes(System.Globalization.CultureInfo.CurrentUICulture);
        NotesText.Text = notes;
        NotesSection.Visibility = string.IsNullOrWhiteSpace(notes) ? Visibility.Collapsed : Visibility.Visible;
        BytesText.Text = $"0 / {_size / 1048576d:F1} MB";
    }
    public void ShowDemoLabel()
    {
        DemoLabel.Visibility = Visibility.Visible;
        FooterText.Text = Loc.Get("Update_DemoFooter");
    }
    public void SetProgress(double value)
    {
        value = Math.Clamp(value, 0, 1);
        DownloadProgress.Value = value * 100;
        PercentText.Text = $"{value:P0}";
        StatusText.Text = Loc.Get("Update_Downloading");
        BytesText.Text = $"{_size * value / 1048576d:F1} / {_size / 1048576d:F1} MB";
    }
    public void SetStatus(string message, bool failed = false)
    {
        StatusText.Text = message;
        if (failed) { CancelButton.Content = Loc.Get("Close"); PercentText.Visibility = Visibility.Collapsed; FooterText.Text = Loc.Get("Update_KeepUsing"); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);
}
