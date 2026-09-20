using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace FilesMate.App.Views;

public sealed partial class AppearancePage : UserControl
{
    private readonly AppearanceSettingsViewModel? _viewModel;
    private bool _syncing;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _transparencySaveTimer;
    private int? _pendingTransparency;
    private bool _savingTransparency;

    public AppearancePage()
    {
        InitializeComponent();
        ApplyStrings();
        BuildAccentSwatches();
        _viewModel = App.AppearanceViewModel;
        if (_viewModel is null)
        {
            return;
        }

        Loaded += (_, _) =>
        {
            _viewModel.Changed -= OnViewModelChanged;
            _viewModel.Changed += OnViewModelChanged;
            SyncFromViewModel();
        };
        Unloaded += async (_, _) =>
        {
            _viewModel.Changed -= OnViewModelChanged;
            _transparencySaveTimer?.Stop();
            await FlushTransparencyAsync();
        };
        SyncFromViewModel();
    }

    private void ApplyStrings()
    {
        Heading.Text = StringTable.Get("AppearanceTitle");
        Lead.Text = StringTable.Get("ThemeDescription");
        ThemeCard.Title = StringTable.Get("ThemeSection");
        ThemeCard.Description = StringTable.Get("ThemeDescription");
        ThemeSystemLabel.Text = StringTable.Get("ThemeSystem");
        ThemeLightLabel.Text = StringTable.Get("ThemeLight");
        ThemeDarkLabel.Text = StringTable.Get("ThemeDark");
        DisplayHeader.Text = StringTable.Get("Display");
        FileIconsCard.Title = StringTable.Get("UseBundledFileIcons");
        FileIconsCard.Description = StringTable.Get("UseBundledFileIconsHint");
        FileIconsToggle.OnContent = StringTable.Get("On");
        FileIconsToggle.OffContent = StringTable.Get("Off");
        AutomationProperties.SetName(FileIconsToggle, StringTable.Get("UseBundledFileIcons"));
        ShellStyleCard.Title = StringTable.Get("ShellStyle");
        ShellStyleCard.Description = StringTable.Get("ShellStyleDescription");
        ShellLayeredItem.Content = StringTable.Get("ShellLayered");
        ShellUnifiedItem.Content = StringTable.Get("ShellUnified");
        AutomationProperties.SetName(ShellStyleBox, StringTable.Get("ShellStyle"));
        GlassHeader.Text = StringTable.Get("GlassSection");
        BackdropCard.Title = StringTable.Get("Backdrop");
        BackdropCard.Description = StringTable.Get("BackdropDescription");
        BackdropAcrylicItem.Content = StringTable.Get("BackdropAcrylic");
        BackdropMicaItem.Content = StringTable.Get("BackdropMica");
        BackdropMicaAltItem.Content = StringTable.Get("BackdropMicaAlt");
        BackdropSolidItem.Content = StringTable.Get("BackdropSolid");
        AccentHeader.Text = StringTable.Get("AccentColor");
        AccentLead.Text = StringTable.Get("AccentDescription");
        StatusBarCard.Title = StringTable.Get("StatusBar");
        StatusBarCard.Description = StringTable.Get("StatusBarDescription");
        StatusBarToggle.OnContent = StringTable.Get("On");
        StatusBarToggle.OffContent = StringTable.Get("Off");
        ToolbarCard.Title = StringTable.Get("Toolbar");
        ToolbarCard.Description = StringTable.Get("ToolbarDescription");
        ToolbarToggle.OnContent = StringTable.Get("On");
        ToolbarToggle.OffContent = StringTable.Get("Off");
        GlassEffectCard.Title = StringTable.Get("GlassEffects");
        GlassEffectCard.Description = StringTable.Get("GlassEffectsDescription");
        GlassEffectOffItem.Content = StringTable.Get("GlassEffectOff");
        GlassEffectBalancedItem.Content = StringTable.Get("GlassEffectBalanced");
        GlassEffectImmersiveItem.Content = StringTable.Get("GlassEffectImmersive");
        TransparencyCard.Title = StringTable.Get("Transparency");
        AutomationProperties.SetName(TransparencySlider, StringTable.Get("Transparency"));
        ReduceMotionCard.Title = StringTable.Get("ReduceMotion");
        ReduceMotionCard.Description = StringTable.Get("ReduceMotionDescription");
        ReduceMotionSystemItem.Content = StringTable.Get("ReduceMotionSystem");
        ReduceMotionOnItem.Content = StringTable.Get("ReduceMotionOn");
        AutomationProperties.SetName(ThemeSystem, StringTable.Get("ThemeSystem"));
        AutomationProperties.SetName(ThemeLight, StringTable.Get("ThemeLight"));
        AutomationProperties.SetName(ThemeDark, StringTable.Get("ThemeDark"));
    }

