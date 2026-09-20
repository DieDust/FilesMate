using FilesMate.App.Models;

namespace FilesMate.App.Controls.Tags;

public static class TagPickerLogic
{
    public static IReadOnlyList<TagDefinition> Filter(IEnumerable<TagDefinition> tags, string query)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var text = query.Trim();
        if (text.Length == 0)
        {
            return tags as IReadOnlyList<TagDefinition> ?? [.. tags];
        }

        return [.. tags.Where(tag => tag.Name.Contains(text, StringComparison.OrdinalIgnoreCase))];
    }

    public static TagDefinition? Match(IEnumerable<TagDefinition> tags, string query)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var text = query.Trim();
        return text.Length == 0
            ? null
            : tags.FirstOrDefault(tag => tag.Name.Equals(text, StringComparison.OrdinalIgnoreCase));
    }

    public static bool CanCreate(IEnumerable<TagDefinition> tags, string query) =>
        query.Trim().Length > 0 && Match(tags, query) is null;

    public static HashSet<long> SharedIds(IEnumerable<IReadOnlyList<TagDefinition>> perFile)
    {
        ArgumentNullException.ThrowIfNull(perFile);
        HashSet<long>? shared = null;
        foreach (var tags in perFile)
        {
            var ids = tags.Select(tag => tag.Id).ToHashSet();
            if (shared is null)
            {
                shared = ids;
                continue;
            }

            shared.IntersectWith(ids);
        }

        return shared ?? [];
    }

    public static HashSet<long> With(IReadOnlyCollection<long> current, long id, bool apply)
    {
        ArgumentNullException.ThrowIfNull(current);
        var next = current.ToHashSet();
        if (apply)
        {
            next.Add(id);
        }
        else
        {
            next.Remove(id);
        }

        return next;
    }
}
