using FilesMate.App.Controls.Tags;
using FilesMate.App.Models;

namespace FilesMate.App.Tests.Metadata;

public sealed class TagPickerLogicTests
{
    private static readonly TagDefinition Work = new(1, "Work", "#2F80ED", 0);
    private static readonly TagDefinition Urgent = new(2, "Urgent", "#EB5757", 1);
    private static readonly TagDefinition Review = new(3, "Review", "#F2C94C", 2);

    [Fact]
    public void Filter_is_case_insensitive_and_create_requires_a_new_name()
    {
        var tags = new[] { Work, Urgent, Review };
        Assert.Equal(["Work", "Urgent", "Review"], TagPickerLogic.Filter(tags, " ").Select(tag => tag.Name));
        Assert.Equal(["Urgent"], TagPickerLogic.Filter(tags, "urg").Select(tag => tag.Name));
        Assert.Equal(Urgent, TagPickerLogic.Match(tags, "URGENT"));
        Assert.True(TagPickerLogic.CanCreate(tags, "Later"));
        Assert.False(TagPickerLogic.CanCreate(tags, "work"));
        Assert.False(TagPickerLogic.CanCreate(tags, "  "));
    }

    [Fact]
    public void Shared_ids_are_the_intersection_and_toggle_adds_or_removes()
    {
        var shared = TagPickerLogic.SharedIds(
        [
            [Work, Urgent],
            [Urgent, Review],
        ]);
        Assert.Equal([2L], shared.OrderBy(id => id));

        var added = TagPickerLogic.With([1L, 2L], 3, apply: true);
        Assert.Equal(new HashSet<long> { 1, 2, 3 }, added);
        Assert.Equal(new HashSet<long> { 1, 3 }, TagPickerLogic.With(added, 2, apply: false));
    }
}
