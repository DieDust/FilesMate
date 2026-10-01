using System.Runtime.InteropServices;
using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

public sealed class ShellDropTargetExportTests
{
    [Fact]
    public void Background_interface_exports_the_same_drop_receiver_and_disconnect_revokes_new_requests()
    {
        if (!OperatingSystem.IsWindows()) return;
        var target = new RecordingTarget();
        var document = new ShellSelectionDocument(0, @"D:\fixture", _ => { }, dropTarget: target);
        var iid = typeof(INativeFolderDropTarget).GUID;
        Assert.Equal(0, document.GetItemObject(0, ref iid, out var pointer));
        Assert.NotEqual(0, pointer);
        try
        {
            var exported = (INativeFolderDropTarget)Marshal.GetObjectForIUnknown(pointer);
            Assert.Equal(0, exported.DragLeave());
            Assert.Equal(1, target.Leaves);
        }
        finally { Marshal.Release(pointer); }
        Assert.True(document.GetItemObject(1, ref iid, out pointer) < 0);
        Assert.Equal(0, pointer);
        document.Disconnect();
        Assert.True(document.GetItemObject(0, ref iid, out pointer) < 0);
        Assert.Equal(0, pointer);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecordingTarget : INativeFolderDropTarget
    {
        public int Leaves { get; private set; }
        public int DragEnter(nint data, uint keys, DropScreenPoint point, ref uint effect) => 0;
        public int DragOver(uint keys, DropScreenPoint point, ref uint effect) => 0;
        public int DragLeave() { Leaves++; return 0; }
        public int Drop(nint data, uint keys, DropScreenPoint point, ref uint effect) => 0;
    }
}
