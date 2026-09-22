using FilesMate.App.Commands;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Navigation;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Commands;

public sealed class ContextMenuPresentationTests
{
    [Fact]
    public void Menu_puts_frequent_actions_on_the_command_strip()
    {
        Assert.Empty(FileContextMenuBuilder.BuildLayout(CommandContext.None).Items);
        Assert.Empty(PrimaryLabels(CommandContext.Blank));
        Assert.Equal(
            ["Select items", "File shelf", "Sort", "View", "Refresh", "Show more options"],
            Labels(CommandContext.Blank));
        Assert.Equal(
            ["Cut", "Copy", "Paste", "Rename", "Move to Recycle Bin", "Properties"],
            PrimaryLabels(CommandContext.Blank with { FolderPath = @"C:\Projects" }));
        Assert.Equal(
            [
                "Select items",
                "New",
                "Copy path",
                "File shelf",
                "Folder appearance",
                "Sort",
                "View",
                "Refresh",
                "Open in Windows Terminal",
                "What's using this?",
                "Show more options",
            ],
            Labels(CommandContext.Blank with { FolderPath = @"C:\Projects" }));
        Assert.Equal(
            ["Cut", "Copy", "Rename", "Move to Recycle Bin", "Properties"],
            PrimaryLabels(CommandContext.SingleFile));
        Assert.Equal(
            [
                "Open",
                "Open with",
                "Select items",
                "Copy path",
                "Create shortcut",
                "Add to favorites bar",
                "Add to file shelf",
                "Compress",
                "What's using this?",
                "Show more options",
            ],
            Labels(CommandContext.SingleFile));
        Assert.Equal(
            [
                "Open",
                "Open in new tab",
                "Open in new window",
                "Select items",
                "Copy path",
                "Create shortcut",
                "Add to favorites bar",
                "Add to file shelf",
                "Folder appearance",
                "Compress",
                "What's using this?",
                "Show more options",
            ],
            Labels(CommandContext.SingleDirectory));
        Assert.Contains(
            "Open in Windows Terminal",
            Labels(CommandContext.SingleDirectory with { PrimaryPath = @"C:\Projects" }));
        Assert.Equal(
            ["Cut", "Copy", "Move to Recycle Bin", "Properties"],
            PrimaryLabels(CommandContext.Multi));
        Assert.Equal(
            [
                "Select items",
                "Copy path",
                "Add to favorites bar",
                "Add to file shelf",
                "Compress",
                "What's using this?",
                "Show more options",
            ],
            Labels(CommandContext.Multi));
        Assert.Empty(PrimaryLabels(CommandContext.Multi with { IsBackground = true }));
        Assert.Equal(
            ["Cut", "Copy", "Paste", "Rename", "Move to Recycle Bin", "Properties"],
            PrimaryLabels(CommandContext.Multi with { IsBackground = true, FolderPath = @"C:\Projects" }));

        var directory = FileContextMenuBuilder.Build(CommandContext.SingleDirectory);
        Assert.True(directory[3].IsSeparator);
        Assert.Contains(directory, entry => entry.Command == AppCommandId.CopyPath);
        Assert.Equal("Ctrl+Shift+C", directory.Single(entry => entry.Command == AppCommandId.CopyPath).Shortcut);
        Assert.Equal("Enter", directory[0].Shortcut);
        Assert.DoesNotContain(directory, entry => entry.Label is "Share" or "Open with");
        Assert.Contains(directory, entry => entry.IsShowMore);
        Assert.False(directory.Single(entry => entry.IsShowMore).HasChevron);
        Assert.DoesNotContain(directory, entry => entry.Command is AppCommandId.CopyPathQuoted);
        Assert.Contains(directory, entry => entry.Command == AppCommandId.Compress && entry.HasChevron);
    }

