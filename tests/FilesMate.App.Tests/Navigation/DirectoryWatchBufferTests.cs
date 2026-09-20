using FilesMate.App.Navigation;
using FilesMate.Core.Directories;
using FilesMate.Core.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class DirectoryWatchBufferTests
{
    private static DirectoryWatchNotification Notice(string name) =>
        new(PaneId.New(), 1, DirectoryWatchKind.Created, name);

    [Fact]
    public void Overflow_discards_granular_events_and_remains_bounded_under_a_large_burst()
    {
        var buffer = new DirectoryWatchBuffer(32);
        for (var i = 0; i < 100_000; i++)
            buffer.Enqueue(Notice("file-" + i));

        Assert.True(buffer.TryDequeue(out var overflow));
        Assert.Equal(DirectoryWatchKind.Overflow, overflow.Kind);
        Assert.False(buffer.TryDequeue(out _));
        buffer.Enqueue(Notice("ignored-until-refresh"));
        Assert.True(buffer.IsEmpty);
        buffer.Clear();
        buffer.Enqueue(Notice("next"));
        Assert.True(buffer.TryDequeue(out var next));
        Assert.Equal("next", next.Name);
    }

    [Fact]
    public void Ordinary_notifications_keep_their_order()
    {
        var buffer = new DirectoryWatchBuffer(32);
        buffer.Enqueue(Notice("first"));
        buffer.Enqueue(Notice("second"));
        Assert.True(buffer.TryDequeue(out var first));
        Assert.True(buffer.TryDequeue(out var second));
        Assert.Equal("first", first.Name);
        Assert.Equal("second", second.Name);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void New_navigation_replaces_old_overflow_and_rejects_late_old_notifications()
    {
        var buffer = new DirectoryWatchBuffer(1);
        buffer.Enqueue(Notice("old"));
        buffer.Enqueue(Notice("overflow"));
        buffer.Enqueue(Notice("new") with { Generation = 2 });
        buffer.Enqueue(Notice("late-old"));
        Assert.True(buffer.TryDequeue(out var current));
        Assert.Equal(2, current.Generation);
        Assert.Equal("new", current.Name);
        Assert.True(buffer.IsEmpty);
    }
}
