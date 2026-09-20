using FilesMate.App.Models;

namespace FilesMate.App.Controls.Tags;

/// <summary>
/// Small, immutable presentation summary used by recycled rows and tiles.
/// The file surface never creates a view-model per tag or per file.
/// </summary>
public readonly record struct TagVisualState(
    IReadOnlyList<TagDefinition> Visible,
    int OverflowCount);

public static class TagPresentation
{
    public const int MaxVisibleTags = 2;

    public static TagVisualState Summarize(
        IEnumerable<TagDefinition> tags,
        int maxVisible = MaxVisibleTags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        maxVisible = Math.Clamp(maxVisible, 0, 8);

        var unique = tags
            .Where(tag => tag.Id > 0 && !string.IsNullOrWhiteSpace(tag.Name))
            .GroupBy(tag => tag.Id)
            .Select(group => group
                .OrderBy(tag => tag.SortOrder)
                .ThenBy(tag => tag.Id)
                .First())
            .OrderBy(tag => tag.SortOrder)
            .ThenBy(tag => tag.Id)
            .ToArray();

        var visible = unique.Take(maxVisible).ToArray();
        return new TagVisualState(visible, Math.Max(0, unique.Length - visible.Length));
    }
}
