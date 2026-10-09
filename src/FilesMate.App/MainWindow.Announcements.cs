using System.Runtime.InteropServices;
using FilesMate.App.Animations;
using FilesMate.App.Updates;
using FilesMate.App.Views;
using FilesMate.Core.Configuration;
using FilesMate.Core.Updates;
using FilesMate.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private sealed record Announcement(string Key, bool Highlights, bool Automatic, string Version,
        Func<Task>? Action, TaskCompletionSource Completion);
    private readonly List<Announcement> _announcements = [];
    private UpdateNoticeCard? _announcementCard;
    private Announcement? _announcementCurrent;
    private FrameworkElement? _announcementRoot;
    private Control? _announcementPreviousFocus;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _announcementTimer;
    private Storyboard? _announcementMotion;
    private readonly TranslateTransform _announcementTranslation = new();
    private bool _announcementClosing;
    private bool _announcementPausedForModal;
    private bool _announcementDeferredByModal;

    internal Task ShowReleaseHighlightsAsync(bool automatic)
    {
        var item = new Announcement("features:" + FeatureTourHistory.CurrentRelease, true, automatic,
            UpdateController.CurrentVersion.ToString(3), () =>
            {
                FeatureTourHistory.Load(RuntimeProfile.DirectoryPath).Completed(FeatureTourHistory.CurrentRelease).Save(RuntimeProfile.DirectoryPath);
                return Task.CompletedTask;
            }, new(TaskCreationOptions.RunContinuationsAsynchronously));
        return QueueAnnouncement(item);
    }

    private void QueueAvailableUpdate(UpdateRelease release) => QueueAnnouncement(new("update:" + release.Version,
        false, false, release.DisplayVersion, () => DownloadAndInstallUpdateAsync(release),
        new(TaskCreationOptions.RunContinuationsAsynchronously)));

    private Task QueueAnnouncement(Announcement item)
    {
        if (_windowClosed || Content is not Grid root) return Task.CompletedTask;
        if (_announcementCurrent?.Key == item.Key) return _announcementCurrent.Completion.Task;
        if (_announcements.FirstOrDefault(p => p.Key == item.Key) is { } queued) return queued.Completion.Task;
        EnsureAnnouncementSurface(root);
        _announcements.Add(item); TryShowAnnouncement();
        return item.Completion.Task;
    }

    private void EnsureAnnouncementSurface(Grid root)
    {
        if (_announcementCard is not null) return;
        _announcementRoot = root;
        _announcementCard = new UpdateNoticeCard { HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed,
            RenderTransform = _announcementTranslation };
        Grid.SetRowSpan(_announcementCard, Math.Max(1, root.RowDefinitions.Count));
        // SettingsOverlay and this surface are direct siblings in ShellRoot.
        // Stay just above that nonmodal layer; ContentDialog uses its own popup
        // layer and is handled by the suspension gate below.
        Canvas.SetZIndex(_announcementCard, Canvas.GetZIndex(SettingsOverlay) + 1);
        root.Children.Add(_announcementCard);
        _announcementCard.DismissRequested += async (_, _) => await CloseAnnouncementAsync(false, true);
        _announcementCard.UpdateRequested += async (_, _) => await CloseAnnouncementAsync(true, true);
        _announcementCard.SizeChanged += (_, _) => PositionAnnouncement();
        root.SizeChanged += (_, _) => PositionAnnouncement();
        AppWindow.Changed += (_, _) => PositionAnnouncement();
        root.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(AnnouncementKey), true);
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (_announcementCurrent is not null && !_announcementPausedForModal && !AnnouncementModalOpen()
                && !WithinAnnouncement(e.OriginalSource as DependencyObject))
                _ = CloseAnnouncementAsync(false, false);
        }), true);
        _announcementTimer = DispatcherQueue.CreateTimer(); _announcementTimer.Interval = TimeSpan.FromMilliseconds(250);
        _announcementTimer.Tick += (_, _) => TryShowAnnouncement(); _announcementTimer.Start();
        Closed += (_, _) =>
        {
            _announcementTimer.Stop(); _announcementMotion?.Stop();
            _announcementCurrent?.Completion.TrySetResult();
            foreach (var item in _announcements) item.Completion.TrySetResult(); _announcements.Clear();
        };
    }

    private void TryShowAnnouncement()
    {
        if (_windowClosed || _announcementCard is null || _announcementRoot?.XamlRoot is null || !_announcementRoot.IsLoaded) return;
        if (AnnouncementModalOpen())
        {
            if (_announcementCurrent is not null || _announcements.Count > 0) _announcementDeferredByModal = true;
            if (_announcementCurrent is not null && !_announcementClosing && !_announcementPausedForModal)
            {
                _announcementMotion?.Stop(); _announcementCard.Visibility = Visibility.Collapsed;
                _announcementCard.IsHitTestVisible = false; _announcementPausedForModal = true;
            }
            return;
        }
        if (_announcementPausedForModal && _announcementCurrent is not null)
        {
            _announcementPausedForModal = false; _announcementDeferredByModal = false;
            _announcementCard.Visibility = Visibility.Visible; _announcementCard.IsHitTestVisible = true;
            PositionAnnouncement(); AnimateAnnouncement(true, null);
            // Let the modal's own focus restoration stand when it closes.
            return;
        }
        if (_announcementCurrent is not null || _announcementClosing) return;
        while (_announcements.Count > 0)
        {
            var next = _announcements[0]; _announcements.RemoveAt(0);
            if (next.Highlights && next.Automatic && FeatureTourHistory.Load(RuntimeProfile.DirectoryPath).Decide(
                Program.ProfileWasNew, App.Features.Completed, FeatureTourHistory.CurrentRelease, FeatureTourHistory.CurrentFeatures) == FeatureTourOffer.None)
            { next.Completion.TrySetResult(); continue; }
            _announcementCurrent = next;
            var focusAnnouncement = !_announcementDeferredByModal;
            _announcementDeferredByModal = false;
            _announcementPreviousFocus = FocusManager.GetFocusedElement(_announcementRoot.XamlRoot) as Control;
            if (next.Highlights) _announcementCard.SetHighlights(next.Version); else _announcementCard.SetAvailable(next.Version);
            _announcementCard.Visibility = Visibility.Visible; _announcementCard.IsHitTestVisible = true;
            PositionAnnouncement(); AnimateAnnouncement(true, null);
            if (next.Highlights)
            {
                try { App.MarkFeatureTourOffered(false); }
                catch (Exception error) { App.LogFailure("Highlights offered", error); }
            }
            DispatcherQueue.TryEnqueue(() =>
            {
                if (focusAnnouncement && _announcementCurrent == next && !_announcementClosing && !AnnouncementModalOpen())
                    (next.Highlights ? _announcementCard.PrimaryControl : _announcementCard.CloseControl).Focus(FocusState.Programmatic);
            });
            break;
        }
    }

    private bool AnnouncementModalOpen()
    {
        if (App.FeatureSetupShowing || _updateDialogOpen || !AnnouncementWindowEnabled(NativeHandle)) return true;
        if (_announcementRoot?.XamlRoot is not { } xaml) return false;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xaml))
        {
            if (popup.Child is null) continue;
            var pending = new Stack<DependencyObject>(); pending.Push(popup.Child);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node is ContentDialog { IsLoaded: true, Visibility: Visibility.Visible }) return true;
                for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--)
                    pending.Push(VisualTreeHelper.GetChild(node, i));
            }
        }
        return false;
    }

    private bool WithinAnnouncement(DependencyObject? source)
    {
        for (var i = 0; source is not null && i < 40; i++, source = VisualTreeHelper.GetParent(source))
            if (source == _announcementCard) return true;
        return false;
    }

    private void AnnouncementKey(object sender, KeyRoutedEventArgs args)
    {
        if (_announcementCurrent is null || _announcementClosing || _announcementPausedForModal || AnnouncementModalOpen() || _announcementCard is null
            || !WithinAnnouncement(FocusManager.GetFocusedElement(_announcementRoot!.XamlRoot) as DependencyObject)) return;
        if (args.Key == VirtualKey.Escape) { args.Handled = true; _ = CloseAnnouncementAsync(false, true); }
        else if (args.Key == VirtualKey.Tab)
        {
            args.Handled = true;
            var focused = FocusManager.GetFocusedElement(_announcementRoot.XamlRoot);
            (ReferenceEquals(focused, _announcementCard.CloseControl) ? _announcementCard.PrimaryControl : _announcementCard.CloseControl).Focus(FocusState.Keyboard);
        }
        else if (args.Key == VirtualKey.Enter && _announcementCurrent.Highlights
            && !ReferenceEquals(FocusManager.GetFocusedElement(_announcementRoot.XamlRoot), _announcementCard.CloseControl))
        { args.Handled = true; _ = CloseAnnouncementAsync(true, true); }
    }

    private async Task CloseAnnouncementAsync(bool primary, bool restoreFocus)
    {
        if (_announcementCurrent is not { } current || _announcementClosing || _announcementCard is null) return;
        _announcementClosing = true; _announcementCard.IsHitTestVisible = false;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AnimateAnnouncement(false, () => closed.TrySetResult()); await closed.Task;
        _announcementCard.Visibility = Visibility.Collapsed; _announcementCurrent = null; _announcementClosing = false;
        try { if (primary && current.Action is not null) await current.Action(); }
        catch (Exception error) { App.LogFailure("Announcement action", error); }
        finally
        {
            current.Completion.TrySetResult();
            if (restoreFocus && _announcementPreviousFocus is { IsLoaded: true, IsEnabled: true } previous) previous.Focus(FocusState.Programmatic);
            _announcementPreviousFocus = null; TryShowAnnouncement();
        }
    }

    private void HideAnnouncementImmediately()
    {
        _announcementMotion?.Stop();
        if (_announcementCard is not null) _announcementCard.Visibility = Visibility.Collapsed;
        _announcementCurrent?.Completion.TrySetResult(); _announcementCurrent = null; _announcementClosing = false;
        _announcementPausedForModal = false;
    }

    private void AnimateAnnouncement(bool show, Action? completed)
    {
        if (_announcementCard is null) { completed?.Invoke(); return; }
        _announcementMotion?.Stop(); var duration = App.Motion.Resolve(MotionDurations.Fast);
        if (duration <= TimeSpan.Zero)
        { _announcementCard.Opacity = show ? 1 : 0; _announcementTranslation.Y = show ? 0 : 8; completed?.Invoke(); return; }
        var motion = _announcementMotion = new Storyboard();
        var fade = new DoubleAnimation { From = show ? 0 : 1, To = show ? 1 : 0, Duration = new Duration(duration) };
        Storyboard.SetTarget(fade, _announcementCard); Storyboard.SetTargetProperty(fade, "Opacity"); motion.Children.Add(fade);
        var slide = new DoubleAnimation { From = show ? 8 : 0, To = show ? 0 : 8, Duration = new Duration(duration), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(slide, _announcementTranslation); Storyboard.SetTargetProperty(slide, "Y"); motion.Children.Add(slide);
        motion.Completed += (_, _) => completed?.Invoke(); motion.Begin();
    }

    private void PositionAnnouncement()
    {
        if (_announcementCard is null || _announcementRoot?.XamlRoot is not { } xaml || _announcementRoot.ActualWidth <= 0) return;
        var scale = xaml.RasterizationScale;
        var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        var origin = new AnnouncementPoint(); if (!AnnouncementClientToScreen(NativeHandle, ref origin)) return;
        var width = _announcementRoot.ActualWidth; var height = _announcementRoot.ActualHeight;
        var left = Math.Clamp((work.X - origin.X) / scale, 0, width);
        var top = Math.Clamp((work.Y - origin.Y) / scale, 0, height);
        var right = Math.Clamp((work.X + work.Width - origin.X) / scale, left, width);
        var bottom = Math.Clamp((work.Y + work.Height - origin.Y) / scale, top, height);
        _announcementCard.Margin = new Thickness(left + 16, top + 16, width - right + 16, height - bottom + 16);
        _announcementCard.Constrain(Math.Max(0, right - left - 32), Math.Max(0, bottom - top - 32));
    }

    [StructLayout(LayoutKind.Sequential)] private struct AnnouncementPoint { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "ClientToScreen")] private static extern bool AnnouncementClientToScreen(nint window, ref AnnouncementPoint point);
    [DllImport("user32.dll", EntryPoint = "IsWindowEnabled")] private static extern bool AnnouncementWindowEnabled(nint window);
}
