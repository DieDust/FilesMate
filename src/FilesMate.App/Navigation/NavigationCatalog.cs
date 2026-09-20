using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FilesMate.App.Localization;

namespace FilesMate.App.Navigation;

public sealed class NavigationItem : INotifyPropertyChanged
{
    private bool _selected;

    public NavigationItem(
        string id,
        string label,
        string? iconGlyph,
        string? target,
        bool selected = false,
        bool expandable = false,
        IReadOnlyList<NavigationItem>? children = null,
        string? badge = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Id = id;
        Label = label;
        IconGlyph = iconGlyph;
        Target = target;
        Selected = selected;
        Expandable = expandable;
        Children = children ?? [];
        Badge = badge;
    }

    public string Id { get; }

    public string Label { get; }

    public string? IconGlyph { get; }

    public string? Target { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            OnPropertyChanged();
        }
    }

    public bool Expandable { get; }

    public IReadOnlyList<NavigationItem> Children { get; }

    public string? Badge { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class NavigationSection : INotifyPropertyChanged
{
    private bool _expanded = true;

    public NavigationSection(string id, string title, IReadOnlyList<NavigationItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(title);
        Id = id;
        Title = title;
        Items = new ObservableCollection<NavigationItem>(items ?? []);
    }

    public string Id { get; }

    public string Title { get; }

    public bool ShowTitle => Title.Length > 0;

    public bool AllowReorder => Id is not "home";

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public ObservableCollection<NavigationItem> Items { get; }

    public bool HasActions => Id is "pinned" or "drives" or "cloud" or "tags";

    public string ActionLabel => Id switch
    {
        "pinned" => StringTable.Get("Sidebar_PinFolder"),
        "drives" => StringTable.Get("Sidebar_MapNetworkDrive"),
        "cloud" => StringTable.Get("Sidebar_AddCloud"),
        "tags" => StringTable.Get("Command_ManageTags"),
        _ => Title,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class NavigationCatalog
{
    public static readonly string[] SectionOrder = ["home", "pinned", "drives", "cloud", "network", "wsl", "tags"];

    public static IReadOnlyList<NavigationSection> Build(
        IReadOnlyList<NavigationItem> pinned,
        IReadOnlyList<NavigationItem> drives,
        IReadOnlyList<NavigationItem>? cloud = null,
        IReadOnlyList<NavigationItem>? network = null,
        IReadOnlyList<NavigationItem>? home = null,
        IReadOnlyList<NavigationItem>? wsl = null,
        IReadOnlyList<NavigationItem>? tags = null)
    {
        var sections = new List<NavigationSection>(7);
        Add(sections, "home", string.Empty, home);
        Add(sections, "pinned", StringTable.Get("Nav_Pinned"), pinned, allowEmpty: true);
        Add(sections, "drives", StringTable.Get("Nav_Drives"), drives, allowEmpty: true);
        Add(sections, "cloud", StringTable.Get("Nav_Cloud"), cloud, allowEmpty: true);
        Add(sections, "network", StringTable.Get("Nav_Network"), network);
        Add(sections, "wsl", StringTable.Get("Nav_Wsl"), wsl);
        Add(sections, "tags", StringTable.Get("Nav_Tags"), tags, allowEmpty: true);
        return sections;
    }

    public static NavigationItem? Match(IReadOnlyList<NavigationSection> sections, string? path, bool exact = false)
    {
        NavigationItem? exactMatch = null;
        NavigationItem? prefix = null;
        var prefixLength = -1;
        var target = (path ?? string.Empty).TrimEnd('\\', '/');
        foreach (var item in Enumerate(sections))
        {
            if (string.IsNullOrEmpty(item.Target))
            {
                continue;
            }

            if (PinnedLocationStore.PathsEqual(item.Target, path))
            {
                exactMatch = item;
                continue;
            }

            if (exact)
            {
                continue;
            }

            var candidate = item.Target.TrimEnd('\\', '/');
            var isDrive = candidate.Length == 2 && candidate[1] == ':';
            var nested = target.StartsWith(candidate + "\\", StringComparison.OrdinalIgnoreCase)
                || (isDrive && target.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
            if (nested && candidate.Length > prefixLength)
            {
                prefix = item;
                prefixLength = candidate.Length;
            }
        }

        return exactMatch ?? (exact ? null : prefix);
    }

    public static void SelectPath(IReadOnlyList<NavigationSection> sections, string? path)
    {
        var selected = Match(sections, path);
        foreach (var item in Enumerate(sections))
        {
            item.Selected = ReferenceEquals(item, selected);
        }

        if (selected is null)
        {
            return;
        }

        foreach (var section in sections)
        {
            if (!section.ShowTitle)
            {
                continue;
            }

            foreach (var item in section.Items)
            {
                if (ReferenceEquals(item, selected) || item.Children.Contains(selected))
                {
                    section.IsExpanded = true;
                    break;
                }
            }
        }
    }

    public static IEnumerable<NavigationItem> Enumerate(IReadOnlyList<NavigationSection> sections)
    {
        foreach (var section in sections)
        {
            foreach (var item in section.Items)
            {
                yield return item;
                foreach (var child in item.Children)
                {
                    yield return child;
                }
            }
        }
    }

    private static void Add(
        List<NavigationSection> sections,
        string id,
        string title,
        IReadOnlyList<NavigationItem>? items,
        bool allowEmpty = false)
    {
        if (items is null || (items.Count == 0 && !allowEmpty))
        {
            return;
        }

        sections.Add(new NavigationSection(id, title, items));
    }

    public static IReadOnlyList<NavigationSection> WithSectionOrder(
        IReadOnlyList<NavigationSection> sections,
        IReadOnlyList<string>? order)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var home = new List<NavigationSection>();
        var rest = new List<NavigationSection>();
        foreach (var section in sections)
        {
            if (section.Id == "home")
            {
                home.Add(section);
            }
            else
            {
                rest.Add(section);
            }
        }

        return [.. home, .. PinnedLocationStore.OrderByIds(rest, order, section => section.Id)];
    }
}
