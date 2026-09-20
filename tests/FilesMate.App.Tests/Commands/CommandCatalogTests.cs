using FilesMate.App.Commands;

namespace FilesMate.App.Tests.Commands;

public sealed class CommandCatalogTests
{
    [Theory]
    [InlineData(CommandSurface.Menu)]
    [InlineData(CommandSurface.Shortcut)]
    [InlineData(CommandSurface.Toolbar)]
    public void DeleteWithoutSelectionNeverTargetsTheOpenFolder(CommandSurface surface)
    {
        var context = CommandContext.ForMenu(0, isBackground: true) with
        { Surface = surface, FolderPath = @"D:\current", IsFolderWritable = true };
        Assert.False(CommandCatalog.CanExecute(AppCommandId.Recycle, context));
        Assert.False(CommandCatalog.CanExecute(AppCommandId.PermanentDelete, context));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.Recycle, context with { SelectionCount = 1 }));
    }

    [Fact]
    public void BackgroundTerminalUsesCurrentFolderEvenWithSelectedDirectory()
    {
        var context = CommandContext.ForMenu(1, primaryIsDirectory: true, isBackground: true, primaryPath: @"D:\current\child")
            with { FolderPath = @"D:\current" };
        Assert.Equal(@"D:\current", CommandCatalog.TerminalTarget(context));
        Assert.Equal(@"D:\current\child", CommandCatalog.TerminalTarget(context with { IsBackground = false }));
    }

    [Fact]
    public void Implemented_commands_share_label_icon_and_can_execute_across_surfaces()
    {
        var menu = CommandContext.SingleFile with { Surface = CommandSurface.Menu };
        var toolbar = CommandContext.ForToolbar(1);
        var shortcut = CommandContext.SingleFile with { Surface = CommandSurface.Shortcut };

        foreach (var id in CommandCatalog.Implemented)
        {
            var fromMenu = CommandCatalog.Resolve(id, menu);
            var fromToolbar = CommandCatalog.Resolve(id, toolbar);
            var fromShortcut = CommandCatalog.Resolve(id, shortcut);
            Assert.Equal(fromMenu.Label, fromToolbar.Label);
            Assert.Equal(fromMenu.Label, fromShortcut.Label);
            Assert.Equal(fromMenu.Glyph, fromToolbar.Glyph);
            Assert.Equal(fromMenu.Glyph, fromShortcut.Glyph);
            Assert.Equal(fromMenu.Enabled, fromToolbar.Enabled);
            Assert.Equal(fromMenu.Enabled, fromShortcut.Enabled);
            Assert.Equal(fromMenu.Shortcut, fromShortcut.Shortcut);
            Assert.Equal(fromMenu.Tooltip, fromToolbar.Tooltip);
        }
    }

    [Fact]
    public void Copy_path_is_hidden_without_selection_and_distinct_from_copy()
    {
        var none = CommandCatalog.Resolve(AppCommandId.CopyPath, CommandContext.ForToolbar(0));
        Assert.Equal("Copy path", none.Label);
        Assert.False(none.Visible);
        Assert.False(none.Enabled);

        var one = CommandCatalog.Resolve(AppCommandId.CopyPath, CommandContext.ForToolbar(1));
        Assert.True(one.Visible);
        Assert.True(one.Enabled);
        Assert.Equal("\uE71B", one.Glyph);
        Assert.Equal("Ctrl+Shift+C", one.Shortcut);

        var copy = CommandCatalog.Resolve(AppCommandId.Copy, CommandContext.ForToolbar(1));
        Assert.Equal("Copy", copy.Label);
        Assert.True(copy.Visible);
        Assert.NotEqual(copy.Glyph, one.Glyph);

        Assert.False(CommandCatalog.Resolve(AppCommandId.Open, CommandContext.ForToolbar(1)).Visible);
        Assert.False(CommandCatalog.Resolve(AppCommandId.Refresh, CommandContext.ForToolbar(1)).Visible);
    }

    [Fact]
    public void Share_is_implemented_when_the_windows_share_bridge_is_available()
    {
        Assert.Equal(
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
                AppCommandId.OpenInNewWindow,
                AppCommandId.OpenWith,
                AppCommandId.OpenInTerminal,
                AppCommandId.OpenInCompactMate,
                AppCommandId.Properties,
                AppCommandId.WhoLocks,
                AppCommandId.PinToSidebar,
                AppCommandId.UnpinFromSidebar,
                AppCommandId.CopyPath,
                AppCommandId.CopyPathQuoted,
                AppCommandId.CreateShortcut,
                AppCommandId.Compress,
                AppCommandId.CompressZip,
                AppCommandId.Compress7z,
                AppCommandId.CompressNew,
                AppCommandId.Extract,
                AppCommandId.ExtractHere,
                AppCommandId.ExtractToFolder,
                AppCommandId.ExtractToOther,
                AppCommandId.SmartExtract,
                AppCommandId.SelectAll,
                AppCommandId.Refresh,
                AppCommandId.Sort,
                AppCommandId.ChangeLayout,
            ],
            CommandCatalog.Implemented);

        var unavailable = CommandCatalog.Resolve(AppCommandId.Share, CommandContext.ForToolbar(1));
        Assert.True(unavailable.Implemented);
        Assert.False(unavailable.Visible);
        Assert.False(unavailable.Enabled);

        var available = CommandCatalog.Resolve(
            AppCommandId.Share,
            CommandContext.ForToolbar(1, shareAvailable: true));
        Assert.True(available.Visible);
        Assert.True(available.Enabled);

        var openWith = CommandCatalog.Resolve(
            AppCommandId.OpenWith,
            CommandContext.ForMenu(1, primaryIsDirectory: false));
        Assert.True(openWith.Implemented);
        Assert.True(openWith.Visible);
        Assert.True(openWith.Enabled);

        Assert.False(CommandCatalog.Resolve(AppCommandId.WhoLocks, CommandContext.ForToolbar(1)).Visible);
        Assert.True(CommandCatalog.Resolve(AppCommandId.WhoLocks, CommandContext.SingleFile).Visible);
        Assert.True(CommandCatalog.Resolve(AppCommandId.WhoLocks, CommandContext.SingleFile).Enabled);
    }

    [Fact]
    public void Pin_commands_are_exclusive_and_only_apply_to_a_single_directory()
    {
        var directory = CommandContext.ForMenu(
            1,
            primaryIsDirectory: true,
            primaryPath: @"C:\Projects");

        var pin = CommandCatalog.Resolve(AppCommandId.PinToSidebar, directory);
        var unpin = CommandCatalog.Resolve(AppCommandId.UnpinFromSidebar, directory);

        Assert.True(pin.Visible);
        Assert.True(pin.Enabled);
        Assert.False(unpin.Visible);
        Assert.False(unpin.Enabled);

        var pinned = directory with { PrimaryIsPinned = true };
        Assert.False(CommandCatalog.Resolve(AppCommandId.PinToSidebar, pinned).Visible);
        Assert.True(CommandCatalog.Resolve(AppCommandId.UnpinFromSidebar, pinned).Visible);

        var file = directory with { PrimaryIsDirectory = false };
        Assert.False(CommandCatalog.Resolve(AppCommandId.PinToSidebar, file).Visible);
        Assert.False(CommandCatalog.Resolve(AppCommandId.UnpinFromSidebar, file).Visible);
    }
}
