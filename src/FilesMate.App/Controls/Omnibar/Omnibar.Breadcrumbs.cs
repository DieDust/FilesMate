using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App.Controls.Omnibar;

public sealed partial class Omnibar
{
    private readonly List<Grid> _crumbViews = [];
    private double[] _crumbWidths = [];
    private Button? _ancestorButton;
    private BreadcrumbOverflow.Layout? _breadcrumbLayout;
    private bool _crumbMeasurementPending;
    private int _crumbMeasurementVersion;

    private void PathViewport_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutCrumbs();

    private void BuildAdaptiveCrumbs()
    {
        _breadcrumbLayout = null;
        Crumbs.Children.Clear();
        _crumbViews.Clear();
        _crumbWidths = new double[_segments.Count];
        var style = (Style)Application.Current.Resources["QuietButtonStyle"];
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            var label = new Grid { ColumnSpacing = segment.HasIcon ? 8 : 0 };
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (segment.HasIcon)
                label.Children.Add(new FontIcon { Glyph = segment.Glyph, FontSize = 14, FontFamily = new FontFamily("Segoe Fluent Icons") });
            var text = new TextBlock { Text = segment.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            label.Children.Add(text);
            var name = new Button
            {
                Content = label, Tag = segment.Path, Style = style, MinWidth = 0, MinHeight = 28,
                Padding = new Thickness(8, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(name, segment.Name);
            ToolTipService.SetToolTip(name, segment.Path);
            name.Click += CrumbName_Click;
            var chevron = new Button
            {
                Content = new FontIcon { Glyph = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 10 },
                Tag = segment.Path, Width = 22, MinWidth = 0, MinHeight = 28, Padding = new Thickness(0), Style = style,
            };
            AutomationProperties.SetName(chevron, segment.Name);
            chevron.Click += CrumbChevron_Click;
            var view = new Grid { Margin = new Thickness(2, 0, 0, 0), MinHeight = 28 };
            view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            view.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            view.Children.Add(name);
            Grid.SetColumn(chevron, 1);
            view.Children.Add(chevron);
            _crumbViews.Add(view);
            Crumbs.Children.Add(view);
        }

        _ancestorButton ??= CreateAncestorButton(style);
        // Measure after attachment, so the inherited font and XAML text scale are applied.
        _crumbMeasurementPending = true;
        var version = ++_crumbMeasurementVersion;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _crumbMeasurementVersion) return;
            for (var i = 0; i < _crumbViews.Count; i++)
            {
                var view = _crumbViews[i];
                view.Measure(new Size(double.PositiveInfinity, 38));
                // Nested text/button layout rounds separately at fractional display scales.
                // Keep two DIPs of slack so a subpixel loss cannot trigger an ellipsis.
                var width = Math.Ceiling(view.DesiredSize.Width) + 2;
                _crumbWidths[i] = i == _segments.Count - 1 ? width : Math.Min(260, width);
            }
            _crumbMeasurementPending = false;
            LayoutCrumbs();
        });
    }

    private Button CreateAncestorButton(Style style)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = "\uE712", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 },
            Width = 36, MinWidth = 0, MinHeight = 28, Padding = new Thickness(0), Style = style,
        };
        AutomationProperties.SetName(button, StringTable.Get("Address_HiddenAncestors"));
        AutomationProperties.SetAutomationId(button, "AddressHiddenAncestors");
        ToolTipService.SetToolTip(button, StringTable.Get("Address_HiddenAncestors"));
        button.Click += (_, _) => ShowHiddenAncestors();
        return button;
    }

    private void LayoutCrumbs()
    {
        if (_crumbMeasurementPending || _ancestorButton is null || PathViewport.ActualWidth <= 0) return;
        // Leave a small, reliable blank target for entering the full path editor.
        var layout = BreadcrumbOverflow.Fit(_crumbWidths, Math.Max(0, PathViewport.ActualWidth - 16));
        if (layout == _breadcrumbLayout) return;
        var structureChanged = _breadcrumbLayout is null
            || layout.ShowRoot != _breadcrumbLayout.ShowRoot
            || layout.SuffixStart != _breadcrumbLayout.SuffixStart
            || layout.HasOverflow != _breadcrumbLayout.HasOverflow;
        DismissCrumbFolders();
        _breadcrumbLayout = layout;
        for (var i = 0; i < _crumbViews.Count; i++)
            _crumbViews[i].Width = Math.Max(0, (i == _crumbViews.Count - 1 ? layout.CurrentWidth : _crumbWidths[i]) - 2);
        if (!structureChanged) return;
        Crumbs.Children.Clear();
        if (layout.ShowRoot) Crumbs.Children.Add(_crumbViews[0]);
        if (layout.HasOverflow) Crumbs.Children.Add(_ancestorButton);
        for (var i = layout.SuffixStart; i < _crumbViews.Count; i++) Crumbs.Children.Add(_crumbViews[i]);
    }

    private void ShowHiddenAncestors()
    {
        if (_breadcrumbLayout is not { HasOverflow: true } layout || _ancestorButton is null) return;
        DismissSearch();
        _crumbRequest?.Cancel();
        var key = "ancestors:" + _session.Path;
        if (CrumbFolderPopup.IsOpen && _crumbFlyoutPath == key)
        {
            RequestCrumbFolderClose();
            return;
        }
        var first = layout.ShowRoot ? 1 : 0;
        ShowCrumbFolderItems(_segments.GetRange(first, layout.SuffixStart - first), _ancestorButton, key);
    }
}
