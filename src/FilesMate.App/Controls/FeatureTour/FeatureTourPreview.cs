using FilesMate.App.Localization;
using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Animation;

namespace FilesMate.App.Controls.FeatureTour;

/// <summary>A short, in-memory illustration. It never opens or scans a directory.</summary>
internal sealed class FeatureTourPreview : StackPanel, IDisposable
{
    private readonly string _topic;
    private readonly FeatureTourChoices _choices;
    private StackPanel _rows;
    private readonly StackPanel _scene = new() { Spacing = 3 };
    private readonly TextBlock _caption = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button _pause = new();
    private readonly Button _replay = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
    private readonly Storyboard _fade = new();
    private int _phase;
    private bool _disposed;
    private bool _running;

    public FeatureTourPreview(string topic, FeatureTourChoices choices)
    {
        _topic = topic;
        _choices = choices;
        _rows = _scene;
        Spacing = 7;
        var frame = (Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Padding='12' CornerRadius='8' BorderThickness='1' BorderBrush='{ThemeResource FilesMate.Card.BorderBrush}' Background='{ThemeResource FilesMate.Menu.BackgroundBrush}'/>");
        frame.Child = _scene;
        Children.Add(frame);
        AutomationProperties.SetAutomationId(_caption, "TourDemoPhase");
        Children.Add(_caption);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _pause.Content = StringTable.Get("Tour_Pause");
        _replay.Content = StringTable.Get("Tour_Replay");
        AutomationProperties.SetAutomationId(_pause, "TourDemoPause");
        AutomationProperties.SetAutomationId(_replay, "TourDemoReplay");
        _pause.Click += Pause_Click;
        _replay.Click += Replay_Click;
        actions.Children.Add(_pause); actions.Children.Add(_replay); Children.Add(actions);
        _timer.Tick += Timer_Tick;
        Loaded += Preview_Loaded;
        Unloaded += Preview_Unloaded;
        Render();
    }

    private static bool MotionAllowed => App.Motion?.Standard > TimeSpan.Zero;
    private void Preview_Loaded(object sender, RoutedEventArgs e)
    {
        if (_disposed) return;
        if (MotionAllowed) Start(restart: true);
        else ShowStatic();
    }

    private void Preview_Unloaded(object sender, RoutedEventArgs e) => Stop();
    private void Replay_Click(object sender, RoutedEventArgs e)
    {
        if (MotionAllowed) Start(restart: true); else ShowStatic();
    }
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_running) Stop(); else Start(restart: false);
        _pause.Content = StringTable.Get(_running ? "Tour_Pause" : "Tour_Resume");
    }
    private void Start(bool restart)
    {
        if (_disposed) return;
        if (!MotionAllowed) { ShowStatic(); return; }
        Stop();
        if (restart || _phase >= 4) _phase = 0;
        _pause.Visibility = Visibility.Visible; _pause.IsEnabled = true;
        _pause.Content = StringTable.Get("Tour_Pause");
        _replay.Content = StringTable.Get("Tour_Replay");
        _running = true; Render(); _timer.Start();
    }
    private void Stop() { _timer.Stop(); _fade.Stop(); _scene.Opacity = 1; _running = false; }
    private void Timer_Tick(object? sender, object e)
    {
        if (!MotionAllowed) { ShowStatic(); return; }
        _phase++; Render();
        if (_phase >= 4) { Stop(); _pause.IsEnabled = false; return; }
        _fade.Stop(); _fade.Children.Clear();
        var animation = new DoubleAnimation { From = .55, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(167)) };
        Storyboard.SetTarget(animation, _scene); Storyboard.SetTargetProperty(animation, "Opacity");
        _fade.Children.Add(animation); _fade.Begin();
    }
    private void ShowStatic()
    {
        Stop(); _phase = 4; _scene.Children.Clear(); _pause.Visibility = Visibility.Collapsed;
        var pair = new Grid { ColumnSpacing = 8 };
        pair.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        pair.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 2; index++)
        {
            var panel = new StackPanel { Spacing = 3 };
            panel.Children.Add(new TextBlock { Text = StringTable.Get(index == 0 ? "Tour_Before" : "Tour_After"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
            RenderFrame(panel, index == 0 ? 0 : 4);
            Grid.SetColumn(panel, index); pair.Children.Add(panel);
        }
        _scene.Children.Add(pair);
        _caption.Text = StringTable.Get("Tour_Static") + " · " + _caption.Text;
        _replay.Content = StringTable.Get("Tour_Static");
    }

    private void Row(string label, int indent = 0, bool selected = false)
    {
        var text = (TextBlock)XamlReader.Load("<TextBlock xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' FontSize='13' TextTrimming='CharacterEllipsis' VerticalAlignment='Center'" + (selected ? " Foreground='{ThemeResource FilesMate.Menu.ItemSelectedForegroundBrush}'" : "") + "/>");
        var folder = label.StartsWith("▸ ", StringComparison.Ordinal) || label.StartsWith("▾ ", StringComparison.Ordinal);
        text.Text = folder ? label[2..] : label;
        var layout = new Grid { ColumnSpacing = 4 };
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(12) });
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(16) });
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        if (folder) layout.Children.Add(new TextBlock { Text = label[..1], VerticalAlignment = VerticalAlignment.Center });
        var icon = new Viewbox { Width = 16, Height = 16, Child = new SymbolIcon(folder ? Symbol.Folder : Symbol.Document) };
        Grid.SetColumn(icon, 1); layout.Children.Add(icon);
        Grid.SetColumn(text, 2); layout.Children.Add(text);
        var row = selected
            ? (Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Padding='6,3' CornerRadius='5' Background='{ThemeResource FilesMate.Menu.ItemSelectedBrush}'/>")
            : new Border { Padding = new Thickness(6, 3, 6, 3) };
        row.Margin = new Thickness(indent * 12, 0, 0, 0); row.Child = layout; _rows.Children.Add(row);
    }

    private void Render()
    {
        _scene.Children.Clear();
        RenderFrame(_scene, _phase);
    }

    private void RenderFrame(StackPanel target, int phase)
    {
        _rows = target;
        var _phase = phase;
        if (_topic is "recursive" or "filters")
        {
            var expanded = _topic == "filters" || _phase > 0;
            var filtered = _topic == "filters" ? _phase >= 2 : _phase >= 3;
            Row((expanded ? "▾ " : "▸ ") + "Project");
            if (expanded)
            {
                if (!filtered) { Row("notes.txt", 1); Row("design.pdf", 1); }
                Row((_phase >= 2 ? "▾ " : "▸ ") + "Images", 1);
                if (_phase >= 2) Row("cover.jpg", 2, filtered);
            }
            if (filtered) Row("*.jpg   ×", selected: true);
            _caption.Text = StringTable.Get(filtered ? "Tour_DemoFilter" : _phase >= 2 ? "Tour_DemoSubtree" : expanded ? "Tour_DemoExpand" : "Tour_DemoCollapsed");
        }
        else if (_topic == "opening")
        {
            Row((_phase >= (_choices.FolderOpening == ItemOpeningMode.SingleClick ? 1 : 2) ? "▾ " : "▸ ") + "Project");
            var opened = _phase >= (_choices.FileOpening == ItemOpeningMode.SingleClick ? 1 : 3);
            Row(opened ? "notes.txt  ↗" : "notes.txt", selected: _phase >= 1); Row("design.pdf");
            _caption.Text = StringTable.Get("Opening_FileTitle") + ": " + StringTable.Get("Opening_" + _choices.FileOpening)
                + " · " + StringTable.Get("Opening_FolderTitle") + ": " + StringTable.Get("Opening_" + _choices.FolderOpening);
        }
        else if (_topic == "search")
        {
            Row(_phase == 0 ? _choices.Hotkey : _phase == 1 ? "⌕  c…" : "⌕  cover");
            if (_phase >= 2) { Row("cover.jpg", selected: _phase >= 3); Row("cover-notes.txt"); }
            _caption.Text = StringTable.Get("Tour_DemoSearch");
        }
        else
        {
            Row(_phase >= 3 ? "Project  |  Images  |  notes.txt" : "Project");
            Row(_phase < 2 ? "notes.txt       .txt       2 KB" : "▧ notes.txt     ▧ design.pdf");
            _caption.Text = StringTable.Get("Tour_DemoView");
        }
    }

    public void RefreshChoices() { if (_disposed) return; if (MotionAllowed) Render(); else ShowStatic(); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop(); _timer.Tick -= Timer_Tick;
        Loaded -= Preview_Loaded; Unloaded -= Preview_Unloaded;
        _pause.Click -= Pause_Click; _replay.Click -= Replay_Click;
        _fade.Children.Clear(); _scene.Children.Clear();
    }
}
