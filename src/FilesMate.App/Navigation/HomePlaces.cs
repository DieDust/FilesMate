using System.IO;

using FilesMate.App.Localization;
using FilesMate.App.Models;

namespace FilesMate.App.Navigation;

public sealed record HomeFolderItem(string Id, string Label, string Glyph, string Path);

public sealed record HomeDriveItem(
    string VolumeName,
    string Letter,
    string Path,
    string Glyph,
    long FreeBytes,
    long TotalBytes)
{
    public string Label => string.IsNullOrWhiteSpace(VolumeName)
        ? Letter
        : $"{VolumeName} ({Letter})";

    public int UsedPercent => DriveCapacity.PercentUsed(FreeBytes, TotalBytes);

    public bool IsLowSpace => DriveCapacity.IsLowSpace(FreeBytes, TotalBytes);

    public string CapacityLabel => DriveCapacity.FormatFreeOfTotal(FreeBytes, TotalBytes);

    public double UsedRatio => UsedPercent / 100d;
}

public static class HomePlaces
{
    private static readonly object DriveGate = new();
    private static Task<IReadOnlyList<HomeDriveItem>>? _driveRead;
#if FILESMATE_UI_TEST
    internal static Func<IReadOnlyList<HomeDriveItem>>? TestDriveReader;
#endif

    public static Task<IReadOnlyList<HomeDriveItem>> LoadDrivesAsync()
    {
        lock (DriveGate)
        {
            // Share an outstanding network/volume query across newly opened tabs.
            if (_driveRead is null || _driveRead.IsCompleted)
            {
#if FILESMATE_UI_TEST
                _driveRead = Task.Run(TestDriveReader ?? Drives);
#else
                _driveRead = Task.Run(Drives);
#endif
            }
            return _driveRead;
        }
    }
    public static IReadOnlyList<HomeFolderItem> UserFolders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        HomeFolderItem[] candidates =
        [
            new("user", StringTable.Get("Home_UserFolder"), "\uE77B", profile),
            new("desktop", StringTable.Get("Desktop"), "\uE8B7", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            new("documents", StringTable.Get("Documents"), "\uE8A5", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            new("downloads", StringTable.Get("Downloads"), "\uE896", string.IsNullOrEmpty(profile) ? string.Empty : Path.Combine(profile, "Downloads")),
            new("pictures", StringTable.Get("Pictures"), "\uEB9F", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            new("videos", StringTable.Get("Videos"), "\uE8B2", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            new("music", StringTable.Get("Music"), "\uE8D6", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
        ];
        return [.. candidates.Where(item => !string.IsNullOrWhiteSpace(item.Path) && Directory.Exists(item.Path))];
    }

    public static IReadOnlyList<HomeDriveItem> Drives()
    {
        var items = new List<HomeDriveItem>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    var letter = drive.Name.TrimEnd('\\');
                    items.Add(new HomeDriveItem(
                        WindowsNavigationSource.DriveLabel(drive),
                        letter,
                        drive.RootDirectory.FullName,
                        WindowsNavigationSource.DriveGlyph(drive.DriveType),
                        drive.AvailableFreeSpace,
                        drive.TotalSize));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }

        return items;
    }

    public static IReadOnlyList<HomeFolderItem> Cloud(PinnedLocationStore? store = null)
    {
        var cloud = WindowsNavigationSource.Cloud(store ?? new PinnedLocationStore(PinnedLocationStore.DefaultFilePath));
        return [.. cloud
            .Where(item => !string.IsNullOrWhiteSpace(item.Target))
            .Select(item => new HomeFolderItem(
                item.Id,
                item.Label,
                item.IconGlyph ?? "\uE753",
                item.Target!))];
    }

    public static IReadOnlyList<HomeFolderItem> Tags(IEnumerable<TagDefinition> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return [.. tags.Select(tag => new HomeFolderItem(
            "tag:" + tag.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tag.Name,
            LocationCaption.TagGlyph,
            TagLocation.Uri(tag.Id)))];
    }

    public static NavigationItem Place(HomeFolderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new NavigationItem(item.Id, item.Label, item.Glyph, item.Path);
    }

    public static NavigationItem Place(HomeDriveItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new NavigationItem("drive:" + item.Letter, item.Label, item.Glyph, item.Path);
    }
}
