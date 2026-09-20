using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Icons;
using FilesMate.App.Localization;
using FilesMate.Platform.Windows.Associations;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class AdvancedSettingsPage : UserControl
{
    private readonly DefaultFolderAssociation _association = new(new CurrentUserRegistry());
    private bool _syncing;

    public AdvancedSettingsPage()
    {
        InitializeComponent();
        Heading.Text = StringTable.Get("AdvancedTitle");
        Lead.Text = StringTable.Get("AdvancedLead");
        PerformanceHeader.Text = StringTable.Get("CacheSection");
        SystemHeader.Text = StringTable.Get("SystemSection");
        IconCacheCard.Title = StringTable.Get("IconCacheTitle");
        IconCacheCard.Description = StringTable.Get("IconCacheDescription");
        ClearCacheButton.Content = StringTable.Get("ClearCache");
        DataFolderCard.Title = StringTable.Get("DataFolderTitle");
        DataFolderCard.Description = StringTable.Get("DataFolderDescription");
        OpenDataFolderButton.Content = StringTable.Get("OpenDataFolder");
        DefaultAppCard.Title = StringTable.Get("DefaultAppTitle");
        DefaultAppCard.Description = StringTable.Get("DefaultAppDescription");
        DefaultAppToggle.OnContent = StringTable.Get("On");
        DefaultAppToggle.OffContent = StringTable.Get("Off");
        ClassicExplorerCard.Title = StringTable.Get("ClassicExplorerTitle");
        ClassicExplorerCard.Description = StringTable.Get("ClassicExplorerDescription");
        ClassicExplorerButton.Content = StringTable.Get("ClassicExplorerOpen");
        _syncing = true;
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe) && _association.HasOurCommand(exe) && !_association.IsEnabled(exe))
        {
            try
            {
                _association.Enable(exe);
            }
            catch (Exception)
            {
                // Keep the toggle off when a previous registration cannot be repaired.
            }
        }

        DefaultAppToggle.IsOn = !string.IsNullOrEmpty(exe) && _association.IsEnabled(exe);
        _syncing = false;
    }

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        ShellIconBinder.ClearCache();
        StatusText.Text = StringTable.Get("CacheCleared");
        StatusText.Visibility = Visibility.Visible;
    }

    private async void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FilesMate");
            Directory.CreateDirectory(path);
            _ = await Windows.System.Launcher.LaunchFolderPathAsync(path);
        }
        catch (Exception)
        {
            StatusText.Text = StringTable.Get("DefaultAppFailed");
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private async void ClassicExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        try
        {
            if (string.IsNullOrEmpty(exe))
            {
                ClassicExplorer.Launch();
                return;
            }

            await Task.Run(() => ClassicExplorer.Launch(_association, exe)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            StatusText.Text = StringTable.Get("ClassicExplorerFailed");
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private void DefaultAppToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            _syncing = true;
            DefaultAppToggle.IsOn = false;
            _syncing = false;
            return;
        }

        try
        {
            if (DefaultAppToggle.IsOn)
            {
                _association.Enable(exe);
            }
            else
            {
                _association.Disable(exe);
            }

            App.NotifyFolderHandlerChanged();
            StatusText.Text = StringTable.Get("DefaultAppUpdated");
            StatusText.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            _syncing = true;
            DefaultAppToggle.IsOn = !DefaultAppToggle.IsOn;
            _syncing = false;
            StatusText.Text = StringTable.Get("DefaultAppFailed");
            StatusText.Visibility = Visibility.Visible;
        }
    }
}
