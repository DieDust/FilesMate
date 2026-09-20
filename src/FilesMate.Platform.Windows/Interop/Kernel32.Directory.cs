using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FilesMate.Platform.Windows.Interop;

internal enum FINDEX_INFO_LEVELS
{
    FindExInfoStandard = 0,
    FindExInfoBasic = 1,
}

internal enum FINDEX_SEARCH_OPS
{
    FindExSearchNameMatch = 0,
}

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateDirectoryW(string path, nint securityAttributes);

    internal const uint FindFirstExCaseSensitive = 1;
    internal const uint FindFirstExLargeFetch = 2;
    internal const uint FindFirstExOnDiskEntriesOnly = 4;

    internal const int ErrorFileNotFound = 2;
    internal const int ErrorPathNotFound = 3;
    internal const int ErrorAccessDenied = 5;
    internal const int ErrorSharingViolation = 32;
    internal const int ErrorBadNetPath = 53;
    internal const int ErrorNetworkBusy = 54;
    internal const int ErrorNetNameDeleted = 64;
    internal const int ErrorInvalidParameter = 87;
    internal const int ErrorInvalidName = 123;
    internal const int ErrorDirectory = 267;
    internal const int ErrorNoMoreFiles = 18;
    internal const int ErrorNotReady = 21;
    internal const int ErrorNetworkUnreachable = 1231;

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFindHandle FindFirstFileExW(
        string lpFileName,
        FINDEX_INFO_LEVELS fInfoLevelId,
        out WIN32_FIND_DATAW lpFindFileData,
        FINDEX_SEARCH_OPS fSearchOp,
        nint lpSearchFilter,
        uint dwAdditionalFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FindNextFileW(SafeFindHandle hFindFile, out WIN32_FIND_DATAW lpFindFileData);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FindClose(nint hFindFile);
}

internal sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeFindHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => Kernel32.FindClose(handle);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FILETIME
{
    public uint dwLowDateTime;
    public uint dwHighDateTime;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct WIN32_FIND_DATAW
{
    public uint dwFileAttributes;
    public FILETIME ftCreationTime;
    public FILETIME ftLastAccessTime;
    public FILETIME ftLastWriteTime;
    public uint nFileSizeHigh;
    public uint nFileSizeLow;
    public uint dwReserved0;
    public uint dwReserved1;
    public fixed char cFileName[260];
    public fixed char cAlternateFileName[14];

    public string GetFileName()
    {
        fixed (char* p = cFileName)
        {
            return new string(p);
        }
    }
}
