using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Forms = System.Windows.Forms;

namespace FilesMate.SearchHost;

/// <summary>Disposable STA host for installed Shell preview handlers, never an Office automation session.</summary>
internal static class NativeOfficePreviewWorker
{
    public static int Run(string path, nint parent, int ownerPid, string handlers)
    {
        if (!File.Exists(path) || !IsWindow(parent)) return 1;
        GetWindowThreadProcessId(parent, out var actualPid);
        if (actualPid != ownerPid) return 1;
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        // Establish COM security before the first previewer is activated.
        CoInitializeSecurity(0, -1, 0, 0, 0, 3, 0, 0, 0);
        using var host = new PreviewHost(parent);
        _ = host.Handle;
        // Show(owner) is essential: Show() replaces CreateParams.Parent with
        // WinForms' hidden owner for taskbar-hidden forms. Such a popup falls
        // behind the card as soon as the user activates/resizes the card.
        host.Location = new System.Drawing.Point(-10000, -10000);
        host.Show(new PreviewOwner(GetAncestor(parent, 2)));
        host.StartWatchdog();
        _ = Task.Run(async () =>
        {
            // Bounds are relative to the UI host. Only this STA thread touches Office.
            try
            {
                while (await Console.In.ReadLineAsync() is { } line && line != "close")
                {
#if FILESMATE_UI_TEST
                    if (line == "TEST_STALL")
                    {
                        host.BeginInvoke(new Action(() => Thread.Sleep(Timeout.Infinite)));
                        continue;
                    }
#endif
                    var parts = line.Split(' ');
                    if (parts.Length != 7 || parts[0] != "BOUNDS") continue;
                    var values = new int[6];
                    if (!Enumerable.Range(0, 6).All(i => int.TryParse(parts[i + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out values[i]))) continue;
                    host.BeginInvoke(new Action(() => host.SetPreviewBounds(values)));
                }
            }
            catch (Exception error) when (error is IOException or InvalidOperationException) { }
            try { host.BeginInvoke(new Action(host.Close)); } catch (InvalidOperationException) { }
        });
        _ = Task.Run(async () =>
        {
            try { using var owner = Process.GetProcessById(ownerPid); await owner.WaitForExitAsync(); }
            catch (ArgumentException) { }
            Environment.Exit(0);
        });
        host.BeginInvoke(new Action(() =>
        {
            foreach (var value in handlers.Split(','))
            {
                if (!Guid.TryParse(value, out var id)) continue;
                if (host.LoadPreview(id, path))
                {
                    Console.WriteLine("READY " + host.Handle);
                    Console.Out.Flush();
                    return;
                }
            }
            Console.WriteLine("FAILED");
            Console.Out.Flush();
            host.Close();
        }));
        Forms.Application.Run();
        return host.Succeeded ? 0 : 1;
    }

    private sealed record PreviewOwner(nint Handle) : Forms.IWin32Window;

    private sealed class PreviewHost : Forms.Form
    {
        private readonly nint _parent;
        private readonly nint _owner;
        private readonly WinEventCallback _windowEvents;
        private nint _objectHook, _minimizeHook;
        private Rect _bounds;
        private bool _requestedVisible;
        private byte _opacity = 255;
        private (int X, int Y, int Width, int Height, bool Visible)? _placement;
        private object? _component;
        private IPreviewHandler? _preview;
        private IStream? _stream;
        private object? _item;
        private readonly PreviewSite _site = new();
        private readonly Forms.Timer _chromeDiscovery = new() { Interval = 250 };
        private readonly Forms.Timer _heartbeat = new() { Interval = 250 };
        private System.Threading.Timer? _watchdog;
        private long _lastHeartbeat = Environment.TickCount64;
        private int _ready;
        private int _previewGeneration;
        private bool _discoveryInFlight;
        private int _chromeAttempts;
        private nint _wpsToolbar;
        private int _topInset;
        public bool Succeeded { get; private set; }
        public PreviewHost(nint parent)
        {
            _parent = parent;
            _owner = GetAncestor(parent, 2);
            _windowEvents = OnOwnerWindowEvent;
            TopLevel = true;
            FormBorderStyle = Forms.FormBorderStyle.None;
            ShowInTaskbar = false;
            Size = new System.Drawing.Size(640, 480);
            BackColor = System.Drawing.Color.White;
            _chromeDiscovery.Tick += async (_, _) => await DiscoverWpsToolbarAsync();
            _heartbeat.Tick += (_, _) => Volatile.Write(ref _lastHeartbeat, Environment.TickCount64);
            FormClosed += (_, _) => { Unload(); Forms.Application.ExitThread(); };
        }
        protected override bool ShowWithoutActivation => true;
        public void StartWatchdog()
        {
            _heartbeat.Start();
            // An owned cross-process HWND shares input queues with its owner.
            // Monitor independently of BOTH window dispatchers, so a wedged COM
            // handler cannot leave the file manager waiting indefinitely.
            _watchdog = new System.Threading.Timer(_ =>
            {
                var budget = Volatile.Read(ref _ready) == 0 ? 15000 : 4000;
                if (Environment.TickCount64 - Volatile.Read(ref _lastHeartbeat) > budget)
                    Environment.Exit(70);
            }, null, 500, 500);
        }
        protected override Forms.CreateParams CreateParams
        {
            // A child HWND underneath WinUI's DirectComposition surface can be
            // alive and visible while completely covered. An owned popup has its
            // own composition surface and receives native pointer/keyboard input.
            get { var p = base.CreateParams; p.Parent = _owner; p.Style = unchecked((int)0x80000000) | 0x02000000 | 0x04000000; p.ExStyle |= 0x80; return p; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            GetWindowThreadProcessId(_owner, out var pid);
            _objectHook = SetWinEventHook(0x8002, 0x800B, 0, _windowEvents, pid, 0, 0);
            _minimizeHook = SetWinEventHook(0x16, 0x17, 0, _windowEvents, pid, 0, 0);
        }
        private void OnOwnerWindowEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
        {
            if (objectId != 0 || (window != _parent && window != _owner) || IsDisposed) return;
            SynchronizePlacement();
        }
        public void SetPreviewBounds(int[] values)
        {
            _bounds = new Rect(values[0], values[1], values[0] + values[2], values[1] + values[3]);
            _requestedVisible = values[4] != 0;
            var opacity = (byte)Math.Clamp(values[5], 0, 255);
            if (opacity != _opacity)
            {
                _opacity = opacity;
                var style = GetWindowLongPtrW(Handle, -20);
                SetWindowLongPtrW(Handle, -20, style | 0x80000);
                SetLayeredWindowAttributes(Handle, 0, opacity, 2);
            }
            SynchronizePlacement();
        }
        private void SynchronizePlacement()
        {
            if (IsDisposed || !IsHandleCreated) return;
            var point = new Point { X = _bounds.Left, Y = _bounds.Top };
            var visible = _requestedVisible && IsWindowVisible(_parent) && IsWindowVisible(_owner) && !IsIconic(_owner) && ClientToScreen(_parent, ref point);
            var width = Math.Max(1, _bounds.Right - _bounds.Left);
            var height = Math.Max(1, _bounds.Bottom - _bounds.Top);
            var placement = (point.X, point.Y, width, height, visible);
            if (_placement == placement) return;
            _placement = placement;
            // Keep normal owner z-order: never make the Office surface topmost.
            SetWindowPos(Handle, 0, point.X, point.Y, width, height, 0x10u | 0x4u | (visible ? 0x40u : 0x80u));
            if (visible) RedrawWindow(Handle, 0, 0, 0x85); // invalidate the provider and its children after restoring
        }
        public bool LoadPreview(Guid id, string path)
        {
            try
            {
                var iid = new Guid("00000000-0000-0000-C000-000000000046");
                // Prefer the registered surrogate. DLL-only handlers remain isolated in this disposable worker.
                var hr = CoCreateInstance(ref id, 0, 4, ref iid, out _component);
                if (hr < 0) Marshal.ThrowExceptionForHR(CoCreateInstance(ref id, 0, 1, ref iid, out _component));
                _preview = (IPreviewHandler)_component!;
                if (_component is IObjectWithSite site) site.SetSite(_site);
                var initialized = false;
                if (_component is IInitializeWithStream streamInit)
                {
                    try
                    {
                        Marshal.ThrowExceptionForHR(SHCreateStreamOnFileEx(path, 0x40, 0, false, null, out _stream));
                        streamInit.Initialize(_stream, 0);
                        initialized = true;
                    }
                    catch (COMException) { if (_stream is not null) { Marshal.FinalReleaseComObject(_stream); _stream = null; } }
                }
                if (!initialized && _component is IInitializeWithItem itemInit)
                {
                    try
                    {
                        var itemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref itemId, out _item));
                        itemInit.Initialize(_item!, 0);
                        initialized = true;
                    }
                    catch (COMException) { if (_item is not null) { Marshal.FinalReleaseComObject(_item); _item = null; } }
                }
                if (!initialized && _component is IInitializeWithFile fileInit) { fileInit.Initialize(path, 0); initialized = true; }
                if (!initialized) throw new NotSupportedException("Preview handler has no read-only initializer.");
                var rect = new Rect(0, 0, ClientSize.Width, ClientSize.Height);
                _preview.SetWindow(Handle, ref rect);
                _preview.DoPreview();
                Volatile.Write(ref _lastHeartbeat, Environment.TickCount64);
                Volatile.Write(ref _ready, 1);
                _chromeAttempts = 0;
                _chromeDiscovery.Start();
                Succeeded = true;
                return true;
            }
            catch (Exception error) when (error is COMException or InvalidCastException or NotSupportedException or IOException or UnauthorizedAccessException)
            { Trace.WriteLine(error); Unload(); return false; }
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!IsHandleCreated || _preview is null) return;
            BeginInvoke(new Action(() =>
            {
                try { UpdatePreviewRect(); }
                catch (COMException) { Close(); }
            }));
        }

