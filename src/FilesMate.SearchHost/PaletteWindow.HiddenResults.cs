using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.Search;
using System.Windows;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private void HideResults(SearchRow[] rows)
    {
        try
        {
            HiddenSearchResults.Hide(rows.Select(row => row.Hit), _host.Profile);
            _offset = 0;
            Search();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { ActionError(Loc.Get("Search_HideFailed") + error.Message); }
    }

    private void ConfirmRecycleResults(SearchRow[] rows)
    {
        var paths = rows.Select(row => row.Hit.Application is { } app ? app.ShortcutPath : row.Path).ToArray();
        if (paths.Any(path => path is null)) { ActionError(Loc.Get("Search_NoShortcut")); return; }
        if (paths.Any(path => !File.Exists(path) && !Directory.Exists(path))) { ActionError(Loc.Get("Search_StaleFiles")); return; }
        var shortcuts = rows.Any(row => row.IsApplication);
        _contextOpen = true;
        try
        {
            var text = Loc.Get("Recycle_ConfirmPrefix") + string.Join("\n\n", paths.Select(path => Path.GetFileName(path) + "\n" + path));
            if (shortcuts) text += Loc.Get("Shortcut_DeleteHint");
            if (MessageBox.Show(this, text, Loc.Get("Command_Recycle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _host.RunFileAction("Recycle", paths.Select(path => path!).ToArray());
            Dismiss();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        { ActionError(Loc.Get("Delete_FailedPrefix") + error.Message); }
        finally { _contextOpen = false; }
    }

    private void HiddenResults_Open(object sender, RoutedEventArgs e)
    {
        CancelSearch();
        SettingsPanel.Visibility = Visibility.Collapsed;
        HiddenResultsPanel.Visibility = Visibility.Visible;
        RefreshHiddenResults();
    }

    private void RefreshHiddenResults() => HiddenResultsList.ItemsSource = HiddenSearchResults.Load(_host.Profile).Values.OrderBy(item => item.Name).ToArray();
    private void HiddenResults_Back(object sender, RoutedEventArgs e) => ShowSettings(true, "");
    private void HiddenResults_Restore(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HiddenSearchResult item }) RestoreHidden([item.Key]);
    }
    private void HiddenResults_RestoreAll(object sender, RoutedEventArgs e) => RestoreHidden(HiddenSearchResults.Load(_host.Profile).Keys);
    private void RestoreHidden(IEnumerable<string> keys)
    {
        try { HiddenSearchResults.Restore(keys, _host.Profile); RefreshHiddenResults(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, Loc.Get("Search_RestoreHiddenFailed") + error.Message, Loc.Get("Search_Settings"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
