using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private Controls.Status.OperationNotice? _actionNotice;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _actionNoticeTimer;

    internal void ShowActionNotice(string message)
    {
        if (TabHost.Content is Views.NavigatorPage navigator) navigator.DismissOperationNotice();
        if (_actionNotice is null)
        {
            _actionNotice = new Controls.Status.OperationNotice
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(20, 0, 20, 48),
                IsHitTestVisible = false
            };
            ((Button)_actionNotice.FindName("UndoButton")).Visibility = Visibility.Collapsed;
            Grid.SetRowSpan(_actionNotice, 2);
            Canvas.SetZIndex(_actionNotice, 90);
            ((Panel)Content).Children.Add(_actionNotice);
            _actionNoticeTimer = DispatcherQueue.CreateTimer();
            _actionNoticeTimer.Interval = TimeSpan.FromSeconds(3);
            _actionNoticeTimer.Tick += (_, _) => { _actionNoticeTimer.Stop(); _actionNotice.Visibility = Visibility.Collapsed; };
            Closed += (_, _) => _actionNoticeTimer.Stop();
        }
        ((TextBlock)_actionNotice.FindName("MessageText")).Text = message;
        Theming.AppTypography.Apply(_actionNotice);
        _actionNotice.Visibility = Visibility.Visible;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(_actionNotice, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(_actionNotice)?.RaiseAutomationEvent(
            Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
        _actionNoticeTimer!.Stop();
        _actionNoticeTimer.Start();
    }

    internal void DismissActionNotice()
    {
        _actionNoticeTimer?.Stop();
        if (_actionNotice is not null) _actionNotice.Visibility = Visibility.Collapsed;
    }
}
