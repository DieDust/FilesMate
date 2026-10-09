using FilesMate.Search;

namespace FilesMate.Search.Tests;

public sealed class AppearancePreviewLeaseTests
{
    private static readonly AppearancePreviewOwner Owner = new(123, 456);
    private static readonly Guid Session = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly string Fingerprint = new('A', 64);

    [Fact]
    public void AcquisitionIsNotTerminalAndFirstDraftApplies()
    {
        var lease = new AppearancePreviewLease();
        Assert.Equal(AppearancePreviewLeaseAction.Acquire, Send(lease, 1, AppearancePreviewProtocol.Acquire).Action);
        Assert.Equal(Owner, lease.ActiveOwner);
        Assert.Equal(AppearancePreviewLeaseAction.Apply, Send(lease, 2).Action);
    }

    [Fact]
    public void CoalescedFirstDraftCanAcquireSession()
    {
        var lease = new AppearancePreviewLease();
        Assert.Equal(AppearancePreviewLeaseAction.Apply, Send(lease, 2).Action);
        Assert.Equal("StaleAppearancePreview", Send(lease, 1, AppearancePreviewProtocol.Acquire).ErrorCode);
    }

    [Theory]
    [InlineData(AppearancePreviewProtocol.Acquire)]
    [InlineData(AppearancePreviewProtocol.Update)]
    public void IdenticalHeartbeatRenewsWithoutApply(string operation)
    {
        var lease = new AppearancePreviewLease();
        Send(lease, 1, operation);
        var oldExpiry = lease.ExpiresAt;
        Assert.Equal(AppearancePreviewLeaseAction.Heartbeat, Send(lease, 1, operation, now: 500).Action);
        Assert.Equal(oldExpiry + 500, lease.ExpiresAt);
    }

    [Fact]
    public void HigherEpochWithSameSnapshotAdvancesSequenceWithoutResourceApply()
    {
        var lease = new AppearancePreviewLease();
        Send(lease, 1);
        Assert.Equal(AppearancePreviewLeaseAction.Heartbeat, Send(lease, 2, now: 500).Action);
        Assert.Equal("StaleAppearancePreview", Send(lease, 1).ErrorCode);
        Assert.Equal(AppearancePreviewLease.LifetimeMilliseconds + 500, lease.ExpiresAt);
    }

    [Fact]
    public void ChangedSnapshotAtSameEpochIsRejected()
    {
        var lease = new AppearancePreviewLease();
        Send(lease, 1);
        var result = lease.Receive(Owner, Session, 1, AppearancePreviewProtocol.Update, new string('B', 64), 100);
        Assert.Equal("StaleAppearancePreview", result.ErrorCode);
        Assert.Equal(AppearancePreviewLease.LifetimeMilliseconds, lease.ExpiresAt);
    }

    [Fact]
    public void ClearIsTerminalEvenForAClaimedNewerEpoch()
    {
        var lease = new AppearancePreviewLease();
        Send(lease, 1);
        Assert.Equal(AppearancePreviewLeaseAction.Clear, Send(lease, 2, AppearancePreviewProtocol.Clear).Action);
        Assert.Null(lease.ActiveOwner);
        Assert.Equal(AppearancePreviewLeaseAction.Unchanged, Send(lease, 2, AppearancePreviewProtocol.Clear).Action);
        Assert.Equal("StaleAppearancePreview", Send(lease, 3).ErrorCode);
        Assert.Equal("StaleAppearancePreview", Send(lease, 1).ErrorCode);
    }

    [Fact]
    public void ClearReceivedBeforeDraftStillPreventsResurrection()
    {
        var lease = new AppearancePreviewLease();
        Assert.Equal(AppearancePreviewLeaseAction.Unchanged, Send(lease, 3, AppearancePreviewProtocol.Clear).Action);
        Assert.Equal("StaleAppearancePreview", Send(lease, 2).ErrorCode);
        Assert.Equal("StaleAppearancePreview", Send(lease, 4).ErrorCode);
    }

