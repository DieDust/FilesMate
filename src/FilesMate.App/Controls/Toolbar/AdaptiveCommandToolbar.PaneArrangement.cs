using FilesMate.App.Models;
using FilesMate.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App.Controls.Toolbar;

public sealed partial class AdaptiveCommandToolbar
{
    private PaneArrangement _arrangement;
    private bool _arrangementActive;
    public event EventHandler<PaneArrangement>? PaneArrangementChanged;
    public void SetPaneArrangement(PaneArrangement value) => _arrangement = value;

    private void PaneArrangement_Click(object sender, RoutedEventArgs e)
    {
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4, Margin = new Thickness(4) };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 2; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var flyout = new Flyout { Content = grid, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterStyle = (Style)Application.Current.Resources["FilesMate.RoundedFlyoutPresenterStyle"] };
        Theming.FlyoutTheme.FollowHost(flyout);
        foreach (var kind in Enum.GetValues<PaneArrangement>())
        {
            var label = StringTable.Get("Pane_" + kind);
            var content = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
            var canvas = new Canvas { Width = 60, Height = 36 };
            var boxes = kind switch
            {
                PaneArrangement.Columns => new[] { new Rect(0,0,18,36), new Rect(21,0,18,36), new Rect(42,0,18,36) },
                PaneArrangement.Rows => new[] { new Rect(0,0,60,10), new Rect(0,13,60,10), new Rect(0,26,60,10) },
                PaneArrangement.LeftFocus => new[] { new Rect(0,0,28,36), new Rect(31,0,29,16), new Rect(31,19,29,17) },
                PaneArrangement.RightFocus => new[] { new Rect(32,0,28,36), new Rect(0,0,29,16), new Rect(0,19,29,17) },
                PaneArrangement.TopFocus => new[] { new Rect(0,0,60,16), new Rect(0,19,28,17), new Rect(31,19,29,17) },
                _ => new[] { new Rect(0,20,60,16), new Rect(0,0,28,17), new Rect(31,0,29,17) },
            };
            foreach (var box in boxes)
            {
                var border = new Border { Width = box.Width, Height = box.Height,
                    Style = (Style)Application.Current.Resources["FilesMate.PaneDiagramStyle"] };
                Canvas.SetLeft(border, box.X); Canvas.SetTop(border, box.Y); canvas.Children.Add(border);
            }
            content.Children.Add(new Viewbox { Child = canvas, Width = 42, Height = 25.2 });
            content.Children.Add(new TextBlock { Text = label, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 86 });
            var button = new Button { Content = content, Width = 98, MinWidth = 0, Height = 62, Padding = new Thickness(6), CornerRadius = new CornerRadius(8),
                Style = (Style)Application.Current.Resources[_arrangementActive && _arrangement == kind ? "FilesMate.ActivePaneChoiceStyle" : "FilesMate.PaneChoiceStyle"] };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
            Grid.SetColumn(button, (int)kind % 3); Grid.SetRow(button, (int)kind / 3);
            button.Click += (_, _) => { flyout.Hide(); PaneArrangementChanged?.Invoke(this, kind); AnimateChoice(DualPaneButton); };
            grid.Children.Add(button);
        }
        Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.SetAttachedFlyout(PaneArrangementButton, flyout);
        flyout.ShowAt(PaneArrangementButton);
    }
}
