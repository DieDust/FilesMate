using FilesMate.App.Commands;
using FilesMate.App.Localization;

namespace FilesMate.App.Controls.Toolbar;

public enum ToolbarCommandId
{
    New,
    Paste,
    Cut,
    Copy,
    Rename,
    Share,
    Delete,
    Sort,
    View,
    More,
    CopyPath
}

public sealed class ToolbarCommandState
{
    public required ToolbarCommandId Id { get; init; }

    public required string Label { get; init; }

    public required bool Visible { get; init; }

    public required bool Enabled { get; init; }

    public string? Tooltip { get; init; }
}

public readonly record struct ToolbarOverflow(bool ShowSort, bool ShowView, bool ShowMore)
{
    public static ToolbarOverflow ForWidth(double width) =>
        new(
            ShowSort: width >= 280,
            ShowView: width >= 200,
            ShowMore: width < 280);
}

public static class ToolbarOverflowController
{
    public static IReadOnlyList<ToolbarCommandState> Present(CommandContext context)
    {
        var toolbar = context.Surface == CommandSurface.Toolbar
            ? context
            : context with { Surface = CommandSurface.Toolbar };

        return
        [
            From(toolbar, ToolbarCommandId.New, AppCommandId.NewFolder),
            From(toolbar, ToolbarCommandId.Paste, AppCommandId.Paste),
            From(toolbar, ToolbarCommandId.Cut, AppCommandId.Cut),
            From(toolbar, ToolbarCommandId.Copy, AppCommandId.Copy),
            From(toolbar, ToolbarCommandId.Rename, AppCommandId.Rename),
            From(toolbar, ToolbarCommandId.Share, AppCommandId.Share),
            From(toolbar, ToolbarCommandId.Delete, AppCommandId.Recycle),
            From(toolbar, ToolbarCommandId.Sort, AppCommandId.Sort),
            From(toolbar, ToolbarCommandId.View, AppCommandId.ChangeLayout),
            new()
            {
                Id = ToolbarCommandId.More,
                Label = StringTable.Get("Nav_More"),
                Visible = true,
                Enabled = true,
                Tooltip = StringTable.Get("Nav_More"),
            },
            From(toolbar, ToolbarCommandId.CopyPath, AppCommandId.CopyPath),
        ];
    }

    private static ToolbarCommandState From(CommandContext context, ToolbarCommandId slot, AppCommandId id)
    {
        var command = CommandCatalog.Resolve(id, context);
        return new()
        {
            Id = slot,
            Label = slot == ToolbarCommandId.New ? StringTable.Get("Command_New") : command.Label,
            Visible = command.Visible,
            Enabled = command.Enabled,
            Tooltip = command.Tooltip,
        };
    }
}
