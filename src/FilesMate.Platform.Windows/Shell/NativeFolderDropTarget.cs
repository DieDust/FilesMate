using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Shell;

/// <summary>Routes the original OLE data object to the Windows folder drop handler.</summary>
[SupportedOSPlatform("windows"), ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class NativeFolderDropTarget : INativeFolderDropTarget, IDisposable
{
    private readonly nint _owner;
    private readonly Func<DropScreenPoint, string?> _destination;
    private readonly Action<DropScreenPoint, string?, uint> _feedback;
    private readonly Action _leave;
    private readonly Action<Exception> _failure;
    private readonly Action<string, uint> _handoff;
    private readonly Func<string, INativeFolderDropTarget> _create;
    private readonly Action<INativeFolderDropTarget> _release;
    private readonly List<nint> _windows = [];
    private IDropTargetHelper? _image;
    private INativeFolderDropTarget? _folderTarget;
    private string? _folder;
    private nint _data;
    private uint _allowed;
    private bool _disposed;
    private bool _imageEntered;
    private bool _oleInitialized;

    public NativeFolderDropTarget(nint owner, IEnumerable<nint> windows,
        Func<DropScreenPoint, string?> destination, Action<DropScreenPoint, string?, uint> feedback,
        Action leave, Action<Exception> failure, Action<string, uint> handoff)
        : this(owner, destination, feedback, leave, failure, handoff, null, null)
    {
        Marshal.ThrowExceptionForHR(OleInitialize(0));
        _oleInitialized = true;
        try
        {
            foreach (var window in windows.Where(window => window != 0).Distinct())
            {
                // Do not revoke or replace another component's registered target.
                var hr = RegisterDragDrop(window, this);
                if (hr == unchecked((int)0x80040101)) continue;
                Marshal.ThrowExceptionForHR(hr);
                _windows.Add(window);
            }
            try
            {
                _image = (IDropTargetHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(
                    new Guid("4657278A-411B-11D2-839A-00C04FD918D0"), throwOnError: true)!)!;
            }
            catch (COMException) { /* Drag images are optional; data transfer remains available. */ }
        }
        catch { Dispose(); throw; }
    }

    internal NativeFolderDropTarget(nint owner, Func<DropScreenPoint, string?> destination,
        Action<DropScreenPoint, string?, uint> feedback, Action leave, Action<Exception> failure,
        Action<string, uint> handoff, Func<string, INativeFolderDropTarget>? create,
        Action<INativeFolderDropTarget>? release)
    {
        _owner = owner; _destination = destination; _feedback = feedback; _leave = leave; _failure = failure;
        _handoff = handoff; _create = create ?? CreateFolderTarget;
        _release = release ?? (target => Marshal.ReleaseComObject(target));
    }

    public int DragEnter(nint data, uint keys, DropScreenPoint point, ref uint effect)
    {
        try
        {
            Reset();
            if (_disposed || data == 0) { effect = 0; return 0; }
            Marshal.AddRef(data); _data = data; _allowed = effect & 7;
            effect = Resolve(keys, point);
            if (_image is not null)
            {
                _image.DragEnter(_owner, data, ref point, effect);
                _imageEntered = true;
            }
            return 0;
        }
        catch (Exception error) { return Fail(error, ref effect); }
    }

    public int DragOver(uint keys, DropScreenPoint point, ref uint effect)
    {
        try
        {
            effect = Resolve(keys, point);
            if (_imageEntered) _image?.DragOver(ref point, effect);
            return 0;
        }
        catch (Exception error) { return Fail(error, ref effect); }
    }

    private uint Resolve(uint keys, DropScreenPoint point)
    {
        var destination = _data == 0 || _disposed ? null : _destination(point);
        var changed = !string.Equals(_folder, destination, StringComparison.OrdinalIgnoreCase);
        var effect = _allowed;
        try
        {
            if (changed)
            {
                ReleaseFolder(leave: true);
                if (destination is not null) { _folderTarget = _create(destination); _folder = destination; }
            }
            if (_folderTarget is null) effect = 0;
            else
            {
                var hr = changed ? _folderTarget.DragEnter(_data, keys, point, ref effect)
                    : _folderTarget.DragOver(keys, point, ref effect);
                Marshal.ThrowExceptionForHR(hr);
                effect &= _allowed;
            }
        }
        catch (Exception error)
        {
            // A bad/inaccessible folder rejects only this destination. Keep
            // the original object so the same drag can enter another folder.
            try { ReleaseFolder(leave: true); } catch (Exception cleanup) { ReportFailure(cleanup); }
            _folder = destination;
            effect = 0;
            ReportFailure(error);
        }
        _feedback(point, destination, effect);
        return effect;
    }

    public int DragLeave()
    {
        try { Reset(); _leave(); return 0; }
        catch (Exception error) { ReportFailure(error); return Marshal.GetHRForException(error); }
    }

    public int Drop(nint data, uint keys, DropScreenPoint point, ref uint effect)
    {
        try
        {
            // Resolve again at the release position. Windows receives the original
            // object, including indexed virtual streams and async-transfer interfaces.
            effect = Resolve(keys, point);
            if (_imageEntered) { _image?.Drop(data, ref point, effect); _imageEntered = false; }
        }
        catch (Exception error) { return Fail(error, ref effect); }

        if (effect == 0 || _folderTarget is null)
        {
            try { Reset(); _leave(); return 0; }
            catch (Exception error) { return Fail(error, ref effect); }
        }
        var target = _folderTarget;
        var reference = _data;
        var destination = _folder!;
        var allowed = _allowed;
        _folderTarget = null; _folder = null; _data = 0; _allowed = 0;
        try
        {
            // Drop may pump messages or start an asynchronous Shell transfer.
            // Detach this session before handing it off; its final cleanup must
            // not reset a later drag that entered while Windows was working.
            _leave();
            _handoff(destination, effect);
            var hr = target.Drop(data, keys, point, ref effect);
            if (hr < 0) effect = 0;
            else effect &= allowed;
            return hr;
        }
        catch (Exception error) { effect = 0; ReportFailure(error); return Marshal.GetHRForException(error); }
        finally
        {
            try { _release(target); } catch (Exception error) { ReportFailure(error); }
            try { if (reference != 0) Marshal.Release(reference); } catch (Exception error) { ReportFailure(error); }
        }
    }

    private INativeFolderDropTarget CreateFolderTarget(string destination)
    {
        Marshal.ThrowExceptionForHR(Shell32.SHParseDisplayName(destination, 0, out var pidl, 0, out _));
        nint parent = 0, target = 0;
        IShellFolder? folder = null;
        try
        {
            var folderId = Shell32.IidIShellFolder;
            Marshal.ThrowExceptionForHR(Shell32.SHBindToParent(pidl, ref folderId, out parent, out var child));
            folder = (IShellFolder)Marshal.GetObjectForIUnknown(parent);
            var targetId = typeof(INativeFolderDropTarget).GUID;
            Marshal.ThrowExceptionForHR(folder.GetUIObjectOf(_owner, 1, [child], ref targetId, 0, out target));
            return (INativeFolderDropTarget)Marshal.GetObjectForIUnknown(target);
        }
        finally
        {
            if (target != 0) Marshal.Release(target);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (parent != 0) Marshal.Release(parent);
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
        }
    }

    private int Fail(Exception error, ref uint effect)
    {
        effect = 0;
        try { Reset(); _leave(); } catch (Exception cleanup) { ReportFailure(cleanup); }
        ReportFailure(error);
        return Marshal.GetHRForException(error);
    }

    private void ReleaseFolder(bool leave)
    {
        var target = _folderTarget;
        _folderTarget = null; _folder = null;
        if (target is not null)
        {
            try { if (leave) target.DragLeave(); }
            finally { _release(target); }
        }
    }

    private void Reset()
    {
        try
        {
            try { if (_imageEntered) { _imageEntered = false; _image?.DragLeave(); } }
            finally { ReleaseFolder(leave: true); }
        }
        finally { if (_data != 0) { Marshal.Release(_data); _data = 0; } _allowed = 0; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var window in _windows) RevokeDragDrop(window);
        try { Reset(); _leave(); }
        finally
        {
            if (_image is not null) { Marshal.ReleaseComObject(_image); _image = null; }
            if (_oleInitialized) { _oleInitialized = false; OleUninitialize(); }
        }
    }

    private void ReportFailure(Exception error)
    {
        // A logging failure must never escape an unmanaged OLE callback.
        try { _failure(error); } catch { }
    }

    [ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTargetHelper
    {
        [PreserveSig] public int DragEnter(nint window, nint data, ref DropScreenPoint point, uint effect);
        [PreserveSig] public int DragLeave();
        [PreserveSig] public int DragOver(ref DropScreenPoint point, uint effect);
        [PreserveSig] public int Drop(nint data, ref DropScreenPoint point, uint effect);
        [PreserveSig] public int Show([MarshalAs(UnmanagedType.Bool)] bool show);
    }

    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern int RegisterDragDrop(nint window, [MarshalAs(UnmanagedType.Interface)] INativeFolderDropTarget target);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(nint window);
}

[StructLayout(LayoutKind.Sequential)]
public struct DropScreenPoint { public int X, Y; }

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface INativeFolderDropTarget
{
    [PreserveSig] public int DragEnter(nint data, uint keys, DropScreenPoint point, ref uint effect);
    [PreserveSig] public int DragOver(uint keys, DropScreenPoint point, ref uint effect);
    [PreserveSig] public int DragLeave();
    [PreserveSig] public int Drop(nint data, uint keys, DropScreenPoint point, ref uint effect);
}
