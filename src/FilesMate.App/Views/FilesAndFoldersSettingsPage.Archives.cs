using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.Platform.Windows.Archives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FilesMate.App.Views;

public sealed partial class FilesAndFoldersSettingsPage
{
    private readonly ArchivePreferencesStore _archiveStore = new(Program.SettingsPath(ArchivePreferencesStore.DefaultPath));
    private ArchivePreferences _archivePreferences = new();
    private bool _archiveSyncing;
    private int _archiveProbe;

    private void InitializeArchiveSettings()
    {
        _archiveSyncing = true;
        foreach (var provider in Enum.GetValues<ArchiveProvider>())
            ArchiveProviderBox.Items.Add(new ComboBoxItem { Content = ArchiveProviderNames.Name(provider), Tag = provider });
        Loaded += async (_, _) =>
        {
            _archivePreferences = await Task.Run(_archiveStore.Load);
            _archiveSyncing = true;
            ArchiveProviderBox.SelectedIndex = (int)_archivePreferences.Preferred;
            _archiveSyncing = false;
            await UpdateArchiveProviderAsync();
        };
    }

    private async void ArchiveProvider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_archiveSyncing || ArchiveProviderBox.SelectedItem is not ComboBoxItem { Tag: ArchiveProvider provider }) return;
        var updated = _archivePreferences with { Preferred = provider };
        ArchiveProviderBox.IsEnabled = ArchiveBrowse.IsEnabled = ArchiveReset.IsEnabled = false;
        try { await Task.Run(() => _archiveStore.Save(updated)); _archivePreferences = updated; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowArchiveSettingsError(error); }
        finally { ArchiveProviderBox.IsEnabled = ArchiveBrowse.IsEnabled = ArchiveReset.IsEnabled = true; }
        await UpdateArchiveProviderAsync();
    }

    private async Task UpdateArchiveProviderAsync()
    {
        var probe = ++_archiveProbe;
        var provider = _archivePreferences.Preferred;
        ArchiveProviderHint.Text = ArchiveProviderNames.Hint(provider);
        var external = provider is not (ArchiveProvider.Automatic or ArchiveProvider.BuiltIn);
        ArchiveBrowse.Visibility = ArchiveReset.Visibility = external ? Visibility.Visible : Visibility.Collapsed;
        ArchiveProviderPath.Text = external ? StringTable.Get("Archive_Detecting") : "";
        if (!external) return;
        var path = await Task.Run(() => ArchiveProviderDiscovery.Find(provider, _archivePreferences.PathFor(provider)));
        if (probe == _archiveProbe && IsLoaded)
            ArchiveProviderPath.Text = path ?? StringTable.Get("Archive_NotInstalled");
    }

    private async void ArchiveBrowse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var provider = _archivePreferences.Preferred;
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            InitializeWithWindow.Initialize(picker, App.WindowForElement(this)!.NativeHandle);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var paths = new Dictionary<ArchiveProvider, string>(_archivePreferences.Executables ?? []) { [provider] = file.Path };
            var updated = _archivePreferences with { Executables = paths };
            await Task.Run(() => _archiveStore.Save(updated));
            _archivePreferences = updated;
            await UpdateArchiveProviderAsync();
        }
        catch (Exception error) { ShowArchiveSettingsError(error); }
    }

    private async void ArchiveReset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var paths = new Dictionary<ArchiveProvider, string>(_archivePreferences.Executables ?? []);
            paths.Remove(_archivePreferences.Preferred);
            var updated = _archivePreferences with { Executables = paths };
            await Task.Run(() => _archiveStore.Save(updated));
            _archivePreferences = updated;
            await UpdateArchiveProviderAsync();
        }
        catch (Exception error) { ShowArchiveSettingsError(error); }
    }

    private void ShowArchiveSettingsError(Exception error)
    {
        ErrorText.Text = error.Message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
