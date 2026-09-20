namespace FilesMate.Core.Directories;

/// <summary>
/// Typed directory-read failure. <see cref="IsTerminal"/> means enumeration stops; otherwise a partial batch may still
/// contain entries. Codes may refer to stale filesystem state (the path may have changed after the error was observed).
/// </summary>
public sealed record DirectoryReadError
{
    public DirectoryReadError(DirectoryReadErrorKind kind, int nativeErrorCode, string message, bool isTerminal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Kind = kind;
        NativeErrorCode = nativeErrorCode;
        Message = message;
        IsTerminal = isTerminal;
    }

    public DirectoryReadErrorKind Kind { get; }

    public int NativeErrorCode { get; }

    public string Message { get; }

    public bool IsTerminal { get; }
}

public enum DirectoryReadErrorKind
{
    Unknown = 0,
    NotFound = 1,
    AccessDenied = 2,
    PathInvalid = 3,
    Offline = 4,
    SharingViolation = 5,
    Cancelled = 6,
    UnsupportedLocation = 7,
    PartialEnumeration = 8,
}
