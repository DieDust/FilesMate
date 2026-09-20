using System.Diagnostics;

using FilesMate.App.Navigation;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace FilesMate.App.Controls.FileSurface;

[ContentProperty(Name = nameof(Body))]
public sealed partial class FilePaneChrome : UserControl
{
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body),
        typeof(UIElement),
        typeof(FilePaneChrome),
        new PropertyMetadata(null, OnBodyChanged));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(FilePaneChrome),
        new PropertyMetadata(true, OnChromeChanged));

    public static readonly DependencyProperty IsDualPaneProperty = DependencyProperty.Register(
        nameof(IsDualPane),
        typeof(bool),
        typeof(FilePaneChrome),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty IsTrailingPaneProperty = DependencyProperty.Register(
        nameof(IsTrailingPane),
        typeof(bool),
        typeof(FilePaneChrome),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading),
        typeof(bool),
        typeof(FilePaneChrome),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText),
        typeof(string),
        typeof(FilePaneChrome),
        new PropertyMetadata(null, OnChromeChanged));

    public static readonly DependencyProperty ItemCountProperty = DependencyProperty.Register(
        nameof(ItemCount),
        typeof(int),
        typeof(FilePaneChrome),
        new PropertyMetadata(0, OnChromeChanged));

    public static readonly DependencyProperty FilterQueryProperty = DependencyProperty.Register(
        nameof(FilterQuery),
        typeof(string),
        typeof(FilePaneChrome),
        new PropertyMetadata(null, OnChromeChanged));

    public static readonly DependencyProperty CanGoUpProperty = DependencyProperty.Register(
        nameof(CanGoUp),
        typeof(bool),
        typeof(FilePaneChrome),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty NavigationGenerationProperty = DependencyProperty.Register(
        nameof(NavigationGeneration),
        typeof(long),
        typeof(FilePaneChrome),
        new PropertyMetadata(0L, OnChromeChanged));

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(FilePaneChrome),
        new PropertyMetadata(string.Empty, OnChromeChanged));

    public static readonly DependencyProperty SelectionTextProperty = DependencyProperty.Register(
        nameof(SelectionText),
        typeof(string),
        typeof(FilePaneChrome),
        new PropertyMetadata(string.Empty, OnChromeChanged));

    public static readonly DependencyProperty SizeTextProperty = DependencyProperty.Register(
        nameof(SizeText),
        typeof(string),
        typeof(FilePaneChrome),
        new PropertyMetadata(string.Empty, OnChromeChanged));

    public static readonly DependencyProperty ZoomTextProperty = DependencyProperty.Register(
        nameof(ZoomText),
        typeof(string),
        typeof(FilePaneChrome),
        new PropertyMetadata(string.Empty, OnChromeChanged));

    public static readonly DependencyProperty ShowStatusBarProperty = DependencyProperty.Register(
        nameof(ShowStatusBar),
        typeof(bool),
        typeof(FilePaneChrome),
        new PropertyMetadata(true, OnChromeChanged));

    private readonly FilePanePresentation _presentation = new();
    private readonly Stopwatch _loadingWatch = new();
    private DispatcherQueueTimer? _delay;
    private long _timerGeneration = long.MinValue;

    public FilePaneChrome()
    {
        InitializeComponent();
        Loaded += FilePaneChrome_Loaded;
        Unloaded += FilePaneChrome_Unloaded;
        ApplyChrome();
    }

    private void FilePaneChrome_Loaded(object sender, RoutedEventArgs e)
    {
        _delay ??= DispatcherQueue.CreateTimer();
        _delay.IsRepeating = false;
        _delay.Interval = TimeSpan.FromMilliseconds(FilePanePresentation.LoadingDelayMilliseconds);
        _delay.Tick -= Delay_Tick;
        _delay.Tick += Delay_Tick;
        ApplyChrome();
    }

    private void FilePaneChrome_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_delay is null)
        {
            return;
        }

        _delay.Stop();
        _delay.Tick -= Delay_Tick;
    }

    private void Delay_Tick(DispatcherQueueTimer sender, object args) => ApplyChrome();

    public event RoutedEventHandler? RetryRequested;

    public event RoutedEventHandler? GoUpRequested;

    public UIElement? Body
    {
        get => (UIElement?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool IsDualPane
    {
        get => (bool)GetValue(IsDualPaneProperty);
        set => SetValue(IsDualPaneProperty, value);
    }

    public bool IsTrailingPane
    {
        get => (bool)GetValue(IsTrailingPaneProperty);
        set => SetValue(IsTrailingPaneProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public string? ErrorText
    {
        get => (string?)GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    public int ItemCount
    {
        get => (int)GetValue(ItemCountProperty);
        set => SetValue(ItemCountProperty, value);
    }

    public string? FilterQuery
    {
        get => (string?)GetValue(FilterQueryProperty);
        set => SetValue(FilterQueryProperty, value);
    }

    public bool CanGoUp
    {
        get => (bool)GetValue(CanGoUpProperty);
        set => SetValue(CanGoUpProperty, value);
    }

    public long NavigationGeneration
    {
        get => (long)GetValue(NavigationGenerationProperty);
        set => SetValue(NavigationGenerationProperty, value);
    }

    public string? StatusText
    {
        get => (string?)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public string? SelectionText
    {
        get => (string?)GetValue(SelectionTextProperty);
        set => SetValue(SelectionTextProperty, value);
    }

    public string? SizeText
    {
        get => (string?)GetValue(SizeTextProperty);
        set => SetValue(SizeTextProperty, value);
    }

    public string? ZoomText
    {
        get => (string?)GetValue(ZoomTextProperty);
        set => SetValue(ZoomTextProperty, value);
    }

    public bool ShowStatusBar
    {
        get => (bool)GetValue(ShowStatusBarProperty);
        set => SetValue(ShowStatusBarProperty, value);
    }

    private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FilePaneChrome chrome && chrome.BodyPresenter is not null)
        {
            chrome.BodyPresenter.Content = e.NewValue;
        }
    }

    private static void OnChromeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FilePaneChrome chrome)
        {
            chrome.ApplyChrome();
        }
    }

    private void ApplyChrome()
    {
        if (Root is null || PaneCard is null || InactiveOverlay is null || LoadingHost is null || Info is null || StatusBar is null)
        {
            return;
        }

        // Keep the base material as a XAML ThemeResource. Looking up a themed
        // brush through Application.Current.Resources here can return a stale
        // dictionary value after the window theme changes (and was the reason
        // the light file surface could render with the dark fallback). The
        // inactive state is a separate overlay so theme resolution stays in
        // the XAML resource system.
        InactiveOverlay.Opacity = IsActive ? 0 : 1;
        ApplyPaneShape();

        SyncLoadingTimer();
        var elapsed = _loadingWatch.IsRunning ? _loadingWatch.Elapsed : TimeSpan.Zero;
        _presentation.Apply(new FilePaneSnapshot(
            NavigationGeneration,
            IsLoading,
            ItemCount,
            ErrorText,
            FilterQuery,
            CanGoUp,
            elapsed));

        LoadingHost.IsShowing = _presentation.ShowLoadingIndicator;
        // Keep the file surface in the input tree even for an empty directory.
        // Collapsing it loses keyboard focus and removes the background drop target.
        BodyPresenter.Opacity = _presentation.ShowList || _presentation.Kind == FilePaneKind.Empty ? 1 : 0;
        Info.Visibility = _presentation.ShowInfo ? Visibility.Visible : Visibility.Collapsed;
        Info.IsHitTestVisible = !string.IsNullOrEmpty(_presentation.PrimaryAction)
            || !string.IsNullOrEmpty(_presentation.SecondaryAction);
        if (_presentation.ShowInfo)
        {
            Info.Apply(
                _presentation.Glyph,
                _presentation.Title,
                _presentation.Body,
                _presentation.PrimaryAction,
                _presentation.SecondaryAction);
        }

        StatusBar.Visibility = ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        StatusBar.Apply(StatusText, SelectionText, ZoomText, SizeText);
    }

    private void ApplyPaneShape()
    {
        var radius = CardCornerRadius();
        if (!IsDualPane)
        {
            PaneCard.Margin = new Thickness(8, 0, 8, 8);
            PaneCard.CornerRadius = radius;
            InactiveOverlay.CornerRadius = radius;
            return;
        }

        if (IsTrailingPane)
        {
            PaneCard.Margin = new Thickness(0, 0, 8, 8);
            var trailing = new CornerRadius(0, radius.TopRight, radius.BottomRight, 0);
            PaneCard.CornerRadius = trailing;
            InactiveOverlay.CornerRadius = trailing;
            return;
        }

        PaneCard.Margin = new Thickness(8, 0, 0, 8);
        var leading = new CornerRadius(radius.TopLeft, 0, 0, radius.BottomLeft);
        PaneCard.CornerRadius = leading;
        InactiveOverlay.CornerRadius = leading;
    }

    private static CornerRadius CardCornerRadius() =>
        Application.Current.Resources.TryGetValue("FilesMate.Corner.Card", out var value)
            && value is CornerRadius radius
            ? radius
            : new CornerRadius(12);

    private void SyncLoadingTimer()
    {
        if (_delay is null)
        {
            return;
        }

        if (IsLoading)
        {
            if (_timerGeneration != NavigationGeneration)
            {
                _timerGeneration = NavigationGeneration;
                _loadingWatch.Restart();
                _delay.Stop();
                _delay.Start();
            }

            return;
        }

        _delay.Stop();
        _loadingWatch.Reset();
        _timerGeneration = long.MinValue;
    }

    private void Info_PrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (_presentation.PrimaryAction == "Go up")
        {
            GoUpRequested?.Invoke(this, e);
            return;
        }

        RetryRequested?.Invoke(this, e);
    }

    private void Info_SecondaryClicked(object sender, RoutedEventArgs e) => GoUpRequested?.Invoke(this, e);

    private static Microsoft.UI.Xaml.Media.Brush? Theme(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out var value)
            && value is Microsoft.UI.Xaml.Media.Brush brush
            ? brush
            : null;
    }
}
