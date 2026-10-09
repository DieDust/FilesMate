namespace FilesMate.App.Models;

public enum ToolbarZone { FileCommands, ViewTools }

public sealed record ToolbarToolDefinition(ToolbarTool Id, string LabelKey, string Glyph, ToolbarZone Zone, string ElementName, bool AvailableInSearch = true);

public static class ToolbarToolCatalog
{
    // One registration drives the live toolbar and its editor. ElementName
    // resolves the real XAML command unit, including composite controls.
    public static IReadOnlyList<ToolbarToolDefinition> All { get; } = [
        new(ToolbarTool.New, "Command_New", "\uE8F4", ToolbarZone.FileCommands, "NewButton", false),
        new(ToolbarTool.Cut, "Command_Cut", "\uE8C6", ToolbarZone.FileCommands, "CutButton"),
        new(ToolbarTool.Copy, "Command_Copy", "\uE8C8", ToolbarZone.FileCommands, "CopyButton"),
        new(ToolbarTool.Paste, "Command_Paste", "\uE77F", ToolbarZone.FileCommands, "PasteButton"),
        new(ToolbarTool.Rename, "Command_Rename", "\uE8AC", ToolbarZone.FileCommands, "RenameButton"),
        new(ToolbarTool.Share, "Command_Share", "\uE72D", ToolbarZone.FileCommands, "ShareButton"),
        new(ToolbarTool.Delete, "Command_Recycle", "\uE74D", ToolbarZone.FileCommands, "DeleteButton"),
        new(ToolbarTool.HiddenFiles, "ShowHiddenFilesTitle", "\uED1A", ToolbarZone.FileCommands, "HiddenFilesButton", false),
        new(ToolbarTool.CopyPath, "Command_CopyPath", "\uE71B", ToolbarZone.FileCommands, "CopyPathButton"),
        new(ToolbarTool.Shelf, "Shelf_Title", "\uE7B8", ToolbarZone.ViewTools, "ShelfButton"),
        new(ToolbarTool.Sort, "Sort", "\uE8CB", ToolbarZone.ViewTools, "SortButton", false),
        new(ToolbarTool.Grouping, "Grouping_Options", "\uE8FD", ToolbarZone.ViewTools, "GroupingGroup"),
        new(ToolbarTool.FolderSizes, "ShowFolderSizesTitle", "\uE838", ToolbarZone.ViewTools, "FolderSizesButton", false),
        new(ToolbarTool.Filters, "Inline_Filter", "\uE71C", ToolbarZone.ViewTools, "FilterHeader", false),
        new(ToolbarTool.ViewMenu, "View_Options", "\uE8FD", ToolbarZone.ViewTools, "ViewMenuButton"),
        new(ToolbarTool.SplitView, "DualPane", "\uE7C3", ToolbarZone.ViewTools, "SplitViewGroup", false),
        new(ToolbarTool.Preview, "PreviewPaneTitle", "\uE946", ToolbarZone.ViewTools, "PreviewButton"),
    ];

    public static ToolbarToolDefinition Get(ToolbarTool id) => All.First(d => d.Id == id);

    // Keep saved relative order. Insert a newly introduced tool beside its
    // preceding default neighbor, without changing visibility preferences.
    public static IReadOnlyList<ToolbarTool> NormalizeOrder(IEnumerable<ToolbarTool>? stored)
    {
        var known = All.Select(d => d.Id).ToHashSet();
        var result = (stored ?? []).Where(known.Contains).Distinct().ToList();
        for (var i = 0; i < All.Count; i++)
        {
            var id = All[i].Id;
            if (result.Contains(id)) continue;
            var preceding = i - 1;
            while (preceding >= 0 && !result.Contains(All[preceding].Id)) preceding--;
            var index = preceding < 0 ? 0 : result.IndexOf(All[preceding].Id) + 1;
            result.Insert(index, id);
        }
        return result;
    }

    public static IReadOnlyList<ToolbarTool>? ParseOrder(IEnumerable<string>? values)
    {
        if (values is null) return null;
        var parsed = values.Where(v => !int.TryParse(v, out _))
            .Select(v => Enum.TryParse<ToolbarTool>(v, true, out var id) ? (ToolbarTool?)id : null)
            .OfType<ToolbarTool>();
        return NormalizeOrder(parsed);
    }

    public static IEnumerable<ToolbarToolDefinition> Ordered(IEnumerable<ToolbarTool>? stored, ToolbarZone zone, bool searchMode) =>
        NormalizeOrder(stored).Select(Get)
            .Where(d => d.Zone == zone && (!searchMode || d.AvailableInSearch));
}
