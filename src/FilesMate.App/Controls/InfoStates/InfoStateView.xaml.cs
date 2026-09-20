using FilesMate.App.Localization;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.InfoStates;

public sealed partial class InfoStateView : UserControl
{
    public InfoStateView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? PrimaryClicked;

    public event RoutedEventHandler? SecondaryClicked;

    public void Apply(string? glyph, string? title, string? body, string? primary, string? secondary)
    {
        GlyphIcon.Glyph = string.IsNullOrEmpty(glyph) ? "\uE8B7" : glyph;
        TitleBlock.Text = title ?? string.Empty;
        BodyBlock.Text = body ?? string.Empty;
        PrimaryAction.Content = LocalizeAction(primary);
        PrimaryAction.Visibility = string.IsNullOrEmpty(primary) ? Visibility.Collapsed : Visibility.Visible;
        SecondaryAction.Content = LocalizeAction(secondary);
        SecondaryAction.Visibility = string.IsNullOrEmpty(secondary) ? Visibility.Collapsed : Visibility.Visible;
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
