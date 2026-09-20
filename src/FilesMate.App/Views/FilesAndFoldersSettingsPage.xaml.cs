using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Navigation;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class FilesAndFoldersSettingsPage : UserControl
{
    private async void BackupManage_Click(object sender, RoutedEventArgs e) => await BackupHistoryDialog.ShowAsync(this);
    private bool _syncing;
    private readonly PinnedLocationStore _pinnedLocations = new(PinnedLocationStore.DefaultFilePath);

    public FilesAndFoldersSettingsPage()
    {
        InitializeComponent();
        Heading.Text = StringTable.Get("FilesFoldersTitle");
        Lead.Text = StringTable.Get("FilesFoldersLead");
        FoldersHeader.Text = StringTable.Get("FileListSection");
        AlphabetHeader.Text = StringTable.Get("AlphabetNavigationSection");
        SafetyHeader.Text = StringTable.Get("SafetySection");
        SidebarHeader.Text = StringTable.Get("SidebarSection");
        StartupViewCard.Title = StringTable.Get("DefaultViewTitle");
        StartupViewCard.Description = StringTable.Get("DefaultViewDescription");
        ViewIconsItem.Content = StringTable.Get("Layout_Icons");
        ViewDetailsItem.Content = StringTable.Get("Layout_Details");
        ExtensionsCard.Title = StringTable.Get("ShowExtensionsTitle");
        ExtensionsCard.Description = StringTable.Get("ShowExtensionsDescription");
        DateFormatCard.Title = StringTable.Get("DateFormatTitle");
        DateFormatCard.Description = StringTable.Get("DateFormatDescription");
        DateSystemItem.Content = StringTable.Get("DateFormatSystem");
        DateIsoItem.Content = StringTable.Get("DateFormatIso");
        FolderSizesCard.Title = StringTable.Get("ShowFolderSizesTitle");
        FolderSizesCard.Description = StringTable.Get("ShowFolderSizesDescription");
        AlphabetCard.Title = StringTable.Get("AlphabetNavigationTitle");
        AlphabetCard.Description = StringTable.Get("AlphabetNavigationDescription");
        AlphabetMinimumItemsCard.Title = StringTable.Get("AlphabetMinimumItemsTitle");
        AlphabetMinimumItemsCard.Description = StringTable.Get("AlphabetMinimumItemsDescription");
        AlphabetDualPaneCard.Title = StringTable.Get("AlphabetDualPaneTitle");
        AlphabetDualPaneCard.Description = StringTable.Get("AlphabetDualPaneDescription");
        SidebarPinsCard.Title = StringTable.Get("SidebarPinsTitle");
        SidebarPinsCard.Description = StringTable.Get("SidebarPinsDescription");
        RestoreSidebarButton.Content = StringTable.Get("RestoreSidebarDefaults");
        HiddenFilesCard.Title = StringTable.Get("ShowHiddenFilesTitle");
        HiddenFilesCard.Description = StringTable.Get("ShowHiddenFilesDescription");
        ConfirmDeleteCard.Title = StringTable.Get("ConfirmDeleteSettingTitle");
        ConfirmDeleteCard.Description = StringTable.Get("ConfirmDeleteSettingDescription");
        Sync();
        _syncing = true;
        FavoritesToggle.IsOn = App.Features.FavoritesBarEnabled;
        _syncing = false;
        Loaded += async (_, _) =>
        {
            var global = await App.FolderCustomizations.GetGlobalViewAsync();
            _syncing = true;
            ViewScopeBox.SelectedIndex = global ? 1 : 0;
            UpdateViewScopeHint(global);
            _syncing = false;
        };
    }

    private async void ViewScope_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || ViewScopeBox.SelectedIndex < 0) return;
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            await App.FolderCustomizations.SetGlobalViewAsync(ViewScopeBox.SelectedIndex == 1);
            UpdateViewScopeHint(ViewScopeBox.SelectedIndex == 1);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ErrorText.Text = StringTable.Get("Error_SaveAppearance");
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void FavoritesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        try { App.SetFavoritesBarEnabled(FavoritesToggle.IsOn); }
        catch (Exception error) { ErrorText.Text = error.Message; ErrorText.Visibility = Visibility.Visible; }
    }

    private async void FeatureSetup_Click(object sender, RoutedEventArgs e)
    {
        await App.ShowFeatureSetupAsync(this);
        _syncing = true;
        FavoritesToggle.IsOn = App.Features.FavoritesBarEnabled;
        _syncing = false;
    }

    private void UpdateViewScopeHint(bool global)
    {
        DefaultViewBox.IsEnabled = !global;
        StartupViewCard.Description = global
            ? Loc.Get("View_SharedHint")
            : StringTable.Get("DefaultViewDescription");
    }

    private void RestoreSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            _pinnedLocations.RestoreDefaults();
            App.NotifyPinnedLocationsChanged();
            StatusText.Text = StringTable.Get("SidebarDefaultsRestored");
            StatusText.Visibility = Visibility.Visible;
        }
        catch
        {
            StatusText.Visibility = Visibility.Collapsed;
            ErrorText.Text = StringTable.Get("Error_SaveAppearance");
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private async void HiddenFilesToggle_Toggled(object sender, RoutedEventArgs e) =>
        await UpdateAsync(App.ExplorerPreferences with { ShowHiddenFiles = HiddenFilesToggle.IsOn });

    private async void ConfirmDeleteToggle_Toggled(object sender, RoutedEventArgs e) =>
        await UpdateAsync(App.ExplorerPreferences with { ConfirmPermanentDelete = ConfirmDeleteToggle.IsOn });

    private async void ExtensionsToggle_Toggled(object sender, RoutedEventArgs e) =>
        await UpdateAsync(App.ExplorerPreferences with { ShowFileExtensions = ExtensionsToggle.IsOn });

    private async void FolderSizesToggle_Toggled(object sender, RoutedEventArgs e) =>
        await UpdateAsync(App.ExplorerPreferences with { ShowFolderSizes = FolderSizesToggle.IsOn });

    private async void AlphabetToggle_Toggled(object sender, RoutedEventArgs e) =>
        await UpdateAsync(App.ExplorerPreferences with { ShowAlphabetNavigation = AlphabetToggle.IsOn });

    private async void AlphabetMinimumItemsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || double.IsNaN(args.NewValue)) return;
        var count = ExplorerPreferences.ClampAlphabetNavigationMinimumItemCount((int)Math.Round(args.NewValue));
        await UpdateAsync(App.ExplorerPreferences with { AlphabetNavigationMinimumItemCount = count });
    }

    private async void AlphabetDualPaneToggle_Toggled(object sender, RoutedEventArgs e) =>
        await UpdateAsync(App.ExplorerPreferences with { ShowAlphabetNavigationInDualPane = AlphabetDualPaneToggle.IsOn });

    private async void MixedNameSortToggle_Toggled(object sender, RoutedEventArgs e) =>
        await UpdateAsync(App.ExplorerPreferences with { MixChineseAndLatin = MixedNameSortToggle.IsOn });

    private async void DefaultViewBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || DefaultViewBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        var view = Enum.TryParse<FolderViewKind>(tag, ignoreCase: true, out var parsed)
            ? parsed
            : ExplorerPreferences.Default.DefaultView;
        await UpdateAsync(App.ExplorerPreferences with { DefaultView = view });
    }

    private async void DateFormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || DateFormatBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        var format = Enum.TryParse<DateFormatKind>(tag, ignoreCase: true, out var parsed)
            ? parsed
            : DateFormatKind.System;
        await UpdateAsync(App.ExplorerPreferences with { DateFormat = format });
    }

    private async Task UpdateAsync(Models.ExplorerPreferences next)
    {
        if (_syncing)
        {
            return;
        }

        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            await App.SetExplorerPreferencesAsync(next);
            Sync();
        }
        catch
        {
            ErrorText.Text = StringTable.Get("Error_SaveAppearance");
            ErrorText.Visibility = Visibility.Visible;
            Sync();
        }
    }

    private void Sync()
    {
        _syncing = true;
        HiddenFilesToggle.IsOn = App.ExplorerPreferences.ShowHiddenFiles;
        ConfirmDeleteToggle.IsOn = App.ExplorerPreferences.ConfirmPermanentDelete;
        ExtensionsToggle.IsOn = App.ExplorerPreferences.ShowFileExtensions;
        FolderSizesToggle.IsOn = App.ExplorerPreferences.ShowFolderSizes;
        AlphabetToggle.IsOn = App.ExplorerPreferences.ShowAlphabetNavigation;
        AlphabetMinimumItemsBox.Value = App.ExplorerPreferences.AlphabetNavigationMinimumItemCount;
        AlphabetDualPaneToggle.IsOn = App.ExplorerPreferences.ShowAlphabetNavigationInDualPane;
        AlphabetMinimumItemsBox.IsEnabled = AlphabetToggle.IsOn;
        AlphabetDualPaneToggle.IsEnabled = AlphabetToggle.IsOn;
        MixedNameSortToggle.IsOn = App.ExplorerPreferences.MixChineseAndLatin;
        SelectTag(DefaultViewBox, App.ExplorerPreferences.DefaultView.ToString());
        SelectTag(DateFormatBox, App.ExplorerPreferences.DateFormat.ToString());
        _syncing = false;
    }

    private static void SelectTag(ComboBox box, string tag)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
    }
}
