using System.Diagnostics;
using System.Runtime.InteropServices;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Search;
using Microsoft.UI.Dispatching;

namespace FilesMate.App;

public partial class App
{
    private DispatcherQueueTimer? _skinPreviewRelayTimer;
    private readonly AppearancePreviewRelay _skinPreviewRelay = new(new(Environment.ProcessId, OwnPreviewStartTime()));
    private bool _skinPreviewRelayPending;
    private bool _skinPreviewRelaySending;
    private bool _skinPreviewRelayEnded;
    private int _skinPreviewHostPid;
    private long _skinPreviewHostStartedTicks;

    private static long OwnPreviewStartTime()
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime().Ticks;
    }

    private void QueueSearchAppearancePreview(AppearanceSettings? settings)
    {
        if (_skinPreviewRelayEnded || AppearanceViewModel is not { } viewModel || viewModel.PreviewSessionId == Guid.Empty) return;
        _skinPreviewRelay.Publish(viewModel.PreviewSessionId, viewModel.HasAppearancePreview,
            settings is null ? null : AppearancePreviewCodec.Encode(settings));
        _skinPreviewRelayPending = true;
        if (_skinPreviewRelayTimer is null && CurrentWindow is { } window)
        {
            _skinPreviewRelayTimer = window.DispatcherQueue.CreateTimer();
            _skinPreviewRelayTimer.IsRepeating = false;
            _skinPreviewRelayTimer.Tick += SkinPreviewRelayTick;
        }
        if (!_skinPreviewRelaySending) SchedulePreviewRelay(150);
    }

    private void SchedulePreviewRelay(int milliseconds)
    {
        if (_skinPreviewRelayEnded || _skinPreviewRelayTimer is null) return;
        _skinPreviewRelayTimer.Stop();
        _skinPreviewRelayTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _skinPreviewRelayTimer.Start();
    }

    private async void SkinPreviewRelayTick(DispatcherQueueTimer sender, object args)
    {
        if (_skinPreviewRelayEnded || _skinPreviewRelaySending || _skinPreviewRelay.Current is not { } request) return;
        _skinPreviewRelaySending = true;
        _skinPreviewRelayPending = false;
        try
        {
            var profile = Path.GetDirectoryName(Program.SettingsPath(AppearanceSettingsService.DefaultFilePath));
            if (SearchHostClient.IsRunning(profile))
            {
                var reply = await SearchHostClient.SendAsync(request, profile);
                if (!_skinPreviewRelayEnded && reply is { Pid: > 0 }) CachePreviewObserver(reply);
                if (!_skinPreviewRelayEnded && reply?.ErrorCode == "AppearancePreviewExpired"
                    && _skinPreviewRelay.RenewExpired(request))
                {
                    _skinPreviewRelayPending = true;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { Trace.TraceError("Search skin preview failed: {0}", error.GetType().Name); }
        finally
        {
            _skinPreviewRelaySending = false;
            if (!_skinPreviewRelayEnded)
            {
                if (_skinPreviewRelayPending) SchedulePreviewRelay(150);
                else if (_skinPreviewRelay.NeedsHeartbeat) SchedulePreviewRelay(3000);
            }
        }
    }

    private void CachePreviewObserver(SearchHostReply reply)
    {
        try
        {
            if (reply.VerifiedServerStartedUtcTicks <= 0) return;
            using var process = Process.GetProcessById(reply.Pid);
            var expected = Path.Combine(AppContext.BaseDirectory, "SearchHost", "FilesMate.SearchHost.exe");
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != reply.VerifiedServerStartedUtcTicks
                || !string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) return;
            _skinPreviewHostStartedTicks = reply.VerifiedServerStartedUtcTicks;
            _skinPreviewHostPid = reply.Pid; // PID was returned by this exact profile's current-user pipe.
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    internal static bool IsAppearancePreviewObserverWindow(nint hwnd)
    {
        if (hwnd == 0 || Current is not App app) return false;
        PreviewWindowProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return true;
        if (pid == 0 || pid != app._skinPreviewHostPid) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == app._skinPreviewHostStartedTicks
                && string.Equals(process.MainModule?.FileName, Path.Combine(AppContext.BaseDirectory,
                    "SearchHost", "FilesMate.SearchHost.exe"), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    internal static async Task<bool> IsAppearancePreviewObserverWindowAsync(nint hwnd)
    {
        if (IsAppearancePreviewObserverWindow(hwnd)) return true;
        if (hwnd == 0 || Current is not App app || app._skinPreviewRelayEnded) return false;
        PreviewWindowProcessId(hwnd, out var pid);
        if (pid == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (process.HasExited || !string.Equals(process.MainModule?.FileName,
                Path.Combine(AppContext.BaseDirectory, "SearchHost", "FilesMate.SearchHost.exe"),
                StringComparison.OrdinalIgnoreCase)) return false;
            var started = process.StartTime.ToUniversalTime().Ticks;
            var profile = Path.GetDirectoryName(Program.SettingsPath(AppearanceSettingsService.DefaultFilePath));
            var reply = await SearchHostClient.StatusAsync(profile);
            if (app._skinPreviewRelayEnded || reply is not { Ok: true } || reply.Pid != pid
                || reply.VerifiedServerStartedUtcTicks != started
                || process.HasExited || process.StartTime.ToUniversalTime().Ticks != started) return false;
            app.CachePreviewObserver(reply);
            return IsAppearancePreviewObserverWindow(hwnd);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { return false; }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint PreviewWindowProcessId(nint hwnd, out uint pid);

    private void EndSearchAppearancePreview()
    {
        _skinPreviewRelayEnded = true;
        if (AppearanceViewModel is { } viewModel) viewModel.PreviewChanged -= QueueSearchAppearancePreview;
        _skinPreviewRelayTimer?.Stop();
        if (_skinPreviewRelayTimer is not null) _skinPreviewRelayTimer.Tick -= SkinPreviewRelayTick;
        _skinPreviewRelayTimer = null;
        if (_skinPreviewRelay.Clear() is { } clear)
        {
            // Capture a newer clear now: an already-running old send cannot resurrect the draft.
            var profile = Path.GetDirectoryName(Program.SettingsPath(AppearanceSettingsService.DefaultFilePath));
            if (SearchHostClient.IsRunning(profile)) _ = SearchHostClient.SendAsync(clear, profile);
        }
        _skinPreviewHostPid = 0;
    }
}
