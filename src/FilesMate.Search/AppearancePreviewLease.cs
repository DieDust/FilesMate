namespace FilesMate.Search;

public readonly record struct AppearancePreviewOwner(int Pid, long StartedUtcTicks);
public enum AppearancePreviewLeaseAction { Reject, Acquire, Apply, Heartbeat, Clear, Unchanged }
public readonly record struct AppearancePreviewLeaseDecision(AppearancePreviewLeaseAction Action, string? ErrorCode = null);

/// <summary>Dispatcher-confined, bounded ownership and replay protection for transient appearance.</summary>
public sealed class AppearancePreviewLease
{
    public const int LifetimeMilliseconds = 12_000;
    public const int MaximumOwners = 16;
    public const int MaximumRetiredSessions = 8;
    private readonly Dictionary<AppearancePreviewOwner, Seen> _seen = [];
    private readonly Func<AppearancePreviewOwner, bool>? _isAlive;
    private AppearancePreviewOwner? _active;
    private long _expiresAt;
    private bool _untrackedCancellation;

    private enum Phase { Acquired, Previewing, Cleared, Expired }

    private sealed class Seen
    {
        public Guid Session;
        public long Epoch;
        public string? Fingerprint;
        public string? Operation;
        public Phase Phase;
        public readonly Queue<Guid> Retired = new();
    }

    public AppearancePreviewLease(Func<AppearancePreviewOwner, bool>? isAlive = null) => _isAlive = isAlive;
    public AppearancePreviewOwner? ActiveOwner => _active;
    public int TrackedOwnerCount => _seen.Count;
    public long ExpiresAt => _expiresAt;

    public AppearancePreviewLeaseDecision Receive(AppearancePreviewOwner owner, Guid session, long epoch,
        string operation, string? fingerprint, long now)
    {
        if (owner.Pid <= 0 || owner.StartedUtcTicks <= 0 || owner.StartedUtcTicks > DateTime.MaxValue.Ticks
            || session == Guid.Empty || epoch <= 0 || now < 0 || now > long.MaxValue - LifetimeMilliseconds
            || !AppearancePreviewProtocol.IsOperation(operation)
            || (operation == AppearancePreviewProtocol.Update) != (fingerprint is not null)
            || fingerprint is not null && (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)))
            return Reject("InvalidAppearancePreview");
        // A clear must record its tombstone even while another process owns the visible preview.
        // Otherwise a delayed draft from the canceled process could acquire ownership later.
        if (operation != AppearancePreviewProtocol.Clear && _active is { } active && active != owner)
            return Reject("AppearancePreviewOwned");
        if (!_seen.TryGetValue(owner, out var last))
        {
            // Once a terminal message could not be retained, admitting an unknown owner after
            // pruning would forget that cancellation. Keep unknown owners closed until restart.
            if (_untrackedCancellation) return Reject("AppearancePreviewCapacity");
            if (_seen.Count == MaximumOwners && _isAlive is not null)
                foreach (var dead in _seen.Keys.Where(key => key != _active && !_isAlive(key)).ToArray()) _seen.Remove(dead);
            if (_seen.Count == MaximumOwners)
            {
                if (operation == AppearancePreviewProtocol.Clear) _untrackedCancellation = true;
                return Reject("AppearancePreviewCapacity");
            }
            last = new Seen();
            _seen.Add(owner, last);
        }
        if (epoch < last.Epoch || last.Retired.Contains(session)) return Reject("StaleAppearancePreview");
        if (epoch == last.Epoch)
        {
            if (session != last.Session || operation != last.Operation || fingerprint != last.Fingerprint)
                return Reject("StaleAppearancePreview");
            if (last.Phase == Phase.Cleared) return new(AppearancePreviewLeaseAction.Unchanged);
            if (last.Phase == Phase.Expired || _active != owner) return Reject("AppearancePreviewExpired");
            _expiresAt = now + LifetimeMilliseconds;
            return new(AppearancePreviewLeaseAction.Heartbeat);
        }
        if (session == last.Session && operation != AppearancePreviewProtocol.Clear)
        {
            if (last.Phase == Phase.Cleared) return Reject("StaleAppearancePreview");
            if (last.Phase == Phase.Expired) return Reject("AppearancePreviewExpired");
        }
        if (last.Session != Guid.Empty && last.Session != session)
        {
            last.Retired.Enqueue(last.Session);
            if (last.Retired.Count > MaximumRetiredSessions) last.Retired.Dequeue();
        }
        var unchangedDraft = _active == owner && last.Phase == Phase.Previewing
            && operation == AppearancePreviewProtocol.Update && last.Fingerprint == fingerprint;
        var unchangedAcquisition = _active == owner && last.Phase == Phase.Acquired
            && operation == AppearancePreviewProtocol.Acquire;
        // Epochs are process-global and strictly increase for new messages. Identical snapshots
        // renew the lease without rebuilding resources. A new session may replace this owner's session after coalesced reopen.
        last.Session = session;
        last.Epoch = epoch;
        last.Fingerprint = fingerprint;
        last.Operation = operation;
        if (operation == AppearancePreviewProtocol.Clear)
        {
            last.Phase = Phase.Cleared;
            if (_active != owner) return new(AppearancePreviewLeaseAction.Unchanged);
            _active = null;
            _expiresAt = 0;
            return new(AppearancePreviewLeaseAction.Clear);
        }
        last.Phase = operation == AppearancePreviewProtocol.Acquire ? Phase.Acquired : Phase.Previewing;
        _active = owner;
        _expiresAt = now + LifetimeMilliseconds;
        if (unchangedDraft || unchangedAcquisition) return new(AppearancePreviewLeaseAction.Heartbeat);
        return new(operation == AppearancePreviewProtocol.Acquire
            ? AppearancePreviewLeaseAction.Acquire : AppearancePreviewLeaseAction.Apply);
    }

    public bool TryExpire(long now)
    {
        if (_active is not { } owner || now < _expiresAt) return false;
        _seen[owner].Phase = Phase.Expired;
        _active = null;
        _expiresAt = 0;
        return true;
    }

    private static AppearancePreviewLeaseDecision Reject(string code) => new(AppearancePreviewLeaseAction.Reject, code);
}