    private void OnViewModelChanged()
    {
        DispatcherQueue.TryEnqueue(() => { if (IsLoaded) SyncFromViewModel(); });
    }

    private async void ThemeSystem_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(() => _viewModel!.SetThemeAsync(AppThemeKind.System));

    private async void ThemeLight_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(() => _viewModel!.SetThemeAsync(AppThemeKind.Light));

    private async void ThemeDark_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(() => _viewModel!.SetThemeAsync(AppThemeKind.Dark));

    private async void ShellStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewModel is null || ShellStyleBox.SelectedItem is not ComboBoxItem item) return;
        await _viewModel.SetShellStyleAsync(item.Tag as string == "Unified" ? ShellStyleKind.Unified : ShellStyleKind.Layered);
    }

    private async void BackdropBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewModel is null || BackdropBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var backdrop = (item.Tag as string) switch
        {
            "Solid" => BackdropKind.Solid,
            "Mica" => BackdropKind.Mica,
            "MicaAlt" => BackdropKind.MicaAlt,
            _ => BackdropKind.Acrylic,
        };
        await _viewModel.SetBackdropAsync(backdrop);
    }

    private async void StatusBarToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || _viewModel is null)
        {
            return;
        }

        await _viewModel.SetShowStatusBarAsync(StatusBarToggle.IsOn);
    }

    private async void FileIconsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || _viewModel is null) return;
        await _viewModel.SetUseBundledFileIconsAsync(FileIconsToggle.IsOn);
    }

    private async void ToolbarToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || _viewModel is null)
        {
            return;
        }

        await _viewModel.SetShowToolbarAsync(ToolbarToggle.IsOn);
    }

    private async void ReduceMotionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewModel is null || ReduceMotionBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var reduce = string.Equals(item.Tag as string, "On", StringComparison.Ordinal)
            ? ReduceMotionKind.On
            : ReduceMotionKind.System;
        await _viewModel.SetReduceMotionAsync(reduce);
    }

    private async void GlassEffectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewModel is null || GlassEffectBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var glassEffect = (item.Tag as string) switch
        {
            "Off" => GlassEffectMode.Off,
            "Immersive" => GlassEffectMode.Immersive,
            _ => GlassEffectMode.Balanced,
        };
        await _viewModel.SetGlassEffectAsync(glassEffect);
    }

    private void TransparencySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncing || _viewModel is null) return;
        var percent = (int)Math.Round(e.NewValue);
        _pendingTransparency = percent;
        TransparencyValue.Text = $"{percent}%";
        _viewModel.PreviewTransparencyPercent(percent);
        if (_transparencySaveTimer is null)
        {
            _transparencySaveTimer = DispatcherQueue.CreateTimer();
            _transparencySaveTimer.IsRepeating = false;
            _transparencySaveTimer.Interval = TimeSpan.FromMilliseconds(250);
            _transparencySaveTimer.Tick += async (_, _) => await FlushTransparencyAsync();
        }
        _transparencySaveTimer.Stop();
        _transparencySaveTimer.Start();
    }

    private async Task FlushTransparencyAsync()
    {
        if (_savingTransparency || _viewModel is null) return;
        _savingTransparency = true;
        try
        {
            // Serialize writes; a newer drag value always wins over a save already in progress.
            while (_pendingTransparency is { } percent)
            {
                _pendingTransparency = null;
                await _viewModel.SetTransparencyPercentAsync(percent);
            }
            if (IsLoaded) SyncFromViewModel();
        }
        finally { _savingTransparency = false; }
    }

    private async Task ApplyAsync(Func<Task> update)
    {
        if (_syncing || _viewModel is null)
        {
            return;
        }

        await update();
        SyncFromViewModel();
    }

    private void SyncFromViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _syncing = true;
        var settings = _viewModel.Current;
        SelectTheme(settings.Theme);
        SelectCombo(ShellStyleBox, settings.ShellStyle.ToString());
        SelectCombo(BackdropBox, settings.Backdrop.ToString());
        StatusBarToggle.IsOn = settings.ShowStatusBar;
        FileIconsToggle.IsOn = settings.UseBundledFileIcons;
        ToolbarToggle.IsOn = settings.ShowToolbar;
        SelectCombo(GlassEffectBox, settings.GlassEffect.ToString());
        var percent = _pendingTransparency ?? settings.EffectiveTransparencyPercent;
        TransparencySlider.Value = percent;
        TransparencyValue.Text = $"{percent}%";
        TransparencySlider.IsEnabled = settings.Backdrop != BackdropKind.Solid && settings.GlassEffect != GlassEffectMode.Off;
        TransparencyCard.Description = StringTable.Get(TransparencySlider.IsEnabled ? "TransparencyDescription" : "TransparencyDisabled");
        if (_pendingTransparency is not null) _viewModel.PreviewTransparencyPercent(percent);
        SelectCombo(ReduceMotionBox, settings.ReduceMotion == ReduceMotionKind.On ? "On" : "System");
        PaintAccentSwatches(settings.Accent);
        ErrorText.Text = _viewModel.ErrorText ?? string.Empty;
        ErrorText.Visibility = string.IsNullOrEmpty(_viewModel.ErrorText) ? Visibility.Collapsed : Visibility.Visible;
        _syncing = false;
    }

    private void SelectTheme(AppThemeKind theme)
    {
        PaintPill(ThemeSystem, theme == AppThemeKind.System);
        PaintPill(ThemeLight, theme == AppThemeKind.Light);
        PaintPill(ThemeDark, theme == AppThemeKind.Dark);
    }

    private static void PaintPill(Button button, bool selected)
    {
        button.BorderThickness = new Thickness(selected ? 2 : 1);
        button.BorderBrush = Theme(selected ? "FilesMate.Glass.AccentBrush" : "FilesMate.Glass.BorderBrush");
    }

    private static Brush? Theme(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : null;

    private static void SelectCombo(ComboBox box, string tag)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private void BuildAccentSwatches()
    {
        AccentHost.Children.Clear();
        StackPanel? row = null;
        var index = 0;
        foreach (var swatch in AccentPalette.Presets)
        {
            if (index % 8 == 0)
            {
                row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                AccentHost.Children.Add(row);
            }

            var color = Color.FromArgb(
                (byte)(swatch.Argb >> 24),
                (byte)(swatch.Argb >> 16),
                (byte)(swatch.Argb >> 8),
                (byte)swatch.Argb);
            var button = new Button
            {
                Tag = swatch.Kind,
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = Theme("FilesMate.Glass.BorderBrush"),
            };
            AutomationProperties.SetName(button, StringTable.Get(swatch.NameKey));
            ToolTipService.SetToolTip(button, StringTable.Get(swatch.NameKey));
            button.Click += Accent_Click;
            row!.Children.Add(button);
            index++;
        }
    }

    private async void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing || _viewModel is null || sender is not Button { Tag: AccentKind kind })
        {
            return;
        }

        await _viewModel.SetAccentAsync(kind);
    }

    private void PaintAccentSwatches(AccentKind selected)
    {
        foreach (var panel in AccentHost.Children.OfType<StackPanel>())
        {
            foreach (var button in panel.Children.OfType<Button>())
            {
                var isSelected = button.Tag is AccentKind kind && kind == selected;
                button.BorderThickness = new Thickness(isSelected ? 3 : 2);
                button.BorderBrush = Theme(isSelected ? "FilesMate.Glass.AccentBrush" : "FilesMate.Glass.BorderBrush");
            }
        }
    }
}
