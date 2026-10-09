using System.Collections.Concurrent;
using FilesMate.App.Navigation;
using FilesMate.Core.Configuration;
using FilesMate.Platform.Windows.Processes;
using Microsoft.Windows.AppLifecycle;

namespace FilesMate.App;

internal static partial class AppLifecycle
{
    internal const string RecoveryArgument = "--activation-recovery";
    private static readonly object IncomingSync = new();
    private static readonly object RecoverySync = new();
    private static readonly ConcurrentQueue<LaunchTarget> EarlyIncoming = new();
    private static AppInstance? _mainInstance;
    private static RegisteredWaitHandle? _incomingWait;
    private static bool _ending;
    private static volatile bool _keyReleased;
    private static bool _recoveryStarted;

    internal static bool EndMainInstance()
    {
        AppInstance? instance;
        lock (IncomingSync)
        {
            if (_ending) return _keyReleased;
            _ending = true;
            instance = _mainInstance;
            _mainInstance = null;
        }
        try
        {
            if (instance is not null)
            {
                instance.Activated -= Instance_Activated;
                instance.UnregisterKey();
            }
            _keyReleased = true;
            TraceInternal("Main-instance activation ownership retired");
        }
        catch (Exception error) { App.LogFailure("ActivationKeyRetirement", error); }
        finally
        {
            _incomingWait?.Unregister(null);
            _incomingWait = null;
            _incoming?.Dispose();
            _incoming = null;
        }
        return _keyReleased;
    }

    internal static void PreserveRecoveryRequest(LaunchTarget target)
    {
        try { PendingLaunchExchange.EnqueueTarget(target); }
        catch (Exception error) { App.LogFailure("ActivationPayloadPreservation", error); }
    }

    internal static void QueueRecoveryRequests(IEnumerable<LaunchTarget> targets)
    {
        lock (RecoverySync)
        {
            foreach (var target in targets) PreserveRecoveryRequest(target);
            if (!_keyReleased)
            {
                TraceInternal("Recovery payload retained; old activation key could not be retired");
                return;
            }
            if (_recoveryStarted)
            {
                if (EventWaitHandle.TryOpenExisting(IncomingEventName, out var incoming))
                    using (incoming) { incoming.Set(); }
                return;
            }
            // A named event alone does not prove a live owner: a redirecting
            // process may still hold the retired event. One bootstrap obtains
            // the SDK key or signals the new real owner without adding a toggle.
            try
            {
                DetachedProcess.Start(Environment.ProcessPath!, RuntimeProfile.ForChild([RecoveryArgument]));
                _recoveryStarted = true;
                TraceInternal("One independent activation-recovery bootstrap requested");
            }
            catch (Exception error) { App.LogFailure("ActivationRecoveryBootstrap", error); }
        }
    }

    private static List<LaunchTarget> ClaimPendingRedirects()
    {
        lock (IncomingSync)
        {
            // A stale callback never consumes the next owner's disk inbox.
            if (_ending) return [];
            var targets = new List<LaunchTarget>();
            while (EarlyIncoming.TryDequeue(out var early)) targets.Add(early);
            targets.AddRange(PendingLaunchExchange.DequeueAll());
            return targets;
        }
    }
}
