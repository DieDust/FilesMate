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
    }

    private static string? LocalizeAction(string? action) => action switch
    {
        "Retry" => StringTable.Get("Retry"),
        "Go up" => StringTable.Get("GoUp"),
        _ => action,
    };

    private void PrimaryAction_Click(object sender, RoutedEventArgs e) => PrimaryClicked?.Invoke(this, e);

    private void SecondaryAction_Click(object sender, RoutedEventArgs e) => SecondaryClicked?.Invoke(this, e);
}
