namespace FilesMate.App.Navigation;

/// <summary>
/// Marshals pane updates onto the UI thread. Implementations must not run filesystem work.
/// </summary>
public interface IUiDispatcher
{
    public bool HasThreadAccess { get; }

    public void Post(Action action);
}
