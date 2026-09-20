using Microsoft.UI.Dispatching;

using FilesMate.App.Navigation;

namespace FilesMate.App.Threading;

public sealed class DispatcherQueueUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (queue.HasThreadAccess)
        {
            action();
            return;
        }

        _ = queue.TryEnqueue(() => action());
    }
}
