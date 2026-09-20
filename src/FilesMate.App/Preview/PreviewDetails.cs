using System.IO;

using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Localization;
using FilesMate.App.Models;

namespace FilesMate.App.Preview;

public static class PreviewDetails
{
    public static string DisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    public static string Location(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var parent = Path.GetDirectoryName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(parent) ? path : parent;
    }

    public static string TypeLabel(string path, bool isDirectory) =>
        FileRowFormatter.FormatType(path, isDirectory);

    public static string Modified(DateTimeOffset? utc, DateFormatKind format = DateFormatKind.System) =>
        utc is null ? "—" : FileRowFormatter.FormatModified(utc.Value.UtcTicks, format);

    public static string Items(int? count)
    {
        if (count is null)
        {
            return "—";
        }

        return count > 500
            ? StringTable.Format("Status_Items", "500+")
            : StringTable.Format("Status_Items", count.Value);
    }

    public static string? Attributes(FileAttributes attributes)
    {
        var parts = new List<string>();
        if ((attributes & FileAttributes.Hidden) != 0)
        {
            parts.Add(StringTable.Get("Preview_Hidden"));
        }

        if ((attributes & FileAttributes.System) != 0)
        {
            parts.Add(StringTable.Get("Preview_System"));
        }

        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            parts.Add(StringTable.Get("Preview_ReadOnly"));
        }

        return parts.Count == 0 ? null : string.Join(StringTable.Get("Preview_AttributeSeparator"), parts);
    }
}