    [Fact]
    public void ReopenNewSessionAcceptsWhileRetiringCanceledSession()
    {
        var lease = new AppearancePreviewLease();
        Send(lease, 1);
        Send(lease, 2, AppearancePreviewProtocol.Clear);
        Assert.Equal(AppearancePreviewLeaseAction.Apply, Send(lease, 3, session: Guid.NewGuid()).Action);
        Assert.Equal("StaleAppearancePreview", Send(lease, 4).ErrorCode);
    }

    [Fact]
    public void RapidReopenCanCoalesceClearWithoutLosingReplayProtection()
    {
        var lease = new AppearancePreviewLease();
        Send(lease, 1);
        Send(lease, 4, session: Guid.NewGuid());
        Assert.Equal("StaleAppearancePreview", Send(lease, 2, AppearancePreviewProtocol.Clear).ErrorCode);
        Assert.Equal("StaleAppearancePreview", Send(lease, 5).ErrorCode);
        Assert.Equal(Owner, lease.ActiveOwner);
    }

    [Fact]
    public void ExpiryDoesNotPermitSameSessionHeartbeatOrNewerDraftToResurrect()
    {
        var lease = new AppearancePreviewLease();
        Send(lease, 1);
        Assert.False(lease.TryExpire(AppearancePreviewLease.LifetimeMilliseconds - 1));
        Assert.True(lease.TryExpire(AppearancePreviewLease.LifetimeMilliseconds));
        Assert.False(lease.TryExpire(AppearancePreviewLease.LifetimeMilliseconds));
        Assert.Equal("AppearancePreviewExpired", Send(lease, 1, now: 13000).ErrorCode);
        Assert.Equal("AppearancePreviewExpired", Send(lease, 2, now: 13000).ErrorCode);
        Assert.Equal(AppearancePreviewLeaseAction.Apply, Send(lease, 3, session: Guid.NewGuid(), now: 13000).Action);
    }

    [Fact]
    public void AnotherOwnerCannotReplaceActiveLease()
    {
        var lease = new AppearancePreviewLease();
        Send(lease, 1);
        Assert.Equal("AppearancePreviewOwned", Send(lease, 1, owner: new(234, 567)).ErrorCode);
        Assert.Equal(Owner, lease.ActiveOwner);
    }

    [Fact]
    public void ForeignClearRecordsTombstoneWithoutClearingActiveOwner()
    {
        var lease = new AppearancePreviewLease();
        var other = new AppearancePreviewOwner(234, 567);
        Send(lease, 1);
        Assert.Equal(AppearancePreviewLeaseAction.Unchanged, Send(lease, 3, AppearancePreviewProtocol.Clear, owner: other).Action);
        Assert.Equal(Owner, lease.ActiveOwner);
        Send(lease, 4, AppearancePreviewProtocol.Clear);
        Assert.Equal("StaleAppearancePreview", Send(lease, 2, owner: other).ErrorCode);
    }

    [Fact]
    public void ProcessStartTimeDisambiguatesPidReuse()
    {
        var lease = new AppearancePreviewLease();
        Send(lease, 5, AppearancePreviewProtocol.Clear);
        Assert.Equal(AppearancePreviewLeaseAction.Apply, Send(lease, 1, owner: Owner with { StartedUtcTicks = 999 }).Action);
    }

    [Fact]
    public void CapacityIsFiniteAndLiveTombstonesAreNeverEvicted()
    {
        var lease = new AppearancePreviewLease(_ => true);
        for (var i = 1; i <= AppearancePreviewLease.MaximumOwners; i++)
            Send(lease, 1, AppearancePreviewProtocol.Clear, owner: new(i, i));
        Assert.Equal(AppearancePreviewLease.MaximumOwners, lease.TrackedOwnerCount);
        Assert.Equal("AppearancePreviewCapacity", Send(lease, 1).ErrorCode);
        Assert.Equal("StaleAppearancePreview", Send(lease, 2, owner: new(1, 1)).ErrorCode);
    }

