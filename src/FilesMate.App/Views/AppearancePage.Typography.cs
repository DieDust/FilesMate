using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class AppearancePage
{
    private void InitializeFileTypography()
    {
        FileTypographyHeader.Text = StringTable.Get("Font_Section");
        FileFontLabel.Text = StringTable.Get("Font_Family");
        FileFontHint.Text = StringTable.Get("Font_Hint");
        FileNameSizeLabel.Text = StringTable.Get("Font_NameSize");
        FileDetailsSizeLabel.Text = StringTable.Get("Font_DetailsSize");
        FileFontPreview.Text = StringTable.Get("Font_Preview");
        FileDetailsPreview.Text = "2026/09/29 12:30 · PNG · 2.4 MB";
        ResetFileTypographyButton.Content = StringTable.Get("Font_Reset");
        foreach (var size in Enumerable.Range(10, 15)) FileNameSizeBox.Items.Add(size);
        foreach (var size in Enumerable.Range(10, 11)) FileDetailsSizeBox.Items.Add(size);
        FileFontBox.Items.Add(StringTable.Get("Font_Default"));
        AutomationProperties.SetName(FileFontBox, FileFontLabel.Text);
        AutomationProperties.SetName(FileNameSizeBox, FileNameSizeLabel.Text);
        AutomationProperties.SetName(FileDetailsSizeBox, FileDetailsSizeLabel.Text);
        Loaded += LoadFonts;
    }

    private void TypographyFields_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 540;
        TypographyFields.RowSpacing = stacked ? 12 : 0;
        Grid.SetColumnSpan(FileFontCard, stacked ? 2 : 1);
        Grid.SetRow(TypographySizes, stacked ? 1 : 0);
        Grid.SetColumn(TypographySizes, stacked ? 0 : 1);
        Grid.SetColumnSpan(TypographySizes, stacked ? 2 : 1);
    }
    private async void LoadFonts(object sender, RoutedEventArgs e)
    {
        try
        {
            var fonts = await InstalledFonts.GetAsync();
            if (!IsLoaded || _viewModel is null) return;
            _syncing = true;
            FileFontBox.Items.Clear(); FileFontBox.Items.Add(StringTable.Get("Font_Default"));
            foreach (var font in fonts) FileFontBox.Items.Add(font);
            SyncFileTypography(_viewModel.Current); _syncing = false;
        }
        catch (Exception error) { _syncing = false; App.LogFailure("InstalledFonts", error); }
    }
    private void SyncFileTypography(AppearanceSettings settings)
    {
        if (settings.FileFontFamily is { } family && !FileFontBox.Items.Contains(family)) FileFontBox.Items.Add(family);
        FileFontBox.SelectedItem = settings.FileFontFamily ?? StringTable.Get("Font_Default");
        FileNameSizeBox.SelectedItem = (int)settings.FileNameFontSize;
        FileDetailsSizeBox.SelectedItem = (int)settings.FileDetailsFontSize;
        Controls.FileSurface.FileTypography.Apply(FileFontPreview, settings, settings.FileNameFontSize);
        Controls.FileSurface.FileTypography.Apply(FileDetailsPreview, settings, settings.FileDetailsFontSize);
    }
    private async void FileTypography_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewModel is null || FileNameSizeBox.SelectedItem is not int name || FileDetailsSizeBox.SelectedItem is not int details) return;
        await ApplyAsync(() => _viewModel.SetFileTypographyAsync(FileFontBox.SelectedIndex <= 0 ? null : FileFontBox.SelectedItem as string, name, details));
    }
    private async void ResetFileTypography_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(() => _viewModel!.SetFileTypographyAsync(null, 13, 12));
}
