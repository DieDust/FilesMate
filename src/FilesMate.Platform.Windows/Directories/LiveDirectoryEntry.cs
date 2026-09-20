using System.IO;

using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Platform.Windows.Directories;

public static class LiveDirectoryEntry
{
    public static bool TryRead(
        string directory,
        string name,
        DirectoryReadOptions options,
        out FileEntryCore entry)
    {
        entry = default;
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name)
            || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        var full = Path.Combine(directory, name);
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(full);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!options.IncludeHidden && attributes.HasFlag(FileAttributes.Hidden))
        {
            return false;
        }

        if (!options.IncludeSystem && attributes.HasFlag(FileAttributes.System))
        {
            return false;
        }

        var isDirectory = attributes.HasFlag(FileAttributes.Directory);
        ulong size = 0;
        var modified = 0L;
        var created = 0L;
        var accessed = 0L;
        try
        {
            if (isDirectory)
            {
                var info = new DirectoryInfo(full);
                modified = info.LastWriteTimeUtc.Ticks;
                accessed = info.LastAccessTimeUtc.Ticks;
                created = options.IncludeCreatedTime ? info.CreationTimeUtc.Ticks : 0;
            }
            else
            {
                var info = new FileInfo(full);
                size = unchecked((ulong)Math.Max(0, info.Length));
                modified = info.LastWriteTimeUtc.Ticks;
                accessed = info.LastAccessTimeUtc.Ticks;
                created = options.IncludeCreatedTime ? info.CreationTimeUtc.Ticks : 0;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        entry = new FileEntryCore(
            Id: 0,
            Name: name,
            Size: size,
            ModifiedUtcTicks: modified,
            CreatedUtcTicks: created,
            Attributes: attributes,
            Kind: isDirectory ? EntryKind.Directory : EntryKind.File) { AccessedUtcTicks = accessed };
        return true;
    }
}
