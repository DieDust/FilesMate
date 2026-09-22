namespace FilesMate.Core.Operations;

public interface IFileUndoCleanupScheduler
{
    public void Schedule(Action cleanup);
    public void Drain();
}
