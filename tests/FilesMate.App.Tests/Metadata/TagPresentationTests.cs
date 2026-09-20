using FilesMate.App.Commands;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Controls.Tags;
using FilesMate.App.Models;

namespace FilesMate.App.Tests.Metadata;

public sealed class TagPresentationTests
{
    [Fact]
    public void Visible_tag_summary_is_bounded_to_two_dots()
    {
        var tags = new[]
        {
            new TagDefinition(1, "Work", "#2F80ED", 0),
            new TagDefinition(2, "Urgent", "#EB5757", 1),
            new TagDefinition(3, "Review", "#F2C94C", 2),
        };

        var summary = TagPresentation.Summarize(tags);

        Assert.Equal(2, summary.Visible.Count);
        Assert.Equal(["Work", "Urgent"], summary.Visible.Select(tag => tag.Name));
        Assert.Equal(1, summary.OverflowCount);
    }

    [Fact]
    public void Empty_and_duplicate_tags_have_a_stable_summary()
    {
        var tags = new[]
        {
            new TagDefinition(2, "Urgent", "#EB5757", 1),
            new TagDefinition(2, "Urgent", "#EB5757", 1),
            new TagDefinition(1, "Work", "#2F80ED", 0),
        };

        var summary = TagPresentation.Summarize(tags);

        Assert.Equal(["Work", "Urgent"], summary.Visible.Select(tag => tag.Name));
        Assert.Equal(0, summary.OverflowCount);
        Assert.Empty(TagPresentation.Summarize([]).Visible);
    }

    [Fact]
    public void Context_menu_exposes_add_and_manage_tags_for_selected_items()
    {
        var labels = FileContextMenuBuilder.Build(CommandContext.SingleFile with { TagsAvailable = true })
            .Where(entry => !entry.IsSeparator)
            .Select(entry => entry.Label)
            .ToArray();

        Assert.Contains("Add tags", labels);
        Assert.DoesNotContain("Manage tags", labels);
        Assert.Contains(
            FileContextMenuBuilder.Build(CommandContext.SingleFile with { TagsAvailable = true }),
            entry => entry.Command == AppCommandId.AddTags && entry.HasChevron);
    }
}
