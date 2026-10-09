using FilesMate.App.Commands;
using FilesMate.Core.Diagnostics;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Tags;
using FilesMate.App.Localization;
using FilesMate.App.Services;
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
        // Observe requested and actual bounds before judging edge placement. No pointer input is generated here.
        var operation = PerformanceTrace.NextOperation();
        PerformanceTrace.Record(PerformanceStage.RecursiveMenuRequested, operation: operation,
            count: (long)Math.Round(position.X * 1000), auxiliary: (long)Math.Round(position.Y * 1000));
        if (PerformanceTrace.Enabled)
        {
            void Opened(object? sender, object args)
            {
                menu.Opened -= Opened;
                _ = anchor.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (anchor.XamlRoot is not { } root) return;
                        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
                        {
                            var presenter = FindPresenter(popup.Child, menu.Content);
                            if (presenter is null) continue;
                            var actual = presenter.TransformToVisual(anchor).TransformPoint(default);
                            PerformanceTrace.Record(PerformanceStage.RecursiveMenuOpened, operation: operation,
                                count: (long)Math.Round(actual.X * 1000), auxiliary: (long)Math.Round(actual.Y * 1000));
                            PerformanceTrace.Record(PerformanceStage.RecursiveMenuBounds, operation: operation,
                                count: (long)Math.Round(presenter.ActualWidth * 1000), auxiliary: (long)Math.Round(presenter.ActualHeight * 1000));
                            break;
                        }
                    }
                    catch (Exception error)
                    {
                        // A diagnostic transform must not turn a working menu into an unhandled UI failure.
                        PerformanceTrace.Record(PerformanceStage.RecursiveMenuOpened, operation: operation,
                            outcome: PerformanceOutcome.Failed, auxiliary: error.HResult);
                    }
                });
            }
            menu.Opened += Opened;
        }
        FileContextPopupPlacement.ShowAt(menu, anchor, position);
    }

    private static FlyoutPresenter? FindPresenter(DependencyObject? root, object content)
    {
        if (root is null) return null;
        if (root is FlyoutPresenter presenter && ReferenceEquals(presenter.Content, content)) return presenter;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindPresenter(VisualTreeHelper.GetChild(root, i), content) is { } match) return match;
        return null;
    }

    public static Flyout Create(
        FileContextMenuLayout layout,
        Action<AppCommandId> invoke,
        Action? showMoreNative = null,
        Action<EntrySortColumn>? sort = null,
        Action<FileLayoutKind>? changeLayout = null,
        UIElement? tagPicker = null,
        Func<EntrySort>? currentSort = null,
        Action<bool>? sortDirection = null,
        Action<GridSizePreset>? gridSize = null,
        Action<EntryGrouping>? grouping = null,
        IReadOnlyList<RecursiveContextAction>? recursiveActions = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(invoke);

        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            // Pointer coordinates are anchor-local DIPs. Let the native popup constrain
            // to the desktop work area, rather than flip at the application client edge.
            ShouldConstrainToRootBounds = false,
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

        if (recursiveActions is { Count: > 0 })
        {
            foreach (var action in recursiveActions)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)Application.Current.Resources["FilesMate.Menu.LeadingSlot"]) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.Children.Add(CreateGlyph(action.Glyph, 16));
                var text = new TextBlock { Text = action.Label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Style = new Style(typeof(TextBlock)) };
                Grid.SetColumn(text, 1); grid.Children.Add(text);
                var button = new Button { Content = grid };
                if (TryStyle("FilesMate.ContextMenuItemStyle", out Style itemStyle)) button.Style = itemStyle;
                var invokeAction = action.Invoke;
                if (action.Children is { Count: > 0 } children)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                    var chevron = CreateGlyph("\uE76C", 10); Grid.SetColumn(chevron, 2); grid.Children.Add(chevron);
                    var depthMenu = CreateSubmenu();
                    foreach (var child in children)
                    {
                        var item = new MenuFlyoutItem { Text = child.Label };
                        if (TryStyle("FilesMate.MenuFlyoutItemStyle", out Style childStyle)) item.Style = childStyle;
                        var childAction = child.Invoke;
                        item.Click += (_, _) => { depthMenu.Hide(); flyout.Hide(); childAction(); };
                        depthMenu.Items.Add(item);
                    }
                    OpenSubmenuOnHover(button, depthMenu, submenu);
                }
                else button.Click += (_, _) => { submenu.Hide(); flyout.Hide(); invokeAction(); };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, action.Label);
                column.Children.Add(button);
            }
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
                tagPicker, currentSort, sortDirection, gridSize, grouping));
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
            Tag = command.Id,
            Content = CreateGlyph(command.Glyph, 16),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, command.Label);
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
        UIElement? tagPicker,
        Func<EntrySort>? currentSort,
        Action<bool>? sortDirection,
        Action<GridSizePreset>? gridSize, Action<EntryGrouping>? grouping)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)Application.Current.Resources["FilesMate.Menu.LeadingSlot"]) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = CreateGlyph(entry.Glyph, 16);
        grid.Children.Add(icon);

        var label = new TextBlock
        {
            Text = entry.Label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            // An explicit empty style permits active menu ink to inherit from
            // its presenter instead of the application's implicit normal ink.
            Style = new Style(typeof(TextBlock)),
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        if (!string.IsNullOrEmpty(entry.Shortcut))
        {
            var shortcut = new TextBlock
            {
                Text = entry.Shortcut,
                Margin = (Thickness)Application.Current.Resources["FilesMate.Menu.AcceleratorGap"],
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
                Margin = (Thickness)Application.Current.Resources["FilesMate.Menu.ChevronGap"], VerticalAlignment = VerticalAlignment.Center,
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

        if (entry.Command is { } providerCommand && CompactMateSession.RequiresExternalProvider(providerCommand))
        {
            _ = ApplyArchiveProviderAsync(entry.Label, entry.Enabled, button, (text, enabled) =>
            {
                label.Text = text;
                button.IsEnabled = enabled;
                ToolTipService.SetToolTip(button, text);
            });
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
            OpenSubmenuOnHover(button, CreateSortFlyout(flyout, sort, currentSort, sortDirection, grouping), submenu);
            return button;
        }

        if (entry.Command is AppCommandId.ChangeLayout && changeLayout is not null)
        {
            OpenSubmenuOnHover(button, CreateLayoutFlyout(flyout, changeLayout, gridSize), submenu);
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

    private static void ShowSubmenu(FrameworkElement anchor, FlyoutBase menu)
    {
        RecordSubmenu("ShowRequested", anchor, menu);
        menu.ShowAt(anchor, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
            ShowMode = FlyoutShowMode.Transient,
        });
        RecordSubmenu("ShowReturned", anchor, menu);
    }

    private static int _submenuAuditCount;
    private static void RecordSubmenu(string stage, FrameworkElement? anchor, FlyoutBase menu)
    {
        if (!FilesMate.Core.Configuration.RuntimeProfile.IsIsolated || _submenuAuditCount >= 48
            || !File.Exists(Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "presentation-audit.enabled"))) return;
        ++_submenuAuditCount;
        try
        {
            var data = new { Utc = DateTimeOffset.UtcNow, Stage = stage, Menu = menu.GetHashCode(),
                Anchor = anchor is null ? null : Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(anchor),
                IsOpen = menu.IsOpen, Scale = anchor?.XamlRoot?.RasterizationScale,
                Choices = (menu as MenuFlyout)?.Items.Count };
            File.AppendAllText(Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "submenu-boundary.jsonl"),
                System.Text.Json.JsonSerializer.Serialize(data) + Environment.NewLine);
        }
        catch (Exception error) { App.AppendCrashRecord("SubmenuBoundaryAudit", error); }
    }

    private static MenuFlyout CreateSortFlyout(Flyout parent, Action<EntrySortColumn> sort, Func<EntrySort>? currentSort, Action<bool>? sortDirection, Action<EntryGrouping>? grouping)
    {
        var menu = CreateSubmenu();
        AddSubItem(menu, parent, "Sort_Name", () => sort(EntrySortColumn.Name));
        AddSubItem(menu, parent, "Sort_Modified", () => sort(EntrySortColumn.Modified));
        AddSubItem(menu, parent, "Sort_Type", () => sort(EntrySortColumn.Type));
        AddSubItem(menu, parent, "Sort_Size", () => sort(EntrySortColumn.Size));
        foreach (var column in FilesMate.App.Models.DetailsColumn.Defaults().Skip(4).Where(c => c.CanSort))
        {
            var item = new MenuFlyoutItem { Text = column.Title };
            if (TryStyle("FilesMate.MenuFlyoutItemStyle", out Style style)) item.Style = style;
            item.Click += (_, _) => { parent.Hide(); sort(column.Sort); };
            menu.Items.Add(item);
        }
        if (currentSort is not null && sortDirection is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(SortDirectionMenu.Create(currentSort, ascending => { parent.Hide(); sortDirection(ascending); }));
        }
        if (currentSort is not null && grouping is not null)
            menu.Items.Add(EntryGroupingMenu.Create(currentSort, value => { parent.Hide(); grouping(value); }));
        return menu;
    }

    private static MenuFlyout CreateLayoutFlyout(Flyout parent, Action<FileLayoutKind> changeLayout, Action<GridSizePreset>? gridSize)
    {
        var menu = CreateSubmenu();
        AddSubItem(menu, parent, "Layout_Details", () => changeLayout(FileLayoutKind.Details));
        AddSubItem(menu, parent, "Layout_List", () => changeLayout(FileLayoutKind.List));
        if (gridSize is null) AddSubItem(menu, parent, "Layout_LargeIcons", () => changeLayout(FileLayoutKind.Grid));
        else
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            string[] keys = ["Small", "Medium", "Large", "ExtraLarge", "Huge", "Jumbo", "Maximum"];
            for (var i = 0; i < GridSizePreset.All.Length; i++)
            {
                var preset = GridSizePreset.All[i];
                AddSubItem(menu, parent, "Layout_" + keys[i] + "Icons", () => { gridSize(preset); changeLayout(FileLayoutKind.Grid); });
            }
        }
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

            if (CompactMateSession.RequiresExternalProvider(id))
            {
                _ = ApplyArchiveProviderAsync(item.Text, item.IsEnabled, item, (text, enabled) =>
                {
                    item.Text = text;
                    item.IsEnabled = enabled;
                    ToolTipService.SetToolTip(item, text);
                });
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

    private static async Task ApplyArchiveProviderAsync(
        string label,
        bool commandEnabled,
        FrameworkElement element,
        Action<string, bool> update)
    {
        var availability = CompactMateSession.IsAvailableAsync();
        if (availability.IsCompletedSuccessfully)
        {
            Apply(availability.Result);
            return;
        }
        update(label + " · " + StringTable.Get("Archive_CheckingProvider"), false);
        bool available;
        try { available = await availability.ConfigureAwait(false); }
        catch (Exception) { available = false; }
        element.DispatcherQueue.TryEnqueue(() => Apply(available));

        void Apply(bool found) => update(
            found ? label : label + " · " + StringTable.Get("Archive_ExternalOnly"),
            commandEnabled && found);
    }

    private static MenuFlyout CreateSubmenu()
    {
        var menu = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
            ShouldConstrainToRootBounds = false,
            AreOpenCloseAnimationsEnabled = false,
        };
        if (TryStyle("FilesMate.MenuFlyoutPresenterStyle", out Style style))
        {
            menu.MenuFlyoutPresenterStyle = style;
        }

        FlyoutTheme.FollowHost(menu);
        if (FilesMate.Core.Configuration.RuntimeProfile.IsIsolated
            && File.Exists(Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "presentation-audit.enabled")))
        {
            menu.Opened += (_, _) => RecordSubmenu("Opened", null, menu);
            menu.Closed += (_, _) => RecordSubmenu("Closed", null, menu);
        }
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
            Style = new Style(typeof(FontIcon)),
        };

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


}
