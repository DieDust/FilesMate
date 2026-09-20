using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Theming;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.Tags;

public sealed record TagEditorValue(string Name, string Color);

public static class TagEditor
{
    public static readonly string[] Palette =
    [
        "#FF453A",
        "#FF9F0A",
        "#FFD60A",
        "#30D158",
        "#0A84FF",
        "#5E5CE6",
        "#BF5AF2",
        "#8E8E93",
    ];

    public static async Task<TagEditorValue?> ShowAsync(XamlRoot? root, TagDefinition? tag)
    {
        if (root is null)
        {
            return null;
        }

        var nameBox = new TextBox
        {
            Header = StringTable.Get("Tag_Name"),
            Text = tag?.Name ?? string.Empty,
            PlaceholderText = StringTable.Get("TagNamePlaceholder"),
            SelectionStart = 0,
        };
        var colorBox = CreateColorBox(tag?.Color ?? Palette[4]);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = StringTable.Get(tag is null ? "Tag_New" : "Tag_EditTitle"),
            Content = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    nameBox,
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = StringTable.Get("Tag_Color") },
                            colorBox,
                        },
                    },
                },
            },
            PrimaryButtonText = StringTable.Get(tag is null ? "Tag_Create" : "Save"),
            CloseButtonText = StringTable.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        ContentDialogTheme.Apply(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var color = (colorBox.SelectedItem as ComboBoxItem)?.Tag as string ?? Palette[4];
        return new TagEditorValue(nameBox.Text.Trim(), color);
    }

    public static async Task<bool> ConfirmDeleteAsync(XamlRoot? root, string name)
    {
        if (root is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = StringTable.Get("Tag_DeleteTitle"),
            Content = StringTable.Format("Tag_DeleteBody", name),
            PrimaryButtonText = StringTable.Get("Tag_Delete"),
            CloseButtonText = StringTable.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        ContentDialogTheme.Apply(dialog);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static Windows.UI.Color ParseColor(string value)
    {
        value = value.TrimStart('#');
        return value.Length == 6
            && byte.TryParse(value[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(value[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b)
            ? Windows.UI.Color.FromArgb(255, r, g, b)
            : Windows.UI.Color.FromArgb(255, 128, 128, 128);
    }

    private static ComboBox CreateColorBox(string selectedColor)
    {
        var box = new ComboBox
        {
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var color in Palette)
        {
            var item = new ComboBoxItem
            {
                Tag = color,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new Border
                        {
                            Width = 14,
                            Height = 14,
                            CornerRadius = new CornerRadius(7),
                            Background = new SolidColorBrush(ParseColor(color)),
                        },
                        new TextBlock { Text = color },
                    },
                },
            };
            box.Items.Add(item);
            if (string.Equals(color, selectedColor, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
            }
        }

        box.SelectedIndex = Math.Max(0, box.SelectedIndex);
        return box;
    }
}
