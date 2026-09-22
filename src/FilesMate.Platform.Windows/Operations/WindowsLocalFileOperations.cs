using System.Runtime.InteropServices;

using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Interop;
using FilesMate.Platform.Windows.Locks;

using Microsoft.Win32.SafeHandles;

namespace FilesMate.Platform.Windows.Operations;

public sealed class WindowsLocalFileOperations : ILocalFileOperations
{
    public bool RequiresUndoValidation => true;

    public void CreateDirectory(string path, bool failIfExists = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (failIfExists)
        {
            if (!Kernel32.CreateDirectoryW(path, 0))
                throw new IOException(new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()).Message);
            return;
        }
        Directory.CreateDirectory(path);
    }

    public void CreateEmptyFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        // The name may have been taken since the UI checked availability.
        // Creating a new item must never truncate an existing user's file.
        using var _ = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    public IReadOnlyList<string> Copy(IReadOnlyList<string> sources, string destinationDirectory)
    {
        EnsureSources(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        foreach (var source in sources) EnsureCopyDestination(source, destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var destinations = new string[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            destinations[i] = CopyOne(sources[i], destinationDirectory);
        }

        return destinations;
    }

    public IReadOnlyList<string> Move(IReadOnlyList<string> sources, string destinationDirectory)
    {
        EnsureSources(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var destinations = new string[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            var destination = UniqueDestination(destinationDirectory, sources[i]);
            if (Directory.Exists(sources[i]))
            {
                Directory.Move(sources[i], destination);
            }
            else
            {
                File.Move(sources[i], destination);
            }

            destinations[i] = destination;
        }

        return destinations;
    }

    public void Rename(string source, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (Directory.Exists(source))
        {
            Directory.Move(source, destinationPath);
            return;
        }

        File.Move(source, destinationPath);
    }

    public bool TryRenameFileWithoutCopy(string source, string destinationPath) =>
        WindowsFileMove.TryRename(source, destinationPath);

    public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null)
    {
        EnsureSources(paths);
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            WindowsRecycleOperation.Run(paths, completed);
        else
            ShellOperationWorker.RunAsync(() => WindowsRecycleOperation.Run(paths, completed)).GetAwaiter().GetResult();
    }

    public void RestoreRecycled(IReadOnlyList<string> originalPaths) =>
        throw new UndoStateChangedException(); // Restoring by name cannot identify the deleted version.

    public void RestoreRecycledItems(IReadOnlyList<RecycleItemResult> items, Action<string>? completed = null) =>
        RecycleBinRestore.RestoreItems(items, completed);

    public void PermanentDelete(IReadOnlyList<string> paths)
    {
        EnsureSources(paths);
        RelinquishWorkingDirectory(paths);
        if (TryShellDelete(paths))
        {
            return;
        }

        IOException? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                foreach (var path in paths)
                {
                    DeleteManaged(path);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex as IOException ?? new IOException(ex.Message, ex);
                if (attempt == 3)
                {
                    break;
                }

                Thread.Sleep(80 * (attempt + 1));
                RelinquishWorkingDirectory(paths);
            }
        }

        throw last ?? new IOException("Could not delete the selected items.");
    }

    public void ShowProperties(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Shell32.SHObjectProperties(0, Shell32.ShopFilePath, path, null))
        {
            throw new IOException($"Could not open properties for '{path}'.");
        }
    }

    private static string CopyOne(string source, string destinationDirectory)
    {
        var destination = UniqueDestination(destinationDirectory, source);
        if (Directory.Exists(source))
        {
            CopyDirectory(source, destination);
            return destination;
        }

        File.Copy(source, destination);
        return destination;
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (new DirectoryInfo(source).LinkTarget is not null)
            throw new IOException("复制包含目录链接，请使用系统资源管理器复制该链接，避免递归进入链接目标。");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static string UniqueDestination(string destinationDirectory, string source)
    {
        var name = Path.GetFileName(source.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name))
        {
            throw new IOException($"Could not copy '{source}'.");
        }

        return UniquePath.CombineAvailable(destinationDirectory, name, Path.Exists);
    }

    internal static void DeleteManaged(string path)
    {
        var full = Path.GetFullPath(path);
        if (DeleteLinkOnly(full)) return;
        if (Directory.Exists(full))
        {
            DeleteDirectoryTree(full);
            return;
        }

        if (!File.Exists(full))
        {
            return;
        }

        var file = Extended(full);
        ClearReadOnly(file);
        File.Delete(file);
    }

    private static void DeleteDirectoryTree(string path)
    {
        var root = Extended(path);
        if (DeleteLinkOnly(root)) return;
        ClearReadOnly(root);
        string[] children;
        try
        {
            children = Directory.GetFileSystemEntries(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteDirectoryRoot(path, root);
            return;
        }

        foreach (var child in children)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                DeleteLinkOnly(child);
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                DeleteDirectoryTree(child);
            }
            else
            {
                ClearReadOnly(child);
                File.Delete(child);
            }
        }

        DeleteDirectoryRoot(path, root);
    }

    private static void DeleteDirectoryRoot(string path, string root)
    {
        try
        {
            Directory.Delete(root);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (!TryPosixDelete(root))
        {
            Directory.Delete(root);
        }
    }

    private static string Extended(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return full;
        }

        if (full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + full[2..];
        }

        return @"\\?\" + full;
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private static bool DeleteLinkOnly(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if ((attributes & FileAttributes.ReparsePoint) == 0) return false;
        if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path);
        else File.Delete(path);
        return true;
    }

    internal static void EnsureCopyDestination(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        var sourcePath = ResolveDirectory(source);
        var destinationPath = ResolveDirectory(destination);
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)
            || destinationPath.StartsWith(Path.TrimEndingDirectorySeparator(sourcePath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("不能将文件夹复制到自身或其子文件夹中。");
    }

    private static string ResolveDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var resolved = root;
        foreach (var part in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, part);
            if (Directory.Exists(resolved)) resolved = new DirectoryInfo(resolved).ResolveLinkTarget(true)?.FullName ?? resolved;
        }
        return Path.TrimEndingDirectorySeparator(resolved);
    }

    private static void RelinquishWorkingDirectory(IReadOnlyList<string> paths)
    {
        string cwd;
        try
        {
            cwd = Directory.GetCurrentDirectory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (!paths.Any(path => Occupies(cwd, path)))
        {
            return;
        }

        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool Occupies(string current, string target) =>
        !string.IsNullOrWhiteSpace(current)
        && !string.IsNullOrWhiteSpace(target)
        && Directory.Exists(target)
        && FileLockPath.Matches(current, target, directory: true);

    private static bool TryShellDelete(IReadOnlyList<string> paths)
    {
        var remaining = paths.Where(Path.Exists).ToArray();
        if (remaining.Length == 0)
        {
            return true;
        }

        var from = Pack(remaining);
        try
        {
            var op = new Shell32.SHFILEOPSTRUCT
            {
                hwnd = 0,
                wFunc = Shell32.FoDelete,
                pFrom = from,
                pTo = 0,
                fFlags = (ushort)(Shell32.FofNoConfirmation | Shell32.FofNoErrorUi | Shell32.FofWantNukeWarning),
            };
            var result = Shell32.SHFileOperation(ref op);
            // A user cancellation is handled: never retry it with a managed delete.
            if (op.fAnyOperationsAborted) return true;
            return result == 0 && remaining.All(path => !Path.Exists(path));
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    private static void EnsureSources(IReadOnlyList<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one source path is required.", nameof(sources));
        }
        foreach (var source in sources)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            if (source.Contains('\0') || !Path.IsPathFullyQualified(source))
                throw new ArgumentException("An absolute path without embedded nulls is required.", nameof(sources));
        }
    }

    private static nint Pack(IReadOnlyList<string> paths)
    {
        var packed = string.Join('\0', paths) + "\0\0";
        return Marshal.StringToHGlobalUni(packed);
    }

    private static bool TryPosixDelete(string path)
    {
        using var handle = CreateFileW(
            path,
            DeleteAccess,
            FileShare.ReadWrite | FileShare.Delete,
            0,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            0);
        if (handle.IsInvalid)
        {
            return false;
        }

        var info = new FileDispositionInfoEx
        {
            Flags = DispositionDelete | DispositionPosix | DispositionIgnoreReadonly,
        };
        return SetFileInformationByHandle(
            handle,
            FileDispositionInfoExClass,
            ref info,
            (uint)Marshal.SizeOf<FileDispositionInfoEx>());
    }

    private const uint DeleteAccess = 0x00010000;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int FileDispositionInfoExClass = 21;
    private const uint DispositionDelete = 0x1;
    private const uint DispositionPosix = 0x2;
    private const uint DispositionIgnoreReadonly = 0x10;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle handle,
        int fileInformationClass,
        ref FileDispositionInfoEx info,
        uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfoEx
    {
        public uint Flags;
    }
}
