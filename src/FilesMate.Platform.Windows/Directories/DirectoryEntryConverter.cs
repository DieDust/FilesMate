using System.IO;

using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Directories;

internal static class DirectoryEntryConverter
{
    public static bool TryConvert(
        in WIN32_FIND_DATAW data,
        DirectoryReadOptions options,
        int id,
        out FileEntryCore entry)
    {
        entry = default;
        var name = data.GetFileName();
        if (name is "." or ".." or "")
        {
            return false;
        }

        var attributes = (FileAttributes)data.dwFileAttributes;
        if (!options.IncludeHidden && attributes.HasFlag(FileAttributes.Hidden))
        {
            return false;
        }

        if (!options.IncludeSystem && attributes.HasFlag(FileAttributes.System))
        {
            return false;
        }

        var kind = attributes.HasFlag(FileAttributes.Directory) ? EntryKind.Directory : EntryKind.File;
        var size = kind == EntryKind.Directory
            ? 0UL
            : ((ulong)data.nFileSizeHigh << 32) | data.nFileSizeLow;

        entry = new FileEntryCore(
            Id: id,
            Name: name,
            Size: size,
            ModifiedUtcTicks: ToUtcTicks(data.ftLastWriteTime),
            CreatedUtcTicks: options.IncludeCreatedTime ? ToUtcTicks(data.ftCreationTime) : 0,
            Attributes: attributes,
            Kind: kind) { AccessedUtcTicks = ToUtcTicks(data.ftLastAccessTime) };
        return true;
    }

    private static long ToUtcTicks(FILETIME time)
    {
        var fileTime = ((long)time.dwHighDateTime << 32) | time.dwLowDateTime;
        if (fileTime <= 0)
        {
            return 0;
        }

        try
        {
            return DateTime.FromFileTimeUtc(fileTime).Ticks;
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }
}
