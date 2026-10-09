using FilesMate.App.Controls.Filters;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Toolbar;

public sealed partial class AdaptiveCommandToolbar
{
    private IReadOnlyDictionary<ToolbarTool, Grid> CreateCustomizationPreviewUnits()
    {
        // This is the actual toolbar XAML, not a second set of hand-drawn command
        // buttons. Load it without subscriptions or menus, then detach its units.
        var preview = new AdaptiveCommandToolbar(customizationPreview: true);
        preview.LeadingGroups.Children.Clear();
        preview.ViewGroup.Children.Clear();
        foreach (var (target, source) in new[] {
            (preview.NewLabel, NewLabel), (preview.SortLabel, SortLabel),
            (preview.ShelfLabel, ShelfLabel), (preview.FolderSizesLabel, FolderSizesLabel),
            (preview.ViewLabel, ViewLabel) })
        {
            target.Text = source.Text;
            target.Visibility = source.Visibility;
        }
        preview.ViewIcon.Glyph = ViewIcon.Glyph;
        preview.HiddenFilesIcon.Glyph = HiddenFilesIcon.Glyph;
        preview.GroupingIcon.SetGrouping(_sort.EffectiveGrouping);
        preview.PaneDividerOne.Margin = PaneDividerOne.Margin;
        preview.PaneDividerTwo.Visibility = PaneDividerTwo.Visibility;
        preview.DualPaneButton.Style = DualPaneButton.Style;
        preview.FilterHeader.Content = InlineFolderFilter.CreateToolbarHeaderPreview();
        var units = new Dictionary<ToolbarTool, Grid>();
        foreach (var definition in ToolbarToolCatalog.All)
        {
            var unit = preview._toolbarUnits[definition.Id];
            unit.Visibility = Visibility.Visible;
            if (unit is Control control && _toolbarUnits[definition.Id] is Control source)
                control.Style = source.Style;
            MakePreviewInert(unit);
            // Content remains a real UIElement to avoid the recycled blank-cell
            // issue from assigning data-container Content during realization.
            var item = new Grid { Tag = definition.Id, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            item.Children.Add(unit);
            AutomationProperties.SetName(item, StringTable.Get(definition.LabelKey));
            AutomationProperties.SetAutomationId(item, "ToolbarPreview" + definition.Id);
            SetEditorToolTip(item, StringTable.Get(definition.LabelKey));
            units.Add(definition.Id, item);
        }
        return units;
    }

    private static void MakePreviewInert(FrameworkElement element)
    {
        element.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
        if (element is Control control) { control.IsTabStop = false; control.IsEnabled = true; }
        if (element is Button button) button.Flyout = null;
        // Use logical children: the XAML has not been attached or templated yet.
        switch (element)
        {
            case Panel panel:
                foreach (var child in panel.Children.OfType<FrameworkElement>()) MakePreviewInert(child);
                break;
            case Border { Child: FrameworkElement child }: MakePreviewInert(child); break;
            case ContentControl { Content: FrameworkElement content }: MakePreviewInert(content); break;
        }
    }
}
