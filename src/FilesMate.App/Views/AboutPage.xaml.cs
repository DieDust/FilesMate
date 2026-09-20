using Loc = FilesMate.App.Localization.StringTable;
using System.Globalization;
using System.Reflection;

using FilesMate.App.Icons;
using FilesMate.App.Localization;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FilesMate.App.Views;

public sealed partial class AboutPage : UserControl
{
    private bool _syncingUpdates;
    public AboutPage()
    {
        InitializeComponent();
        Heading.Text = StringTable.Get("AboutTitle");
        Lead.Text = StringTable.Get("AboutLead");
        AboutBody.Text = StringTable.Get("AboutBody");
        VersionText.Text = string.Format(
            CultureInfo.InvariantCulture,
            StringTable.Get("AboutVersion"),
            ReadVersion());
        Loaded += (_, _) => RasterizeBrandMark();
        _syncingUpdates = true;
        AutoUpdateCheck.IsOn = App.Updates.AutomaticallyCheck;
        _syncingUpdates = false;
        if (App.Updates.Available is { } release)
        {
            UpdateStatus.Text = Loc.Get("Update_FoundPrefix") + release.DisplayVersion;
            InstallUpdateButton.Visibility = Visibility.Visible;
        }
    }

    private void AutoUpdateCheck_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingUpdates || UpdateStatus is null) return;
        try { App.Updates.SetAutomatic(AutoUpdateCheck.IsOn); }
        catch (Exception)
        {
            UpdateStatus.Text = Loc.Get("Update_SaveFailed");
            _syncingUpdates = true; AutoUpdateCheck.IsOn = App.Updates.AutomaticallyCheck; _syncingUpdates = false;
        }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatus.Text = Loc.Get("Update_Checking");
        try
        {
            var release = await App.Updates.CheckAsync(manual: true);
            UpdateStatus.Text = release is null ? Loc.Get("Update_Current") : Loc.Get("Update_FoundPrefix") + release.DisplayVersion;
            InstallUpdateButton.Visibility = release is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception error)
        {
            App.LogFailure("ManualUpdateCheck", error);
            UpdateStatus.Text = error is InvalidDataException ? Loc.Get("Update_MetadataInvalid") : Loc.Get("Update_CheckFailed");
        }
        finally { CheckUpdateButton.IsEnabled = true; }
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Updates.Available is { } release && App.WindowForElement(this) is { } window)
            await window.DownloadAndInstallUpdateAsync(release);
    }

    private void RasterizeBrandMark()
    {
        var pixels = ShellIconBinder.RasterizePixelSize(XamlRoot, 44);
        if (BrandMark.Source is SvgImageSource existing
            && existing.RasterizePixelWidth == pixels
            && existing.RasterizePixelHeight == pixels)
        {
            return;
        }

        BrandMark.Source = new SvgImageSource(new Uri("ms-appx:///Assets/Branding/FilesMate.svg"))
        {
            RasterizePixelWidth = pixels,
            RasterizePixelHeight = pixels,
        };
    }

    private static string ReadVersion()
    {
        var assembly = typeof(App).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
