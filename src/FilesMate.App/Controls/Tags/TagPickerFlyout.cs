using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Core.Metadata;
using FilesMate.App.Theming;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.System;

namespace FilesMate.App.Controls.Tags;

/// <summary>
/// Tag picker used by the file context menu. Hovering Add tags reveals this
/// panel to the right; applying a tag writes immediately so multiple tags can
/// be toggled without a Done button.
/// </summary>
public static class TagPickerFlyout
{
    public static FrameworkElement CreatePanel(
        IFileMetadataStore store,
        IReadOnlyList<FileIdentity> identities,
        Action? applied)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(identities);
        return new TagPickerPanel(store, identities, applied);
    }

    public static Flyout Create(
        IFileMetadataStore store,
        IReadOnlyList<FileIdentity> identities,
        Action? applied)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
            ShouldConstrainToRootBounds = true,
            AreOpenCloseAnimationsEnabled = false,
        };
        if (Application.Current.Resources.TryGetValue("FilesMate.SubmenuFlyoutPresenterStyle", out var resource)
            && resource is Style style)
        {
            flyout.FlyoutPresenterStyle = style;
        }

        flyout.Content = CreatePanel(store, identities, applied);
        FlyoutTheme.FollowHost(flyout);
        return flyout;
    }

    public static void Show(
        FrameworkElement owner,
        IFileMetadataStore store,
        IReadOnlyList<FileIdentity> identities,
        Action? applied)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Create(store, identities, applied).ShowAt(owner, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.Bottom,
            ShowMode = FlyoutShowMode.Standard,
        });
    }
}

internal sealed class TagPickerPanel : UserControl
{
    private readonly IFileMetadataStore _store;
    private readonly IReadOnlyList<FileIdentity> _identities;
    private readonly Action? _applied;
    private readonly TextBox _filter;
    private readonly StackPanel _list;
    private readonly Dictionary<long, Button> _removeButtons = [];
    private IReadOnlyList<TagDefinition> _tags = [];
    private HashSet<long> _appliedIds = [];
    private bool _busy;

