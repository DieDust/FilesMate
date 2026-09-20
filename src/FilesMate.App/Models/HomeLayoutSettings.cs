namespace FilesMate.App.Models;

public enum HomeSectionKind
{
    UserFolders,
    Drives,
    Cloud,
    Tags,
    System,
}

public sealed record HomeLayoutSettings(
    IReadOnlyList<HomeSectionKind> Order,
    IReadOnlySet<HomeSectionKind> Hidden)
{
    public static HomeLayoutSettings Default { get; } = new(
        Enum.GetValues<HomeSectionKind>(),
        new HashSet<HomeSectionKind>());

    public static HomeLayoutSettings Sanitize(IEnumerable<string>? order, IEnumerable<string>? hidden)
    {
        var known = Enum.GetValues<HomeSectionKind>();
        var parsedOrder = (order ?? [])
            .Select(Parse)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Distinct()
            .ToList();
        parsedOrder.AddRange(known.Where(value => !parsedOrder.Contains(value)));

        var parsedHidden = (hidden ?? [])
            .Select(Parse)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToHashSet();
        return new HomeLayoutSettings(parsedOrder, parsedHidden);
    }

    public HomeLayoutSettings Move(HomeSectionKind kind, int offset)
    {
        var next = Order.ToList();
        var current = next.IndexOf(kind);
        if (current < 0) return this;
        var target = Math.Clamp(current + offset, 0, next.Count - 1);
        if (target == current) return this;
        next.RemoveAt(current);
        next.Insert(target, kind);
        return this with { Order = next };
    }

    public HomeLayoutSettings SetVisible(HomeSectionKind kind, bool visible)
    {
        var next = Hidden.ToHashSet();
        if (visible) next.Remove(kind); else next.Add(kind);
        return this with { Hidden = next };
    }

    private static HomeSectionKind? Parse(string value) =>
        Enum.TryParse<HomeSectionKind>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : null;
}
