using System.Globalization;
using System.IO;

using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.Core.Entries;

namespace FilesMate.App.Controls.FileSurface;

public static class FileColumnLayout
{
    public const double RowHeight = 28;
    public const double RowGap = 0;
    public const double RowStride = RowHeight + RowGap;
    public const double RowInset = 2;
    public const double ContentLeft = 12;
    public const double ContentRight = 16;
    public const double AccentWidth = 3;
    public const double GlyphWidth = 24;
    public const double NameWidth = 240;
    public const double ModifiedWidth = 148;
    public const double TypeWidth = 100;
    public const double SizeWidth = 88;
    public const double MinNameWidth = 96;
    public const double MinMetaWidth = 64;
    public const double MaxColumnWidth = 560;
    public const double NameCellPad = 4;
    public const double DetailsIconSize = 20;
    public const double TileMinWidth = 120;
    public const double TileMinHeight = 148;
    public const double TileColumnSpacing = 8;
    public const double TileRowSpacing = 8;
    public const double TileWidth = TileMinWidth + TileColumnSpacing;
    public const double TileHeight = TileMinHeight + TileRowSpacing;

    public const double NameHeaderPad = AccentWidth + GlyphWidth + NameCellPad;

    public static double ColumnsWidth(double name, double modified, double type, double size) =>
        AccentWidth + GlyphWidth + name + modified + type + size;

    public static double RowWidth(double name, double modified, double type, double size) =>
        ContentLeft + ContentRight + ColumnsWidth(name, modified, type, size);

    public static double ClampName(double width) => Math.Clamp(width, MinNameWidth, MaxColumnWidth);

    public static double ClampMeta(double width) => Math.Clamp(width, MinMetaWidth, MaxColumnWidth);

    public static int IndexFromPoint(
        double x,
        double y,
        int count,
        double name,
        double modified,
        double type,
        double size)
    {
        if (count <= 0 || y < 0)
        {
            return -1;
        }

        var row = (int)(y / RowStride);
        if ((uint)row >= (uint)count)
        {
            return -1;
        }

        var localY = y - (row * RowStride);
        if (localY < RowInset || localY >= RowHeight - RowInset)
        {
            return -1;
        }

        var left = ContentLeft;
        var right = left + ColumnsWidth(name, modified, type, size);
        return x < left || x >= right ? -1 : row;
    }

    public static void CollectIndicesInRect(
        double left,
        double top,
        double right,
        double bottom,
        int count,
        double name,
        double modified,
        double type,
        double size,
        List<int> into)
    {
        // Match the painted row and point hit testing, not the full viewport.
        var contentRight = ContentLeft + ColumnsWidth(name, modified, type, size);
        if (count <= 0 || bottom <= top || right <= left ||
            right <= ContentLeft || left >= contentRight)
        {
            return;
        }

        var from = Math.Max(0, (int)Math.Floor(top / RowStride) - 1);
        var to = Math.Min(count - 1, (int)Math.Floor((bottom - double.Epsilon) / RowStride) + 1);
        for (var i = from; i <= to; i++)
        {
            var contentTop = (i * RowStride) + RowInset;
            var contentBottom = (i * RowStride) + RowHeight - RowInset;
            if (bottom > contentTop && top < contentBottom)
            {
                into.Add(i);
            }
        }
    }
}

public enum FileLayoutKind
{
    Details = 0,
    Grid = 1,
}

public readonly record struct FileRowContent(
    string Name,
    string Modified,
    string Type,
    string Size,
    bool IsDirectory,
    bool IsSelected,
    bool IsHidden);

public static class FileRowVisualStates
{
    public const string Normal = "Normal";
    public const string PointerOver = "PointerOver";
    public const string Pressed = "Pressed";
    public const string Selected = "Selected";
    public const string SelectedPointerOver = "SelectedPointerOver";
    public const string SelectedPressed = "SelectedPressed";
    public const string Focused = "Focused";
    public const string Dragging = "Dragging";
    public const string DropTarget = "DropTarget";

    public static readonly string[] All =
    [
        Normal,
        PointerOver,
        Pressed,
        Selected,
        SelectedPointerOver,
        SelectedPressed,
        Focused,
        Dragging,
        DropTarget,
    ];

    public static string Resolve(
        bool selected,
        bool pointerOver,
        bool pressed,
        bool focused,
        bool dragging,
        bool dropTarget)
    {
        if (dropTarget)
        {
            return DropTarget;
        }

        if (dragging)
        {
            return Dragging;
        }

        if (selected && pressed)
        {
            return SelectedPressed;
        }

        if (selected && pointerOver)
        {
            return SelectedPointerOver;
        }

        if (selected)
        {
            return Selected;
        }

        if (pressed)
        {
            return Pressed;
        }

        if (pointerOver)
        {
            return PointerOver;
        }

        if (focused)
        {
            return Focused;
        }

        return Normal;
    }
}

