namespace FilesMate.App.Navigation;

/// <summary>
/// Runs posted work immediately. Used by unit tests that do not host a DispatcherQueue.
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public bool HasThreadAccess => true;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}
