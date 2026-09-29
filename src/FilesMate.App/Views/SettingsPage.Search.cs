using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Views;

public sealed partial class SettingsPage
{
    private SettingsSearchEntry? _searchTarget;
    private int _searchSequence;

    private void SettingsSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var matches = SettingsSearchCatalog.Search(sender.Text);
        sender.ItemsSource = matches;
        SettingsNoResults.Visibility = !string.IsNullOrWhiteSpace(sender.Text) && matches.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SettingsSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var entry = args.ChosenSuggestion as SettingsSearchEntry ?? SettingsSearchCatalog.Search(sender.Text).FirstOrDefault();
        if (entry is not null) NavigateToSetting(entry);
    }

    private void NavigateToSetting(SettingsSearchEntry entry)
    {
        DismissSearch();
        _searchTarget = entry;
        ShowSection(entry.Category);
    }

    internal void DismissSearch()
    {
        _searchSequence++;
        _searchTarget = null;
        SettingsSearchBox.IsSuggestionListOpen = false;
        SettingsSearchBox.Text = "";
        SettingsSearchBox.ItemsSource = null;
        SettingsNoResults.Visibility = Visibility.Collapsed;
    }

    private async void RevealSearchTarget(string category, UIElement section)
    {
        if (_searchTarget is not { } entry || entry.Category != category || section is not FrameworkElement root) return;
        _searchTarget = null;
        var sequence = _searchSequence;
        for (var attempt = 0; attempt < 20 && (!root.IsLoaded || root.ActualHeight <= 0); attempt++) await Task.Delay(25);
        if (sequence != _searchSequence || !root.IsLoaded || !ReferenceEquals(SectionHost.Content, section)) return;
        var target = root.FindName(entry.TargetName) as FrameworkElement ?? root;
        var focus = target is Control { IsTabStop: true, IsEnabled: true } control ? control
            : Descendants(target).OfType<Control>().FirstOrDefault(candidate => candidate.IsTabStop && candidate.IsEnabled);
        focus?.Focus(FocusState.Keyboard);
        target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .2 });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
