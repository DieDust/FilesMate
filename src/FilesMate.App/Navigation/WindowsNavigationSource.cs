using System.IO;

using FilesMate.App.Localization;

namespace FilesMate.App.Navigation;

public static class WindowsNavigationSource
{
    private static readonly HashSet<string> DefaultPinnedIds =
    [
        "home", "desktop", "documents", "downloads", "pictures", "music", "videos",
    ];

    public static IReadOnlyList<NavigationSection> Create(
        PinnedLocationStore? pinnedStore = null,
        IReadOnlyList<NavigationItem>? tags = null)
    {
        var store = pinnedStore ?? new PinnedLocationStore(PinnedLocationStore.DefaultFilePath);
        var pinned = Pinned(store);
        List<NavigationItem>? home = null;
        for (var i = 0; i < pinned.Count; i++)
        {
            if (pinned[i].Id != "home")
            {
                continue;
            }

            home = [pinned[i]];
            pinned.RemoveAt(i);
            break;
        }

        return NavigationCatalog.WithSectionOrder(
            NavigationCatalog.Build(pinned, Drives(store), Cloud(store), home: home, tags: tags ?? []),
            store.LoadState().SectionOrder);
    }

    private static List<NavigationItem> Pinned(PinnedLocationStore? pinnedStore)
    {
        var store = pinnedStore ?? new PinnedLocationStore(PinnedLocationStore.DefaultFilePath);
        var state = store.LoadState();
        var hidden = state.HiddenDefaultIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaults = DefaultLocations();
        var items = new List<NavigationItem>(7);
        foreach (var location in defaults)
        {
            if (!hidden.Contains(location.Id))
            {
                Add(items, location.Id, location.Label, location.Glyph, location.Path);
            }
        }

        var knownPaths = new HashSet<string>(
            defaults.Select(location => PinnedLocationStore.Normalize(location.Path)).Where(path => path is not null).Select(path => path!),
            StringComparer.OrdinalIgnoreCase);
        foreach (var path in state.Paths)
        {
            if (!knownPaths.Add(path))
            {
                continue;
            }

            Add(items, "custom:" + path, DisplayName(path), "\uE8B7", path);
        }

        return PinnedLocationStore.OrderByIds(items, state.ItemOrder, item => item.Id);
    }

    public static bool IsDefaultPinnedId(string? id) =>
        id is not null && DefaultPinnedIds.Contains(id);

    public static bool TryGetDefaultId(string? path, out string id)
    {
        var normalized = PinnedLocationStore.Normalize(path);
        if (normalized is not null)
        {
            foreach (var location in DefaultLocations())
            {
                if (string.Equals(
                    normalized,
                    PinnedLocationStore.Normalize(location.Path),
                    StringComparison.OrdinalIgnoreCase))
                {
                    id = location.Id;
                    return true;
                }
            }
        }

        id = string.Empty;
        return false;
    }

    public static bool IsPinned(string? path, PinnedLocationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return TryGetDefaultId(path, out var id)
            ? !store.IsDefaultHidden(id)
            : store.IsPinned(path);
    }

    public static void Pin(string path, PinnedLocationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (TryGetDefaultId(path, out var id))
        {
            store.ShowDefault(id);
        }
        else
        {
            store.Add(path);
        }
    }

    public static void Unpin(string path, PinnedLocationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (TryGetDefaultId(path, out var id))
        {
            store.HideDefault(id);
        }
        else
        {
            store.Remove(path);
        }
    }