        private void UpdatePreviewRect()
        {
            if (_wpsToolbar != 0 && GetWindowRect(_wpsToolbar, out var toolbar))
                _topInset = Math.Max(0, toolbar.Bottom - toolbar.Top);
            // Expand upward only by a positively identified WPS command strip.
            // The host clips that strip; the document and bottom navigation keep
            // the full viewport. Unknown providers/layouts are never cropped.
            var rect = new Rect(0, -_topInset, ClientSize.Width, ClientSize.Height);
            _preview?.SetRect(ref rect);
        }

        private async Task DiscoverWpsToolbarAsync()
        {
            if (_discoveryInFlight) return;
            if (++_chromeAttempts >= 24 || _preview is null) _chromeDiscovery.Stop();
            if (_preview is null || _wpsToolbar != 0) return;
            var candidates = new List<(nint Window, int Height)>();
            GetWindowRect(Handle, out var host);
            EnumChildWindows(Handle, (child, _) =>
            {
                if (!IsWindowVisible(child) || !GetWindowRect(child, out var rect)) return true;
                if (Math.Abs(rect.Left - host.Left) > 2 || Math.Abs(rect.Top - host.Top) > 2
                    || Math.Abs((rect.Right - rect.Left) - ClientSize.Width) > 2) return true;
                var windowClass = new System.Text.StringBuilder(128);
                GetClassName(child, windowClass, windowClass.Capacity);
                if (!windowClass.ToString().StartsWith("Qt", StringComparison.Ordinal)) return true;
                candidates.Add((child, rect.Bottom - rect.Top));
                return candidates.Count < 64;
            }, 0);
            if (candidates.Count == 0) return;
            _discoveryInFlight = true;
            var generation = _previewGeneration;
            var maximumHeight = DeviceDpi;
            try
            {
                // UI Automation must run on a windowless MTA thread. Never wait
                // synchronously on the handler's STA, including FromHandle.
                var toolbar = await Task.Run(() => FindWpsToolbar(candidates, maximumHeight))
                    .WaitAsync(TimeSpan.FromMilliseconds(1200));
                if (IsDisposed || generation != _previewGeneration || _preview is null || toolbar == 0) return;
                _wpsToolbar = toolbar;
                _chromeDiscovery.Stop();
                UpdatePreviewRect();
            }
            catch (TimeoutException)
            {
                // UIA calls cannot be forcibly cancelled. Stop probing for this
                // session; at most one background call remains until worker exit.
                _chromeDiscovery.Stop();
#if FILESMATE_UI_TEST
                Console.WriteLine("CHROME_TIMEOUT");
                Console.Out.Flush();
#endif
            }
            catch (COMException) { Close(); }
            finally { _discoveryInFlight = false; }
        }

