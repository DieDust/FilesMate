using FilesMate.Core.Lifetime;

namespace FilesMate.Core.Tests.Lifetime;

public sealed class WindowActivationInboxTests
{
    [Fact]
    public void Startup_preserves_every_request_and_claims_one_dispatch()
    {
        var inbox = new WindowActivationInbox<string>();
        Assert.Equal(WindowActivationDisposition.Deferred, inbox.Enqueue("first"));
        Assert.Equal(WindowActivationDisposition.Deferred, inbox.Enqueue("second"));
        Assert.False(inbox.TryTake(out _));
        Assert.True(inbox.CompleteInitialization());
        Assert.True(inbox.TryTake(out var first));
        Assert.Equal("first", first.Target);
        Assert.True(first.RestoreOnly);
        Assert.True(inbox.TryTake(out var second));
        Assert.Equal("second", second.Target);
        Assert.True(second.RestoreOnly);
        Assert.False(inbox.TryTake(out _));
        Assert.False(inbox.FinishPump());
    }

    [Fact]
    public void Ready_activation_is_dispatched_with_its_original_target()
    {
        var inbox = Ready();
        Assert.Equal(WindowActivationDisposition.Dispatch, inbox.Enqueue("folder"));
        Assert.True(inbox.TryTake(out var request));
        Assert.Equal("folder", request.Target);
        Assert.False(request.RestoreOnly);
        Assert.Equal(WindowActivationPhase.Running, inbox.Phase);
        Assert.False(inbox.FinishPump());
    }

    [Fact]
    public void Reentrant_requests_share_the_existing_pump_and_keep_order()
    {
        var inbox = Ready();
        Assert.Equal(WindowActivationDisposition.Dispatch, inbox.Enqueue("creating"));
        Assert.True(inbox.TryTake(out _));
        Assert.Equal(WindowActivationDisposition.Deferred, inbox.Enqueue("during factory"));
        Assert.True(inbox.TryTake(out var next));
        Assert.Equal("during factory", next.Target);
        Assert.False(inbox.FinishPump());
        Assert.Equal(WindowActivationDisposition.Dispatch, inbox.Enqueue("later"));
    }

    [Fact]
    public void Arrival_between_empty_read_and_pump_completion_is_not_lost()
    {
        var inbox = Ready();
        inbox.Enqueue("first");
        Assert.True(inbox.TryTake(out _));
        Assert.False(inbox.TryTake(out _));
        Assert.Equal(WindowActivationDisposition.Deferred, inbox.Enqueue("late"));
        Assert.True(inbox.FinishPump());
        Assert.True(inbox.TryTake(out var late));
        Assert.Equal("late", late.Target);
    }

    [Fact]
    public void Activation_during_close_cancels_exit_and_restores_instead_of_toggling()
    {
        var inbox = Ready();
        Assert.True(inbox.BeginClosing());
        Assert.Equal(WindowActivationDisposition.Deferred, inbox.Enqueue("activate"));
        Assert.False(inbox.TryCommitExit(hasPendingUserWork: false));
        Assert.True(inbox.ResumeClosing(hasPendingUserWork: false));
        Assert.True(inbox.TryTake(out var request));
        Assert.True(request.RestoreOnly);
        Assert.Equal(WindowActivationPhase.Running, inbox.Phase);
    }

    [Fact]
    public void Pending_user_work_prevents_exit_and_keeps_the_ui_owner_alive()
    {
        var inbox = Ready();
        inbox.BeginClosing();
        Assert.False(inbox.TryCommitExit(hasPendingUserWork: true));
        Assert.True(inbox.ResumeClosing(hasPendingUserWork: true));
        Assert.Equal(WindowActivationPhase.Running, inbox.Phase);
        Assert.Equal(0, inbox.PendingCount);
    }

    [Fact]
    public void Requests_racing_key_release_are_handed_off_exactly_once_after_exit()
    {
        var inbox = Ready();
        inbox.BeginClosing();
        Assert.True(inbox.TryCommitExit(hasPendingUserWork: false));
        Assert.Equal(WindowActivationDisposition.Deferred, inbox.Enqueue("during release"));
        Assert.False(inbox.TryTake(out _));
        var handoff = inbox.CompleteExit();
        Assert.Single(handoff);
        Assert.Equal("during release", handoff[0].Target);
        Assert.Equal(WindowActivationDisposition.Handoff, inbox.Enqueue("after release"));
        Assert.Empty(inbox.TakeRejectedPump());
        Assert.Throws<InvalidOperationException>(() => inbox.CompleteExit());
    }

    [Fact]
    public void Concurrent_callers_claim_one_pump_and_preserve_all_requests()
    {
        var inbox = Ready();
        var owners = 0;
        Parallel.For(0, 64, index =>
        {
            if (inbox.Enqueue(index.ToString()) == WindowActivationDisposition.Dispatch) Interlocked.Increment(ref owners);
        });
        Assert.Equal(1, owners);
        var received = new HashSet<string>();
        while (inbox.TryTake(out var request)) Assert.True(received.Add(request.Target));
        Assert.Equal(64, received.Count);
        Assert.False(inbox.FinishPump());
    }

    [Fact]
    public void Rejected_dispatcher_preserves_payload_without_running_a_factory()
    {
        var inbox = Ready();
        inbox.Enqueue("retained");
        var retained = inbox.TakeRejectedPump();
        Assert.Single(retained);
        Assert.Equal("retained", retained[0].Target);
        Assert.Equal(WindowActivationPhase.Running, inbox.Phase);
        Assert.Equal(WindowActivationDisposition.Dispatch, inbox.Enqueue("new explicit attempt"));
    }

    private static WindowActivationInbox<string> Ready()
    {
        var inbox = new WindowActivationInbox<string>();
        Assert.False(inbox.CompleteInitialization());
        return inbox;
    }
}