    private static IReadOnlyList<DefaultLocation> DefaultLocations()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            new("home", StringTable.Get("Home"), "\uE80F", HomeLocation.Uri),
            new("desktop", StringTable.Get("Desktop"), "\uE8B7", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            new("documents", StringTable.Get("Documents"), "\uE8A5", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            new("downloads", StringTable.Get("Downloads"), "\uE896", string.IsNullOrEmpty(profile) ? null : Path.Combine(profile, "Downloads")),
            new("pictures", StringTable.Get("Pictures"), "\uEB9F", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            new("music", StringTable.Get("Music"), "\uE8D6", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            new("videos", StringTable.Get("Videos"), "\uE8B2", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        ];
    }

    private static List<NavigationItem> Drives(PinnedLocationStore store)
    {
        var items = new List<NavigationItem>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady && drive.DriveType is not DriveType.Network and not DriveType.Removable)
                        continue;
                    var letter = drive.Name.TrimEnd('\\');
                    var volume = DriveLabel(drive);
                    Add(items, "drive:" + letter, $"{volume} ({letter})", DriveGlyph(drive.DriveType), drive.RootDirectory.FullName);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (IOException)
        {
        }

        return PinnedLocationStore.OrderByIds(items, store.LoadState().DriveOrder, item => item.Id);
    }

    internal static List<NavigationItem> Cloud(PinnedLocationStore store)
    {
        var items = new List<NavigationItem>();
        var state = store.LoadState();
        void TryAdd(string id, string path)
        {
            Add(items, id, DisplayName(path), "\uE753", path);
        }

        foreach (var key in new[] { "OneDriveCommercial", "OneDriveConsumer", "OneDrive" })
        {
            var path = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(path))
            {
                TryAdd("cloud:" + key, path);
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            TryAdd("cloud:GoogleDrive", Path.Combine(profile, "Google Drive"));
            TryAdd("cloud:GoogleDriveDesktop", Path.Combine(profile, "Google Drive for desktop"));
            TryAdd("cloud:MyDrive", Path.Combine(profile, "My Drive"));
            TryAdd("cloud:Dropbox", Path.Combine(profile, "Dropbox"));
            TryAdd("cloud:iCloudDrive", Path.Combine(profile, "iCloudDrive"));
            TryAdd("cloud:iCloudDriveSpaced", Path.Combine(profile, "iCloud Drive"));
            TryAdd("cloud:WpsCloud", Path.Combine(profile, "WPS Cloud"));
            TryAdd("cloud:WpsYunPan", Path.Combine(profile, "WPS云盘"));
            TryAdd("cloud:WpsYunDocs", Path.Combine(profile, "WPS云文档"));
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents))
        {
            TryAdd("cloud:WpsCloudDocuments", Path.Combine(documents, "WPS Cloud"));
            TryAdd("cloud:WpsYunDocuments", Path.Combine(documents, "WPS云盘"));
        }

        foreach (var path in state.CloudPaths)
        {
            TryAdd("cloud:custom:" + (PinnedLocationStore.Normalize(path) ?? path), path);
        }

        return ResolveCloudLocations(items, state, Directory.Exists);
    }

    internal static List<NavigationItem> ResolveCloudLocations(
        IEnumerable<NavigationItem> candidates, PinnedLocationState state, Func<string, bool> exists)
    {
        var overrides = (state.CloudOverrides ?? []).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var hidden = (state.HiddenCloudPaths ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<NavigationItem>();
        foreach (var candidate in candidates)
        {
            overrides.TryGetValue(candidate.Id, out var saved);
            saved ??= (state.CloudOverrides ?? []).FirstOrDefault(item => PinnedLocationStore.PathsEqual(item.OriginalTarget, candidate.Target));
            var item = saved is not null
                ? new NavigationItem(saved.Id, saved.Label, candidate.IconGlyph, saved.Target) : candidate;
            var target = PinnedLocationStore.Normalize(item.Target);
            if (target is null || hidden.Contains(target) || !seen.Add(target) || !exists(target)) continue;
            items.Add(item);
        }
        return PinnedLocationStore.OrderByIds(items, state.CloudOrder, item => item.Id);
    }

    internal static string DriveLabel(DriveInfo drive)
    {
        try
        {
            if (drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel))
            {
                return drive.VolumeLabel;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return drive.DriveType switch
        {
            DriveType.Network => StringTable.Get("NetworkDrive"),
            DriveType.Removable => StringTable.Get("RemovableDrive"),
            DriveType.CDRom => StringTable.Get("RemovableDrive"),
            _ => StringTable.Get("LocalDisk"),
        };
    }

    internal static string DriveGlyph(DriveType type) => type switch
    {
        DriveType.Network => "\uE968",
        DriveType.Removable or DriveType.CDRom => "\uE88E",
        _ => "\uEDA2",
    };

    private static void Add(List<NavigationItem> items, string id, string label, string glyph, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        items.Add(new NavigationItem(id, label, glyph, path));
    }

    private static string DisplayName(string path)
    {
        var normalized = PinnedLocationStore.Normalize(path) ?? path;
        var name = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return Path.GetPathRoot(normalized)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            ?? normalized;
    }

    private sealed record DefaultLocation(string Id, string Label, string Glyph, string? Path);
}
