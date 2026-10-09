namespace FilesMate.Core.Lifetime;

public enum WindowActivationPhase { Initializing, Running, Closing, Exiting, Exited }
public enum WindowActivationDisposition { Deferred, Dispatch, Handoff }
public readonly record struct WindowActivation<T>(T Target, bool RestoreOnly);

/// <summary>Thread-safe inbox; window creation and exit decisions belong to one UI dispatcher.</summary>
public sealed class WindowActivationInbox<T>
{
    private readonly object _sync = new();
    private readonly Queue<WindowActivation<T>> _pending = new();
    private WindowActivationPhase _phase;
    private bool _pumpScheduled;

    public WindowActivationPhase Phase { get { lock (_sync) return _phase; } }
    public int PendingCount { get { lock (_sync) return _pending.Count; } }

    public WindowActivationDisposition Enqueue(T target)
    {
        lock (_sync)
        {
            if (_phase == WindowActivationPhase.Exited) return WindowActivationDisposition.Handoff;
            _pending.Enqueue(new(target, RestoreOnly: _phase != WindowActivationPhase.Running));
            return SchedulePumpLocked() ? WindowActivationDisposition.Dispatch : WindowActivationDisposition.Deferred;
        }
    }

    public bool CompleteInitialization()
    {
        lock (_sync)
        {
            if (_phase != WindowActivationPhase.Initializing) throw new InvalidOperationException("Initialization already ended.");
            _phase = WindowActivationPhase.Running;
            return SchedulePumpLocked();
        }
    }

    public bool TryTake(out WindowActivation<T> activation)
    {
        lock (_sync)
        {
            if (_phase == WindowActivationPhase.Running && _pending.TryDequeue(out activation)) return true;
            activation = default;
            return false;
        }
    }

    public bool FinishPump()
    {
        lock (_sync) { _pumpScheduled = false; return SchedulePumpLocked(); }
    }

    public IReadOnlyList<WindowActivation<T>> TakeRejectedPump()
    {
        lock (_sync) { _pumpScheduled = false; return DrainLocked(); }
    }

    public bool BeginClosing()
    {
        lock (_sync)
        {
            if (_phase != WindowActivationPhase.Running) return false;
            _phase = WindowActivationPhase.Closing;
            return true;
        }
    }

    public bool ResumeClosing(bool hasPendingUserWork)
    {
        lock (_sync)
        {
            if (_phase != WindowActivationPhase.Closing || (!hasPendingUserWork && _pending.Count == 0)) return false;
            _phase = WindowActivationPhase.Running;
            return true;
        }
    }

    public bool TryCommitExit(bool hasPendingUserWork)
    {
        lock (_sync)
        {
            if (_phase != WindowActivationPhase.Closing || hasPendingUserWork || _pending.Count != 0) return false;
            _phase = WindowActivationPhase.Exiting;
            return true;
        }
    }

    // Called only after the old main-instance key/listener has been retired.
    public IReadOnlyList<WindowActivation<T>> CompleteExit()
    {
        lock (_sync)
        {
            if (_phase != WindowActivationPhase.Exiting) throw new InvalidOperationException("Exit was not committed.");
            _phase = WindowActivationPhase.Exited;
            _pumpScheduled = false;
            return DrainLocked();
        }
    }

    private bool SchedulePumpLocked()
    {
        if (_phase != WindowActivationPhase.Running || _pending.Count == 0 || _pumpScheduled) return false;
        _pumpScheduled = true;
        return true;
    }

    private WindowActivation<T>[] DrainLocked()
    {
        var pending = _pending.ToArray();
        _pending.Clear();
        return pending;
    }
}
