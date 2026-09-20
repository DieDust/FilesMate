using FilesMate.App.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace FilesMate.App.Controls.Tags;

internal static class TagVisuals
{
    public static void Apply(Panel host, IEnumerable<TagDefinition>? tags, bool showNames = true)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Children.Clear();

        var summary = TagPresentation.Summarize(tags ?? []);
        foreach (var tag in summary.Visible)
        {
            var pill = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = showNames ? 4 : 0,
                VerticalAlignment = VerticalAlignment.Center,
            };
            pill.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = BrushFor(tag.Color),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (showNames)
            {
                pill.Children.Add(new TextBlock
                {
                    Text = tag.Name,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["FilesMate.SecondaryTextStyle"],
                });
            }

            AutomationProperties.SetName(pill, tag.Name);
            host.Children.Add(pill);
        }

        if (summary.OverflowCount > 0)
        {
            var overflow = new TextBlock
            {
                Text = $"+{summary.OverflowCount}",
                FontSize = 11,
                Style = (Style)Application.Current.Resources["FilesMate.SecondaryTextStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(overflow, $"{summary.OverflowCount} more tags");
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
