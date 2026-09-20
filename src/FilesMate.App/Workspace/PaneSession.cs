using FilesMate.App.Navigation;
using FilesMate.Core.Navigation;

namespace FilesMate.App.Workspace;

/// <summary>
/// Owns the state of one workspace pane. The view model is optional so the
/// workspace controller can be tested without constructing WinUI controls.
/// </summary>
public sealed class PaneSession : IAsyncDisposable
{
    private bool _disposed;

    public PaneSession(PaneId id, PaneViewModel? viewModel = null)
    {
        Id = id;
        ViewModel = viewModel;
    }

    public PaneSession(PaneViewModel viewModel)
        : this(viewModel.Navigation.PaneId, viewModel)
    {
    }

    public PaneId Id { get; }

    public PaneViewModel? ViewModel { get; }

    public bool IsDisposed => _disposed;

    public string? CurrentPath => ViewModel?.AddressText;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ViewModel is not null)
        {
            await ViewModel.DisposeAsync().ConfigureAwait(false);
        }
    }
}
