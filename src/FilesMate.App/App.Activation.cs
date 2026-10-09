using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Core.Lifetime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;

namespace FilesMate.App;

public partial class App
{
    private readonly DispatcherQueue _activationDispatcher = DispatcherQueue.GetForCurrentThread()
        ?? throw new InvalidOperationException("Application activation requires its UI dispatcher.");
    private readonly WindowActivationInbox<LaunchTarget> _activationInbox = new();
    private IDisposable? _shutdownReservation;

    internal void HandleRedirectedLaunch(LaunchTarget target)
    {
        switch (_activationInbox.Enqueue(target))
        {
            case WindowActivationDisposition.Dispatch:
                ScheduleActivationPump();
                break;
            case WindowActivationDisposition.Handoff:
                AppLifecycle.QueueRecoveryRequests([target]);
                break;
        }
    }

    private void CompleteActivationInitialization()
    {
        RequireActivationUiThread();
        if (_activationInbox.CompleteInitialization()) ScheduleActivationPump();
        foreach (var target in AppLifecycle.TakePendingRedirects()) HandleRedirectedLaunch(target);
    }

    private void ScheduleActivationPump()
    {
        if (_activationDispatcher.TryEnqueue(DrainRedirectedLaunches)) return;
        // A dead dispatcher cannot safely construct XAML or determine whether
        // active file work permits exit. Preserve requests for the next start.
        foreach (var request in _activationInbox.TakeRejectedPump()) AppLifecycle.PreserveRecoveryRequest(request.Target);
        AppLifecycle.TraceInternal("Activation dispatcher rejected request; payload retained for a later launch");
    }

    private void DrainRedirectedLaunches()
    {
        RequireActivationUiThread();
        try
        {
            while (_activationInbox.TryTake(out var request))
            {
                try
                {
                    var window = FindUsableActivationWindow();
                    if (window is null)
                    {
                        // Creation consumes this target once. Reentrant requests
                        // stay in the same inbox until the factory has returned.
                        CreateActivationWindow(request.Target);
                        continue;
                    }
                    var target = request.Target;
                    if (target.Folder is null && target.SelectPath is null && target.SettingsSection is null
                        && target.SearchAction is null && target.SearchPage is null && target.MissingPath is null
                        && !target.ActivateOnly && !request.RestoreOnly)
                    {
                        window.ToggleTaskbarVisibility();
                        continue;
                    }
                    if (!IsDuplicateExternalLaunch(target)) window.HandleLaunch(target);
                    ActivateExistingWindow(window);
                }
                catch (Exception error)
                {
                    AppLifecycle.PreserveRecoveryRequest(request.Target);
                    LogFailure("ActivationRecovery", error);
                }
            }
        }
        finally { if (_activationInbox.FinishPump()) ScheduleActivationPump(); }
    }

    private MainWindow? FindUsableActivationWindow()
    {
        RequireActivationUiThread();
        PruneUnavailableWindows();
        return CurrentWindow ?? (_windows.Count > 0 ? _windows[0] : null);
    }

    private void PruneUnavailableWindows()
    {
        RequireActivationUiThread();
        foreach (var window in _windows.ToArray())
        {
            if (window.CanReceiveActivation) continue;
            window.Closed -= Window_Closed;
            _windows.Remove(window);
            if (ReferenceEquals(CurrentWindow, window)) CurrentWindow = null;
            try { window.RetireUnavailableWindow(); }
            catch (Exception error) { LogFailure("RetireUnavailableWindow", error); }
        }
    }

    private MainWindow CreateActivationWindow(LaunchTarget target)
    {
        RequireActivationUiThread();
        if (_appearanceUiEnded || _activationInbox.Phase != WindowActivationPhase.Running)
            throw new InvalidOperationException("Closing application no longer owns a window factory.");
        var window = new MainWindow(new AppPageFactory(), target, hostTearOut: false);
        TrackWindow(window);
        IsDuplicateExternalLaunch(target);
        window.ApplyAppearance(AppearanceViewModel?.Effective ?? new AppearanceSettingsService(
            Program.SettingsPath(AppearanceSettingsService.DefaultFilePath)).Load());
        ActivateExistingWindow(window);
        ScheduleFeatureSetup(window);
        AppLifecycle.TraceInternal("Activation created a usable main window");
        return window;
    }

    private static void ActivateExistingWindow(MainWindow window)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized) presenter.Restore();
        window.Activate();
        AppLifecycle.TryBringToForeground(window.NativeHandle);
    }

    private bool ResumeAfterLastClose(bool keepUserWorkAlive)
    {
        RequireActivationUiThread();
        if (!_activationInbox.ResumeClosing(keepUserWorkAlive)) return false;
        _shutdownReservation?.Dispose();
        _shutdownReservation = null;
        if (_activationInbox.PendingCount == 0)
            CreateActivationWindow(new LaunchTarget(null, null, ActivateOnly: true));
        DrainRedirectedLaunches();
        AppLifecycle.TraceInternal("Last-window shutdown cancelled for activation or pending user work");
        return true;
    }

    private void RequireActivationUiThread()
    {
        if (!_activationDispatcher.HasThreadAccess)
            throw new InvalidOperationException("Only the application UI dispatcher may create or retire windows.");
    }
}