    [Fact]
    public void Virtual_place_background_menu_keeps_browse_actions_not_folder_create()
    {
        var tag = CommandContext.Blank with { FolderPath = TagLocation.Uri(1) };
        var home = CommandContext.Blank with { FolderPath = HomeLocation.Uri };
        Assert.Equal(
            ["Select items", "File shelf", "Sort", "View", "Refresh", "Show more options"],
            Labels(tag));
        Assert.Equal(Labels(tag), Labels(home));
        Assert.DoesNotContain("New folder", Labels(tag));
        Assert.DoesNotContain("Open in Windows Terminal", Labels(tag));
        Assert.Contains("Add tags", Labels(CommandContext.SingleFile with { TagsAvailable = true }));
        Assert.DoesNotContain("Select all", Labels(CommandContext.SingleFile with { TagsAvailable = true }));
    }

    [Fact]
    public void Archive_menu_adds_extract_and_open_in_compactmate()
    {
        var context = CommandContext.SingleFile with
        {
            PrimaryPath = @"C:\payload.zip",
            PrimaryIsArchive = true,
            SelectionIsArchive = true,
        };
        var labels = Labels(context);
        Assert.Contains("Open in CompactMate", labels);
        Assert.Contains("Extract", labels);
        Assert.Contains("Compress", labels);
        Assert.Contains(
            FileContextMenuBuilder.Build(context),
            entry => entry.Command == AppCommandId.Extract && entry.HasChevron);
    }

    [Fact]
    public void Right_click_policy_preserves_multi_select_and_background_selection()
    {
        Assert.False(ContextMenuSelectionPolicy.ClearsSelectionOnBackground);
        Assert.True(ContextMenuSelectionPolicy.ShouldReplaceSelection(hitItem: true, alreadySelected: false));
        Assert.False(ContextMenuSelectionPolicy.ShouldReplaceSelection(hitItem: true, alreadySelected: true));
        Assert.False(ContextMenuSelectionPolicy.ShouldReplaceSelection(hitItem: false, alreadySelected: true));
    }

    [Fact]
    public void Nested_actions_keep_context_enablement_and_leave_search_in_its_existing_entry_points()
    {
        var context = CommandContext.Blank with { FolderPath = @"C:\Projects" };
        var entries = FileContextMenuBuilder.Build(context);
        var appearance = Assert.Single(entries, entry => entry.Label == "Folder appearance");
        Assert.True(appearance.HasChevron);
        Assert.Equal(new[] { AppCommandId.ChooseFolderCover, AppCommandId.ResetFolderCover, AppCommandId.ResetFolderView },
            appearance.Children!.Select(child => child.Command!.Value));
        foreach (var child in entries.SelectMany(entry => entry.Children ?? []))
            Assert.Equal(CommandCatalog.Resolve(child.Command!.Value, context).Enabled, child.Enabled);
        Assert.DoesNotContain(entries.SelectMany(entry => entry.Children ?? [entry]), entry => entry.Command == AppCommandId.SearchCommands);
        Assert.True(CommandCatalog.Resolve(AppCommandId.SearchCommands, context).Enabled);
        Assert.DoesNotContain(entries, entry => entry.Command is AppCommandId.ChooseFolderCover or AppCommandId.ResetFolderCover or AppCommandId.ResetFolderView);
        Assert.True(entries.Count(entry => !entry.IsSeparator) <= 11);
        Assert.True(entries.Count(entry => entry.IsSeparator) <= 3);
    }

    [Fact]
    public void Directory_menu_exposes_pin_or_unpin_for_a_real_path()
    {
        var context = CommandContext.SingleDirectory with { PrimaryPath = @"C:\Projects" };
        var labels = Labels(context);

        Assert.Contains("Pin to sidebar", labels);
        Assert.DoesNotContain("Unpin from sidebar", labels);

        var pinned = context with { PrimaryIsPinned = true };
        labels = Labels(pinned);
        Assert.Contains("Unpin from sidebar", labels);
        Assert.DoesNotContain("Pin to sidebar", labels);
    }

