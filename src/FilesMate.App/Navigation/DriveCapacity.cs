using System.Globalization;
using System.IO;

using FilesMate.App.Localization;

namespace FilesMate.App.Navigation;

public static class DriveCapacity
{
    public static bool IsVolumeRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.Length == 2 && char.IsLetter(path[0]) && path[1] == ':') return true;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var root = Path.GetPathRoot(full);
            return !string.IsNullOrEmpty(root)
                && string.Equals(full, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static int PercentUsed(long freeBytes, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return 0;
        }

        var used = Math.Clamp(totalBytes - Math.Max(0, freeBytes), 0, totalBytes);
        return (int)Math.Clamp(Math.Round(used * 100d / totalBytes), 0, 100);
    }

    public static bool IsLowSpace(long freeBytes, long totalBytes) =>
        totalBytes > 0 && freeBytes * 100d / totalBytes < 10;

    public static string FormatBytes(long bytes)
    {
        var size = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = size;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{size} {units[0]}")
            : string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[unit]}");
    }

    public static string FormatFreeOfTotal(long freeBytes, long totalBytes) =>
        StringTable.Format(
            "Status_FreeOfTotal",
            FormatBytes(Math.Max(0, freeBytes)),
            FormatBytes(Math.Max(0, totalBytes)));

    public static string? FormatFreeSpace(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || HomeLocation.IsHome(path)
            || TagLocation.TryParse(path, out _))
        {
            return null;
        }

        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return null;
            }

            return FormatFreeOfTotal(drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