/// <summary>
/// Formats a published entry for a recycled details row. No filesystem access.
/// </summary>
public static class FileRowFormatter
{
    public static FileRowContent Format(
        in FileEntryCore entry,
        bool selected,
        bool showFileExtensions = true,
        DateFormatKind dateFormat = DateFormatKind.Iso)
    {
        return new FileRowContent(
            DisplayName(entry, showFileExtensions),
            FormatModified(entry.ModifiedUtcTicks, dateFormat),
            FormatType(entry),
            FormatSize(entry),
            entry.Kind == EntryKind.Directory,
            selected,
            IsGhosted(entry.Attributes));
    }

    public static string DisplayName(in FileEntryCore entry, bool showFileExtensions)
    {
        var name = FileName(entry.Name);
        if (entry.Kind == EntryKind.Directory || showFileExtensions)
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrEmpty(stem) ? name : stem;
    }

    private static string FileName(string name)
    {
        if (name.IndexOfAny(['\\', '/']) < 0)
        {
            return name;
        }

        var fileName = Path.GetFileName(name.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(fileName) ? name : fileName;
    }

    public static bool IsGhosted(FileAttributes attributes) =>
        (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    public static string FormatModified(long utcTicks, DateFormatKind dateFormat = DateFormatKind.Iso)
    {
        if (utcTicks <= 0)
        {
            return string.Empty;
        }

        var local = new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime();
        if (dateFormat != DateFormatKind.System)
        {
            return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        var culture = CultureInfo.CurrentCulture;
        var pattern = PadDateTimePattern(culture.DateTimeFormat.ShortDatePattern)
            + " "
            + PadDateTimePattern(culture.DateTimeFormat.ShortTimePattern);
        return local.ToString(pattern, culture);
    }

    public static string PadDateTimePattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        return PadUnit(PadUnit(PadUnit(PadUnit(PadUnit(pattern, 'd'), 'M'), 'H'), 'h'), 'm');
    }

    public static string FormatType(in FileEntryCore entry) =>
        FormatType(entry.Name, entry.Kind == EntryKind.Directory);

    public static string FormatType(string name, bool directory)
    {
        if (directory)
        {
            return StringTable.Get("Type_Folder");
        }

        var extension = Path.GetExtension(name);
        return string.IsNullOrEmpty(extension)
            ? StringTable.Get("Type_File")
            : extension.TrimStart('.').ToUpperInvariant() + " " + StringTable.Get("Type_FileSuffix");
    }

    public static string FormatSize(in FileEntryCore entry)
    {
        if (entry.Kind == EntryKind.Directory || entry.Size == 0)
        {
            return entry.Kind == EntryKind.Directory ? string.Empty : "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = entry.Size;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{entry.Size} {units[0]}"
            : string.Create(CultureInfo.CurrentCulture, $"{size:0.#} {units[unit]}");
    }

    public static string Glyph(in FileEntryCore entry)
    {
        if (entry.Kind == EntryKind.Directory)
        {
            return "\uE8B7";
        }

        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        if (IsImageExtension(extension))
        {
            return "\uEB9F";
        }

        return extension switch
        {
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" => "\uE8B2",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".wma" => "\uE8D6",
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".iso" => "\uF012",
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => "\uE756",
            ".dll" or ".sys" => "\uE74C",
            ".txt" or ".md" or ".log" or ".json" or ".xml" or ".csv" => "\uE8A5",
            ".pdf" => "\uEA90",
            ".doc" or ".docx" or ".rtf" => "\uE8A5",
            ".xls" or ".xlsx" => "\uE8A5",
            ".ppt" or ".pptx" => "\uE8A5",
            ".lnk" => "\uE71B",
            _ => "\uE8A5",
        };
    }

    public static bool IsImage(in FileEntryCore entry) =>
        entry.Kind != EntryKind.Directory && IsImageExtension(Path.GetExtension(entry.Name));

    private static bool IsImageExtension(string extension)
    {
        return extension.ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" or ".ico" or ".heic";
    }

    private static string PadUnit(string pattern, char unit)
    {
        var quad = new string(unit, 4);
        var triple = new string(unit, 3);
        var pair = new string(unit, 2);
        const char mark = '\uE000';
        var protectedQuad = pattern.Replace(quad, new string(mark, 1) + "4", StringComparison.Ordinal);
        var protectedTriple = protectedQuad.Replace(triple, new string(mark, 1) + "3", StringComparison.Ordinal);
        var protectedPair = protectedTriple.Replace(pair, new string(mark, 1) + "2", StringComparison.Ordinal);
        var padded = protectedPair.Replace(unit.ToString(), pair, StringComparison.Ordinal);
        return padded
            .Replace(new string(mark, 1) + "2", pair, StringComparison.Ordinal)
            .Replace(new string(mark, 1) + "3", triple, StringComparison.Ordinal)
            .Replace(new string(mark, 1) + "4", quad, StringComparison.Ordinal);
    }
}