    [Fact]
    public void Flyout_uses_shared_menu_chrome_and_the_command_builder()
    {
        var styles = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "MenuStyles.xaml"));
        Assert.Contains("FilesMate.MenuFlyoutPresenterStyle", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.SubmenuFlyoutPresenterStyle", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.MenuFlyoutItemStyle", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.ContextFlyoutPresenterStyle", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.ContextMenuItemStyle", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Corner.Card", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Control.Height.Row", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Control.Height.ContextItem", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.ContextMenu.MinWidth", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.SidebarFlyoutPresenterStyle", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.SidebarFlyoutItemStyle", styles, StringComparison.Ordinal);
        Assert.Contains("Property=\"MinHeight\" Value=\"26\"", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Menu.BackgroundBrush", styles, StringComparison.Ordinal);
        Assert.Contains("FilesMate.LiquidGlass.BorderBrush", styles, StringComparison.Ordinal);
        Assert.Contains("Property=\"Margin\" Value=\"4,0\"", styles, StringComparison.Ordinal);
        Assert.Contains("Property=\"CornerRadius\" Value=\"{StaticResource FilesMate.Corner.Small}\"", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("Transparent", styles, StringComparison.OrdinalIgnoreCase);

        var buttons = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "ButtonStyles.xaml"));
        Assert.Contains("FilesMate.ContextCommandButtonStyle", buttons, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Selection.AccentBrush", buttons, StringComparison.Ordinal);

        var surface = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml.cs"));
        Assert.Contains("FileContextMenuBuilder.BuildLayout", surface, StringComparison.Ordinal);
        Assert.Contains("CreateTagPicker?.Invoke()", surface, StringComparison.Ordinal);
        Assert.Contains("FolderPath:", surface, StringComparison.Ordinal);
        var flyout = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Menus",
            "FileContextFlyout.cs"));
        Assert.Contains("AppCommandId.AddTags", flyout, StringComparison.Ordinal);
        Assert.Contains("ShowTags(button, tags)", flyout, StringComparison.Ordinal);
        Assert.Contains("EnsureTags", flyout, StringComparison.Ordinal);
        Assert.Contains("FilesMate.SubmenuFlyoutPresenterStyle", flyout, StringComparison.Ordinal);
        Assert.Contains("ShowSubmenu(anchor, tags)", flyout, StringComparison.Ordinal);
        Assert.Contains("ShouldConstrainToRootBounds = false", flyout, StringComparison.Ordinal);
        Assert.Contains("ShouldConstrainToRootBounds = true", flyout, StringComparison.Ordinal);
        var picker = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Tags",
            "TagPickerFlyout.cs"));
        Assert.Contains("QuietButtonStyle", picker, StringComparison.Ordinal);
        Assert.Contains("FilesMate.SubmenuFlyoutPresenterStyle", picker, StringComparison.Ordinal);
        Assert.DoesNotContain("GlassButtonStyle", picker, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachTagPicker", flyout, StringComparison.Ordinal);
        Assert.Contains("AreOpenCloseAnimationsEnabled = false", flyout, StringComparison.Ordinal);
        Assert.Contains("OpenSubmenuOnHover", flyout, StringComparison.Ordinal);
        Assert.Contains("submenu.Show(", flyout, StringComparison.Ordinal);
        Assert.Contains("submenu.Hide()", flyout, StringComparison.Ordinal);
        Assert.Contains("FlyoutShowMode.TransientWithDismissOnPointerMoveAway", flyout, StringComparison.Ordinal);
        Assert.Contains("PointerEntered", flyout, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateShellOverflow", flyout, StringComparison.Ordinal);
        Assert.DoesNotContain("showMore?.Invoke()", flyout, StringComparison.Ordinal);
        Assert.Contains("showMoreNative?.Invoke();", flyout, StringComparison.Ordinal);
        Assert.Contains("flyout.Hide();", flyout, StringComparison.Ordinal);
        Assert.True(
            flyout.IndexOf("showMoreNative?.Invoke();", StringComparison.Ordinal)
            < flyout.IndexOf("flyout.Hide();", flyout.IndexOf("entry.IsShowMore", StringComparison.Ordinal)));
        Assert.DoesNotContain("button.Flyout = menu", flyout, StringComparison.Ordinal);
        Assert.DoesNotContain("flyout.Closed += OnClosed", flyout, StringComparison.Ordinal);
        Assert.Contains("CreateGlyph(command.Glyph, 16)", flyout, StringComparison.Ordinal);
        Assert.Contains("AppCommandId.Sort", flyout, StringComparison.Ordinal);
        Assert.Contains("AppCommandId.Compress", flyout, StringComparison.Ordinal);
        Assert.Contains("AppCommandId.Extract", flyout, StringComparison.Ordinal);
        Assert.Contains("preferShell", surface, StringComparison.Ordinal);
        Assert.Contains("ShellContextMenu.TryShow", surface, StringComparison.Ordinal);
        Assert.Contains("() => showMore = true", surface, StringComparison.Ordinal);
        Assert.Contains("host.Closed", surface, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueuePriority.Low", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellContextMenu.TryCreate", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateShellOverflow", surface, StringComparison.Ordinal);
        Assert.Contains("ContextMenuSelectionPolicy", surface, StringComparison.Ordinal);
        var shellMenu = File.ReadAllText(Path.Combine(
            ThemeXaml.RepoRoot,
            "src",
            "FilesMate.Platform.Windows",
            "Shell",
            "ShellContextMenu.cs"));
        Assert.Contains("CreateWindowExW", shellMenu, StringComparison.Ordinal);
        Assert.Contains("CreateHostWindow()", shellMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateHostWindow(hwnd)", shellMenu, StringComparison.Ordinal);
        Assert.Contains("hwnd = host", shellMenu, StringComparison.Ordinal);
        Assert.Contains("HandleMenuMsg2", shellMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWindowsHookEx", shellMenu, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Cut\"", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Delete\"", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("new MenuFlyoutItem { Text = \"Open\" }", surface, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_with_is_wired_to_the_windows_application_picker()
    {
        var actions = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "PaneFileActions.cs"));
        Assert.Contains("AppCommandId.OpenWith", actions, StringComparison.Ordinal);
        Assert.Contains("DisplayApplicationPicker = true", actions, StringComparison.Ordinal);
        Assert.Contains("Launcher.LaunchFileAsync", actions, StringComparison.Ordinal);
        Assert.Contains("AppCommandId.OpenInTerminal", actions, StringComparison.Ordinal);
        Assert.Contains("TerminalLaunch.Open(folder)", actions, StringComparison.Ordinal);
        Assert.Contains("CompactMateSession.Launch", actions, StringComparison.Ordinal);
        Assert.Contains("ShellShortcut.Create", actions, StringComparison.Ordinal);
        Assert.Contains("AppCommandId.WhoLocks", actions, StringComparison.Ordinal);
        Assert.Contains("ReleasePreviewAsync", actions, StringComparison.Ordinal);
        Assert.Contains("FileLockDialog", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("FileLockQuery.ReleaseOwn", actions, StringComparison.Ordinal);
        Assert.Contains("ShowLockOverlayAsync", actions, StringComparison.Ordinal);
        Assert.Contains("HideLockOverlay", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialogMaxWidth", actions, StringComparison.Ordinal);
    }

    private static string[] Labels(CommandContext context) =>
        FileContextMenuBuilder.Build(context)
            .Where(entry => !entry.IsSeparator)
            .Select(entry => entry.Label)
            .ToArray();

    private static string[] PrimaryLabels(CommandContext context) =>
        FileContextMenuBuilder.BuildLayout(context).Primary
            .Select(command => command.Label)
            .ToArray();
}
