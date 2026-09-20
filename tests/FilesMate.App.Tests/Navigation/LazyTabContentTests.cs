using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class LazyTabContentTests
{
    [Fact]
    public void Pending_load_can_begin_and_complete_once()
    {
        var session = new LazyTabLoadSession();

        Assert.Equal(LazyTabLoadState.Pending, session.State);
        Assert.True(session.TryBegin());
        Assert.False(session.TryBegin());
        Assert.True(session.TryComplete());
        Assert.Equal(LazyTabLoadState.Loaded, session.State);
    }

    [Fact]
    public void Cancelled_load_cannot_publish_content()
    {
        var session = new LazyTabLoadSession();

        session.Cancel();

        Assert.Equal(LazyTabLoadState.Cancelled, session.State);
        Assert.False(session.TryBegin());
        Assert.False(session.TryComplete());
    }
}
