#if FILESMATE_UI_TEST
using FilesMate.App.Controls.FileSurface;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    internal async Task VerifyPaneCycleAsync(Dictionary<string, object> report, string fixture)
    {
        var preferences = App.ExplorerPreferences;
        try
        {
            SetPaneCount(1, false);
            var button = (Button)Commands.FindName("DualPaneButton");
            void Click() => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            Click(); await Ready(() => _paneCount == 2 && _rightVm?.IsLoading == false);
            Click(); await Ready(() => _paneCount == 3 && _thirdVm?.IsLoading == false);
            var right = Path.Combine(fixture, "pane-two"); var third = Path.Combine(fixture, "pane-three");
            Directory.CreateDirectory(right); Directory.CreateDirectory(third);
            _rightVm!.Navigate(right); _thirdVm!.Navigate(third);
            await Ready(() => !_rightVm.IsLoading && !_thirdVm.IsLoading && _rightVm.AddressText == right && _thirdVm.AddressText == third);
            // Use actual keyboard focus, as a user does. Changing only the pane
            // field races the initial view restore's pending focus callback.
            _thirdSurface!.Focus(Microsoft.UI.Xaml.FocusState.Keyboard); await Task.Delay(100);
            if (ViewModel != _thirdVm || ActiveSurface != _thirdSurface || !_thirdChrome!.IsActive || _rightChrome!.IsActive)
                throw new InvalidOperationException("The third pane did not own navigation and file commands");
            var state = CaptureClosedTab();
            if (state.Third?.Path != third || !state.ThirdActive || state.Right?.Path != right)
                throw new InvalidOperationException("Three-pane state was lost");
            if (OtherPaneDestination(1) != third || OtherPaneDestination(2) != _leftVm.AddressText)
                throw new InvalidOperationException("Pane transfer destinations did not follow the active pane");
            Click(); await Ready(() => _paneCount == 1);
            if (ViewModel != _leftVm || _thirdActive || _rightActive) throw new InvalidOperationException("Single pane retained a hidden active pane");
            await RestoreClosedTabAsync(state);
            if (_paneCount != 3 || ViewModel != _thirdVm || _thirdVm.AddressText != third)
                throw new InvalidOperationException("Restoring the third pane failed");
            report["SingleDualTripleCycleAndRestore"] = true;
            await VerifySharedPaneStatusAsync(report);
            ActivatePane(2);
            var originalPaths = new[] { _leftVm.AddressText, _rightVm.AddressText, _thirdVm.AddressText };
            foreach (var arrangement in Enum.GetValues<Models.PaneArrangement>())
            {
                if (_paneArrangement != arrangement) Commands_PaneArrangementChanged(Commands, arrangement);
                await Task.Delay(120);
                var bounds = new[] { PaneChrome, _rightChrome!, _thirdChrome! }.Select(p =>
                    p.TransformToVisual(WorkspaceSplit).TransformBounds(new Windows.Foundation.Rect(0, 0, p.ActualWidth, p.ActualHeight))).ToArray();
                if (bounds.Any(b => b.Width < 60 || b.Height < 60 || b.Right > WorkspaceSplit.ActualWidth + 2 || b.Bottom > WorkspaceSplit.ActualHeight + 2))
                    throw new InvalidOperationException("Pane arrangement escaped its workspace: " + arrangement);
                for (var i = 0; i < 3; i++) for (var j = i + 1; j < 3; j++)
                {
                    var overlap = bounds[i]; overlap.Intersect(bounds[j]);
                    if (!overlap.IsEmpty && overlap.Width > 1 && overlap.Height > 1) throw new InvalidOperationException("Pane arrangement overlapped: " + arrangement);
                }
                if (!originalPaths.SequenceEqual(new[] { _leftVm.AddressText, _rightVm.AddressText, _thirdVm.AddressText }) || ViewModel != _thirdVm)
                    throw new InvalidOperationException("Arrangement lost pane navigation or active state");
                var captured = CaptureClosedTab();
                if (captured.PaneArrangement != arrangement) throw new InvalidOperationException("Arrangement not preserved in tab state");
                await MainWindow.Capture((Microsoft.UI.Xaml.UIElement)App.WindowForElement(this)!.Content, "search-page-panes-" + arrangement + ".png");
            }
            report["SixArrangementsKeepPathsAndFocus"] = true;
            var choiceButton = (Button)Commands.FindName("PaneArrangementButton");
            ((IInvokeProvider)new ButtonAutomationPeer(choiceButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(220);
            var choiceFlyout = (Flyout)Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.GetAttachedFlyout(choiceButton);
            var choices = (Grid)choiceFlyout.Content;
            if (choices.Children.Count != 6) throw new InvalidOperationException("Arrangement picker lost choices");
            if (choices.ActualWidth > 320 || choices.ActualHeight > 140)
                throw new InvalidOperationException("Arrangement picker is no longer compact");
            report["CompactArrangementPicker"] = new { Width = choices.ActualWidth, Height = choices.ActualHeight };
            var fill = Models.SurfacePalette.Floating(ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark);
            choices.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(fill >> 16), (byte)(fill >> 8), (byte)fill));
            await MainWindow.Capture(choices, "search-page-pane-picker.png");
            await Choose(choiceFlyout, 2);
            if (_paneArrangement != Models.PaneArrangement.LeftFocus) throw new InvalidOperationException("Arrangement picker did not apply selection");
            report["ArrangementPickerMouseAction"] = true;
            foreach (var arrangement in Enum.GetValues<Models.PaneArrangement>())
            {
                // Exercise the real picker event, including closing and reopening it.
                if (_paneArrangement != arrangement) await Choose(await OpenChoices(), (int)arrangement);
                var selected = await OpenChoices();
                var activeStyle = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["FilesMate.ActivePaneChoiceStyle"];
                if (((Grid)selected.Content).Children.OfType<Button>().Count(b => b.Style == activeStyle) != 1)
                    throw new InvalidOperationException("Three panes should highlight exactly one arrangement");
                await Choose(selected, (int)arrangement);
                await Ready(() => _paneCount == 1 && App.ExplorerPreferences.EffectivePaneCount == 1);
                if (ViewModel != _leftVm || _thirdActive || _rightActive)
                    throw new InvalidOperationException("Clicking the selected arrangement retained a hidden active pane");
                var single = await OpenChoices();
                if (((Grid)single.Content).Children.OfType<Button>().Any(b => b.Style == activeStyle))
                    throw new InvalidOperationException("Single pane still highlights an arrangement");
                await Choose(single, (int)arrangement);
                await Ready(() => _paneCount == 3 && App.ExplorerPreferences.EffectivePaneCount == 3);
                if (!originalPaths.SequenceEqual(new[] { _leftVm.AddressText, _rightVm.AddressText, _thirdVm.AddressText }))
                    throw new InvalidOperationException("Toggling an arrangement lost pane paths");
            }
            report["EachArrangementTogglesSingleAndTriple"] = true;
            report["SinglePaneClearsArrangementHighlight"] = true;
            report["ArrangementTogglePersistsAndRetainsPaths"] = true;

            async Task<Flyout> OpenChoices()
            {
                ((IInvokeProvider)new ButtonAutomationPeer(choiceButton).GetPattern(PatternInterface.Invoke)).Invoke();
                var flyout = (Flyout)Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.GetAttachedFlyout(choiceButton);
                await Ready(() => ((Grid)flyout.Content).ActualWidth > 0);
                await Task.Delay(120);
                return flyout;
            }
            static async Task Choose(Flyout flyout, int index)
            {
                var closed = new TaskCompletionSource();
                void OnClosed(object? sender, object args) { flyout.Closed -= OnClosed; closed.TrySetResult(); }
                flyout.Closed += OnClosed;
                var choice = (Button)((Grid)flyout.Content).Children[index];
                ((IInvokeProvider)new ButtonAutomationPeer(choice).GetPattern(PatternInterface.Invoke)).Invoke();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        finally { SetPaneCount(1, false); await App.SetExplorerPreferencesAsync(preferences with { DualPane = false, PaneCount = 1 }); }
        static async Task Ready(Func<bool> condition)
        {
            for (var i = 0; i < 160; i++) { if (condition()) return; await Task.Delay(50); }
            throw new TimeoutException("Pane cycling did not settle");
        }
    }

    private async Task VerifySharedPaneStatusAsync(Dictionary<string, object> report)
    {
        var summary = (Microsoft.UI.Xaml.Controls.TextBlock)SharedStatusBar.FindName("SummaryBlock");
        var panes = new[] { PaneChrome, _rightChrome!, _thirdChrome! };
        if (panes.Any(p => CountStatusBars(p) != 0) || CountStatusBars(this) != 1)
            throw new InvalidOperationException("Pane layouts must share exactly one status bar");
        var selectedPath = Directory.GetFiles(_leftVm.AddressText).First();
        if (!FileSurface.TrySelectByPath(selectedPath)) throw new InvalidOperationException("Shared status fixture selection failed");
        ActivatePane(0); await Task.Delay(100);
        if (!summary.Text.Contains(_leftVm.StatusText) || !summary.Text.Contains(PaneChrome.SelectionText!))
            throw new InvalidOperationException("Status did not show active pane count and selection");
        var leftSummary = summary.Text;
        // Metadata arriving for a background pane must be cached without replacing the active summary.
        var previous = _rightChrome!.ZoomText;
        _rightChrome.ZoomText = "background-completion";
        if (summary.Text != leftSummary) throw new InvalidOperationException("Background pane overwrote active status");
        ActivatePane(1);
        if (!summary.Text.Contains("background-completion") || summary.Text.Contains(PaneChrome.SelectionText!))
            throw new InvalidOperationException("Changing active pane retained stale status or selection");
        _rightChrome.ZoomText = previous;
        _thirdSurface!.Focus(Microsoft.UI.Xaml.FocusState.Keyboard); await Task.Delay(80);
        if (ViewModel != _thirdVm || summary.Text.Contains("background-completion"))
            throw new InvalidOperationException("Keyboard focus did not update shared pane status");
        ActivatePane(0); await Task.Delay(80);
        if (summary.Text != leftSummary) throw new InvalidOperationException("Returning to pane lost its cached status");
        var settings = App.AppearanceViewModel!.Current;
        ApplyAppearance(settings with { ShowStatusBar = false });
        if (SharedStatusBar.Visibility != Microsoft.UI.Xaml.Visibility.Collapsed) throw new InvalidOperationException("Shared status visibility ignored settings");
        ApplyAppearance(settings with { ShowStatusBar = true });
        report["OneStatusBarFollowsFocusSelectionAndBackgroundUpdates"] = true;

        static int CountStatusBars(Microsoft.UI.Xaml.DependencyObject parent)
        {
            var count = parent is Controls.Status.FileStatusBar ? 1 : 0;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                count += CountStatusBars(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i));
            return count;
        }
    }
}
#endif
