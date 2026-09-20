using FilesMate.Core.Navigation;

namespace FilesMate.App.Workspace;

/// <summary>
/// Routes workspace-level state to one or two independent pane sessions.
/// It deliberately contains no XAML and never touches directory enumeration.
/// </summary>
public sealed class WorkspaceController : IAsyncDisposable
{
    private readonly Func<PaneSession> _sessionFactory;
    private bool _disposed;
    private PaneSession? _right;

    public WorkspaceController(Func<PaneSession> sessionFactory)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        Left = _sessionFactory();
        ActivePane = Left;
        State = new WorkspaceState();
    }

    public event EventHandler? StateChanged;

    public PaneSession Left { get; }

    public PaneSession? Right => _right;

    public PaneSession ActivePane { get; private set; }

    public WorkspaceState State { get; private set; }

    public bool IsDualPane => State.Layout != WorkspaceLayoutKind.Single && _right is not null;

    public PaneSession EnsureSecondary()
    {
        _right ??= _sessionFactory();
        return _right;
    }

    public void SetLayout(WorkspaceLayoutKind layout)
    {
        if (!Enum.IsDefined(layout))
        {
            throw new ArgumentOutOfRangeException(nameof(layout));
        }

        if (layout != WorkspaceLayoutKind.Single)
        {
            EnsureSecondary();
        }

        State = State with { Layout = layout };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetActivePane(PaneSession pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (!ReferenceEquals(pane, Left) && !ReferenceEquals(pane, _right))
        {
            throw new ArgumentException("The pane does not belong to this workspace.", nameof(pane));
        }

        ActivePane = pane;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSplitRatio(double ratio)
    {
        var normalized = double.IsFinite(ratio) ? Math.Clamp(ratio, 0.2, 0.8) : 0.5;
        if (Math.Abs(State.SplitRatio - normalized) < 0.0001)
        {
            return;
        }

        State = State with { SplitRatio = normalized };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetSplitRatio() => SetSplitRatio(0.5);

    public void SetPreviewVisible(bool visible)
    {
        if (State.IsPreviewVisible == visible)
        {
            return;
        }

        State = State with { IsPreviewVisible = visible };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public WorkspaceState Snapshot() => State with
    {
        LeftPath = Left.CurrentPath,
        RightPath = Right?.CurrentPath,
    };

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await Left.DisposeAsync().ConfigureAwait(false);
        if (_right is not null)
        {
            await _right.DisposeAsync().ConfigureAwait(false);
        }
    }
}
