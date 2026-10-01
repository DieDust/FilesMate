using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

public sealed class NativeFolderDropTargetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unavailable_folder_does_not_cancel_drag_into_another_folder(bool enterFails)
    {
        if (!OperatingSystem.IsWindows()) return;
        var good = new FolderTarget();
        var bad = new FolderTarget { EnterResult = unchecked((int)0x80070005) };
        var errors = new List<Exception>();
        var handoffs = new List<string>();
        using var receiver = Receiver(point => point.X == 0 ? "bad" : "good", path =>
        {
            if (path != "bad") return good;
            if (enterFails) return bad;
            throw new UnauthorizedAccessException();
        }, handoffs, errors);
        var data = Marshal.GetIUnknownForObject(new object());
        try
        {
            uint effect = 1;
            Assert.Equal(0, receiver.DragEnter(data, 0, default, ref effect));
            Assert.Equal(0u, effect);
            Assert.Single(errors);
            Assert.Equal(0, receiver.DragOver(0, new() { X = 1 }, ref effect));
            Assert.Equal(1u, effect);
            Assert.Equal(data, good.EnteredData);
            Assert.Equal(0, receiver.Drop(data, 0, new() { X = 1 }, ref effect));
            Assert.Equal(data, good.DroppedData);
            Assert.Equal("good", Assert.Single(handoffs));
        }
        finally { Marshal.Release(data); }
    }

    [Fact]
    public void Release_position_selects_the_final_folder_and_preserves_original_data_and_allowed_effects()
    {
        if (!OperatingSystem.IsWindows()) return;
        var first = new FolderTarget { RequestedEffect = 2 };
        var final = new FolderTarget { RequestedEffect = 3 };
        var handoffs = new List<string>();
        using var receiver = Receiver(point => point.X == 0 ? "first" : "final", path => path == "first" ? first : final, handoffs);
        var data = Marshal.GetIUnknownForObject(new object());
        try
        {
            uint effect = 1; // Copy-only source: a folder may not invent Move.
            receiver.DragEnter(data, 0, default, ref effect);
            Assert.Equal(0u, effect);
            Assert.Equal(0, receiver.Drop(data, 0, new() { X = 1 }, ref effect));
            Assert.Equal(1u, effect);
            Assert.Equal(1, first.Leaves);
            Assert.Equal(0, first.Drops);
            Assert.Equal(data, final.EnteredData);
            Assert.Equal(data, final.DroppedData);
            Assert.Equal("final", Assert.Single(handoffs));
        }
        finally { Marshal.Release(data); }
    }

    [Fact]
    public void Leave_cancels_the_session_without_transfer_or_history_handoff()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = new FolderTarget();
        var handoffs = new List<string>();
        using var receiver = Receiver(_ => "folder", _ => folder, handoffs);
        var data = Marshal.GetIUnknownForObject(new object());
        try
        {
            uint effect = 1;
            receiver.DragEnter(data, 0, default, ref effect);
            receiver.DragLeave();
            receiver.Drop(data, 0, default, ref effect);
            Assert.Equal(0u, effect);
            Assert.Equal(1, folder.Leaves);
            Assert.Equal(0, folder.Drops);
            Assert.Empty(handoffs);
        }
        finally { Marshal.Release(data); }
    }

    [Fact]
    public void Transfer_that_pumps_messages_does_not_clear_a_later_drag_session()
    {
        if (!OperatingSystem.IsWindows()) return;
        var first = new FolderTarget();
        var second = new FolderTarget();
        var handoffs = new List<string>();
        var cleared = 0;
        string? feedback = null;
        using var receiver = new NativeFolderDropTarget(0, p => p.X == 0 ? "first" : "second",
            (_, path, _) => feedback = path, () => { cleared++; feedback = null; }, _ => { },
            (path, _) => handoffs.Add(path), path => path == "first" ? first : second, _ => { });
        var data = Marshal.GetIUnknownForObject(new object());
        var nextData = Marshal.GetIUnknownForObject(new object());
        try
        {
            first.OnDrop = () =>
            {
                if (!OperatingSystem.IsWindows()) return;
                uint nextEffect = 1;
                receiver.DragEnter(nextData, 0, new() { X = 1 }, ref nextEffect);
                Assert.Equal(1u, nextEffect);
            };
            uint effect = 1;
            receiver.DragEnter(data, 0, default, ref effect);
            receiver.Drop(data, 0, default, ref effect);
            Assert.Equal("second", feedback);
            Assert.Equal(1, cleared);
            Assert.Equal(0, second.Leaves);
            receiver.Drop(nextData, 0, new() { X = 1 }, ref effect);
            Assert.Equal(nextData, second.DroppedData);
            Assert.Equal(new[] { "first", "second" }, handoffs);
        }
        finally { Marshal.Release(data); Marshal.Release(nextData); }
    }

    [SupportedOSPlatform("windows")]
    private static NativeFolderDropTarget Receiver(Func<DropScreenPoint, string?> destination,
        Func<string, INativeFolderDropTarget> create, List<string> handoffs, List<Exception>? errors = null) =>
        new(0, destination, (_, _, _) => { }, () => { }, error => errors?.Add(error),
            (path, _) => handoffs.Add(path), create, _ => { });

    private sealed class FolderTarget : INativeFolderDropTarget
    {
        public int EnterResult { get; init; }
        public uint RequestedEffect { get; init; } = 1;
        public nint EnteredData { get; private set; }
        public nint DroppedData { get; private set; }
        public int Leaves { get; private set; }
        public int Drops { get; private set; }
        public Action? OnDrop { get; set; }
        public int DragEnter(nint data, uint keys, DropScreenPoint point, ref uint effect)
        { EnteredData = data; effect = RequestedEffect; return EnterResult; }
        public int DragOver(uint keys, DropScreenPoint point, ref uint effect) { effect = RequestedEffect; return 0; }
        public int DragLeave() { Leaves++; return 0; }
        public int Drop(nint data, uint keys, DropScreenPoint point, ref uint effect)
        { DroppedData = data; Drops++; OnDrop?.Invoke(); effect = RequestedEffect; return 0; }
    }
}
