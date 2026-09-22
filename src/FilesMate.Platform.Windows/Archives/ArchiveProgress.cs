namespace FilesMate.Platform.Windows.Archives;

public sealed record ArchiveProgress(string CurrentPath, long ProcessedBytes, long TotalBytes, int CompletedEntries, int TotalEntries);

public enum ArchiveErrorCode
{
    InvalidPath, UnsafeLink, DestinationNotEmpty, DestinationExists, InvalidArchive,
    UnsupportedEntry, TooManyEntries, ArchiveTooLarge, MetadataTooLarge, InsufficientSpace, SourceChanged, SecurityMetadataUnavailable,
}

public sealed class ArchiveOperationException(ArchiveErrorCode errorCode, string message) : IOException(message)
{
    public ArchiveErrorCode ErrorCode { get; } = errorCode;
}
