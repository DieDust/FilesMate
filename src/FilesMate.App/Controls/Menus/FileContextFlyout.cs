using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Tags;
using FilesMate.App.Localization;
using FilesMate.Core.Entries;
using FilesMate.App.Theming;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

using Windows.Foundation;

namespace FilesMate.App.Controls.Menus;

public static class FileContextFlyout
{
    public static void ShowAt(Flyout menu, FrameworkElement anchor, Point position)
    {
        // Clamp the desired center before WinUI chooses a side. Its fallback for a
        // tall flyout near the bottom can otherwise move the menu all the way up.
        if (anchor.XamlRoot?.Content is FrameworkElement root && menu.Content is FrameworkElement content)
        {
            var maximumWidth = Application.Current.Resources.TryGetValue("FilesMate.ContextMenu.MaxWidth", out var width)
                && width is double value ? value : 320;
            // ContextFlyoutPresenterStyle has 4x6 padding and a one-pixel border.
            content.Measure(new Size(Math.Max(1, maximumWidth - 10), double.PositiveInfinity));
            var height = Math.Min(content.DesiredSize.Height + 14, Math.Max(1, root.ActualHeight - 16));
            var pointer = anchor.TransformToVisual(root).TransformPoint(position);
            pointer.Y = Math.Clamp(pointer.Y, 8 + height / 2, Math.Max(8 + height / 2, root.ActualHeight - 8 - height / 2));
            position = root.TransformToVisual(anchor).TransformPoint(pointer);
        }
        menu.ShowAt(anchor, new FlyoutShowOptions { Position = position, Placement = FlyoutPlacementMode.Right });
    }

    public static Flyout Create(
        FileContextMenuLayout layout,
        Action<AppCommandId> invoke,
        Action? showMoreNative = null,
        Action<EntrySortColumn>? sort = null,
        Action<FileLayoutKind>? changeLayout = null,
        UIElement? tagPicker = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(invoke);

        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Right,
            ShouldConstrainToRootBounds = true,
            AreOpenCloseAnimationsEnabled = false,
        };
        if (TryStyle("FilesMate.ContextFlyoutPresenterStyle", out Style presenter))
        {
            flyout.FlyoutPresenterStyle = presenter;
        }

