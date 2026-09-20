using FilesMate.App.Commands;
using FilesMate.App.Controls.Toolbar;

namespace FilesMate.App.Tests.Commands;

public sealed class CommandStateMatrixTests
{
    private static readonly AppCommandId[] ExpectedOrder =
    [
        AppCommandId.AddToFavorites,
        AppCommandId.AddToShelf,
        AppCommandId.ShowShelf,
        AppCommandId.ChooseFolderCover,
        AppCommandId.ResetFolderCover,
        AppCommandId.ResetFolderView,
        AppCommandId.NewFolder,
        AppCommandId.NewFile,
        AppCommandId.Cut,
        AppCommandId.Copy,
        AppCommandId.Paste,
        AppCommandId.Rename,
        AppCommandId.Share,
        AppCommandId.Recycle,
        AppCommandId.PermanentDelete,
        AppCommandId.Open,
        AppCommandId.OpenInNewTab,
        AppCommandId.OpenWith,
        AppCommandId.OpenInTerminal,
        AppCommandId.Properties,
        AppCommandId.PinToSidebar,
        AppCommandId.UnpinFromSidebar,
        AppCommandId.CopyPath,
        AppCommandId.SelectAll,
        AppCommandId.Refresh,
        AppCommandId.Sort,
        AppCommandId.ChangeLayout,
    ];

    [Fact]
    public void Catalog_defines_the_complete_stable_command_order()
    {
        Assert.Equal(ExpectedOrder, CommandCatalog.All);
    }

    [Fact]
    public void Unimplemented_commands_remain_hidden_on_every_surface()
    {
        var contexts = new[]
        {
            CommandContext.ForToolbar(0, isFolderWritable: true, clipboardHasFiles: true),
            CommandContext.ForToolbar(1, primaryIsDirectory: false),
            CommandContext.ForToolbar(1, primaryIsDirectory: true),
            CommandContext.ForToolbar(3),
            CommandContext.ForMenu(1),
            CommandContext.Blank,
        };

        foreach (var context in contexts)
        {
            foreach (var id in CommandCatalog.All.Except(CommandCatalog.Implemented))
            {
                var command = CommandCatalog.Resolve(id, context);
                Assert.False(command.Implemented);
                Assert.False(command.Visible);
                Assert.False(command.Enabled);
            }
        }
    }

    [Fact]
    public void Implemented_toolbar_commands_follow_selection_and_folder_capabilities()
    {
        var none = CommandContext.ForToolbar(0, isFolderWritable: true);
        var oneFile = CommandContext.ForToolbar(1, primaryIsDirectory: false);
        var oneDirectory = CommandContext.ForToolbar(1, primaryIsDirectory: true);
        var many = CommandContext.ForToolbar(3);
        var readOnly = CommandContext.ForToolbar(0, isFolderWritable: false);

        Assert.True(CommandCatalog.Resolve(AppCommandId.NewFolder, none).Visible);
        Assert.True(CommandCatalog.Resolve(AppCommandId.Cut, none).Visible);
        Assert.False(CommandCatalog.Resolve(AppCommandId.Cut, none).Enabled);
        Assert.True(CommandCatalog.Resolve(AppCommandId.Copy, oneFile).Visible);
        Assert.True(CommandCatalog.Resolve(AppCommandId.Copy, oneFile).Enabled);
        Assert.False(CommandCatalog.Resolve(AppCommandId.CopyPath, none).Visible);
        Assert.True(CommandCatalog.Resolve(AppCommandId.CopyPath, oneFile).Visible);
        Assert.True(CommandCatalog.Resolve(AppCommandId.CopyPath, oneFile).Enabled);
        Assert.True(CommandCatalog.Resolve(AppCommandId.CopyPath, oneDirectory).Enabled);
        Assert.True(CommandCatalog.Resolve(AppCommandId.CopyPath, many).Enabled);
        Assert.True(CommandCatalog.Resolve(AppCommandId.Sort, readOnly).Enabled);
        Assert.True(CommandCatalog.Resolve(AppCommandId.ChangeLayout, readOnly).Enabled);
        Assert.False(CommandCatalog.Resolve(AppCommandId.Refresh, oneFile).Visible);
        Assert.False(CommandCatalog.Resolve(AppCommandId.NewFolder, readOnly).Enabled);
    }

    [Fact]
    public void Capability_matrix_distinguishes_single_multi_writable_and_clipboard_states()
    {
        var none = CommandContext.ForToolbar(0, isFolderWritable: true, clipboardHasFiles: false);
        var clipboard = none with { ClipboardHasFiles = true };
        var file = CommandContext.ForToolbar(1, primaryIsDirectory: false);
        var directory = CommandContext.ForToolbar(1, primaryIsDirectory: true);
        var many = CommandContext.ForToolbar(3);
        var readOnlyFile = file with { IsFolderWritable = false };
        var readOnlyClipboard = clipboard with { IsFolderWritable = false };

        Assert.True(CommandCatalog.CanExecute(AppCommandId.NewFolder, none));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.NewFile, none));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.Paste, none));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.Paste, clipboard));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.Cut, none));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.Cut, file));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.Copy, many));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.OpenWith, file));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.OpenWith, directory));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.OpenInNewTab, directory));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.OpenInNewTab, file));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.OpenInNewWindow, directory));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.OpenInNewWindow, file));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.Compress, file));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.Extract, file));
        Assert.True(CommandCatalog.CanExecute(
            AppCommandId.Extract,
            file with { SelectionIsArchive = true, PrimaryIsArchive = true }));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.Rename, file));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.Rename, many));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.Rename, none));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.NewFolder, readOnlyFile));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.Paste, readOnlyClipboard));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.Recycle, readOnlyFile));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.PermanentDelete, readOnlyFile));
    }

    [Fact]
    public void Copy_and_copy_path_stay_unambiguous()
    {
        var copy = CommandCatalog.Resolve(AppCommandId.Copy, CommandContext.ForToolbar(1));
        var copyPath = CommandCatalog.Resolve(AppCommandId.CopyPath, CommandContext.ForToolbar(1));

        Assert.Equal("Copy", copy.Label);
        Assert.Equal("Copy path", copyPath.Label);
        Assert.Equal("Ctrl+C", copy.Shortcut);
        Assert.Equal("Ctrl+Shift+C", copyPath.Shortcut);
        Assert.Equal("\uE8C8", copy.Glyph);
        Assert.Equal("\uE71B", copyPath.Glyph);
        Assert.NotEqual(copy.Label, copyPath.Label);
        Assert.NotEqual(copy.Shortcut, copyPath.Shortcut);
        Assert.NotEqual(copy.Glyph, copyPath.Glyph);
        Assert.Contains("Ctrl+Shift+C", copyPath.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Rename_tooltip_explains_multi_select_even_when_hidden()
    {
        var many = CommandCatalog.Resolve(AppCommandId.Rename, CommandContext.ForToolbar(3));
        Assert.True(many.Visible);
        Assert.False(many.Enabled);
        Assert.Equal("Cannot rename multiple items", many.Tooltip);
    }

    [Fact]
    public void Overflow_presentation_is_stable_across_narrow_medium_and_wide_widths()
    {
        Assert.Equal(new ToolbarOverflow(true, true, false), ToolbarOverflow.ForWidth(280));
        Assert.Equal(new ToolbarOverflow(false, true, true), ToolbarOverflow.ForWidth(220));
        Assert.Equal(new ToolbarOverflow(false, false, true), ToolbarOverflow.ForWidth(160));
        Assert.Equal(ToolbarOverflow.ForWidth(280), ToolbarOverflow.ForWidth(1440));
    }
}
