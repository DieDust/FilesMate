using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace FilesMate.App.Preview;

/// <summary>A single disposable native preview. No Office DLL is loaded into the UI process.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class NativeOfficePreview : IDisposable
{
    private Process? _process;
    private CancellationTokenRegistration _cancellation;
    private int _disposed;
    private (int, int, int, int, bool)? _bounds;
    private readonly Channel<string> _commands = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest });
    private Task? _commandPump;
    private byte _opacity = 255;
    public nint Window { get; private set; }
    public int ProcessId => _process?.Id ?? 0;
    public bool IsAlive => _disposed == 0 && _process is { HasExited: false };
    public event Action? CloseRequested;
    public event Action? Failed;
#if FILESMATE_UI_TEST
    internal void SimulateStallForTest() => _commands.Writer.TryWrite("TEST_STALL");
    internal bool ToolbarProbeTimedOut { get; private set; }
#endif

    public bool OwnsWindow(nint window)
    {
        if (Window == 0 || window == 0 || _disposed != 0) return false;
        for (var i = 0; window != 0 && i < 16; i++, window = GetWindow(window, 4))
            if (window == Window || IsChild(Window, window)) return true;
        return false;
    }

    public static bool CanHandle(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".rtf";

    public static Guid[] FindHandlers(string path)
    {
        if (!CanHandle(path)) return [];
        const string previewInterface = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";
        var extension = Path.GetExtension(path);
        // Query the user's effective default for this extension on every open:
        // DOCX and XLSX may intentionally use different suites.
        var progId = QueryAssociation(extension, 20);
        var preferred = OfficePreviewHandlerPolicy.DetectApplication(progId,
            QueryAssociation(extension, 2, "open"), QueryAssociation(extension, 4));
        Guid? associated = Guid.TryParse(QueryAssociation(extension, 16, previewInterface), out var associationId) ? associationId : null;
        Guid? progIdHandler = !string.IsNullOrEmpty(progId) && Guid.TryParse(QueryAssociation(progId, 16, previewInterface), out var progIdClass) ? progIdClass : null;
        var result = new Dictionary<Guid, string>();
        if (progIdHandler is { } direct) result[direct] = "";
        if (associated is { } fallback) result[fallback] = "";
        // File-open defaults and Shell preview associations can belong to different
        // suites. Collect matching installed providers before applying preference.
        var names = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xls" or ".xlsx" => new[] { "Excel", "WPS表格" },
            ".ppt" or ".pptx" => new[] { "PowerPoint", "WPS演示" },
            _ => new[] { "Word", "WPS文字" },
        };
        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryHive.LocalMachine })
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
        {
            using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers");
            if (key is null) continue;
            foreach (var name in key.GetValueNames())
                if (key.GetValue(name) is string label && Guid.TryParse(name, out var id)
                    && (result.ContainsKey(id) || names.Any(n => label.Contains(n, StringComparison.OrdinalIgnoreCase))))
                    if (!result.TryGetValue(id, out var existing) || string.IsNullOrEmpty(existing)) result[id] = label;
        }
        return OfficePreviewHandlerPolicy.Order(preferred, result.Select(p => new OfficePreviewHandlerCandidate(p.Key, p.Value)), progIdHandler, associated);
    }

    private static string? QueryAssociation(string association, uint kind, string? extra = null)
    {
        var buffer = new StringBuilder(1024);
        uint length = (uint)buffer.Capacity;
        var hr = AssocQueryString(0, kind, association, extra, buffer, ref length);
        if (hr != 0 && length > buffer.Capacity && length <= 32768)
        {
            buffer = new StringBuilder((int)length);
            hr = AssocQueryString(0, kind, association, extra, buffer, ref length);
        }
        return hr == 0 ? buffer.ToString() : null;
    }

    public static async Task<NativeOfficePreview?> TryOpenAsync(string path, nint parent, CancellationToken token)
    {
        if (parent == 0 || !CanHandle(path)) return null;
        var session = new NativeOfficePreview();
        try
        {
            token.ThrowIfCancellationRequested();
            var handlers = FindHandlers(path);
            if (handlers.Length == 0) return null;
            var exe = Path.Combine(AppContext.BaseDirectory, "SearchHost", "FilesMate.SearchHost.exe");
            if (!File.Exists(exe)) exe = Path.Combine(AppContext.BaseDirectory, "FilesMate.SearchHost.exe");
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "--native-office-preview", Path.GetFullPath(path), parent.ToString(), Environment.ProcessId.ToString(), string.Join(",", handlers) }) start.ArgumentList.Add(arg);
            var process = Process.Start(start) ?? throw new IOException("Native preview process did not start.");
            session._process = process;
            session._commandPump = session.SendCommandsAsync(process);
            session._cancellation = token.Register(session.Dispose);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var line = await process.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            if (line is null || !line.StartsWith("READY ", StringComparison.Ordinal) || !long.TryParse(line.AsSpan(6), out var handle))
            { session.Dispose(); return null; }
            session.Window = (nint)handle;
            GetWindowThreadProcessId(session.Window, out var pid);
            if (pid != process.Id || !session.IsAlive) { session.Dispose(); return null; }
            token.ThrowIfCancellationRequested();
            _ = session.ObserveAsync();
            return session;
        }
        catch (OperationCanceledException) { session.Dispose(); token.ThrowIfCancellationRequested(); return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or System.Security.SecurityException)
        { session.Dispose(); return null; }
    }

    private async Task ObserveAsync()
    {
        try
        {
            while (_process is { } process && await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
#if FILESMATE_UI_TEST
                if (line == "CHROME_TIMEOUT") ToolbarProbeTimedOut = true;
#endif
                if (line == "CLOSE" && _disposed == 0) CloseRequested?.Invoke();
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
        if (_disposed == 0) Failed?.Invoke();
    }

    public void SetBounds(int x, int y, int width, int height, bool visible)
    {
        visible &= width > 0 && height > 0 && _disposed == 0;
        var bounds = (x, y, width, height, visible);
        if (_bounds == bounds || Window == 0) return;
        _bounds = bounds;
        SendBounds();
    }

    public void SetOpacity(byte opacity)
    {
        if (_opacity == opacity) return;
        _opacity = opacity;
        SendBounds();
    }

    private void SendBounds()
    {
        if (_disposed != 0 || _bounds is not { } bounds) return;
        _commands.Writer.TryWrite(FormattableString.Invariant($"BOUNDS {bounds.Item1} {bounds.Item2} {Math.Max(1, bounds.Item3)} {Math.Max(1, bounds.Item4)} {(bounds.Item5 ? 1 : 0)} {_opacity}"));
    }

    private async Task SendCommandsAsync(Process process)
    {
        try
        {
            // Coalesce layout updates; a stalled provider must never block the UI.
            await foreach (var command in _commands.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            await process.StandardInput.WriteLineAsync("close").ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        { if (_disposed == 0) Failed?.Invoke(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancellation.Unregister();
        _commands.Writer.TryComplete();
        var process = _process;
        _process = null;
        if (Window != 0) SetWindowPos(Window, 0, 0, 0, 0, 0, 0x4000 | 0x13 | 0x80);
        if (process is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(1500);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException) { }
            finally
            {
                try { if (!process.HasExited) process.Kill(); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                process.Dispose();
            }
        });
    }

    [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int AssocQueryString(uint flags, uint str, string assoc, string? extra, StringBuilder output, ref uint length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
