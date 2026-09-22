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

    [Fact]
    public void Repeated_writes_to_pending_names_do_not_trigger_a_directory_rescan()
    {
        var buffer = new DirectoryWatchBuffer(32);
        for (var i = 0; i < 100_000; i++)
            buffer.Enqueue(Notice("file-" + i % 8) with { Kind = DirectoryWatchKind.Modified });
        var names = new HashSet<string>();
        while (buffer.TryDequeue(out var notice))
        {
            Assert.Equal(DirectoryWatchKind.Modified, notice.Kind);
            Assert.True(names.Add(notice.Name));
        }
        Assert.Equal(8, names.Count);
    }

    [Fact]
    public void Rename_and_delete_recreate_preserve_metadata_ordering_boundaries()
    {
        var buffer = new DirectoryWatchBuffer(32);
        var events = new[]
        {
            Notice("old") with { Kind = DirectoryWatchKind.Modified },
            Notice("new") with { Kind = DirectoryWatchKind.Renamed, OldName = "old" },
            Notice("old") with { Kind = DirectoryWatchKind.Created },
            Notice("old") with { Kind = DirectoryWatchKind.Modified },
            Notice("old") with { Kind = DirectoryWatchKind.Deleted },
            Notice("old") with { Kind = DirectoryWatchKind.Created },
            Notice("old") with { Kind = DirectoryWatchKind.Modified },
        };
        foreach (var notice in events) buffer.Enqueue(notice);
        foreach (var expected in events)
        {
            Assert.True(buffer.TryDequeue(out var actual));
            Assert.Equal(expected, actual);
        }
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Write_after_dequeue_is_retained_for_the_next_metadata_read()
    {
        var buffer = new DirectoryWatchBuffer(32);
        var notice = Notice("live") with { Kind = DirectoryWatchKind.Modified };
        buffer.Enqueue(notice);
        Assert.True(buffer.TryDequeue(out _));
        buffer.Enqueue(notice);
        Assert.True(buffer.TryDequeue(out _));
    }

    [Fact]
    public void Dequeuing_before_a_structural_barrier_does_not_forget_later_pending_write()
    {
        var buffer = new DirectoryWatchBuffer(32);
        var modified = Notice("live") with { Kind = DirectoryWatchKind.Modified };
        buffer.Enqueue(modified);
        buffer.Enqueue(modified with { Kind = DirectoryWatchKind.Created });
        buffer.Enqueue(modified);
        Assert.True(buffer.TryDequeue(out _));
        buffer.Enqueue(modified);
        Assert.True(buffer.TryDequeue(out _));
        Assert.True(buffer.TryDequeue(out _));
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Case_distinct_names_and_new_navigation_do_not_share_coalesced_changes()
    {
        var buffer = new DirectoryWatchBuffer(32);
        buffer.Enqueue(Notice("A") with { Kind = DirectoryWatchKind.Modified });
        buffer.Enqueue(Notice("a") with { Kind = DirectoryWatchKind.Modified });
        Assert.True(buffer.TryDequeue(out _));
        Assert.True(buffer.TryDequeue(out _));
        buffer.Enqueue(Notice("a") with { Kind = DirectoryWatchKind.Modified });
        buffer.Enqueue(Notice("a") with { Kind = DirectoryWatchKind.Modified, Generation = 2 });
        Assert.True(buffer.TryDequeue(out var newer));
        Assert.Equal(2, newer.Generation);
        Assert.True(buffer.IsEmpty);
    }
}
