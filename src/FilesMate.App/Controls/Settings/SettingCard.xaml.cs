using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace FilesMate.App.Controls.Settings;

[ContentProperty(Name = nameof(Action))]
public sealed partial class SettingCard : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SettingCard),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingCard),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionProperty = DependencyProperty.Register(
        nameof(Action),
        typeof(UIElement),
        typeof(SettingCard),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ShowDividerProperty = DependencyProperty.Register(
        nameof(ShowDivider),
        typeof(bool),
        typeof(SettingCard),
        new PropertyMetadata(false, OnShowDividerChanged));

    public SettingCard()
    {
        InitializeComponent();
    }

    private void LayoutGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Long translations need their own row in a narrow settings pane.
        var stacked = e.NewSize.Width < 560;
        Grid.SetColumnSpan(TextHost, stacked ? 2 : 1);
        Grid.SetColumn(ActionHost, stacked ? 0 : 1);
        Grid.SetColumnSpan(ActionHost, stacked ? 2 : 1);
        Grid.SetRow(ActionHost, stacked ? 1 : 0);
        ActionHost.Margin = new Thickness(0, stacked ? 10 : 0, 0, 0);
        ActionHost.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        ActionHost.MaxWidth = Math.Max(0, e.NewSize.Width - 36);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public UIElement? Action
    {
        get => (UIElement?)GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public bool ShowDivider
    {
        get => (bool)GetValue(ShowDividerProperty);
        set => SetValue(ShowDividerProperty, value);
    }

    private static void OnShowDividerChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is SettingCard { Divider: { } divider } card)
        {
            divider.Visibility = card.ShowDivider ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