    public TagPickerPanel(
        IFileMetadataStore store,
        IReadOnlyList<FileIdentity> identities,
        Action? applied)
    {
        _store = store;
        _identities = identities;
        _applied = applied;
        Width = 256;
        _filter = new TextBox
        {
            PlaceholderText = StringTable.Get("TagPickerPlaceholder"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 34,
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 13,
            CornerRadius = new CornerRadius(6),
        };
        AutomationProperties.SetName(_filter, StringTable.Get("TagPickerTitle"));
        _filter.TextChanged += (_, _) => RebuildList();
        _filter.KeyDown += Filter_KeyDown;
        _list = new StackPanel { Spacing = 2 };
        Content = new StackPanel
        {
            Margin = new Thickness(12, 8, 12, 10),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = StringTable.Get("TagPickerTitle"),
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                },
                _filter,
                new ScrollViewer
                {
                    Content = _list,
                    MaxHeight = 280,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
                new TextBlock
                {
                    Text = StringTable.Get("TagPickerActionHint"),
                    FontSize = 12,
                    Opacity = .65,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
        RebuildList();
        Loaded += (_, _) => _ = LoadAsync();
    }

    public void FocusFilter() => _filter.Focus(FocusState.Keyboard);

    private async Task LoadAsync()
    {
        if (_identities.Count == 0)
        {
            return;
        }

        try
        {
            _tags = await _store.ListTagsAsync().ConfigureAwait(true);
            var perFile = new List<IReadOnlyList<TagDefinition>>(_identities.Count);
            foreach (var identity in _identities)
            {
                perFile.Add(await _store.GetTagsAsync(identity).ConfigureAwait(true));
            }

            _appliedIds = TagPickerLogic.SharedIds(perFile);
            RebuildList();
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Tag picker load failed: {0}", error);
        }
    }

    private void Filter_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }

        args.Handled = true;
        var query = _filter.Text;
        if (TagPickerLogic.Match(_tags, query) is { } match)
        {
            _ = ApplyAsync(match.Id, apply: true);
            return;
        }

        if (TagPickerLogic.CanCreate(_tags, query))
        {
            _ = CreateAndApplyAsync(query.Trim());
        }
    }

    private void RebuildList()
    {
        _list.Children.Clear();
        _removeButtons.Clear();
        var query = _filter.Text;
        if (TagPickerLogic.CanCreate(_tags, query))
        {
            var name = query.Trim();
            var create = CreateRowButton(
                StringTable.Get("TagCreate") + " “" + name + "”",
                color: "#5B8DEF");
            create.Click += (_, _) => _ = DispatcherQueue.TryEnqueue(() => _ = CreateAndApplyAsync(name));
            _list.Children.Add(create);
        }

        foreach (var tag in TagPickerLogic.Filter(_tags, query))
        {
            _list.Children.Add(CreateTagRow(tag));
        }
        if (_list.Children.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = StringTable.Get("TagPickerEmpty"),
                FontSize = 13,
                Opacity = .65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 8, 2, 8),
            });
        }
    }

    private UIElement CreateTagRow(TagDefinition tag)
    {
        var applied = _appliedIds.Contains(tag.Id);
        var remove = CreateRemoveButton(tag);
        SetRemoveVisible(remove, applied);
        _removeButtons[tag.Id] = remove;
        var add = CreateRowButton(tag.Name, tag.Color);
        var captured = tag.Id;
        add.Click += (_, _) =>
        {
            if (!_appliedIds.Contains(captured))
            {
                _ = DispatcherQueue.TryEnqueue(() => _ = ApplyAsync(captured, apply: true));
            }
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(add);
        Grid.SetColumn(remove, 1);
        row.Children.Add(remove);
        return row;
    }

    private Button CreateRowButton(string name, string color)
    {
        var content = new Grid { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(ParseColor(color)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var label = new TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0),
        };
        if (TryStyle("FilesMate.ContextMenuItemStyle", out var style))
        {
            button.Style = style;
        }

        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    private Button CreateRemoveButton(TagDefinition tag)
    {
        var button = new Button
        {
            Content = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 10,
                Glyph = "\uE711",
            },
        };
        if (TryStyle("QuietButtonStyle", out var style))
        {
            button.Style = style;
        }

        button.Width = 22;
        button.Height = 22;
        button.MinWidth = 22;
        button.Padding = new Thickness(0);
        button.VerticalAlignment = VerticalAlignment.Center;

        AutomationProperties.SetName(button, StringTable.Get("Tag_Remove"));
        ToolTipService.SetToolTip(button, StringTable.Get("Tag_Remove"));
        var id = tag.Id;
        button.Click += (_, _) => _ = DispatcherQueue.TryEnqueue(() => _ = ApplyAsync(id, apply: false));
        return button;
    }

    private async Task CreateAndApplyAsync(string name)
    {
        if (_busy || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _busy = true;
        try
        {
            var tag = await _store.CreateTagAsync(name, "#5B8DEF").ConfigureAwait(true);
            _tags = [.. _tags, tag];
            _filter.Text = string.Empty;
            App.NotifyTagsChanged();
            await WriteAsync(tag.Id, apply: true).ConfigureAwait(true);
            MarkApplied(tag.Id, apply: true);
        }
        catch (InvalidOperationException)
        {
            var existing = TagPickerLogic.Match(_tags, name)
                ?? (await _store.ListTagsAsync().ConfigureAwait(true))
                    .FirstOrDefault(tag => tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _filter.Text = string.Empty;
                await WriteAsync(existing.Id, apply: true).ConfigureAwait(true);
                MarkApplied(existing.Id, apply: true);
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Tag creation failed: {0}", error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ApplyAsync(long id, bool apply)
    {
        if (_busy || _identities.Count == 0)
        {
            return;
        }

        _busy = true;
        try
        {
            await WriteAsync(id, apply).ConfigureAwait(true);
            MarkApplied(id, apply);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Tag apply failed: {0}", error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task WriteAsync(long id, bool apply)
    {
        foreach (var identity in _identities)
        {
            var current = await _store.GetTagsAsync(identity).ConfigureAwait(true);
            var next = TagPickerLogic.With(current.Select(tag => tag.Id).ToArray(), id, apply);
            await _store.SetTagsAsync(identity, next).ConfigureAwait(true);
        }
    }

    private void MarkApplied(long id, bool apply)
    {
        _appliedIds = TagPickerLogic.With(_appliedIds, id, apply);
        if (_removeButtons.TryGetValue(id, out var remove))
        {
            SetRemoveVisible(remove, apply);
        }
        else
        {
            RebuildList();
        }

        _applied?.Invoke();
    }

    private static void SetRemoveVisible(Button button, bool visible)
    {
        button.Opacity = visible ? 1 : 0;
        button.IsHitTestVisible = visible;
        button.IsTabStop = visible;
    }

    private static bool TryStyle(string key, out Style style)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Style found)
        {
            style = found;
            return true;
        }

        style = null!;
        return false;
    }

    private static Windows.UI.Color ParseColor(string value)
    {
        value = value.TrimStart('#');
        return value.Length == 6
            && byte.TryParse(value[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(value[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b)
            ? Windows.UI.Color.FromArgb(255, r, g, b)
            : Windows.UI.Color.FromArgb(255, 128, 128, 128);
    }
}
