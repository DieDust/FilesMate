using FilesMate.App.Controls.Tags;
using FilesMate.App.Localization;
using FilesMate.App.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Views;

public sealed partial class TagManagementPage : UserControl
{
    public TagManagementPage()
    {
        InitializeComponent();
        Heading.Text = StringTable.Get("TagsTitle");
        Lead.Text = StringTable.Get("TagsLead");
        TagsHeader.Text = StringTable.Get("TagsSection");
        NewTagLabel.Text = StringTable.Get("Tag_New");
        EmptyText.Text = StringTable.Get("Tag_Empty");
        Loaded += TagManagementPage_Loaded;
    }

    private void TagManagementPage_Loaded(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var store = App.MetadataStore;
        if (store is null)
        {
            return;
        }

        try
        {
            var tags = await store.ListTagsAsync().ConfigureAwait(true);
            TagRows.Children.Clear();
            for (var index = 0; index < tags.Count; index++)
            {
                TagRows.Children.Add(CreateTagRow(tags[index], index < tags.Count - 1));
            }

            EmptyText.Visibility = tags.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
    }

    private UIElement CreateTagRow(TagDefinition tag, bool showDivider)
    {
        var row = new Grid
        {
            MinHeight = 60,
            Padding = new Thickness(16, 8, 12, 8),
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        row.Children.Add(new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(TagEditor.ParseColor(tag.Color)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var name = new TextBlock
        {
            Text = tag.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var edit = CreateActionButton(StringTable.Get("Tag_Edit"), "\uE70F", tag, EditTag_Click);
        Grid.SetColumn(edit, 2);
        row.Children.Add(edit);

        var delete = CreateActionButton(StringTable.Get("Tag_Delete"), "\uE74D", tag, DeleteTag_Click);
        Grid.SetColumn(delete, 3);
        row.Children.Add(delete);

        if (!showDivider)
        {
            return row;
        }

        var host = new Grid();
        host.Children.Add(row);
        host.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(16, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = ThemeBrush("FilesMate.Divider.Brush"),
        });
        return host;
    }

    private static Button CreateActionButton(
        string label,
        string glyph,
        TagDefinition tag,
        RoutedEventHandler handler)
    {
        var button = new Button
        {
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            Tag = tag,
            Content = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 14,
                Glyph = glyph,
            },
        };
        if (Application.Current.Resources.TryGetValue("GlassButtonStyle", out var resource)
            && resource is Style style)
        {
            button.Style = style;
        }

        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += handler;
        return button;
    }

    private async void NewTagButton_Click(object sender, RoutedEventArgs e)
    {
        var value = await TagEditor.ShowAsync(XamlRoot, null);
        if (value is null || App.MetadataStore is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(value.Name))
        {
            ShowError(StringTable.Get("Tag_NameRequired"));
            return;
        }

        try
        {
            await App.MetadataStore.CreateTagAsync(value.Name, value.Color).ConfigureAwait(true);
            App.NotifyTagsChanged();
            await RefreshAsync();
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
    }

    private async void EditTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TagDefinition tag } || App.MetadataStore is null)
        {
            return;
        }

        var value = await TagEditor.ShowAsync(XamlRoot, tag);
        if (value is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(value.Name))
        {
            ShowError(StringTable.Get("Tag_NameRequired"));
            return;
        }

        try
        {
            await App.MetadataStore.UpdateTagAsync(tag.Id, value.Name, value.Color).ConfigureAwait(true);
            App.NotifyTagsChanged();
            await RefreshAsync();
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
    }

    private async void DeleteTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TagDefinition tag } || App.MetadataStore is null)
        {
            return;
        }

        if (!await TagEditor.ConfirmDeleteAsync(XamlRoot, tag.Name).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await App.MetadataStore.DeleteTagAsync(tag.Id).ConfigureAwait(true);
            App.NotifyTagsChanged();
            await RefreshAsync();
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
}
