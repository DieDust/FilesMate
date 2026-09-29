using FilesMate.App.Localization;
using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Toolbar;

public sealed partial class AdaptiveCommandToolbar
{
    private readonly ToggleMenuFlyoutItem _overflowHiddenFiles = new();

    private void InitializeHiddenFiles()
    {
        _overflowHiddenFiles.Text = StringTable.Get("ShowHiddenFilesTitle");
        _overflowHiddenFiles.Click += HiddenFilesButton_Click;
        ((MenuFlyout)MoreButton.Flyout).Items.Insert(0, _overflowHiddenFiles);
        Loaded += (_, _) =>
        {
            App.ExplorerPreferencesChanged -= HiddenFilesPreferencesChanged;
            App.ExplorerPreferencesChanged += HiddenFilesPreferencesChanged;
            SyncHiddenFiles();
            ApplyContext(_context);
        };
        Unloaded += (_, _) => App.ExplorerPreferencesChanged -= HiddenFilesPreferencesChanged;
        SyncHiddenFiles();
    }

    private void HiddenFilesPreferencesChanged(object? sender, ExplorerPreferences preferences)
    {
        SyncHiddenFiles();
        ApplyContext(_context);
    }

    private void SyncHiddenFiles()
    {
        var shown = App.ExplorerPreferences.ShowHiddenFiles;
        HiddenFilesIcon.Glyph = shown ? "\uE890" : "\uED1A";
        SetActive(HiddenFilesButton, shown);
        _overflowHiddenFiles.IsChecked = shown;
        var label = StringTable.Get(shown ? "HiddenFiles_Shown" : "HiddenFiles_Hidden");
        AutomationProperties.SetName(HiddenFilesButton, label);
        ToolTipService.SetToolTip(HiddenFilesButton, label);
    }

    private async void HiddenFilesButton_Click(object sender, RoutedEventArgs e)
    {
        try { await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowHiddenFiles = !App.ExplorerPreferences.ShowHiddenFiles }); }
        catch (Exception error) { App.LogFailure("HiddenFilesToggle", error); }
        SyncHiddenFiles();
    }
}
