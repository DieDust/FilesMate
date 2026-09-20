using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.Core.Entries;
using FilesMate.App.Navigation;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private bool _alphabetEnabled = App.ExplorerPreferences.ShowAlphabetNavigation;
    private int _alphabetMinimumItemCount = App.ExplorerPreferences.AlphabetNavigationMinimumItemCount;
    private bool _alphabetAllowedInDualPane = App.ExplorerPreferences.ShowAlphabetNavigationInDualPane;
    private bool _alphabetIsDualPane;

    public void SetAlphabetNavigationEnabled(bool enabled)
    {
        if (_alphabetEnabled == enabled) return;
        _alphabetEnabled = enabled;
        RefreshAlphabet();
    }

    public void SetAlphabetNavigationPolicy(bool enabled, int minimumItemCount, bool showInDualPane, bool isDualPane)
    {
        minimumItemCount = FilesMate.App.Models.ExplorerPreferences.ClampAlphabetNavigationMinimumItemCount(minimumItemCount);
        if (_alphabetEnabled == enabled
            && _alphabetMinimumItemCount == minimumItemCount
            && _alphabetAllowedInDualPane == showInDualPane
            && _alphabetIsDualPane == isDualPane)
        {
            return;
        }

        _alphabetEnabled = enabled;
        _alphabetMinimumItemCount = minimumItemCount;
        _alphabetAllowedInDualPane = showInDualPane;
        _alphabetIsDualPane = isDualPane;
        RefreshAlphabet();
    }

    private AlphabetNavigation _alphabet = new([], 0);
    private IReadOnlyList<NameSection> _nameSections = [];
    private readonly Dictionary<string, Button> _alphabetButtons = [];
    private uint? _alphabetPointer;
    private double _alphabetGrabOffset;
    private bool _alphabetHovered;
    private int _activeNameSection = -1;
    private int _alphabetGridColumns;
    private DispatcherQueueTimer? _alphabetHideTimer;

    private void InitializeAlphabet()
    {
        AutomationProperties.SetName(AlphabetOverlay, Loc.Get("Alphabet_AccessibleName"));
        AutomationProperties.SetName(AlphabetTrack, Loc.Get("Alphabet_AccessibleName"));
        foreach (var label in AlphabetNavigation.Labels)
        {
            var button = new Button { Content = label, Style = (Style)Resources["AlphabetButtonStyle"] };
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetAutomationId(button, "Alphabet_" + label);
            button.Click += (_, _) => JumpLetter(label);
            _alphabetButtons.Add(label, button);
            AlphabetList.Children.Add(button);
        }
        AlphabetOverlay.GotFocus += (_, _) => ShowAlphabet();
        AlphabetTrack.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(AlphabetTrack).Properties.IsLeftButtonPressed || !AlphabetTrack.CapturePointer(e.Pointer)) return;
            _alphabetPointer = e.Pointer.PointerId;
            var y = e.GetCurrentPoint(AlphabetTrack).Position.Y - 8;
            var top = AlphabetThumb.Margin.Top;
            _alphabetGrabOffset = y >= top && y <= top + AlphabetThumb.Height ? y - top : AlphabetThumb.Height / 2;
            ShowAlphabet();
            ScrollAlphabetAt(y);
            e.Handled = true;
        };
        AlphabetTrack.PointerMoved += (_, e) =>
        {
            if (_alphabetPointer != e.Pointer.PointerId) return;
            ScrollAlphabetAt(e.GetCurrentPoint(AlphabetTrack).Position.Y - 8);
            e.Handled = true;
        };
        AlphabetTrack.PointerReleased += (_, e) =>
        {
            if (_alphabetPointer != e.Pointer.PointerId) return;
            _alphabetPointer = null;
            AlphabetTrack.ReleasePointerCapture(e.Pointer);
            ScheduleAlphabetHide();
            e.Handled = true;
        };
        AlphabetTrack.PointerCaptureLost += (_, _) => { _alphabetPointer = null; ScheduleAlphabetHide(); };
        AlphabetOverlay.PointerEntered += (_, _) => { _alphabetHovered = true; ShowAlphabet(); };
        AlphabetOverlay.PointerExited += (_, _) => { _alphabetHovered = false; ScheduleAlphabetHide(); };
        AlphabetOverlay.PointerPressed += (_, e) => e.Handled = true;
        AlphabetOverlay.RightTapped += (_, e) => e.Handled = true;
        AlphabetOverlay.PointerWheelChanged += (_, e) =>
        {
            if (IsModifier(Windows.System.VirtualKey.Control)) return;
            ShowAlphabet();
            var delta = e.GetCurrentPoint(AlphabetOverlay).Properties.MouseWheelDelta;
            ScrollAlphabetTo(Scroller.VerticalOffset - delta / 120d * ItemHeight() * 3);
            e.Handled = true;
        };
        AlphabetOverlay.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { HideAlphabet(); Focus(FocusState.Programmatic); e.Handled = true; }
            else if (e.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End)
            {
                var labelIndex = Math.Max(0, Array.IndexOf(AlphabetNavigation.Labels.ToArray(), AlphabetBubbleText.Text));
                var step = e.Key == Windows.System.VirtualKey.Up ? -1 : 1;
                var next = e.Key switch { Windows.System.VirtualKey.Home => 0, Windows.System.VirtualKey.End => AlphabetNavigation.Labels.Count - 1, _ => labelIndex + step };
                while (next >= 0 && next < AlphabetNavigation.Labels.Count && !_alphabet.Contains(AlphabetNavigation.Labels[next]))
                    next += e.Key == Windows.System.VirtualKey.End ? -1 : step;
                if (next >= 0 && next < AlphabetNavigation.Labels.Count) JumpLetter(AlphabetNavigation.Labels[next]);
                e.Handled = true;
            }
        };
        AlphabetList.SizeChanged += (_, _) => AlignAlphabetBubble();
        AlphabetTrack.SizeChanged += (_, _) => { LayoutAlphabet(); UpdateAlphabetPosition(false); };
        Unloaded += (_, _) => { HideAlphabet(); _alphabetHideTimer?.Stop(); };
    }

    private void RefreshAlphabet()
    {
        HideAlphabet();
        var show = AlphabetNavigationPolicy.ShouldShow(
            _alphabetEnabled,
            _items.Count,
            _alphabetMinimumItemCount,
            _alphabetIsDualPane,
            _alphabetAllowedInDualPane);
        _nameSections = show && _items.Index?.Sort.Column == EntrySortColumn.Name ? _items.Index.NameSections : [];
        _alphabet = new AlphabetNavigation(_nameSections, _items.Count);
        _activeNameSection = -1;
        AlphabetOverlay.Visibility = _nameSections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Scroller.VerticalScrollBarVisibility = _nameSections.Count > 0 ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        foreach (var (label, button) in _alphabetButtons)
        {
            button.IsEnabled = _alphabet.Contains(label);
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
        }
        UpdateAlphabetTailSpace();
        LayoutAlphabet();
        UpdateAlphabetPosition(false);
    }

    private void UpdateAlphabetTailSpace()
    {
        // Uniform rows let us extend the scroll extent without changing the
        // repeater's origin or its virtualization viewport with a bottom margin.
        var stride = ItemHeight();
        var rowHeight = stride - (_layout == FileLayoutKind.Grid ? _gridPreset.Gutter : FileColumnLayout.RowGap);
        var rows = Math.Ceiling(_items.Count / (double)Columns());
        var extent = _nameSections.Count > 0 ? Math.Max(0, rows * stride + Scroller.ViewportHeight - rowHeight) : 0;
        if (Math.Abs(Repeater.MinHeight - extent) > .1) Repeater.MinHeight = extent;
    }

    private void LayoutAlphabet()
    {
        var columns = AlphabetNavigationPolicy.ColumnCount(AlphabetTrack.ActualHeight, AlphabetNavigation.Labels.Count);
        if (columns == _alphabetGridColumns) return;
        _alphabetGridColumns = columns;
        AlphabetLetters.Width = columns == 1 ? 34 : 60;
        AlphabetOverlay.Width = columns == 1 ? 80 : 96;
        AlphabetList.RowDefinitions.Clear();
        AlphabetList.ColumnDefinitions.Clear();
        var rows = (AlphabetNavigation.Labels.Count + columns - 1) / columns;
        for (var i = 0; i < rows; i++) AlphabetList.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < columns; i++) AlphabetList.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < AlphabetNavigation.Labels.Count; i++)
        {
            var button = _alphabetButtons[AlphabetNavigation.Labels[i]];
            Grid.SetRow(button, i % rows);
            Grid.SetColumn(button, i / rows);
        }
    }

    private void ShowAlphabet()
    {
        if (_nameSections.Count == 0) return;
        _alphabetHideTimer?.Stop();
        if (AlphabetLetters.Visibility != Visibility.Visible)
        {
            AlphabetLetters.Visibility = Visibility.Visible;
            AlphabetLetters.UpdateLayout();
        }
        UpdateAlphabetPosition(true);
    }

    private void ScheduleAlphabetHide()
    {
        if (_alphabetPointer is not null || _alphabetHovered) return;
        AlphabetRange.Visibility = Visibility.Collapsed;
        if (_alphabetHideTimer is null)
        {
            _alphabetHideTimer = DispatcherQueue.CreateTimer();
            _alphabetHideTimer.Interval = TimeSpan.FromMilliseconds(450);
            _alphabetHideTimer.IsRepeating = false;
            _alphabetHideTimer.Tick += (_, _) => HideAlphabet();
        }
        _alphabetHideTimer.Start();
    }

    private void HideAlphabet()
    {
        _alphabetHovered = false;
        _alphabetPointer = null;
        AlphabetTrack.ReleasePointerCaptures();
        _alphabetHideTimer?.Stop();
        AlphabetLetters.Visibility = AlphabetBubble.Visibility = AlphabetRange.Visibility = Visibility.Collapsed;
    }

    private void ScrollAlphabetAt(double y)
    {
        var travel = Math.Max(1, AlphabetTrack.ActualHeight - 16 - AlphabetThumb.Height);
        ScrollAlphabetTo((y - _alphabetGrabOffset) / travel * Scroller.ScrollableHeight);
    }

    private void ScrollAlphabetTo(double offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, Scroller.ScrollableHeight));
        _ = Scroller.ChangeView(null, offset, null, disableAnimation: true);
        UpdateAlphabetAt(offset, true);
    }

    private void JumpLetter(string label)
    {
        var row = _alphabet.FirstVisibleIndex(Scroller.VerticalOffset, ItemHeight(), Columns());
        var section = _alphabet.Destination(label, row);
        if (section < 0) return;
        UpdateAlphabetTailSpace();
        Scroller.UpdateLayout();
        ScrollAlphabetTo(_alphabet.OffsetForItem(_nameSections[section].FirstIndex, ItemHeight(), Columns()));
    }

    private void UpdateAlphabetPosition(bool showBubble) => UpdateAlphabetAt(Scroller.VerticalOffset, showBubble);

    private void UpdateAlphabetAt(double offset, bool showBubble)
    {
        AlphabetRange.Visibility = _alphabetHovered || _alphabetPointer is not null
            ? Visibility.Visible : Visibility.Collapsed;
        var track = Math.Max(1, AlphabetTrack.ActualHeight - 16);
        var height = Math.Clamp(track * Scroller.ViewportHeight / Math.Max(1, Scroller.ExtentHeight), Math.Min(24, track), track);
        AlphabetThumb.Height = height;
        var y = offset / Math.Max(1, Scroller.ScrollableHeight) * (track - height);
        AlphabetThumb.Margin = new Thickness(0, y, 0, 0);
        var section = _alphabet.SectionAt(_alphabet.FirstVisibleIndex(offset, ItemHeight(), Columns()));
        if (section < 0) return;
        SetActiveNameSection(section);
        var range = _alphabet.Range(section);
        var start = track * range.Start / Math.Max(1, _items.Count);
        var end = track * range.End / Math.Max(1, _items.Count);
        AlphabetRange.Margin = new Thickness(0, start, 0, 0);
        AlphabetRange.Height = Math.Max(1, end - start);
        AlphabetBubble.Visibility = showBubble ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AlignAlphabetBubble()
    {
        if (!_alphabetButtons.TryGetValue(AlphabetBubbleText.Text, out var button) || button.ActualHeight <= 0) return;
        var center = button.TransformToVisual(AlphabetOverlay).TransformPoint(new Windows.Foundation.Point(0, button.ActualHeight / 2));
        AlphabetBubble.Margin = new Thickness(-58, center.Y - AlphabetBubble.Height / 2, 0, 0);
    }

    private void SetActiveNameSection(int section)
    {
        var label = _nameSections[section].Label;
        if (_activeNameSection != section)
        {
            if (_activeNameSection >= 0)
            {
                var previous = _alphabetButtons[_nameSections[_activeNameSection].Label];
                previous.ClearValue(Control.BackgroundProperty);
                previous.ClearValue(Control.ForegroundProperty);
            }
            _activeNameSection = section;
            var button = _alphabetButtons[label];
            button.Background = (Brush)Application.Current.Resources["FilesMate.Selection.AccentBrush"];
            button.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        }
        AlphabetBubbleText.Text = label;
        AlignAlphabetBubble();
    }
}
