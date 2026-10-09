using FilesMate.App.Localization;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.InfoStates;

public sealed partial class InfoStateView : UserControl
{
    private bool _compactWhenSmall;
    public InfoStateView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyCompactLayout();
    }

    public event RoutedEventHandler? PrimaryClicked;

    public event RoutedEventHandler? SecondaryClicked;

    public void Apply(string? glyph, string? title, string? body, string? primary, string? secondary, bool compactWhenSmall = false)
    {
        _compactWhenSmall = compactWhenSmall;
        GlyphIcon.Glyph = string.IsNullOrEmpty(glyph) ? "\uE8B7" : glyph;
        TitleBlock.Text = title ?? string.Empty;
        BodyBlock.Text = body ?? string.Empty;
        PrimaryAction.Content = LocalizeAction(primary);
        PrimaryAction.Visibility = string.IsNullOrEmpty(primary) ? Visibility.Collapsed : Visibility.Visible;
        SecondaryAction.Content = LocalizeAction(secondary);
        SecondaryAction.Visibility = string.IsNullOrEmpty(secondary) ? Visibility.Collapsed : Visibility.Visible;
        Actions.Visibility = PrimaryAction.Visibility == Visibility.Visible || SecondaryAction.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyCompactLayout();
    }

    private void ApplyCompactLayout()
    {
        var compact = _compactWhenSmall && ActualHeight > 0 && ActualHeight < 200;
        ContentStack.Padding = compact ? new Thickness(16, 8, 16, 8) : new Thickness(24);
        ContentStack.Spacing = compact ? 6 : 12;
        GlyphIcon.FontSize = compact ? 24 : 32;
        GlyphIcon.Visibility = _compactWhenSmall && ActualHeight is > 0 and < 80 ? Visibility.Collapsed : Visibility.Visible;
        BodyBlock.Visibility = _compactWhenSmall && ActualHeight is > 0 and < 120 || string.IsNullOrEmpty(BodyBlock.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        var available = Math.Max(0, Math.Min(ActualWidth, ContentStack.MaxWidth)
            - ContentStack.Padding.Left - ContentStack.Padding.Right);
        if (available <= 0) return;
        PrimaryAction.MaxWidth = SecondaryAction.MaxWidth = double.PositiveInfinity;
        var naturalSize = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
        PrimaryAction.Measure(naturalSize);
        SecondaryAction.Measure(naturalSize);
        var primaryWidth = PrimaryAction.Visibility == Visibility.Visible ? PrimaryAction.DesiredSize.Width : 0;
        var secondaryWidth = SecondaryAction.Visibility == Visibility.Visible ? SecondaryAction.DesiredSize.Width : 0;
        var gap = primaryWidth > 0 && secondaryWidth > 0 ? Actions.Spacing : 0;
        Actions.Orientation = primaryWidth + secondaryWidth + gap > available ? Orientation.Vertical : Orientation.Horizontal;
        PrimaryAction.MaxWidth = SecondaryAction.MaxWidth = available;
    }

    private static string? LocalizeAction(string? action) => action switch
    {
        "Retry" => StringTable.Get("Retry"),
        "Go up" => StringTable.Get("GoUp"),
        "ClearInlineFilter" => StringTable.Get("Inline_ClearSearchAndFilters"),
        _ => action,
    };

    private void PrimaryAction_Click(object sender, RoutedEventArgs e) => PrimaryClicked?.Invoke(this, e);

    private void SecondaryAction_Click(object sender, RoutedEventArgs e) => SecondaryClicked?.Invoke(this, e);
}
