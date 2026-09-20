namespace FilesMate.Core.Operations;

public enum FileOperationKind
{
    Copy = 0,
    Move = 1,
    Recycle = 2,
    PermanentDelete = 3,
    Rename = 4,
    CreateDirectory = 5,
}

/// <summary>
/// Immutable operation request. UI state is not the source of truth for in-progress work.
/// Thread-safety: immutable after <see cref="Create"/>. Ownership: the operation host owns execution; the UI holds the id.
/// Cancellation: supplied out of band via the operation id.
/// Errors: results are reported separately; this type does not collapse partial success into a boolean.
/// Staleness: source paths may disappear before the host starts.
/// </summary>
public sealed record FileOperationRequest
{
    private FileOperationRequest(
        Guid operationId,
        FileOperationKind kind,
        IReadOnlyList<string> sources,
        string? destination)
    {
        OperationId = operationId;
        Kind = kind;
        Sources = sources;
        Destination = destination;
    }

    public Guid OperationId { get; }

    public FileOperationKind Kind { get; }

    public IReadOnlyList<string> Sources { get; }

    public string? Destination { get; }

    public static FileOperationRequest Create(
        Guid operationId,
        FileOperationKind kind,
        IReadOnlyList<string> sources,
        string? destination)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("Operation id is required.", nameof(operationId));
        }

        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one source path is required.", nameof(sources));
        }

        var snapshot = new string[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("Source paths must be non-empty.", nameof(sources));
            }

            snapshot[i] = source;
        }

        return new FileOperationRequest(operationId, kind, snapshot, NormalizeDestination(destination));
    }

    public static string? NormalizeDestination(string? destination)
    {
        if (destination is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("Destination path is empty.", nameof(destination));
        }

        var trimmed = destination.TrimEnd('\\', '/');
        if (trimmed.Length == 2 && trimmed[1] == ':')
        {
            return trimmed + '\\';
        }

        return trimmed;
    }
}
