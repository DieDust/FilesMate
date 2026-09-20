using FilesMate.App.Localization;
using FilesMate.App.Navigation;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls;

public sealed partial class NavigationToolbar : UserControl
{
    public NavigationToolbar()
    {
        InitializeComponent();
        Caption(BackButton, "Nav_Back");
        Caption(ForwardButton, "Nav_Forward");
        Caption(UpButton, "Nav_Up");
        Caption(RefreshButton, "Nav_Refresh");
        Caption(OverflowButton, "Nav_More");
        OverflowForward.Text = StringTable.Get("Nav_Forward");
        OverflowUp.Text = StringTable.Get("Nav_Up");
        OverflowRefresh.Text = StringTable.Get("Nav_Refresh");
    }

    private static void Caption(Button button, string key)
    {
        var text = StringTable.Get(key);
        AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, text);
    }

    public event RoutedEventHandler? BackClicked;

    public event RoutedEventHandler? ForwardClicked;

    public event RoutedEventHandler? UpClicked;

    public event RoutedEventHandler? RefreshClicked;

    public bool CanGoBack
    {
        get => BackButton.IsEnabled;
        set => BackButton.IsEnabled = value;
    }

    public bool CanGoForward
    {
        get => ForwardButton.IsEnabled;
        set
        {
            ForwardButton.IsEnabled = value;
            OverflowForward.IsEnabled = value;
        }
    }

    public bool CanGoUp
    {
        get => UpButton.IsEnabled;
        set
        {
            UpButton.IsEnabled = value;
            OverflowUp.IsEnabled = value;
        }
    }

    public bool CanRefresh
    {
        get => RefreshButton.IsEnabled;
        set
        {
            RefreshButton.IsEnabled = value;
            OverflowRefresh.IsEnabled = value;
        }
    }

    public void ApplyOverflow(OmnibarOverflow overflow)
    {
        ForwardButton.Visibility = overflow.ShowForward ? Visibility.Visible : Visibility.Collapsed;
        UpButton.Visibility = overflow.ShowUp ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.Visibility = overflow.ShowRefresh ? Visibility.Visible : Visibility.Collapsed;
        OverflowForward.Visibility = overflow.ShowForward ? Visibility.Collapsed : Visibility.Visible;
        OverflowUp.Visibility = overflow.ShowUp ? Visibility.Collapsed : Visibility.Visible;
        OverflowRefresh.Visibility = overflow.ShowRefresh ? Visibility.Collapsed : Visibility.Visible;
        OverflowButton.Visibility = overflow.ShowOverflow ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackClicked?.Invoke(this, e);

    private void ForwardButton_Click(object sender, RoutedEventArgs e) => ForwardClicked?.Invoke(this, e);

    private void UpButton_Click(object sender, RoutedEventArgs e) => UpClicked?.Invoke(this, e);

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshClicked?.Invoke(this, e);
}
