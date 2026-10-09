using System.Diagnostics;
using FilesMate.App.Models;
using FilesMate.Search;
using System.Windows.Threading;

namespace FilesMate.SearchHost;

internal sealed partial class SearchHost
{
    private AppearanceSettings? _appearancePreview;
    private readonly AppearancePreviewLease _appearanceLeaseState = new(IsPreviewOwnerAlive);
    private readonly DispatcherTimer _appearancePreviewLease = new() { Interval = TimeSpan.FromMilliseconds(AppearancePreviewLease.LifetimeMilliseconds) };

    internal AppearanceSettings EffectiveAppearance
    {
        get
        {
            var committed = PaletteAppearance.Load(Profile);
            return _appearancePreview is { } preview ? AppearancePreviewCodec.Overlay(committed, preview) : committed;
        }
    }

    private void InitializeAppearancePreview()
    {
        _appearancePreviewLease.Tick += (_, _) =>
        {
            _appearancePreviewLease.Stop();
            if (_appearanceLeaseState.TryExpire(Environment.TickCount64)) ClearAppearancePreview();
            else if (_appearanceLeaseState.ActiveOwner is not null) RenewAppearanceExpiry();
        };
    }

    private SearchHostReply ApplyAppearancePreview(SearchHostRequest request, uint pipeClientPid)
    {
        if (_appearanceLeaseState.TryExpire(Environment.TickCount64)) ClearAppearancePreview();
        var owner = new AppearancePreviewOwner(request.PreviewOwnerPid, request.PreviewOwnerStartedUtcTicks);
        if (pipeClientPid != (uint)owner.Pid || !IsPreviewOwnerAlive(owner)
            || !IsPreviewManager(owner.Pid) || !Guid.TryParseExact(request.PreviewSessionId, "N", out var session))
            return Reply(false, "Invalid appearance preview owner.", "InvalidAppearancePreview");
        AppearanceSettings? settings = null;
        if (request.AppearancePreview is { } preview && (!AppearancePreviewCodec.TryDecode(preview, out settings) || settings is null))
            return Reply(false, "Invalid appearance preview.", "InvalidAppearancePreview");
        var decision = _appearanceLeaseState.Receive(owner, session, request.PreviewEpoch, request.PreviewOperation!,
            request.AppearancePreview is null ? null : AppearancePreviewProtocol.Fingerprint(request.AppearancePreview), Environment.TickCount64);
        if (decision.Action == AppearancePreviewLeaseAction.Reject)
            return Reply(false, "Appearance preview rejected.", decision.ErrorCode);
        if (decision.Action == AppearancePreviewLeaseAction.Heartbeat)
        {
            RenewAppearanceExpiry(); // No palette load, resource rebuild, tray refresh or measure on a heartbeat.
            return Reply();
        }
        if (decision.Action == AppearancePreviewLeaseAction.Acquire)
        {
            if (_appearancePreview is not null) ClearAppearancePreview();
            RenewAppearanceExpiry();
            return Reply();
        }
        if (decision.Action == AppearancePreviewLeaseAction.Clear) ClearAppearancePreview();
        if (decision.Action != AppearancePreviewLeaseAction.Apply) return Reply();
        var existing = PaletteAppearance.Load(Profile);
        var next = AppearancePreviewCodec.Overlay(existing, settings!);
        var unchanged = _appearancePreview is not null && AppearancePreviewCodec.VisuallyEquivalent(_appearancePreview, next);
        _appearancePreview = next;
        RenewAppearanceExpiry();
        if (unchanged) return Reply();
        _window?.ApplyAppearancePreview(_appearancePreview);
        if (_tray.ContextMenuStrip is TrayMenu menu && menu.Visible) menu.RefreshAppearance();
        return Reply();
    }

    private void RenewAppearanceExpiry()
    {
        _appearancePreviewLease.Stop();
        _appearancePreviewLease.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _appearanceLeaseState.ExpiresAt - Environment.TickCount64));
        _appearancePreviewLease.Start();
    }

    private static bool IsPreviewOwnerAlive(AppearancePreviewOwner owner)
    {
        try
        {
            using var process = Process.GetProcessById(owner.Pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == owner.StartedUtcTicks;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private bool IsPreviewManager(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return string.Equals(process.MainModule?.FileName, ManagerPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private void ClearAppearancePreview()
    {
        _appearancePreview = null;
        _appearancePreviewLease.Stop();
        _window?.ClearAppearancePreview();
        if (_tray.ContextMenuStrip is TrayMenu menu && menu.Visible) menu.RefreshAppearance();
    }
}
