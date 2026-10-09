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
        InitializeFileTypography();
        BuildAccentSwatches();
        ActualThemeChanged += (_, _) =>
        {
            if (_skinOwnerClosed) return;
            PaintAccentSwatches(_viewModel?.Current.Accent ?? AccentKind.Default);
            RefreshSkinThemePreviews();
        };
        _viewModel = App.AppearanceViewModel;
        InitializeSkinEditor();
        if (_viewModel is null)
        {
            return;
        }

        Loaded += (_, _) =>
        {
            AttachSkinOwnerWindow();
            _viewModel.Changed -= OnViewModelChanged;
            _viewModel.Changed += OnViewModelChanged;
            SyncFromViewModel();
            RefreshSkinThemePreviews(force: true);
        };
        Unloaded += (_, _) =>
        {
            _viewModel.Changed -= OnViewModelChanged;
            _transparencySaveTimer?.Stop();
            CancelSkinEditorPreview();
            DetachSkinOwnerWindow();
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
        GlassHeader.Text = StringTable.Get("Appearance_MaterialMotion");
        BackdropCard.Title = StringTable.Get("Backdrop");
        BackdropCard.Description = StringTable.Get("BackdropDescription");
        BackdropAcrylicItem.Content = StringTable.Get("BackdropAcrylic");
        BackdropMicaItem.Content = StringTable.Get("BackdropMica");
        BackdropMicaAltItem.Content = StringTable.Get("BackdropMicaAlt");
        BackdropSolidItem.Content = StringTable.Get("BackdropSolid");
        AccentHeader.Text = StringTable.Get("AccentColor");
        AccentLead.Text = StringTable.Get("Skin_AccentLead");
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

    private void AppearancePage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (AppearanceRoot is null) return;
        var inset = e.NewSize.Width < 560 ? 8 : 24;
        AppearanceRoot.Margin = new Thickness(inset, 0, inset, 24);
    }

    private void SkinSelectors_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SkinModeBox is null || SkinRoleBox is null) return;
        var stacked = e.NewSize.Width < 360;
        Grid.SetColumnSpan(SkinModeBox, stacked ? 2 : 1);
        Grid.SetRow(SkinRoleBox, stacked ? 1 : 0);
        Grid.SetColumn(SkinRoleBox, stacked ? 0 : 1);
        Grid.SetColumnSpan(SkinRoleBox, stacked ? 2 : 1);
    }

    private void SkinRgbFields_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SkinRedBox is null || SkinGreenBox is null || SkinBlueBox is null) return;
        var stacked = e.NewSize.Width < 256;
        var fields = new[] { SkinRedBox, SkinGreenBox, SkinBlueBox };
        for (var index = 0; index < fields.Length; index++)
        {
            Grid.SetRow(fields[index], stacked ? index : 0);
            Grid.SetColumn(fields[index], stacked ? 0 : index);
            Grid.SetColumnSpan(fields[index], stacked ? 3 : 1);
        }
    }

    private void SkinTools_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not StackPanel panel || panel.Children.Count == 0) return;
        var preferred = panel.Children.OfType<FrameworkElement>().Sum(child => child.DesiredSize.Width)
            + panel.Spacing * (panel.Children.Count - 1);
        panel.Orientation = e.NewSize.Width < preferred ? Orientation.Vertical : Orientation.Horizontal;
    }

    private void SkinUtilityActions_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SkinJsonActions is null || SkinHistoryActions is null) return;
        var preferred = SkinHistoryActions.Children.OfType<FrameworkElement>().Sum(child => child.DesiredSize.Width)
            + SkinJsonActions.Children.OfType<FrameworkElement>().Sum(child => child.DesiredSize.Width) + 32;
        var stacked = e.NewSize.Width < preferred;
        Grid.SetColumnSpan(SkinHistoryActions, stacked ? 2 : 1);
        Grid.SetRow(SkinJsonActions, stacked ? 1 : 0);
        Grid.SetColumn(SkinJsonActions, stacked ? 0 : 1);
        Grid.SetColumnSpan(SkinJsonActions, stacked ? 2 : 1);
    }

    private void AccentPaletteContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (AccentPreviewPane is null || AccentHost is null) return;
        // Four rows balance the sample and explanation; keep a readable English
        // preview column, then stack naturally in the narrow settings pane.
        var swatchWidth = AccentSwatchLayout.MaximumWidth;
        var stacked = e.NewSize.Width < swatchWidth + 20 + 280;
        AccentPaletteContent.ColumnDefinitions[0].Width = stacked
            ? new GridLength(1, GridUnitType.Star) : new GridLength(swatchWidth);
        AccentHost.MaxWidth = swatchWidth;
        Grid.SetColumnSpan(AccentHost, stacked ? 2 : 1);
        Grid.SetRow(AccentPreviewPane, stacked ? 1 : 0);
        Grid.SetColumn(AccentPreviewPane, stacked ? 0 : 1);
        Grid.SetColumnSpan(AccentPreviewPane, stacked ? 2 : 1);
    }

    private void OnViewModelChanged()
    {
        DispatcherQueue.TryEnqueue(() => { if (!_skinOwnerClosed && IsLoaded) SyncFromViewModel(); });
    }

    private void ThemeCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ThemeChipsHost is null || ThemeSystem is null || ThemeLight is null || ThemeDark is null) return;
        // Include localized label measurements in the compact theme choices.
        static double ChipWidth(Button chip) => Math.Max(chip.DesiredSize.Width,
            20 + chip.Padding.Left + chip.Padding.Right + chip.BorderThickness.Left + chip.BorderThickness.Right);
        var available = Math.Max(0, e.NewSize.Width - SettingsRowLayout.HorizontalInset * 2);
        var preferred = ChipWidth(ThemeSystem) + ChipWidth(ThemeLight) + ChipWidth(ThemeDark) + ThemeChipsHost.Spacing * 2;
        ThemeChipsHost.Orientation = available < preferred ? Orientation.Vertical : Orientation.Horizontal;
    }

    private async void ThemeSystem_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(() => _viewModel!.SetThemeAsync(AppThemeKind.System));

    private async void ThemeLight_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(() => _viewModel!.SetThemeAsync(AppThemeKind.Light));

    private async void ThemeDark_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(() => _viewModel!.SetThemeAsync(AppThemeKind.Dark));


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
        if (_syncing || _viewModel is null || AnotherWindowOwnsPreview || GlassEffectBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var glassEffect = (item.Tag as string) switch
        {
            "Off" => GlassEffectMode.Off,
            "Immersive" => GlassEffectMode.Immersive,
            _ => GlassEffectMode.Balanced,
        };
        // This explicit preset choice is newer than a still-debounced slider
        // preview. An already-started save is ordered before the preset commit.
        _transparencySaveTimer?.Stop();
        _pendingTransparency = null;
        if (!TryAcquireSkinPreview()) return;
        await _viewModel.SetGlassEffectAsync(_skinPreviewOwner, _skinPreviewSession, glassEffect);
    }

    private void TransparencySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncing || _viewModel is null) return;
        if (!TryAcquireSkinPreview()) return;
        var percent = (int)Math.Round(e.NewValue);
        _pendingTransparency = percent;
        TransparencyValue.Text = $"{percent}%";
        try { _viewModel.TryPreviewTransparencyPercent(_skinPreviewOwner, _skinPreviewSession, percent); }
        catch (Exception error)
        {
            App.LogFailure("TransparencyPreview", error);
            _pendingTransparency = null;
            _transparencySaveTimer?.Stop();
            SyncFromViewModel();
            return;
        }
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
                var session = _skinPreviewSession;
                var saved = await _viewModel.SetTransparencyPercentAsync(_skinPreviewOwner, session, percent);
                // A failed older write must not strand a newer slider value whose
                // debounce fired while this loop was busy. Never retry the failed value.
                if (!saved && !_viewModel.IsPreviewOwner(_skinPreviewOwner, session)) break;
            }
            if (!_skinOwnerClosed && IsLoaded) SyncFromViewModel();
        }
        finally
        {
            _savingTransparency = false;
            // If the old session was canceled and a new drag began during its save,
            // reschedule only that newer, still-owned intent.
            if (!_skinOwnerClosed && IsLoaded && _pendingTransparency is not null
                && _viewModel.IsPreviewOwner(_skinPreviewOwner, _skinPreviewSession))
                _transparencySaveTimer?.Start();
        }
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
        if (_skinOwnerClosed || _viewModel is null)
        {
            return;
        }

        _syncing = true;
        var settings = _viewModel.Effective;
        SyncFileTypography(settings);
        SelectTheme(settings.Theme);
        SelectCombo(BackdropBox, settings.Backdrop.ToString());
        StatusBarToggle.IsOn = settings.ShowStatusBar;
        FileIconsToggle.IsOn = settings.UseBundledFileIcons;
        ToolbarToggle.IsOn = settings.ShowToolbar;
        SelectCombo(GlassEffectBox, settings.GlassEffect.ToString());
        var percent = _pendingTransparency ?? settings.EffectiveTransparencyPercent;
        TransparencySlider.Value = percent;
        TransparencyValue.Text = $"{percent}%";
        TransparencySlider.IsEnabled = !AnotherWindowOwnsPreview && settings.Backdrop != BackdropKind.Solid && settings.GlassEffect != GlassEffectMode.Off;
        GlassEffectBox.IsEnabled = !AnotherWindowOwnsPreview;
        TransparencyCard.Description = StringTable.Get(TransparencySlider.IsEnabled ? "TransparencyDescription" : "TransparencyDisabled");
        SelectCombo(ReduceMotionBox, settings.ReduceMotion == ReduceMotionKind.On ? "On" : "System");
        PaintAccentSwatches(settings.Accent);
        ErrorText.Text = _viewModel.ErrorText ?? string.Empty;
        ErrorText.Visibility = string.IsNullOrEmpty(_viewModel.ErrorText) ? Visibility.Collapsed : Visibility.Visible;
        SyncSkinEditor();
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
        Theming.ThemeResources.Bind(button, Control.BorderBrushProperty, selected ? "FilesMate.Glass.AccentBrush" : "FilesMate.Glass.BorderBrush");
    }


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
        foreach (var swatch in AccentPalette.Presets.Where(s => s.Kind != AccentKind.Default))
        {
            var color = Color.FromArgb(
                (byte)(swatch.Argb >> 24),
                (byte)(swatch.Argb >> 16),
                (byte)(swatch.Argb >> 8),
                (byte)swatch.Argb);
            var button = new Button
            {
                Tag = swatch.Kind,
                Width = AccentSwatchLayout.SwatchSize,
                Height = AccentSwatchLayout.SwatchSize,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Content = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(color),
                    Child = new FontIcon { Glyph = "\uE73E", FontSize = 15, Visibility = Visibility.Collapsed } },
            };
            Theming.ThemeResources.Bind(button, Control.BorderBrushProperty, "FilesMate.Glass.BorderBrush");
            AutomationProperties.SetName(button, StringTable.Get(swatch.NameKey));
            ToolTipService.SetToolTip(button, StringTable.Get(swatch.NameKey));
            button.Click += Accent_Click;
            AccentHost.Children.Add(button);
        }
    }

    private async void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing || _viewModel is null || AnotherWindowOwnsPreview || _skinBusy || sender is not Button { Tag: AccentKind accent }) return;
        await CommitOrdinarySkinSelectionAsync(() => _viewModel.SelectAccentAsync(_skinPreviewOwner, accent, SkinActualDark));
    }

    private void PaintAccentSwatches(AccentKind selected)
    {
        var dark = SkinActualDark;
        var current = _skinDraft is not null && (_pendingSkinPreview || _viewModel?.HasSkinPreview == true)
            ? _skinDraft.Resolve(dark).Accent
            : _viewModel is not null ? SkinPalette.For(dark, _viewModel.Effective).Accent
            : AccentPalette.Resolve(selected, null, dark);
        var matched = AccentPalette.Presets.FirstOrDefault(s => AccentPalette.Resolve(s.Kind, null, dark) == current);
        AccentCurrentName.Text = matched.NameKey is not null ? StringTable.Get(matched.NameKey) : StringTable.Get("Accent_Custom");
        AccentCurrentHex.Text = SkinColors.ToHex(current);
        AccentCurrentColor.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(current >> 16), (byte)(current >> 8), (byte)current));
        foreach (var button in AccentHost.Children.OfType<Button>())
        {
            button.IsEnabled = !_skinBusy && !AnotherWindowOwnsPreview;
            if (button.Tag is AccentKind accent && button.Content is Border well && well.Background is SolidColorBrush brush)
            {
                var value = AccentPalette.Resolve(accent, null, SkinActualDark);
                brush.Color = Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            }
            var isSelected = button.Tag is AccentKind kind && AccentPalette.Resolve(kind, null, dark) == current;
            button.BorderThickness = new Thickness(isSelected ? 2 : 1);
            Theming.ThemeResources.Bind(button, Control.BorderBrushProperty, isSelected ? "FilesMate.Glass.AccentBrush" : "FilesMate.Glass.BorderBrush");
            if (button.Content is Border { Child: FontIcon check })
            {
                check.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                var ink = AccentPalette.Foreground(AccentPalette.Resolve((AccentKind)button.Tag, null, dark));
                check.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(ink >> 16), (byte)(ink >> 8), (byte)ink));
            }
        }
    }
}
