using FilesMate.App.Localization;
using FilesMate.App.Theming;
using FilesMate.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FilesMate.App;

public partial class App
{
    private void ScheduleFeatureTour(MainWindow window)
    {
        if (Program.IsUiTestBuild || window.Content is not FrameworkElement root) return;
        window.StartAnnouncementDiagnostics();
        var history = FeatureTourHistory.Load(FeatureProfile);
        var offer = history.Decide(Program.ProfileWasNew, Features.Completed, FeatureTourHistory.CurrentRelease, FeatureTourHistory.CurrentFeatures);
        if (offer == FeatureTourOffer.None) return;
        var launch = AppLifecycle.PendingLaunch;
        if (launch.Folder is not null || launch.SelectPath is not null || launch.MissingPath is not null
            || launch.SearchAction is not null || launch.SettingsSection is not null || launch.SearchPage is not null) return;
        async void Offer()
        {
            await Task.Delay(1500);
            if (!root.IsLoaded || root.XamlRoot is null) return;
            if (offer == FeatureTourOffer.FirstRun) await ShowFeatureSetupAsync(root);
            else window.ShowFeatureTourNotice(firstRun: false);
        }
        if (root.IsLoaded) Offer();
        else
        {
            RoutedEventHandler? loaded = null;
            loaded = (_, _) => { root.Loaded -= loaded; Offer(); };
            root.Loaded += loaded;
        }
    }

    internal static void MarkFeatureTourOffered(bool firstRun)
    {
        var history = FeatureTourHistory.Load(FeatureProfile);
        history.Offered(firstRun, FeatureTourHistory.CurrentRelease, FeatureTourHistory.CurrentFeatures).Save(FeatureProfile);
    }

    internal static async Task ShowReleaseTourAsync(FrameworkElement root)
    {
        if (_setupShowing || root.XamlRoot is null || WindowForElement(root) is not { } window) return;
        await window.ShowReleaseHighlightsAsync(automatic: false);
    }

    internal static bool FeatureSetupShowing => _setupShowing;
}
