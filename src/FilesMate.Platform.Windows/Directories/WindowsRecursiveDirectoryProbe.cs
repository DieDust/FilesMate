using System.Runtime.InteropServices;
using System.Security;

using FilesMate.Core.Recursive;

using Microsoft.Win32.SafeHandles;

namespace FilesMate.Platform.Windows.Directories;

/// <summary>
/// Read-only recursive preflight. Directory attributes retain the shared root-first safety check;
/// legacy operation compatibility additionally requires observed case-insensitive directories
/// all the way from the volume root. Call off the UI thread. Safety checks are always live;
/// per-scan identity observations only classify watcher hints. No filesystem state is changed.
/// </summary>
/// <remarks>
/// This is advisory: shared handles and path-based ancestor checks cannot prevent concurrent
/// replacement or a later case-sensitivity change. It is not an atomic security boundary.
/// Unknown compatibility restricts operations, not read-only enumeration.
/// </remarks>
public sealed class WindowsRecursiveDirectoryProbe : IRecursiveDirectoryProbe, IRecursiveOperationCompatibilityProbe, IRecursiveObservationMemory
{
    private readonly RecursiveDirectoryProbe _attributes = new();
    // Per-scan observations, captured before the read handle closes. Windows can
    // notify Changed for a directory that was only queried; those notifications
    // must not cancel the scan that performed the query.
    private readonly BoundedDirectoryObservationCache<DirectoryFingerprint> _observedDirectories = new();
    public long ObservationByteLimit => _observedDirectories.MaximumBytes;
    public long EstimatedObservationBytes => _observedDirectories.Statistics.EstimatedBytes;

    // Win32 values: CreateFileW and File Access Rights Constants on Microsoft Learn.
    // https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew
    // https://learn.microsoft.com/windows/win32/fileio/file-access-rights-constants
    private const uint FileReadAttributes = 0x00000080;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagOpenNoRecall = 0x00100000;

    // FILE_INFO_BY_HANDLE_CLASS, minwinbase.h: FileAttributeTagInfo = 9,
    // FileCaseSensitiveInfo = 23 (not FILE_INFORMATION_CLASS's similarly named value).
    // https://learn.microsoft.com/windows/win32/api/minwinbase/ne-minwinbase-file_info_by_handle_class
    private enum FileInfoByHandleClass { FileAttributeTagInfo = 9, FileCaseSensitiveInfo = 23 }

    // https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_case_sensitive_information
    private const uint FileCsFlagCaseSensitiveDir = 0x00000001;

    public FileAttributes GetAttributes(string path) => GetAttributes(path, CancellationToken.None);

    public FileAttributes GetAttributes(string path, CancellationToken cancellationToken)
    {
        // Core reads attributes before it asks for operation compatibility. That first
        // read can itself emit Changed; keep classification behind the corresponding
        // identity observation, rather than publishing it only in the later probe.
        lock (_observedDirectories)
        {
            var attributes = _attributes.GetAttributes(path, cancellationToken);
            if (IsOrdinaryDirectory(attributes))
            {
                try { ObserveCaseInsensitiveDirectory(path, cancellationToken); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                    or NotSupportedException or SecurityException or DllNotFoundException or EntryPointNotFoundException)
                { cancellationToken.ThrowIfCancellationRequested(); }
            }
            return attributes;
        }
    }

