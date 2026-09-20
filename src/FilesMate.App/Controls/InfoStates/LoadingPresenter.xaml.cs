using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.InfoStates;

public sealed partial class LoadingPresenter : UserControl
{
    public static readonly DependencyProperty IsShowingProperty = DependencyProperty.Register(
        nameof(IsShowing),
        typeof(bool),
        typeof(LoadingPresenter),
        new PropertyMetadata(false, OnShowingChanged));

    public LoadingPresenter()
    {
        InitializeComponent();
    }

    public bool IsShowing
    {
        get => (bool)GetValue(IsShowingProperty);
        set => SetValue(IsShowingProperty, value);
    }

    private static void OnShowingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LoadingPresenter presenter && presenter.Bar is not null)
        {
            presenter.Bar.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
