using FilesMate.Core.Navigation;
using FilesMate.Core.Scheduling;

namespace FilesMate.App.Navigation;

/// <summary>
/// Pane history and generation. Does not enumerate the filesystem or own a session.
/// Thread-safety: not thread-safe; call from the UI thread.
/// </summary>
public sealed class NavigationController
{
    private readonly GenerationGate _gate;
    private readonly IPathService _paths;
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();

    public NavigationController(IPathService paths, PaneId paneId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        PaneId = paneId;
        _gate = new GenerationGate(paneId);
    }

    public PaneId PaneId { get; }

    public long CurrentGeneration => _gate.CurrentGeneration;

    public string? CurrentPath { get; private set; }

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    public bool CanGoUp => CurrentPath is not null && _paths.GetParent(CurrentPath) is not null;

    public bool Allows(long generation) => _gate.Allows(PaneId, generation);

    public NavigationHistoryState CaptureHistory() => new(_back.ToArray(), _forward.ToArray());

    public void RestoreHistory(NavigationHistoryState state)
    {
        _back.Clear();
        _forward.Clear();
        foreach (var path in state.Back.Reverse()) _back.Push(path);
        foreach (var path in state.Forward.Reverse()) _forward.Push(path);
    }

    public NavigationIntent Open(string normalizedPath, bool recordHistory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);
        if (recordHistory && CurrentPath is not null && !_paths.IsSamePath(CurrentPath, normalizedPath))
        {
            _back.Push(CurrentPath);
            _forward.Clear();
        }

        CurrentPath = normalizedPath;
        var generation = _gate.Begin();
        return new NavigationIntent(PaneId, generation, normalizedPath);
    }

    public NavigationIntent? Back()
    {
        if (_back.Count == 0)
        {
            return null;
        }

        var target = _back.Pop();
        if (CurrentPath is not null)
        {
            _forward.Push(CurrentPath);
        }

        return Open(target, recordHistory: false);
    }

    public NavigationIntent? Forward()
    {
        if (_forward.Count == 0)
        {
            return null;
        }

        var target = _forward.Pop();
        if (CurrentPath is not null)
        {
            _back.Push(CurrentPath);
        }

        return Open(target, recordHistory: false);
    }

    public NavigationIntent? Up()
    {
        if (CurrentPath is null)
        {
            return null;
        }

        var parent = _paths.GetParent(CurrentPath);
        return parent is null ? null : Open(parent, recordHistory: true);
    }

    public NavigationIntent? Refresh() =>
        CurrentPath is null ? null : Open(CurrentPath, recordHistory: false);

    /// <summary>
    /// Leaves the current folder on the back stack so an invalid address can still be recovered.
    /// </summary>
    public long ParkCurrentForRecovery()
    {
        if (CurrentPath is not null)
        {
            _back.Push(CurrentPath);
            _forward.Clear();
            CurrentPath = null;
        }

        return _gate.Begin();
    }
}

public readonly record struct NavigationIntent(PaneId PaneId, long Generation, string Path);
public sealed record NavigationHistoryState(string[] Back, string[] Forward);