    [Fact]
    public void OnlyDeadInactiveOwnersArePrunedAtCapacity()
    {
        var dead = false;
        var lease = new AppearancePreviewLease(owner => !(dead && owner.Pid == 1));
        for (var i = 1; i <= AppearancePreviewLease.MaximumOwners; i++)
            Send(lease, 1, AppearancePreviewProtocol.Clear, owner: new(i, i));
        dead = true;
        Assert.Equal(AppearancePreviewLeaseAction.Apply, Send(lease, 1).Action);
        Assert.Equal(AppearancePreviewLease.MaximumOwners, lease.TrackedOwnerCount);
    }

    [Fact]
    public void UnretainedCancellationFailsClosedEvenAfterAnotherOwnerDies()
    {
        var dead = false;
        var lease = new AppearancePreviewLease(owner => !(dead && owner.Pid == 1));
        for (var i = 1; i <= AppearancePreviewLease.MaximumOwners; i++)
            Send(lease, 1, AppearancePreviewProtocol.Clear, owner: new(i, i));
        Assert.Equal("AppearancePreviewCapacity", Send(lease, 3, AppearancePreviewProtocol.Clear).ErrorCode);
        dead = true;
        Assert.Equal("AppearancePreviewCapacity", Send(lease, 2).ErrorCode);
        Assert.Null(lease.ActiveOwner);
        // The bounded known-owner history remains usable, including explicit new sessions.
        Assert.Equal(AppearancePreviewLeaseAction.Apply, Send(lease, 4, owner: new(2, 2), session: Guid.NewGuid()).Action);
    }

    [Fact]
    public void ProcessGlobalHighWaterMarkProtectsBeyondBoundedRetiredSessionWindow()
    {
        var lease = new AppearancePreviewLease();
        Send(lease, 1);
        for (var i = 2; i < 50; i++) Send(lease, i, session: Guid.NewGuid());
        Assert.Equal("StaleAppearancePreview", Send(lease, 1).ErrorCode);
        Assert.Equal(1, lease.TrackedOwnerCount);
    }

    [Theory]
    [InlineData(0, 1, 1, 0)]
    [InlineData(1, 0, 1, 0)]
    [InlineData(1, 1, 0, 0)]
    [InlineData(1, 1, 1, -1)]
    [InlineData(1, 1, 1, long.MaxValue)]
    public void InvalidIdentitySequenceAndClockAreRejected(int pid, long started, long epoch, long now)
    {
        var lease = new AppearancePreviewLease();
        Assert.Equal("InvalidAppearancePreview", Send(lease, epoch, owner: new(pid, started), now: now).ErrorCode);
        Assert.Equal(0, lease.TrackedOwnerCount);
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData(AppearancePreviewProtocol.Update, null)]
    [InlineData(AppearancePreviewProtocol.Clear, "A")]
    [InlineData(AppearancePreviewProtocol.Acquire, "A")]
    [InlineData(AppearancePreviewProtocol.Update, "not a digest")]
    public void InvalidOperationAndPayloadDoNotMutateState(string operation, string? fingerprint)
    {
        var lease = new AppearancePreviewLease();
        Assert.Equal("InvalidAppearancePreview", lease.Receive(Owner, Session, 1, operation, fingerprint, 0).ErrorCode);
        Assert.Equal(0, lease.TrackedOwnerCount);
    }

    private static AppearancePreviewLeaseDecision Send(AppearancePreviewLease lease, long epoch,
        string operation = AppearancePreviewProtocol.Update, AppearancePreviewOwner? owner = null,
        Guid? session = null, long now = 0) => lease.Receive(owner ?? Owner, session ?? Session, epoch, operation,
            operation == AppearancePreviewProtocol.Update ? Fingerprint : null, now);
}