        var submenu = new SubmenuHost();
        var column = new StackPanel { Spacing = 0 };
        if (layout.Primary.Count > 0)
        {
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 2, 4, 4),
            };
            bar.PointerEntered += (_, _) => submenu.Hide();
            foreach (var command in layout.Primary)
            {
                bar.Children.Add(CreateCommandButton(command, flyout, invoke));
            }

            column.Children.Add(bar);
            column.Children.Add(CreateSeparator());
        }

        foreach (var entry in layout.Items)
        {
            if (entry.IsSeparator)
            {
                column.Children.Add(CreateSeparator());
                continue;
            }

            column.Children.Add(CreateItemButton(
                entry,
                flyout,
                invoke,
                showMoreNative,
                sort,
                changeLayout,
                submenu,
                tagPicker));
        }

        flyout.Content = column;
        FlyoutTheme.FollowHost(flyout);
        flyout.Closed += (_, _) => submenu.Hide();
        return flyout;
    }

    private static Button CreateCommandButton(
        AppCommand command,
        Flyout flyout,
        Action<AppCommandId> invoke)
    {
        var button = new Button
        {
            IsEnabled = command.Enabled,
            Content = CreateGlyph(command.Glyph, 16),
        };
        if (TryStyle("FilesMate.ContextCommandButtonStyle", out Style style))
        {
            button.Style = style;
        }

        ToolTipService.SetToolTip(button, command.Tooltip);
        var id = command.Id;
        button.Click += (_, _) =>
        {
            flyout.Hide();
            invoke(id);
        };
        return button;
    }

    private static Button CreateItemButton(
        ContextMenuEntry entry,
        Flyout flyout,
        Action<AppCommandId> invoke,
        Action? showMoreNative,
        Action<EntrySortColumn>? sort,
        Action<FileLayoutKind>? changeLayout,
        SubmenuHost submenu,
        UIElement? tagPicker)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = CreateGlyph(entry.Glyph, 16);
        icon.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(icon);

        var label = new TextBlock
        {
            Text = entry.Label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        if (!string.IsNullOrEmpty(entry.Shortcut))
        {
            var shortcut = new TextBlock
            {
                Text = entry.Shortcut,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            };
            if (TryStyle("FilesMate.MenuShortcutStyle", out var shortcutStyle)) shortcut.Style = shortcutStyle;

            Grid.SetColumn(shortcut, 2);
            grid.Children.Add(shortcut);
        }

        if (entry.HasChevron)
        {
            var chevron = new TextBlock
            {
                Text = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12,
                Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            if (TryStyle("FilesMate.MenuShortcutStyle", out var chevronStyle)) chevron.Style = chevronStyle;

            Grid.SetColumn(chevron, 3);
            grid.Children.Add(chevron);
        }

        var button = new Button
        {
            Content = grid,
            IsEnabled = entry.Enabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        if (TryStyle("FilesMate.ContextMenuItemStyle", out Style style))
        {
            button.Style = style;
        }

        if (entry.Command is AppCommandId.AddTags && tagPicker is not null)
        {
            var tags = submenu.EnsureTags(tagPicker);
            button.PointerEntered += (_, _) => submenu.ShowTags(button, tags);
            button.Click += (_, _) => submenu.ShowTags(button, tags);
            return button;
        }

        if (entry.Children is { Count: > 0 } children)
        {
            var menu = CreateSubmenu();
            foreach (var child in children)
            {
                var item = new MenuFlyoutItem { Text = child.Label, IsEnabled = child.Enabled };
                if (TryStyle("FilesMate.MenuFlyoutItemStyle", out Style childStyle)) item.Style = childStyle;
                if (!string.IsNullOrEmpty(child.Shortcut)) item.KeyboardAcceleratorTextOverride = child.Shortcut;
                item.Click += (_, _) => { flyout.Hide(); if (child.Command is { } id) invoke(id); };
                menu.Items.Add(item);
            }
            OpenSubmenuOnHover(button, menu, submenu);
            return button;
        }

        if (entry.Command is AppCommandId.Sort && sort is not null)
        {
            OpenSubmenuOnHover(button, CreateSortFlyout(flyout, sort), submenu);
            return button;
        }

        if (entry.Command is AppCommandId.ChangeLayout && changeLayout is not null)
        {
            OpenSubmenuOnHover(button, CreateLayoutFlyout(flyout, changeLayout), submenu);
            return button;
        }

        if (entry.Command is AppCommandId.Compress)
        {
            OpenSubmenuOnHover(
                button,
                CreateCommandSubmenu(
                    flyout,
                    invoke,
                    AppCommandId.CompressZip,
                    AppCommandId.Compress7z,
                    AppCommandId.CompressNew),
                submenu);
            return button;
        }

        if (entry.Command is AppCommandId.Extract)
        {
            OpenSubmenuOnHover(
                button,
                CreateCommandSubmenu(
                    flyout,
                    invoke,
                    AppCommandId.SmartExtract,
                    AppCommandId.ExtractHere,
                    AppCommandId.ExtractToFolder,
                    AppCommandId.ExtractToOther),
                submenu);
            return button;
        }

        button.PointerEntered += (_, _) => submenu.Hide();
        if (entry.IsShowMore)
        {
            button.Click += (_, _) =>
            {
                showMoreNative?.Invoke();
                flyout.Hide();
            };
            return button;
        }

        button.Click += (_, _) =>
        {
            flyout.Hide();
            if (entry.Command is AppCommandId id)
            {
                invoke(id);
            }
        };
        return button;
    }

    private static void OpenSubmenuOnHover(Button button, MenuFlyout menu, SubmenuHost submenu)
    {
        button.PointerEntered += (_, _) => submenu.Show(button, menu);
        button.Click += (_, _) => submenu.Show(button, menu);
        button.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Right) return;
            submenu.Show(button, menu);
            e.Handled = true;
        };
    }

    private static void ShowSubmenu(FrameworkElement anchor, FlyoutBase menu) =>
        menu.ShowAt(anchor, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
            ShowMode = FlyoutShowMode.TransientWithDismissOnPointerMoveAway,
        });

    private static MenuFlyout CreateSortFlyout(Flyout parent, Action<EntrySortColumn> sort)
    {
        var menu = CreateSubmenu();
        AddSubItem(menu, parent, "Sort_Name", () => sort(EntrySortColumn.Name));
        AddSubItem(menu, parent, "Sort_Modified", () => sort(EntrySortColumn.Modified));
        AddSubItem(menu, parent, "Sort_Type", () => sort(EntrySortColumn.Type));
        AddSubItem(menu, parent, "Sort_Size", () => sort(EntrySortColumn.Size));
        foreach (var column in FilesMate.App.Models.DetailsColumn.Defaults().Skip(4))
        {
            var item = new MenuFlyoutItem { Text = column.Title };
            if (TryStyle("FilesMate.MenuFlyoutItemStyle", out Style style)) item.Style = style;
            item.Click += (_, _) => { parent.Hide(); sort(column.Sort); };
            menu.Items.Add(item);
        }
        return menu;
    }

    private static MenuFlyout CreateLayoutFlyout(Flyout parent, Action<FileLayoutKind> changeLayout)
    {
        var menu = CreateSubmenu();
        AddSubItem(menu, parent, "Layout_Details", () => changeLayout(FileLayoutKind.Details));
        AddSubItem(menu, parent, "Layout_LargeIcons", () => changeLayout(FileLayoutKind.Grid));
        return menu;
    }

    private static MenuFlyout CreateCommandSubmenu(
        Flyout parent,
        Action<AppCommandId> invoke,
        params AppCommandId[] ids)
    {
        var menu = CreateSubmenu();
        foreach (var id in ids)
        {
            var item = new MenuFlyoutItem { Text = CommandCatalog.Resolve(id, CommandContext.SingleFile).Label };
            if (TryStyle("FilesMate.MenuFlyoutItemStyle", out Style style))
            {
                item.Style = style;
            }

            var captured = id;
            item.Click += (_, _) =>
            {
                parent.Hide();
                invoke(captured);
            };
            menu.Items.Add(item);
        }

        return menu;
    }

    private static MenuFlyout CreateSubmenu()
    {
        var menu = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
            AreOpenCloseAnimationsEnabled = false,
        };
        if (TryStyle("FilesMate.MenuFlyoutPresenterStyle", out Style style))
        {
            menu.MenuFlyoutPresenterStyle = style;
        }

        FlyoutTheme.FollowHost(menu);
        return menu;
    }

    private static void AddSubItem(MenuFlyout menu, Flyout parent, string labelKey, Action invoke)
    {
        var item = new MenuFlyoutItem { Text = StringTable.Get(labelKey) };
        if (TryStyle("FilesMate.MenuFlyoutItemStyle", out Style style))
        {
            item.Style = style;
        }

        item.Click += (_, _) =>
        {
            parent.Hide();
            invoke();
        };
        menu.Items.Add(item);
    }

    private static Border CreateSeparator()
    {
        var line = new Border
        {
            Height = 1,
            Margin = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (TryStyle("FilesMate.MenuSeparatorStyle", out var style)) line.Style = style;

        return line;
    }

    private static FontIcon CreateGlyph(string glyph, double size)
    {
        var icon = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = glyph,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (TryBrush("FilesMate.Selection.AccentBrush", out var accent))
        {
            icon.Foreground = accent;
        }

        return icon;
    }

    private sealed class SubmenuHost
    {
        public MenuFlyout? Current;
        public Flyout? Tags;

        public Flyout EnsureTags(UIElement panel)
        {
            if (Tags is not null)
            {
                return Tags;
            }

            var flyout = new Flyout
            {
                Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
                // Match MenuFlyout submenus: the desktop, not the app edge, bounds this panel.
                ShouldConstrainToRootBounds = false,
                AreOpenCloseAnimationsEnabled = false,
                Content = panel,
            };
            if (TryStyle("FilesMate.SubmenuFlyoutPresenterStyle", out Style presenter))
            {
                flyout.FlyoutPresenterStyle = presenter;
            }

            Tags = flyout;
            FlyoutTheme.FollowHost(flyout);
            flyout.Opened += (_, _) =>
            {
                if (panel is TagPickerPanel picker)
                {
                    picker.FocusFilter();
                }
            };
            return flyout;
        }

        public void Hide()
        {
            Current?.Hide();
            Current = null;
            Tags?.Hide();
        }

        public void Show(FrameworkElement anchor, MenuFlyout menu)
        {
            Tags?.Hide();
            if (!ReferenceEquals(Current, menu))
            {
                Current?.Hide();
            }

            Current = menu;
            if (!menu.IsOpen)
            {
                ShowSubmenu(anchor, menu);
            }
        }

        public void ShowTags(FrameworkElement anchor, Flyout tags)
        {
            Current?.Hide();
            Current = null;
            Tags = tags;
            if (!tags.IsOpen)
            {
                ShowSubmenu(anchor, tags);
            }
        }
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

    private static bool TryBrush(string key, out Brush brush)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush found)
        {
            brush = found;
            return true;
        }

        brush = null!;
        return false;
    }
}
