using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FilesMate.Core.Operations;

/// <summary>A bounded snapshot of the objects produced by an operation, never file contents.</summary>
internal sealed class FileUndoState
{
    private const int MaximumEntries = 4096;
    private readonly Dictionary<string, Version> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _roots;
    private readonly bool _complete = true;
    private readonly bool _recursive;

    private FileUndoState(IEnumerable<string> roots, bool recursive = true)
    {
        _recursive = recursive;
        _roots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        try
        {
            foreach (var root in _roots) Read(root, 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { _complete = false; }
    }

    public static FileUndoState Capture(IEnumerable<string> paths) => new(paths);
    internal static FileUndoState CaptureRootsOnly(IEnumerable<string> paths) => new(paths, recursive: false);

    private FileUndoState(FileUndoState original, IEnumerable<string> roots)
    {
        var selected = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        _roots = original._roots.Where(selected.Contains).ToArray();
        _complete = original._complete;
        _recursive = original._recursive;
        foreach (var entry in original._entries)
            if (_roots.Any(root => string.Equals(root, entry.Key, StringComparison.OrdinalIgnoreCase)
                || entry.Key.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                _entries.Add(entry.Key, entry.Value);
    }

    internal FileUndoState SelectRoots(IEnumerable<string> roots) => new(this, roots);

    private FileUndoState(FileUndoState original, FileUndoState replacement)
    {
        _roots = original._roots;
        _recursive = original._recursive;
        _complete = original._complete && replacement._complete;
        foreach (var entry in original._entries) _entries.Add(entry.Key, entry.Value);
        foreach (var root in replacement._roots.Where(root => original._roots.Contains(root, StringComparer.OrdinalIgnoreCase)))
        {
            foreach (var key in _entries.Keys.Where(key => string.Equals(key, root, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToArray())
                _entries.Remove(key);
            foreach (var entry in replacement._entries)
                if (string.Equals(entry.Key, root, StringComparison.OrdinalIgnoreCase)
                    || entry.Key.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    _entries.Add(entry.Key, entry.Value);
        }
    }
    internal FileUndoState WithReplacement(FileUndoState replacement) => new(this, replacement);

    public void Validate()
    {
        var current = new FileUndoState(_roots, _recursive);
        if (!_complete || !current._complete || _entries.Count != current._entries.Count
            || _entries.Any(pair => !current._entries.TryGetValue(pair.Key, out var value) || pair.Value != value))
            throw new UndoStateChangedException();
    }

    private void Read(string path, int depth)
    {
        if (_entries.Count >= MaximumEntries || depth > 128) throw new IOException("Undo snapshot limit.");
        var attributes = File.GetAttributes(path);
        // Following a reparse point could inspect or later remove unrelated data.
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked item.");
        var directory = (attributes & FileAttributes.Directory) != 0;
        FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path);
        var identity = ReadIdentity(path);
        _entries[path] = new(attributes, info.CreationTimeUtc, info.LastWriteTimeUtc,
            directory ? 0 : ((FileInfo)info).Length, identity.Volume, identity.File);
        if (directory && _recursive)
            foreach (var child in Directory.EnumerateFileSystemEntries(path)) Read(child, depth + 1);
    }

    private static (uint Volume, ulong File) ReadIdentity(string path)
    {
        if (!OperatingSystem.IsWindows()) return default;
        using var handle = CreateFileW(path, 0, FileShare.ReadWrite | FileShare.Delete, 0, 3, 0x02000000, 0);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
            throw new IOException("Could not verify file identity.");
        var fileId = ((ulong)info.IndexHigh << 32) | info.IndexLow;
        if (fileId == 0) throw new IOException("The filesystem did not supply a file identity.");
        return (info.Volume, fileId);
    }

    private sealed record Version(FileAttributes Attributes, DateTime Created, DateTime Written, long Length, uint Volume, ulong File);
    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo info);
}
