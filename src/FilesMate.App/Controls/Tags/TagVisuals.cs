using FilesMate.App.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace FilesMate.App.Controls.Tags;

internal static class TagVisuals
{
    public static void Apply(Panel host, IEnumerable<TagDefinition>? tags, bool showNames = true,
        double availableWidth = 160, int maxVisible = 2)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Children.Clear();

        var all = (tags ?? []).Where(t => t.Id > 0 && !string.IsNullOrWhiteSpace(t.Name))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id).DistinctBy(t => t.Id).ToArray();
        var fullText = string.Join(Environment.NewLine, all.Select(t => t.Name));
        ToolTipService.SetToolTip(host, all.Length > 0 ? fullText : null);
        AutomationProperties.SetName(host, fullText);
        availableWidth = Math.Max(0, availableWidth);
        host.MaxWidth = availableWidth;
        showNames &= availableWidth >= 72;
        var summary = TagPresentation.Summarize(all, showNames ? Math.Min(maxVisible, availableWidth >= 240 ? 2 : 1) : 2);
        var overflowWidth = summary.OverflowCount > 0 ? 30 : 0;
        var chipWidth = Math.Max(0, (availableWidth - overflowWidth - Math.Max(0, summary.Visible.Count - 1) * 4)
            / Math.Max(1, summary.Visible.Count));
        foreach (var tag in summary.Visible)
        {
            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = BrushFor(tag.Color),
                VerticalAlignment = VerticalAlignment.Center,
            };
            FrameworkElement visual = dot;
            if (showNames)
            {
                var content = new Grid { ColumnSpacing = 4 };
                content.ColumnDefinitions.Add(new() { Width = new GridLength(8) });
                content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                content.Children.Add(dot);
                var label = new TextBlock
                {
                    Text = tag.Name,
                    FontSize = 11,
                    MaxWidth = Math.Max(0, chipWidth - 24),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["FilesMate.SecondaryTextStyle"],
                };
                Grid.SetColumn(label, 1);
                content.Children.Add(label);
                visual = new Border
                {
                    Child = content, Padding = new Thickness(6, 0, 6, 0), Height = 20,
                    MaxWidth = chipWidth, CornerRadius = new CornerRadius(6),
                    Style = (Style)Application.Current.Resources["FilesMate.TagChipStyle"],
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            AutomationProperties.SetName(visual, tag.Name);
            ToolTipService.SetToolTip(visual, tag.Name);
            host.Children.Add(visual);
        }

        if (summary.OverflowCount > 0)
        {
            var overflow = new TextBlock
            {
                Text = $"+{summary.OverflowCount}",
                FontSize = 11,
                MaxWidth = Math.Max(0, availableWidth - summary.Visible.Count * ((showNames ? chipWidth : 8) + 4)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Style = (Style)Application.Current.Resources["FilesMate.SecondaryTextStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(overflow, fullText);
            ToolTipService.SetToolTip(overflow, fullText);
            host.Children.Add(overflow);
        }
    }

    private static Brush BrushFor(string color)
    {
        try
        {
            var value = color.TrimStart('#');
            if (value.Length == 6
                && byte.TryParse(value[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
                && byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)
                && byte.TryParse(value[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return new SolidColorBrush(Color.FromArgb(255, r, g, b));
            }
        }
        catch
        {
        }

        return new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    }
}
