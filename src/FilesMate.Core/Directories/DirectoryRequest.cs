using FilesMate.Core.Navigation;

namespace FilesMate.Core.Directories;

/// <summary>
/// Identifies one enumeration of one pane. The UI must reject batches whose pane or generation do not match.
/// Thread-safety: immutable. Cancellation: supplied separately to the enumerator; this value does not own a token.
/// Staleness: <see cref="Path"/> is the requested path, not a guarantee that it still exists.
/// </summary>
public readonly record struct DirectoryRequest
{
    public DirectoryRequest(PaneId paneId, long generation, string path, DirectoryReadOptions options)
    {
        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Directory path is empty.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(options);
        PaneId = paneId;
        Generation = generation;
        Path = path;
        Options = options;
    }

    public PaneId PaneId { get; }

    public long Generation { get; }

    public string Path { get; }

    public DirectoryReadOptions Options { get; }
}
