namespace FilesMate.Core.Directories;

/// <summary>
/// Options for a single directory enumeration request.
/// Thread-safety: immutable after construction. Cancellation is owned by the caller of
/// <see cref="IDirectoryEnumerator.EnumerateAsync"/>.
/// </summary>
public sealed record DirectoryReadOptions
{
    public static DirectoryReadOptions Default { get; } = new();

    private readonly int _batchSize = 256;
    private readonly TimeSpan _batchDeadline = TimeSpan.FromMilliseconds(12);

    public bool IncludeHidden { get; init; } = true;

    public bool IncludeSystem { get; init; } = true;

    public bool IncludeCreatedTime { get; init; } = true;

    public bool IncludeFolderItemCounts { get; init; }

    public bool IncludeShortcutTargets { get; init; }

    public bool IncludeHardLinkCounts { get; init; }

    public int BatchSize
    {
        get => _batchSize;
        init
        {
            if (value is < 1 or > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(BatchSize), value, "BatchSize must be between 1 and 4096.");
            }

            _batchSize = value;
        }
    }

    public TimeSpan BatchDeadline
    {
        get => _batchDeadline;
        init
        {
            if (value <= TimeSpan.Zero || value > TimeSpan.FromSeconds(1))
            {
                throw new ArgumentOutOfRangeException(nameof(BatchDeadline), value, "BatchDeadline must be > 0 and <= 1s.");
            }

            _batchDeadline = value;
        }
    }
}
