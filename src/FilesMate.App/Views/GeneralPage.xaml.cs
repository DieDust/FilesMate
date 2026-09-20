using FilesMate.App.Localization;
using FilesMate.App.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class GeneralPage : UserControl
{
    private bool _syncing;
    private readonly string _languagePath = Program.SettingsPath(LanguageSettings.DefaultFilePath);

    public GeneralPage()
    {
        InitializeComponent();
        Heading.Text = StringTable.Get("GeneralTitle");
        Lead.Text = StringTable.Get("GeneralLead");
        StartupHeader.Text = StringTable.Get("StartupSection");
        StartupCard.Title = StringTable.Get("StartupFolderTitle");
        StartupCard.Description = StringTable.Get("StartupFolderDescription");
        StartupDesktopItem.Content = StringTable.Get("Desktop");
        StartupHomeItem.Content = StringTable.Get("Home");
        RestoreSessionCard.Title = StringTable.Get("RestoreLastSessionTitle");
        RestoreSessionCard.Description = StringTable.Get("RestoreLastSessionDescription");
        WindowsHeader.Text = StringTable.Get("WindowsSection");
        OpenInExistingWindowCard.Title = StringTable.Get("OpenInExistingWindowTitle");
        OpenInExistingWindowCard.Description = StringTable.Get("OpenInExistingWindowDescription");
        OpenFoldersCard.Title = StringTable.Get("OpenFoldersNewTabTitle");
        OpenFoldersCard.Description = StringTable.Get("OpenFoldersNewTabDescription");
        RestoreSessionToggle.OnContent = StringTable.Get("On");
        RestoreSessionToggle.OffContent = StringTable.Get("Off");
        OpenInExistingWindowToggle.OnContent = StringTable.Get("On");
        OpenInExistingWindowToggle.OffContent = StringTable.Get("Off");
        OpenFoldersToggle.OnContent = StringTable.Get("On");
        OpenFoldersToggle.OffContent = StringTable.Get("Off");
        Sync();
    }

    private async void StartupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || StartupBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        var startup = Enum.TryParse<FolderStartupKind>(tag, ignoreCase: true, out var parsed)
            ? parsed
            : FolderStartupKind.Desktop;
        await UpdateAsync(App.ExplorerPreferences with { StartupFolder = startup });
    }

    private async void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || !LanguageBox.IsEnabled || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string language }
            || language == LanguageSettings.Load(_languagePath)) return;
        LanguageBox.IsEnabled = false;
        LanguageStatus.Visibility = Visibility.Visible;
        LanguageRetry.Visibility = Visibility.Collapsed;
        var saved = false;
        try
        {
            LanguageSettings.Save(_languagePath, language);
            saved = true;
            LanguageStatus.Text = StringTable.Get("Language_Saved");
            if (Application.Current is App app) await app.RestartForLanguageAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
            or InvalidOperationException or TimeoutException)
        {
            App.LogFailure("LanguageChange", error);
            LanguageStatus.Text = StringTable.Get(saved ? "Language_RestartFailed" : "Language_SaveFailed");
            LanguageRetry.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
            Sync();
            LanguageBox.IsEnabled = true;
        }
    }

    private async void LanguageRetry_Click(object sender, RoutedEventArgs e)
    {
        LanguageBox.IsEnabled = false;
        LanguageRetry.IsEnabled = false;
        LanguageStatus.Text = StringTable.Get("Language_Saved");
        try { if (Application.Current is App app) await app.RestartForLanguageAsync(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
            or InvalidOperationException or TimeoutException)
        {
            App.LogFailure("LanguageRestart", error);
            LanguageStatus.Text = StringTable.Get("Language_RestartFailed");
            LanguageBox.IsEnabled = true;
            LanguageRetry.IsEnabled = true;
        }
    }

    private async void RestoreSessionToggle_Toggled(object sender, RoutedEventArgs e) =>
        await UpdateAsync(App.ExplorerPreferences with { RestoreLastSession = RestoreSessionToggle.IsOn });

    private async void OpenInExistingWindowToggle_Toggled(object sender, RoutedEventArgs e) =>
        await UpdateAsync(App.ExplorerPreferences with { OpenInExistingWindow = OpenInExistingWindowToggle.IsOn });

    private async void OpenFoldersToggle_Toggled(object sender, RoutedEventArgs e) =>
        await UpdateAsync(App.ExplorerPreferences with { OpenFoldersInNewTab = OpenFoldersToggle.IsOn });

    private async void TabMemoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && TabMemoryBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && Enum.TryParse<TabMemoryMode>(tag, out var mode))
            await UpdateAsync(App.ExplorerPreferences with { TabMemory = mode });
    }

    private async Task UpdateAsync(ExplorerPreferences next)
    {
        if (_syncing)
        {
            return;
        }

        try
        {
            SaveErrorText.Visibility = Visibility.Collapsed;
            await App.SetExplorerPreferencesAsync(next);
            Sync();
        }
        catch
        {
            SaveErrorText.Text = StringTable.Get("Error_SaveAppearance");
            SaveErrorText.Visibility = Visibility.Visible;
            Sync();
        }
    }

    private void Sync()
    {
        _syncing = true;
        LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => (string)item.Tag == LanguageSettings.Load(_languagePath));
        var tag = App.ExplorerPreferences.StartupFolder.ToString();
        foreach (var item in StartupBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                StartupBox.SelectedItem = item;
                break;
            }
        }

        RestoreSessionToggle.IsOn = App.ExplorerPreferences.RestoreLastSession;
        OpenInExistingWindowToggle.IsOn = App.ExplorerPreferences.OpenInExistingWindow;
        OpenFoldersToggle.IsOn = App.ExplorerPreferences.OpenFoldersInNewTab;
        TabMemoryBox.SelectedItem = TabMemoryBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == App.ExplorerPreferences.TabMemory.ToString());
        _syncing = false;
    }
}
