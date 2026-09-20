using FilesMate.App.Commands;
using FilesMate.App.Localization;

namespace FilesMate.App.Controls.Menus;

public readonly record struct ContextMenuEntry(
    bool IsSeparator,
    AppCommandId? Command,
    string Label,
    string Glyph,
    string? Shortcut,
    bool Enabled,
    bool IsShowMore = false,
    bool HasChevron = false,
    IReadOnlyList<ContextMenuEntry>? Children = null);

public sealed record FileContextMenuLayout(
    IReadOnlyList<AppCommand> Primary,
    IReadOnlyList<ContextMenuEntry> Items);

public static class ContextMenuSelectionPolicy
{
    public static bool ClearsSelectionOnBackground => false;

    public static bool ShouldReplaceSelection(bool hitItem, bool alreadySelected) =>
        hitItem && !alreadySelected;
}

public static class FileContextMenuBuilder
{
    public static readonly AppCommandId[] ItemPrimary =
    [
        AppCommandId.Cut,
        AppCommandId.Copy,
        AppCommandId.Rename,
        AppCommandId.Share,
        AppCommandId.Recycle,
        AppCommandId.Properties,
    ];

    public static readonly AppCommandId[] BackgroundPrimary =
    [
        AppCommandId.Cut,
        AppCommandId.Copy,
        AppCommandId.Paste,
        AppCommandId.Rename,
        AppCommandId.Share,
        AppCommandId.Recycle,
        AppCommandId.Properties,
    ];

    public static IReadOnlyList<ContextMenuEntry> Build(CommandContext context) =>
        BuildLayout(context).Items;

    public static FileContextMenuLayout BuildLayout(CommandContext context)
    {
        var menu = context with { Surface = CommandSurface.Menu };
        var primaryIds = menu.IsBackground ? BackgroundPrimary : ItemPrimary;
        var primary = new List<AppCommand>(primaryIds.Length);
        var taken = new HashSet<AppCommandId>();
        foreach (var id in primaryIds)
        {
            var command = CommandCatalog.Resolve(id, menu);
            if (!command.Visible)
            {
                continue;
            }

            primary.Add(command);
            taken.Add(id);
        }

        var entries = new List<ContextMenuEntry>();
        CommandGroup? previous = null;
        foreach (var id in CommandCatalog.MenuOrder)
        {
            if (taken.Contains(id) || id == AppCommandId.SearchCommands)
            {
                continue;
            }

            var command = CommandCatalog.Resolve(id, menu);
            if (!command.Visible)
            {
                continue;
            }

            var groupIds = id switch
            {
                AppCommandId.SelectAll or AppCommandId.SelectSameType or AppCommandId.InvertSelection =>
                    new[] { AppCommandId.SelectAll, AppCommandId.SelectSameType, AppCommandId.InvertSelection },
                AppCommandId.NewFolder or AppCommandId.NewFile => new[] { AppCommandId.NewFolder, AppCommandId.NewFile },
                AppCommandId.ChooseFolderCover or AppCommandId.ResetFolderCover or AppCommandId.ResetFolderView =>
                    new[] { AppCommandId.ChooseFolderCover, AppCommandId.ResetFolderCover, AppCommandId.ResetFolderView },
                _ => Array.Empty<AppCommandId>(),
            };
            var children = groupIds.Select(child => CommandCatalog.Resolve(child, menu))
                .Where(child => child.Visible).Select(child => Item(child, false)).ToArray();
            var entry = Item(command, hasChevron: id is AppCommandId.AddTags or AppCommandId.Sort
                or AppCommandId.ChangeLayout or AppCommandId.Compress or AppCommandId.Extract);
            if (children.Length > 1)
            {
                foreach (var child in groupIds) taken.Add(child);
                var key = id is AppCommandId.NewFolder or AppCommandId.NewFile ? "Menu_New"
                    : id is AppCommandId.ChooseFolderCover or AppCommandId.ResetFolderCover or AppCommandId.ResetFolderView ? "Menu_FolderAppearance"
                    : "Menu_Selection";
                entry = new ContextMenuEntry(false, null, StringTable.Get(key), command.Glyph, null,
                    children.Any(child => child.Enabled), HasChevron: true, Children: children);
            }

            var presentationGroup = menu.IsBackground ? id switch
            {
                AppCommandId.SelectAll or AppCommandId.SelectSameType or AppCommandId.InvertSelection
                    or AppCommandId.NewFolder or AppCommandId.NewFile or AppCommandId.CopyPath => CommandGroup.Clipboard,
                AppCommandId.ShowShelf or AppCommandId.ChooseFolderCover or AppCommandId.ResetFolderCover
                    or AppCommandId.ResetFolderView or AppCommandId.Sort or AppCommandId.ChangeLayout => CommandGroup.View,
                AppCommandId.Refresh or AppCommandId.OpenInTerminal or AppCommandId.WhoLocks => CommandGroup.Open,
                _ => command.Group,
            } : command.Group;
            if (previous is { } group && group != presentationGroup)
            {
                entries.Add(Separator());
            }

            previous = presentationGroup;
            entries.Add(entry);
        }

        if (primary.Count == 0 && entries.Count == 0)
        {
            return new FileContextMenuLayout([], []);
        }

        if (entries.Count > 0)
        {
            entries.Add(Separator());
        }

        entries.Add(new ContextMenuEntry(
            false,
            null,
            StringTable.Get("Command_ShowMoreOptions"),
            "\uE712",
            null,
            true,
            IsShowMore: true));
        return new FileContextMenuLayout(primary, entries);
    }

    private static ContextMenuEntry Separator() =>
        new(true, null, string.Empty, string.Empty, null, false);

    private static ContextMenuEntry Item(AppCommand command, bool hasChevron) =>
        new(
            false,
            command.Id,
            command.Label,
            command.Glyph,
            command.Shortcut,
            command.Enabled,
            HasChevron: hasChevron);
}
