namespace FilesMate.Core.Operations;

public sealed class IrreversibleDeletionException(int count) : IOException("Some items were permanently deleted and cannot be restored from the Recycle Bin.")
{
    public int Count { get; } = count;
}
