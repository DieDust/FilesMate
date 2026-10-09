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
        _searchSequence++; // New typing cancels an earlier pending reveal.
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
        // Collapsed Expander content may have no visual ancestors yet. Reveal the
        // catalog's named container before resolving and focusing its target.
        if (entry.RevealContainerName is { } containerName && root.FindName(containerName) is Expander container)
            container.IsExpanded = true;
        // Expanders and suggestion popups finish their layout/focus work on a
        // later dispatcher turn. Resolve after that turn and scroll the owning
        // settings viewer after its expanded extent has settled.
        await Task.Delay(25);
        if (sequence != _searchSequence || !ReferenceEquals(SectionHost.Content, section)) return;
        var target = root.FindName(entry.TargetName) as FrameworkElement
            ?? Descendants(root).OfType<FrameworkElement>().FirstOrDefault(element => element.Name == entry.TargetName);
        if (target is null) return;
        for (DependencyObject? ancestor = target; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is Expander expander) expander.IsExpanded = true;
        var stable = 0;
        var priorExtent = -1d;
        var priorOffset = -1d;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(25);
            if (sequence != _searchSequence || !root.IsLoaded || !ReferenceEquals(SectionHost.Content, section)) return;
            root.UpdateLayout();
            if (!target.IsLoaded || target.ActualHeight <= 0 || SectionScroller.ViewportHeight <= 0) continue;
            var position = target.TransformToVisual(SectionHost).TransformPoint(new Windows.Foundation.Point());
            var extent = SectionScroller.ScrollableHeight;
            var offset = Math.Clamp(position.Y - SectionScroller.ViewportHeight * .2, 0, extent);
            SectionScroller.ChangeView(null, offset, null, disableAnimation: true);
            stable = Math.Abs(extent - priorExtent) < .5 && Math.Abs(offset - priorOffset) < .5 ? stable + 1 : 0;
            priorExtent = extent;
            priorOffset = offset;
            if (stable >= 2) break;
        }
        if (sequence != _searchSequence || !target.IsLoaded || !ReferenceEquals(SectionHost.Content, section)) return;
        var focus = target is Control { IsTabStop: true, IsEnabled: true } control ? control
            : Descendants(target).OfType<Control>().FirstOrDefault(candidate => candidate.IsTabStop && candidate.IsEnabled);
        focus?.Focus(FocusState.Keyboard);

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
