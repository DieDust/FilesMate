using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.Platform.Windows.Archives;
using FilesMate.Platform.Windows.CompactMate;
using FilesMate.Platform.Windows.Processes;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

internal static class ArchiveAppPicker
{
    internal static async Task<bool> ShowAsync(FrameworkElement host, string path)
    {
        var providers = await Task.Run(() =>
        {
            var settings = new ArchivePreferencesStore(Program.SettingsPath(ArchivePreferencesStore.DefaultPath)).Load();
            return Enum.GetValues<ArchiveProvider>().Where(p => p is not (ArchiveProvider.Automatic or ArchiveProvider.BuiltIn))
                .Select(p => new ArchiveRoute(p, ArchiveProviderDiscovery.Find(p, settings.PathFor(p)), false))
                .Where(p => p.Executable is not null).OrderBy(p => p.Provider == settings.Preferred ? 0 : 1).ToArray();
        });
        if (!host.IsLoaded) return true;
        if (providers.Length == 0) return false;
        var list = new ComboBox { MinWidth = 280, MaxWidth = 420 };
        foreach (var provider in providers) list.Items.Add(new ComboBoxItem { Content = ArchiveProviderNames.Name(provider.Provider), Tag = provider });
        list.SelectedIndex = 0;
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 });
        content.Children.Add(list);
        var dialog = new ContentDialog { Title = StringTable.Get("Archive_OpenWithApp"), Content = content,
            PrimaryButtonText = StringTable.Get("Command_Open"), SecondaryButtonText = StringTable.Get("Archive_SystemOpenWith"),
            CloseButtonText = StringTable.Get("Cancel"), DefaultButton = ContentDialogButton.Primary, XamlRoot = host.XamlRoot };
        ContentDialogTheme.Apply(dialog, host);
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary) return false;
        if (result == ContentDialogResult.Primary && list.SelectedItem is ComboBoxItem { Tag: ArchiveRoute route })
        {
            var launch = ArchiveRouting.Create(route, CompactMateVerb.Open, [path], "", "");
            await ShellOperationWorker.RunAsync(() => DetachedProcess.Start(launch.Executable, launch.Arguments));
        }
        return true;
    }
}
