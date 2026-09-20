using FilesMate.Core.Directories;
using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Errors;

public static class Win32ErrorMapper
{
    public static DirectoryReadError Map(int nativeError, bool isTerminal = true)
    {
        var kind = nativeError switch
        {
            Kernel32.ErrorFileNotFound or Kernel32.ErrorPathNotFound => DirectoryReadErrorKind.NotFound,
            Kernel32.ErrorAccessDenied => DirectoryReadErrorKind.AccessDenied,
            Kernel32.ErrorSharingViolation => DirectoryReadErrorKind.SharingViolation,
            Kernel32.ErrorBadNetPath or Kernel32.ErrorNetNameDeleted or Kernel32.ErrorNotReady
                or Kernel32.ErrorNetworkBusy or Kernel32.ErrorNetworkUnreachable => DirectoryReadErrorKind.Offline,
            Kernel32.ErrorInvalidName or Kernel32.ErrorDirectory or Kernel32.ErrorInvalidParameter => DirectoryReadErrorKind.PathInvalid,
            _ => DirectoryReadErrorKind.Unknown,
        };

        return new DirectoryReadError(kind, nativeError, MessageFor(kind, nativeError), isTerminal);
    }

    private static string MessageFor(DirectoryReadErrorKind kind, int nativeError) => kind switch
    {
        DirectoryReadErrorKind.NotFound => "The folder was not found.",
        DirectoryReadErrorKind.AccessDenied => "You do not have permission to open this folder.",
        DirectoryReadErrorKind.Offline => "This location is offline or unreachable.",
        DirectoryReadErrorKind.SharingViolation => "The folder is in use by another program.",
        DirectoryReadErrorKind.PathInvalid => "The path is not a valid folder.",
        _ => $"The folder could not be read (error {nativeError}).",
    };
}
