namespace FilesMate.App.Navigation;

public enum LazyTabLoadState
{
    Pending,
    Loading,
    Loaded,
    Cancelled,
    Failed,
}

public sealed class LazyTabLoadSession
{
    public LazyTabLoadState State { get; private set; } = LazyTabLoadState.Pending;

    public Exception? Error { get; private set; }

    public bool TryBegin()
    {
        if (State != LazyTabLoadState.Pending)
        {
            return false;
        }

        State = LazyTabLoadState.Loading;
        return true;
    }

    public bool TryComplete()
    {
        if (State != LazyTabLoadState.Loading)
        {
            return false;
        }

        State = LazyTabLoadState.Loaded;
        return true;
    }

    public void Cancel()
    {
        if (State is LazyTabLoadState.Loaded or LazyTabLoadState.Failed)
        {
            return;
        }

        State = LazyTabLoadState.Cancelled;
    }

    public bool TryFail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (State != LazyTabLoadState.Loading)
        {
            return false;
        }

        Error = error;
        State = LazyTabLoadState.Failed;
        return true;
    }
}