    public bool IsLegacyOperationCompatible(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            // Only ordinary local drive-qualified paths are verified here. UNC, device and
            // extended namespaces remain unknown. Reject remote drive mappings before file I/O.
            var localPath = path.Replace('/', '\\');
            if (localPath.Length < 3 || !char.IsAsciiLetter(localPath[0])
                || localPath[1] != ':' || localPath[2] != '\\') return false;
            var root = localPath[..3];
            var driveType = new DriveInfo(root).DriveType;
            cancellationToken.ThrowIfCancellationRequested();
            if (driveType is not (DriveType.Fixed or DriveType.Removable)) return false;
            var directDevice = HasDirectDeviceMapping(root);
            cancellationToken.ThrowIfCancellationRequested();
            if (!directDevice) return false;

            // Reuse the Core syntax/reparse/offline guard before opening any path component.
            var attributes = _attributes.GetAttributes(localPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsOrdinaryDirectory(attributes)) return false;

            var current = root;
            if (!IsCaseInsensitiveDirectory(current, cancellationToken)) return false;
            foreach (var segment in localPath[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                current = Path.Combine(current, segment);
                if (!IsCaseInsensitiveDirectory(current, cancellationToken)) return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or SecurityException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Unsupported filesystems/APIs and inaccessible/replaced paths are never evidence
            // of compatibility. Cancellation is intentionally not swallowed.
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static bool HasDirectDeviceMapping(string root)
    {
        // SUBST and other aliases can hide ancestors (including reparse/network targets).
        // Inspect the current DOS mapping only; never follow it to discover a target.
        // https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-querydosdevicew
        var buffer = new char[1024];
        var length = QueryDosDeviceW(root[..2], buffer, (uint)buffer.Length);
        if (length == 0 || length > buffer.Length) return false;
        var end = Array.IndexOf(buffer, '\0', 0, (int)length);
        if (end < 0) return false;
        var mapping = buffer.AsSpan(0, end);
        const string devicePrefix = @"\Device\";
        return mapping.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)
            && mapping.Length > devicePrefix.Length
            && mapping[devicePrefix.Length..].IndexOfAny('\\', '/') < 0;
    }

    private static bool IsOrdinaryDirectory(FileAttributes attributes) =>
        (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Offline))
            == FileAttributes.Directory;

    private bool IsCaseInsensitiveDirectory(string path, CancellationToken cancellationToken)
    {
        // A notification from this native read may arrive before its handle closes.
        // Publish the observation before a watcher can classify that notification.
        lock (_observedDirectories) return ObserveCaseInsensitiveDirectory(path, cancellationToken);
    }

    private bool ObserveCaseInsensitiveDirectory(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var handle = CreateFileW(path, FileReadAttributes, FileShare.ReadWrite | FileShare.Delete,
            0, OpenExisting, FileFlagBackupSemantics | FileFlagOpenReparsePoint | FileFlagOpenNoRecall, 0);
        cancellationToken.ThrowIfCancellationRequested();
        if (handle.IsInvalid) return false;

        // Check this exact handle before the next ancestor is opened: OPEN_REPARSE_POINT
        // protects the final component only. Stop at a reparse/offline directory, never its child.
        var attributesRead = GetAttributeInformation(handle, FileInfoByHandleClass.FileAttributeTagInfo,
            out var attributes, (uint)Marshal.SizeOf<FileAttributeTagInfo>());
        cancellationToken.ThrowIfCancellationRequested();
        if (!attributesRead || !IsOrdinaryDirectory((FileAttributes)attributes.FileAttributes)
            || attributes.ReparseTag != 0) return false;
        var caseSensitivityRead = GetCaseSensitiveInformation(handle, FileInfoByHandleClass.FileCaseSensitiveInfo,
            out var information, (uint)Marshal.SizeOf<FileCaseSensitiveInfo>());
        cancellationToken.ThrowIfCancellationRequested();
        if (!caseSensitivityRead) return false;
        if ((information.Flags & FileCsFlagCaseSensitiveDir) != 0) return false;
        if (information.Flags != 0) return false; // Unknown future flags are not assumed compatible.
        if (TryFingerprint(handle, attributes, information, out var fingerprint))
            _observedDirectories.TryAdd(path, fingerprint);
        return true;
    }

    /// <summary>
    /// Classifies only an already-observed ordinary directory's Changed hint.
    /// File changes, unknown paths, replaced identities, altered attributes/times,
    /// changed case policy and failed queries remain invalidating notifications.
    /// Call on the watcher worker, never the UI thread.
    /// </summary>
    public bool IsUnchangedObservedDirectory(string path)
    {
        lock (_observedDirectories) return MatchesObservedDirectory(path);
    }

    private bool MatchesObservedDirectory(string path)
    {
        if (!_observedDirectories.TryGetValue(path, out var expected)) return false;
        try
        {
            using var handle = CreateFileW(path, FileReadAttributes, FileShare.ReadWrite | FileShare.Delete,
                0, OpenExisting, FileFlagBackupSemantics | FileFlagOpenReparsePoint | FileFlagOpenNoRecall, 0);
            if (handle.IsInvalid
                || !GetAttributeInformation(handle, FileInfoByHandleClass.FileAttributeTagInfo, out var attributes,
                    (uint)Marshal.SizeOf<FileAttributeTagInfo>())
                || !IsOrdinaryDirectory((FileAttributes)attributes.FileAttributes) || attributes.ReparseTag != 0
                || !GetCaseSensitiveInformation(handle, FileInfoByHandleClass.FileCaseSensitiveInfo, out var information,
                    (uint)Marshal.SizeOf<FileCaseSensitiveInfo>()) || information.Flags != 0)
                return false;
            return TryFingerprint(handle, attributes, information, out var actual) && actual == expected;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or SecurityException or DllNotFoundException or EntryPointNotFoundException)
        { return false; }
    }

    private static bool TryFingerprint(SafeFileHandle handle, FileAttributeTagInfo attributes,
        FileCaseSensitiveInfo sensitivity, out DirectoryFingerprint fingerprint)
    {
        fingerprint = default;
        if (!GetFileInformationByHandle(handle, out var information)
            || information.Attributes != attributes.FileAttributes) return false;
        fingerprint = new(information.VolumeSerial, information.FileIndexHigh, information.FileIndexLow,
            information.CreationHigh, information.CreationLow, information.WriteHigh, information.WriteLow,
            information.Attributes, sensitivity.Flags, information.SizeHigh, information.SizeLow, information.Links);
        return true;
    }

    private readonly record struct DirectoryFingerprint(uint Volume, uint IdHigh, uint IdLow,
        uint CreationHigh, uint CreationLow, uint WriteHigh, uint WriteLow, uint Attributes, uint CaseFlags,
        uint SizeHigh, uint SizeLow, uint Links);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, FileIndexHigh, FileIndexLow;
    }

    // SDK WinBase.h defines FILE_CASE_SENSITIVE_INFO as one ULONG (4 bytes).
    // https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/WinBase.h
    [StructLayout(LayoutKind.Sequential)]
    private struct FileCaseSensitiveInfo { public uint Flags; }

    // https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-file_attribute_tag_info
    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo { public uint FileAttributes; public uint ReparseTag; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint desiredAccess, FileShare shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string deviceName, [Out] char[] targetPath, uint maximumLength);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAttributeInformation(SafeFileHandle handle, FileInfoByHandleClass informationClass,
        out FileAttributeTagInfo information, uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCaseSensitiveInformation(SafeFileHandle handle, FileInfoByHandleClass informationClass,
        out FileCaseSensitiveInfo information, uint bufferSize);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleInformation information);
}
