namespace FilesMate.Core.Operations;

/// <summary>Moves only the completed portion to the opposite history stack.</summary>
internal sealed class PartialFileUndoException(FileUndoRecord remaining, FileUndoRecord completed, Exception cause)
    : IOException(cause.Message, cause)
{
    internal FileUndoRecord Remaining { get; } = remaining;
    internal FileUndoRecord Completed { get; } = completed;
}
