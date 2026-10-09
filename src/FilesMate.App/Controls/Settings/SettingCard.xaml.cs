using FilesMate.App.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace FilesMate.App.Controls.Settings;

[ContentProperty(Name = nameof(Action))]
public sealed partial class SettingCard : UserControl
{
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SettingCard),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingCard),
        new PropertyMetadata(string.Empty, OnDescriptionChanged));

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

    public static readonly DependencyProperty ActionPreferredWidthProperty = DependencyProperty.Register(
        nameof(ActionPreferredWidth),
        typeof(double),
        typeof(SettingCard),
        new PropertyMetadata(0d, OnActionPreferredWidthChanged));

    private static void OnActionPreferredWidthChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (SettingCard)sender;
        if (card.LayoutGrid is not null) card.UpdateActionLayout(card.LayoutGrid.ActualWidth);
    }

    public SettingCard()
    {
        InitializeComponent();
        UpdateDescriptionLayout();
    }

    private static void OnDescriptionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((SettingCard)sender).UpdateDescriptionLayout();

    private void UpdateDescriptionLayout()
    {
        if (DescriptionText is null) return;
        var hasDescription = !string.IsNullOrWhiteSpace(Description);
        DescriptionText.Visibility = hasDescription ? Visibility.Visible : Visibility.Collapsed;
        CardRoot.MinHeight = hasDescription ? 68 : 56;
    }

    private void LayoutGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateActionLayout(e.NewSize.Width);
    }

    private void ActionHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateActionLayout(LayoutGrid.ActualWidth);

    private void UpdateActionLayout(double width)
    {
        // A longer translation or larger Windows text must not collide with
        // the action. Measure it instead of using one breakpoint for both a
        // compact switch and a row of buttons.
        var actionWidth = Action is FrameworkElement action
            ? Math.Max(action.MinWidth, action.DesiredSize.Width)
            : ActionHost.DesiredSize.Width;
        // Flexible controls can report only their thumb width after being
        // constrained by an Auto column. Reserve their intended width before
        // deciding whether to put the action below the description.
        var preferredWidth = double.IsFinite(ActionPreferredWidth) ? Math.Max(0, ActionPreferredWidth) : 0;
        actionWidth = preferredWidth > 0 ? preferredWidth : actionWidth;
        var layout = SettingsRowLayout.Resolve(width, actionWidth, _uiSettings.TextScaleFactor);
        var stacked = layout.StackActions;
        Grid.SetColumnSpan(TextHost, stacked ? 2 : 1);
        Grid.SetColumn(ActionHost, stacked ? 0 : 1);
        Grid.SetColumnSpan(ActionHost, stacked ? 2 : 1);
        Grid.SetRow(ActionHost, stacked ? 1 : 0);
        ActionHost.Margin = new Thickness(0, stacked ? 10 : 0, 0, 0);
        ActionHost.HorizontalAlignment = stacked && preferredWidth == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        ActionHost.MaxWidth = layout.ActionMaxWidth;
        ActionHost.Width = !stacked && preferredWidth > 0 ? Math.Min(preferredWidth, layout.ActionMaxWidth) : double.NaN;
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

    public double ActionPreferredWidth
    {
        get => (double)GetValue(ActionPreferredWidthProperty);
        set => SetValue(ActionPreferredWidthProperty, value);
    }

    private static void OnShowDividerChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is SettingCard { Divider: { } divider } card)
        {
            divider.Visibility = card.ShowDivider ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
