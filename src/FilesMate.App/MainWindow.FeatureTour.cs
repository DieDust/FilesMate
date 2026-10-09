using FilesMate.App.Localization;
using FilesMate.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    internal void ShowFeatureTourNotice(bool firstRun)
    {
        if (_windowClosed || Content is not FrameworkElement root) return;
        var history = FeatureTourHistory.Load(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath);
        if (history.Decide(Program.ProfileWasNew, App.Features.Completed, FeatureTourHistory.CurrentRelease, FeatureTourHistory.CurrentFeatures) == FeatureTourOffer.None) return;
        if (firstRun) _ = App.ShowFeatureSetupAsync(root);
        else _ = ShowReleaseHighlightsAsync(automatic: true);
    }
}
