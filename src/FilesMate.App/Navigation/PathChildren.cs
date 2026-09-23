using System.IO;

namespace FilesMate.App.Navigation;

public static class PathChildren
{
    public const int Limit = 48;

    public static async Task<IReadOnlyList<PathSegment>> FoldersAsync(
        string path, bool showHidden, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows() && FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out var device))
        {
            var children = await FilesMate.Platform.Windows.Shell.PortableDeviceService.ReadFolderAsync(device, cancellationToken).ConfigureAwait(false);
            return children.Where(e => e.IsFolder).Take(Limit).Select(e => new PathSegment(e.Name, e.Location.Uri)).ToArray();
        }
        return await Task.Run(() => Folders(path, showHidden, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<PathSegment> Folders(
        string path, bool showHidden, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || TagLocation.TryParse(path, out _))
        {
            return [];
        }

        if (HomeLocation.IsHome(path))
        {
            return [.. HomePlaces.UserFolders()
                .Select(item => new PathSegment(item.Label, item.Path, item.Glyph))];
        }

        var skip = showHidden
            ? FileAttributes.None
            : FileAttributes.Hidden | FileAttributes.System;
        try
        {
            var names = Directory.EnumerateDirectories(
                path,
                "*",
                new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    AttributesToSkip = skip,
                });
            // Retain only the first alphabetic page, even in a very large folder.
            var comparer = Comparer<string>.Create((left, right) =>
            {
                var order = Path.GetFileName(left.AsSpan()).CompareTo(
                    Path.GetFileName(right.AsSpan()), StringComparison.CurrentCultureIgnoreCase);
                return order != 0 ? order : StringComparer.Ordinal.Compare(left, right);
            });
            var selected = new SortedSet<string>(comparer);
            foreach (var name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (selected.Count == Limit && comparer.Compare(name, selected.Max!) >= 0)
                {
                    continue;
                }

                selected.Add(name);
                if (selected.Count > Limit)
                {
                    selected.Remove(selected.Max!);
                }
            }

            return [.. selected.Select(item => new PathSegment(Path.GetFileName(item), item))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}