        private static nint FindWpsToolbar(List<(nint Window, int Height)> candidates, int maximumHeight)
        {
#if FILESMATE_UI_TEST
            if (Environment.GetEnvironmentVariable("FILESMATE_TEST_UIA_STALL") == "1") Thread.Sleep(Timeout.Infinite);
#endif
            var isWps = false;
            var bars = new List<nint>();
            foreach (var (window, height) in candidates)
            {
                try
                {
                    var name = System.Windows.Automation.AutomationElement.FromHandle(window)?.Current.ClassName;
                    if (name == "KAxPreviewMainWidget") isWps = true;
                    if (name == "KxHintWidget" && height > 0 && height <= maximumHeight) bars.Add(window);
                }
                catch (Exception error) when (error is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException or COMException) { }
            }
            return isWps && bars.Count == 1 ? bars[0] : 0;
        }
        protected override bool ProcessCmdKey(ref Forms.Message msg, Forms.Keys keyData)
        {
            if (keyData == Forms.Keys.Escape) { PreviewSite.RequestClose(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void Unload()
        {
            _previewGeneration++;
            _chromeDiscovery.Stop();
            _wpsToolbar = 0;
            _topInset = 0;
            try { _preview?.Unload(); } catch (COMException) { }
            try { if (_component is IObjectWithSite site) site.SetSite(null); } catch (COMException) { }
            _preview = null;
            if (_component is not null) { Marshal.FinalReleaseComObject(_component); _component = null; }
            if (_stream is not null) { Marshal.FinalReleaseComObject(_stream); _stream = null; }
            if (_item is not null) { Marshal.FinalReleaseComObject(_item); _item = null; }
        }
        protected override void Dispose(bool disposing)
        {
            if (_objectHook != 0) { UnhookWinEvent(_objectHook); _objectHook = 0; }
            if (_minimizeHook != 0) { UnhookWinEvent(_minimizeHook); _minimizeHook = 0; }
            if (disposing) Unload();
            if (disposing) _chromeDiscovery.Dispose();
            if (disposing) { _heartbeat.Dispose(); _watchdog?.Dispose(); }
            base.Dispose(disposing);
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class PreviewSite : IPreviewHandlerFrame
    {
        public int GetWindowContext(out FrameInfo info) { info = default; return 0; }
        public int TranslateAccelerator(ref NativeMessage message)
        {
            if (message.Message == 0x100 && message.WParam == 0x1B) { RequestClose(); return 0; }
            return 1;
        }
        internal static void RequestClose() { Console.WriteLine("CLOSE"); Console.Out.Flush(); }
    }
    [StructLayout(LayoutKind.Sequential)] public struct FrameInfo { public nint Accelerators; public uint Count; }
    [StructLayout(LayoutKind.Sequential)] public struct NativeMessage { public nint Window; public uint Message; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect(int left, int top, int right, int bottom) { public int Left = left, Top = top, Right = right, Bottom = bottom; }
    [ComImport, Guid("8895B1C6-B41F-4C1C-A562-0D564250836F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPreviewHandler
    {
        public void SetWindow(nint parent, ref Rect rect); public void SetRect(ref Rect rect); public void DoPreview(); public void Unload(); public void SetFocus(); public void QueryFocus(out nint window); [PreserveSig] public int TranslateAccelerator(ref NativeMessage message);
    }
    [ComImport, Guid("B824B49D-22AC-4161-AC8A-9916E8FA3F7F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithStream { public void Initialize(IStream stream, uint mode); }
    [ComImport, Guid("B7D14566-0509-4CCE-A71F-0A554233BD9B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithFile { public void Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode); }
    [ComImport, Guid("7F73BE3F-FB79-493C-A6C7-7EE14E245841"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithItem { public void Initialize([MarshalAs(UnmanagedType.IUnknown)] object item, uint mode); }
    [ComImport, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectWithSite { public void SetSite([MarshalAs(UnmanagedType.IUnknown)] object? site); public void GetSite(ref Guid iid, out nint site); }
    [ComImport, Guid("FEC87AAF-35F9-447A-ADB7-20234491401A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPreviewHandlerFrame { [PreserveSig] public int GetWindowContext(out FrameInfo info); [PreserveSig] public int TranslateAccelerator(ref NativeMessage message); }
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object? component);
    [DllImport("ole32.dll")] private static extern int CoInitializeSecurity(nint descriptor, int count, nint services, nint reserved, uint authentication, uint impersonation, nint authenticationList, uint capabilities, nint reserved2);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object? item);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateStreamOnFileEx(string path, uint mode, uint attributes, [MarshalAs(UnmanagedType.Bool)] bool create, IStream? template, out IStream stream);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    private delegate void WinEventCallback(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(nint window, nint rect, nint region, uint flags);
    private delegate bool EnumWindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint window, EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, System.Text.StringBuilder name, int length);
}
