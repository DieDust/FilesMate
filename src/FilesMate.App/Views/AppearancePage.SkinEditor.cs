using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace FilesMate.App.Views;

public sealed partial class AppearancePage
{
    private readonly Guid _skinPreviewOwner = Guid.NewGuid();
    private Guid _skinPreviewSession;
    private Guid _skinDraftSession;
    private Microsoft.UI.Xaml.Window? _skinOwnerWindow;
    private bool _skinOwnerClosed;
    private bool _nativeSkinPickerOpen;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _skinFocusTimer;
    private bool _skinFocusCheckPending;
    private long _skinFocusRevision;
    private Guid? _observedForeignPreviewSession;
    private SkinPackage? _skinDraft;
    private AppearanceSettings? _lastCommittedAppearance;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _skinPreviewTimer;
    private bool _pendingSkinPreview;
    private bool _syncingSkinInputs;
    private bool _skinBusy;
    private bool _invalidSkinColor;
    private bool _invalidSkinName;
    private long _skinFileRequest;
    private string? _skinMessageKey;
    private SkinPackage? _paintedSkin;
    private bool _paintedDark;
    private uint _paintedAccent;
    private bool? _presetCardsDark;
    private bool _observingSkinSelection;
    private long _observedSkinSelectionRevision;

    private bool SkinActualDark => ActualTheme == ElementTheme.Dark;

    private bool SkinEditingDark => SkinModeBox.SelectedItem is ComboBoxItem { Tag: "Dark" };
    private bool AnotherWindowOwnsPreview => _viewModel?.CurrentPreviewOwner is { } owner && owner != _skinPreviewOwner;
    private SkinColorRole EditingRole => SkinRoleBox.SelectedItem is ComboBoxItem { Tag: string role }
        && Enum.TryParse<SkinColorRole>(role, out var parsed) ? parsed : SkinColorRole.Accent;

    private void InitializeSkinEditor()
    {
        SkinHeader.Text = StringTable.Get("Appearance_ThemeColors");
        SkinAdvancedExpander.Header = StringTable.Get("Skin_Advanced");
        SkinLead.Text = StringTable.Get("Skin_Lead");
        SkinLightItem.Content = StringTable.Get("Skin_Light");
        SkinDarkItem.Content = StringTable.Get("Skin_Dark");
        SkinAccentItem.Content = StringTable.Get("Skin_Accent");
        SkinBackgroundItem.Content = StringTable.Get("Skin_Background");
        SkinTextItem.Content = StringTable.Get("Skin_Text");
        SkinSelectionItem.Content = StringTable.Get("Skin_Selection");
        SkinNameBox.Header = StringTable.Get("Skin_Name");
        SkinHexBox.Header = StringTable.Get("Skin_Hex");
        SkinDerivedHint.Text = StringTable.Get("Skin_DerivedHint");
        SkinApplyButton.Content = StringTable.Get("Skin_ApplySkin");
        SkinCancelButton.Content = StringTable.Get("Skin_Cancel");
        SkinResetButton.Content = StringTable.Get("Skin_Reset");
        SkinRevertButton.Content = StringTable.Get("Skin_Revert");
        SkinImportButton.Content = StringTable.Get("Skin_Import");
        SkinExportButton.Content = StringTable.Get("Skin_Export");
        AutomationProperties.SetName(SkinColorPicker, StringTable.Get("Skin_Colors"));
        AutomationProperties.SetName(SkinModeBox, StringTable.Get("Skin_Section") + " " + StringTable.Get("ThemeSection"));
        AutomationProperties.SetName(SkinRoleBox, StringTable.Get("Skin_Colors"));
        AutomationProperties.SetName(SkinRedBox, "R");
        AutomationProperties.SetName(SkinGreenBox, "G");
        AutomationProperties.SetName(SkinBlueBox, "B");
        _syncingSkinInputs = true;
        SkinModeBox.SelectedIndex = SkinActualDark ? 1 : 0;
        SkinRoleBox.SelectedIndex = 0;
        _syncingSkinInputs = false;
        BuildSkinPresetCards();
    }

    private bool TryAcquireSkinPreview()
    {
        if (_viewModel is null || !_viewModel.TryBeginPreview(_skinPreviewOwner))
        { PaintSkinEditorState(); return false; }
        _skinPreviewSession = _viewModel.PreviewSessionId;
        _skinDraftSession = _viewModel.SkinEditSessionId;
        StartSkinFocusWatch();
        return true;
    }

    private void AttachSkinOwnerWindow()
    {
        DetachSkinOwnerWindow();
        _skinOwnerWindow = App.WindowForElement(this);
        _skinOwnerClosed = false;
        if (_skinOwnerWindow is null) return;
        _skinOwnerWindow.Activated += SkinOwnerWindow_Activated;
        _skinOwnerWindow.Closed += SkinOwnerWindow_Closed;
    }

    private void DetachSkinOwnerWindow()
    {
        if (_skinOwnerWindow is null) return;
        _skinOwnerWindow.Activated -= SkinOwnerWindow_Activated;
        _skinOwnerWindow.Closed -= SkinOwnerWindow_Closed;
        _skinOwnerWindow = null;
    }

    private void SkinOwnerWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated
            && (_nativeSkinPickerOpen || _viewModel?.IsPreviewOwner(_skinPreviewOwner, _skinPreviewSession) == true))
            StartSkinFocusWatch();
    }

    private void SkinOwnerWindow_Closed(object sender, WindowEventArgs args)
    {
        _skinOwnerClosed = true;
        if (_viewModel is not null) _viewModel.Changed -= OnViewModelChanged;
        CancelSkinEditorPreview(4);
        DetachSkinOwnerWindow();
    }

    private void StartSkinFocusWatch()
    {
        if (_skinFocusTimer is null)
        {
            _skinFocusTimer = DispatcherQueue.CreateTimer();
            _skinFocusTimer.IsRepeating = true;
            _skinFocusTimer.Interval = TimeSpan.FromMilliseconds(150);
            _skinFocusTimer.Tick += async (_, _) => await CheckSkinFocusAsync();
        }
        if (!_skinFocusTimer.IsRunning) _skinFocusTimer.Start();
    }

    private async Task CheckSkinFocusAsync()
    {
        if (_skinOwnerClosed || _skinFocusCheckPending) return;
        if (!IsLoaded || (!_nativeSkinPickerOpen
            && _viewModel?.IsPreviewOwner(_skinPreviewOwner, _skinPreviewSession) != true))
        { _skinFocusTimer?.Stop(); return; }
        if (SkinOwnerIsForeground()) return;
        _skinFocusCheckPending = true;
        var revision = _skinFocusRevision;
        var session = _skinPreviewSession;
        var owner = _viewModel?.CurrentPreviewOwner;
        var foreground = SkinGetForegroundWindow();
        try
        {
            // A newly opened same-profile SearchHost may receive focus before the
            // first relay reply. Verify it through the exact profile's pipe, never
            // exempt an unrelated process just because a picker is open.
            var observer = await App.IsAppearancePreviewObserverWindowAsync(foreground);
            if (_skinOwnerClosed || !IsLoaded || revision != _skinFocusRevision || session != _skinPreviewSession
                || owner != _viewModel?.CurrentPreviewOwner || foreground != SkinGetForegroundWindow()) return;
            if (!observer && !SkinOwnerIsForeground()) CancelSkinEditorPreview(2);
        }
        catch (Exception error)
        {
            App.LogFailure("SkinPreviewFocus", error);
            if (!_skinOwnerClosed && IsLoaded && revision == _skinFocusRevision && session == _skinPreviewSession
                && owner == _viewModel?.CurrentPreviewOwner && foreground == SkinGetForegroundWindow()
                && !SkinOwnerIsForeground()) CancelSkinEditorPreview(5);
        }
        finally { _skinFocusCheckPending = false; }
    }

    private bool SkinOwnerIsForeground()
    {
        if (App.WindowForElement(this) is not { } window) return false;
        var foreground = SkinGetForegroundWindow();
        return foreground == window.NativeHandle || (foreground != 0 && SkinGetAncestor(foreground, 3) == window.NativeHandle)
            || App.IsAppearancePreviewObserverWindow(foreground);
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint SkinGetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetAncestor")]
    private static extern nint SkinGetAncestor(nint hwnd, uint flags);

    private static SolidColorBrush SkinBrush(uint argb) => new(Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));

    private void RefreshSkinThemePreviews(bool force = false)
    {
        // Constructor-time ActualTheme can precede attachment to the dark root.
        // Recheck on Loaded, actual-theme changes and every settings sync; the
        // miniature cards and the right sample always use this same page theme.
        if (force || _presetCardsDark != SkinActualDark || SkinPresetGrid.Children.Count != SkinPresets.All.Count)
            BuildSkinPresetCards();
        PaintSkinEditorState();
    }

    private void BuildSkinPresetCards()
    {
        _presetCardsDark = SkinActualDark;
        SkinPresetGrid.Children.Clear();
        foreach (var preset in SkinPresets.All)
        {
            var content = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Stretch };
            content.Children.Add(BuildSkinSample(preset.Package.Resolve(SkinActualDark), compact: true));
            content.Children.Add(new TextBlock { Text = StringTable.Get("Skin_" + preset.Id), FontSize = 12,
                TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button { Tag = preset, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(9),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
            Theming.ThemeResources.Bind(button, Control.BorderBrushProperty, "FilesMate.Glass.BorderBrush");
            AutomationProperties.SetName(button, StringTable.Get("Skin_" + preset.Id));
            button.Click += SkinPreset_Click;
            SkinPresetGrid.Children.Add(button);
        }
        LayoutSkinPresetCards(SkinPresetGrid.ActualWidth);
    }

    // Each illustration uses the package's own brushes. SDK brush instances are never mutated.
    private static Border BuildSkinSample(SkinPalette palette, bool compact)
    {
        var fileColors = palette.FileItemColors();
        Border NavigationSample() => new()
        {
            Background = SkinBrush(palette.Navigation),
            Child = new Border { Background = SkinBrush(palette.Accent), Height = 9,
                Margin = new Thickness(3, 8, 3, 0), VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(3) }
        };
        if (compact)
        {
            var miniature = new Grid { Background = SkinBrush(palette.Content), Height = 60 };
            miniature.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            miniature.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            miniature.Children.Add(NavigationSample());
            var lines = new StackPanel { Spacing = 5, Padding = new Thickness(8) };
            lines.Children.Add(new Border { Background = SkinBrush(palette.Input), Height = 6, CornerRadius = new CornerRadius(3) });
            lines.Children.Add(new Border { Background = SkinBrush(fileColors["FilesMate.FileItem.SelectedBrush"]),
                BorderBrush = SkinBrush(fileColors["FilesMate.FileItem.SelectionBorderBrush"]), BorderThickness = new Thickness(1),
                Height = 13, CornerRadius = new CornerRadius(3) });
            lines.Children.Add(new Border { Background = SkinBrush(palette.Hover), Height = 10, CornerRadius = new CornerRadius(3) });
            Grid.SetColumn(lines, 1); miniature.Children.Add(lines);
            return new Border { BorderBrush = SkinBrush(palette.Outline), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Child = miniature };
        }
        var root = new Grid { Background = SkinBrush(palette.Canvas), ColumnSpacing = 0, RowSpacing = 0 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 14 : 25) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new Border { Background = SkinBrush(palette.Chrome), Padding = new Thickness(7, 4, 7, 4),
            Child = new TextBlock { Text = StringTable.Get("Skin_PreviewTitle"), FontSize = compact ? 10 : 12,
                Foreground = SkinBrush(palette.Text) } };
        Grid.SetColumnSpan(title, 2);
        root.Children.Add(title);
        var nav = NavigationSample();
        Grid.SetRow(nav, 1); root.Children.Add(nav);
        var rows = new StackPanel { Background = SkinBrush(palette.Content), Padding = new Thickness(6), Spacing = 4 };
        rows.Children.Add(new Border { Background = SkinBrush(palette.Input), CornerRadius = new CornerRadius(3),
            Height = compact ? 5 : 9, Margin = new Thickness(0, 0, compact ? 15 : 26, 2) });
        rows.Children.Add(new Border { Background = SkinBrush(fileColors["FilesMate.FileItem.SelectedBrush"]),
            BorderBrush = SkinBrush(fileColors["FilesMate.FileItem.SelectionBorderBrush"]), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 2, 5, 2), Child = new TextBlock { Text = StringTable.Get("Skin_PreviewItem"),
                FontSize = compact ? 10 : 12, Foreground = SkinBrush(fileColors["FilesMate.FileItem.SelectedForegroundBrush"]), TextTrimming = TextTrimming.CharacterEllipsis } });
        rows.Children.Add(new Border { Background = SkinBrush(palette.Hover), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 2, 5, 2), Child = new TextBlock { Text = StringTable.Get("Skin_PreviewHover"),
                FontSize = compact ? 10 : 12, Foreground = SkinBrush(palette.Text), TextTrimming = TextTrimming.CharacterEllipsis } });
        rows.Children.Add(new Border { Background = SkinBrush(palette.Floating), BorderBrush = SkinBrush(palette.Outline),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 2, 5, 2),
            Child = new TextBlock { Text = StringTable.Get("Skin_PreviewMaterial"), FontSize = compact ? 9 : 11,
                Foreground = SkinBrush(palette.Muted), TextTrimming = TextTrimming.CharacterEllipsis } });
        Grid.SetRow(rows, 1); Grid.SetColumn(rows, 1); root.Children.Add(rows);
        return new Border { Background = SkinBrush(palette.Card), BorderBrush = SkinBrush(palette.Outline),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = root };
    }

    private void SkinPresetGrid_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutSkinPresetCards(e.NewSize.Width);
    private void LayoutSkinPresetCards(double width)
    {
        var columns = width >= 640 ? 4 : width >= 360 ? 2 : 1;
        for (var index = 0; index < SkinPresetGrid.Children.Count; index++)
        {
            if (SkinPresetGrid.Children[index] is not FrameworkElement button) continue;
            Grid.SetRow(button, index / columns);
            Grid.SetColumn(button, index % columns);
            Grid.SetColumnSpan(button, 4 / columns);
            Grid.SetColumn(button, (index % columns) * (4 / columns));
        }
    }

    private void SkinEditorFields_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 600;
        SkinEditorFields.ColumnDefinitions[0].Width = stacked
            ? new GridLength(1, GridUnitType.Star) : new GridLength(240);
        SkinColorPicker.MaxWidth = Math.Min(240, Math.Max(0, e.NewSize.Width));
        Grid.SetColumnSpan(SkinColorPicker, stacked ? 2 : 1);
        Grid.SetRow(SkinValueFields, stacked ? 1 : 0);
        Grid.SetColumn(SkinValueFields, stacked ? 0 : 1);
        Grid.SetColumnSpan(SkinValueFields, stacked ? 2 : 1);
    }

    private void SyncSkinEditor()
    {
        if (_viewModel is null) return;
        // A fast save can finish before an observer window receives any queued
        // Changed callback. The durable intent revision still retires its local
        // invalid/unsaved fields, even if it never saw the pending overlay.
        if (_observedSkinSelectionRevision != _viewModel.SkinSelectionRevision)
        {
            _observedSkinSelectionRevision = _viewModel.SkinSelectionRevision;
            _observingSkinSelection = true;
            _skinPreviewTimer?.Stop();
            _pendingSkinPreview = false;
            _invalidSkinColor = _invalidSkinName = false;
            _skinMessageKey = null;
            _skinFileRequest++;
            _skinFocusRevision++;
        }
        if (_viewModel.PendingSkinSelection is { } selected)
        {
            _observingSkinSelection = true;
            if (_skinDraft != selected)
            {
                _skinDraft = selected;
                _invalidSkinColor = _invalidSkinName = false;
                SyncSkinColorInputs();
            }
            PaintSkinEditorState();
            return;
        }
        if (_observingSkinSelection)
        {
            _observingSkinSelection = false;
            ResetSkinDraftFromCurrent();
        }
        if (AnotherWindowOwnsPreview)
        {
            _observedForeignPreviewSession = _viewModel.PreviewSessionId;
            // A page created during another window's preview still starts from the
            // committed palette. It never adopts a foreign draft for its next edit.
            if (_skinDraft is null) ResetSkinDraftFromCurrent();
            PaintSkinEditorState();
            return;
        }
        if (_observedForeignPreviewSession is not null)
        {
            _observedForeignPreviewSession = null;
            _skinPreviewTimer?.Stop(); _pendingSkinPreview = false;
            ResetSkinDraftFromCurrent();
        }
        // Preview notifications and saves of unrelated settings must not reset typing.
        if (!_pendingSkinPreview && !_viewModel.HasSkinPreview)
        {
            if (_skinDraft is null || (!_invalidSkinColor && !_invalidSkinName
                && !ReferenceEquals(_lastCommittedAppearance, _viewModel.Committed)))
            {
                _skinDraft = _viewModel.CommittedSkin;
                _lastCommittedAppearance = _viewModel.Committed;
                _invalidSkinColor = _invalidSkinName = false;
                SyncSkinColorInputs();
            }
        }
        else if (!_pendingSkinPreview && !AnotherWindowOwnsPreview && _viewModel.CurrentPreview?.Skin is { } preview
            && !ReferenceEquals(preview, _skinDraft))
        {
            _skinDraft = preview;
            SyncSkinColorInputs();
        }
        PaintSkinEditorState();
    }

    private uint CurrentEditorColor()
    {
        var palette = _skinDraft!.Resolve(SkinEditingDark);
        return EditingRole switch { SkinColorRole.Background => palette.Canvas, SkinColorRole.Text => palette.Text,
            SkinColorRole.Selection => palette.Selection ?? palette.Selected, _ => palette.Accent };
    }

    private void SyncSkinColorInputs(bool preserveName = false)
    {
        if (_skinOwnerClosed || _skinDraft is null) return;
        _syncingSkinInputs = true;
        try
        {
            var color = CurrentEditorColor();
            SkinColorPicker.Color = Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color);
            SkinHexBox.Text = SkinColors.ToHex(color);
            SkinRedBox.Value = (byte)(color >> 16); SkinGreenBox.Value = (byte)(color >> 8); SkinBlueBox.Value = (byte)color;
            if (!preserveName) SkinNameBox.Text = _skinDraft.Name;
        }
        finally { _syncingSkinInputs = false; }
        _invalidSkinColor = false;
        PaintSkinEditorState();
    }

    private void PaintSkinEditorState()
    {
        if (_skinOwnerClosed || _viewModel is null || _skinDraft is null) return;
        // Coalesce the more expensive sample/contrast rendering with live preview.
        var sampleDark = SkinActualDark;
        var effectiveAccent = SkinPalette.For(sampleDark, _viewModel.Effective).Accent;
        if (_presetCardsDark != sampleDark) BuildSkinPresetCards();
        SkinSampleLabel.Text = StringTable.Format("Skin_EffectPreview", StringTable.Get(sampleDark ? "ThemeDark" : "ThemeLight"));
        if ((_paintedSkin != _skinDraft && !_pendingSkinPreview) || _paintedSkin is null || _paintedDark != sampleDark
            || _paintedAccent != effectiveAccent)
        {
            _paintedSkin = _skinDraft; _paintedDark = sampleDark; _paintedAccent = effectiveAccent;
            SkinSampleHost.Child = BuildSkinSample(_skinDraft.Resolve(sampleDark) with { Accent = effectiveAccent }, compact: false);
            var warnings = _skinDraft.GetContrastWarnings();
            SkinContrastText.Visibility = warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            SkinContrastText.Text = warnings.Count == 0 ? "" : StringTable.Format("Skin_ContrastWarning",
                string.Join(", ", warnings.Take(4).Select(w => StringTable.Get(w.Dark ? "ThemeDark" : "ThemeLight")
                    + " " + w.Ratio.ToString("0.0", CultureInfo.CurrentCulture) + ":1")));
        }
        SkinCurrentName.Text = StringTable.Format("Skin_Current", _skinDraft.Name);
        var dirty = _pendingSkinPreview || _viewModel.HasSkinPreview;
        SkinPrimaryActions.Visibility = dirty || _invalidSkinColor || _invalidSkinName
            ? Visibility.Visible : Visibility.Collapsed;
        var ordinaryEditable = !_skinBusy && !AnotherWindowOwnsPreview;
        var editable = ordinaryEditable && !_viewModel.IsSavingSkinSelection;
        SkinApplyButton.IsEnabled = editable && dirty && !_invalidSkinColor && !_invalidSkinName;
        SkinCancelButton.IsEnabled = editable && (dirty || _invalidSkinColor || _invalidSkinName);
        SkinResetButton.IsEnabled = editable; SkinRevertButton.IsEnabled = editable && _viewModel.CanRevertSkin;
        SkinImportButton.IsEnabled = editable;
        SkinExportButton.IsEnabled = editable && !_invalidSkinColor && !_invalidSkinName;
        SkinColorPicker.IsEnabled = SkinModeBox.IsEnabled = SkinRoleBox.IsEnabled = SkinNameBox.IsEnabled
            = SkinHexBox.IsEnabled = SkinRedBox.IsEnabled = SkinGreenBox.IsEnabled = SkinBlueBox.IsEnabled = editable;
        foreach (var button in SkinPresetGrid.Children.OfType<Button>())
        {
            button.IsEnabled = ordinaryEditable;
            var selected = button.Tag is SkinPreset preset && preset.Package == _skinDraft;
            button.BorderThickness = new Thickness(selected ? 2 : 1);
            Theming.ThemeResources.Bind(button, Control.BorderBrushProperty,
                selected ? "FilesMate.Glass.AccentBrush" : "FilesMate.Glass.BorderBrush");
        }
        var key = AnotherWindowOwnsPreview ? "Skin_PreviewLocked"
            : _viewModel.IsSavingSkin ? "Skin_Saving"
            : _invalidSkinColor ? "Skin_InvalidColor" : _invalidSkinName ? "Skin_InvalidName"
            : _skinMessageKey ?? (dirty ? "Skin_Previewing"
                : _viewModel.LastSkinSaveSucceeded is true ? "Skin_Saved"
                : _viewModel.LastSkinSaveSucceeded is false ? "Skin_SaveFailed" : null);
        SkinMessageText.Text = key is null ? "" : StringTable.Get(key);
        SkinMessageText.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
        PaintAccentSwatches(_viewModel.Current.Accent);
    }

    private async void SkinPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_skinBusy || AnotherWindowOwnsPreview || _viewModel is null || sender is not Button { Tag: SkinPreset preset }) return;
        await CommitOrdinarySkinSelectionAsync(() => _viewModel.SelectSkinAsync(_skinPreviewOwner, preset.Package));
    }

    private async Task CommitOrdinarySkinSelectionAsync(Func<Task<bool>> select)
    {
        // Invalidate queued advanced/JSON/focus callbacks before the new intent.
        // Do not stop independent opacity work or tie this save to the page lease.
        _skinPreviewTimer?.Stop();
        _pendingSkinPreview = false;
        _skinFileRequest++;
        _skinFocusRevision++;
        _invalidSkinColor = _invalidSkinName = false;
        _skinMessageKey = null;
        _observingSkinSelection = true;
        var save = select();
        SyncSkinEditor();
        await save;
        // A previous click may finish after a newer click. Read the coordinator's
        // latest state instead of restoring any captured draft or success label.
        if (!_skinOwnerClosed && IsLoaded) SyncFromViewModel();
    }

    private void SkinModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSkinInputs || _skinDraft is null) return;
        SyncSkinColorInputs(preserveName: true);
    }
    private void SkinRoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingSkinInputs && _skinDraft is not null) SyncSkinColorInputs(preserveName: true);
    }

    private void SkinColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_syncingSkinInputs || _skinBusy || _viewModel?.IsSavingSkinSelection == true || AnotherWindowOwnsPreview || _skinDraft is null) return;
        EditSkinColor(0xFF000000u | (uint)args.NewColor.R << 16 | (uint)args.NewColor.G << 8 | args.NewColor.B);
        SyncSkinColorInputs(preserveName: true);
    }

    private void SkinHexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSkinInputs || _skinBusy || _viewModel?.IsSavingSkinSelection == true || AnotherWindowOwnsPreview || _skinDraft is null) return;
        if (!SkinColors.TryParseRgb(SkinHexBox.Text, out var color))
        {
            _invalidSkinColor = true; PaintSkinEditorState(); return;
        }
        _invalidSkinColor = false;
        EditSkinColor(color);
        _syncingSkinInputs = true;
        try
        {
            SkinColorPicker.Color = Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color);
            SkinRedBox.Value = (byte)(color >> 16); SkinGreenBox.Value = (byte)(color >> 8); SkinBlueBox.Value = (byte)color;
        }
        finally { _syncingSkinInputs = false; }
    }

    private void SkinRgbBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncingSkinInputs || _skinBusy || _viewModel?.IsSavingSkinSelection == true || AnotherWindowOwnsPreview || _skinDraft is null) return;
        if (!double.IsFinite(SkinRedBox.Value) || !double.IsFinite(SkinGreenBox.Value) || !double.IsFinite(SkinBlueBox.Value))
        { _invalidSkinColor = true; PaintSkinEditorState(); return; }
        static uint Channel(double value) => (uint)Math.Clamp(Math.Round(value), 0, 255);
        EditSkinColor(0xFF000000u | Channel(SkinRedBox.Value) << 16 | Channel(SkinGreenBox.Value) << 8 | Channel(SkinBlueBox.Value));
        SyncSkinColorInputs(preserveName: true);
    }

    private void SkinNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSkinInputs || _skinBusy || _viewModel?.IsSavingSkinSelection == true || AnotherWindowOwnsPreview || _skinDraft is null) return;
        try { _skinDraft = _skinDraft.WithName(SkinNameBox.Text); _invalidSkinName = false; QueueSkinPreview(); }
        catch (ArgumentException) { _invalidSkinName = true; PaintSkinEditorState(); }
    }

    private void EditSkinColor(uint color)
    {
        _skinDraft = _skinDraft!.WithColor(SkinEditingDark, EditingRole, color);
        _invalidSkinColor = false; _skinMessageKey = null; QueueSkinPreview();
    }

    private void QueueSkinPreview()
    {
        if (_viewModel is null || _skinDraft is null) return;
        if (!TryAcquireSkinPreview() || !_viewModel.TryReserveSkinPreview(_skinPreviewOwner, _skinDraftSession)) return;
        _pendingSkinPreview = true;
        if (_skinPreviewTimer is null)
        {
            _skinPreviewTimer = DispatcherQueue.CreateTimer();
            _skinPreviewTimer.IsRepeating = false;
            _skinPreviewTimer.Interval = TimeSpan.FromMilliseconds(250);
            _skinPreviewTimer.Tick += (_, _) => FlushSkinPreview();
        }
        if (!_skinPreviewTimer.IsRunning) _skinPreviewTimer.Start();
        PaintSkinEditorState();
    }

    private bool FlushSkinPreview()
    {
        _skinPreviewTimer?.Stop();
        if (_viewModel is null || _skinDraft is null) return false;
        if (!_pendingSkinPreview)
            return _viewModel.IsSkinPreviewOwner(_skinPreviewOwner, _skinDraftSession) && _viewModel.HasSkinPreview;
        _pendingSkinPreview = false;
        try
        {
            if (!_viewModel.TryPreviewSkin(_skinPreviewOwner, _skinDraftSession, _skinDraft))
            {
                ResetSkinDraftFromCurrent();
                return false;
            }
        }
        catch (Exception error)
        {
            App.LogFailure("SkinPreview", error);
            ResetSkinDraftFromCurrent();
            if (_viewModel.IsSkinPreviewOwner(_skinPreviewOwner, _skinDraftSession)
                && _viewModel.CurrentPreview?.Skin is { } previous)
            { _skinDraft = previous; SyncSkinColorInputs(); }
            _skinMessageKey = "Skin_PreviewFailed";
            PaintSkinEditorState();
            return false;
        }
        PaintSkinEditorState();
        return true;
    }

    private async void SkinApply_Click(object sender, RoutedEventArgs e)
    {
        if (_skinBusy || AnotherWindowOwnsPreview || _viewModel is null || _viewModel.IsSavingSkinSelection || _skinDraft is null || _invalidSkinColor || _invalidSkinName) return;
        _skinBusy = true;
        try
        {
            if (!FlushSkinPreview()) return;
            _skinMessageKey = "Skin_Saving";
            PaintSkinEditorState();
            PerformanceTrace.Record(PerformanceStage.SkinApplyStarted, _skinPreviewOwner);
            var applied = await _viewModel.ApplySkinAsync(_skinPreviewOwner, _skinDraftSession, _skinDraft);
            PerformanceTrace.Record(PerformanceStage.SkinApplyCompleted, _skinPreviewOwner,
                outcome: applied ? PerformanceOutcome.Completed : PerformanceOutcome.Failed);
            ResetSkinDraftFromCurrent();
            _skinMessageKey = applied ? "Skin_Saved" : "Skin_SaveFailed";
        }
        finally { _skinBusy = false; if (!_skinOwnerClosed && IsLoaded) { SyncFromViewModel(); PaintSkinEditorState(); } }
    }

    private void SkinCancel_Click(object sender, RoutedEventArgs e) => CancelSkinEditorPreview(1);

    private void CancelSkinEditorPreview(long reason = 0)
    {
        var hadUnsavedPreview = _pendingSkinPreview
            || _viewModel?.IsPreviewOwner(_skinPreviewOwner, _skinPreviewSession) == true;
        PerformanceTrace.Record(PerformanceStage.SkinPreviewCancelled, _skinPreviewOwner,
            count: _viewModel?.IsPreviewOwner(_skinPreviewOwner, _skinPreviewSession) == true ? 1 : 0,
            auxiliary: reason);
        _skinPreviewTimer?.Stop(); _pendingSkinPreview = false; _skinFileRequest++;
        _skinFocusTimer?.Stop(); _skinFocusRevision++;
        _transparencySaveTimer?.Stop(); _pendingTransparency = null;
        try { _viewModel?.TryCancelPreview(_skinPreviewOwner, _skinPreviewSession); }
        catch (Exception error) { App.LogFailure("SkinPreviewCancel", error); }
        finally
        {
            _skinPreviewSession = _skinDraftSession = Guid.Empty;
            ResetSkinDraftFromCurrent(updateControls: !_skinOwnerClosed && IsLoaded);
            if (!_skinOwnerClosed && IsLoaded && reason == 2 && hadUnsavedPreview)
            {
                _skinMessageKey = "Skin_PreviewCancelledUnsaved";
                PaintSkinEditorState();
            }
        }
    }

    private void ResetSkinDraftFromCurrent(bool updateControls = true)
    {
        if (_viewModel is null) return;
        _skinDraft = _viewModel.PendingSkinSelection ?? _viewModel.CommittedSkin;
        _lastCommittedAppearance = _viewModel.Committed;
        _invalidSkinColor = _invalidSkinName = false; _skinMessageKey = null;
        if (updateControls) SyncSkinColorInputs();
    }

    private void SkinReset_Click(object sender, RoutedEventArgs e)
    {
        if (_skinBusy || AnotherWindowOwnsPreview || _viewModel is null || _viewModel.IsSavingSkinSelection) return;
        _skinDraft = SkinPresets.All.First(p => p.Id == "warmWhite").Package;
        _invalidSkinColor = _invalidSkinName = false; _skinMessageKey = null;
        SyncSkinColorInputs(); QueueSkinPreview();
    }

    private async void SkinRevert_Click(object sender, RoutedEventArgs e)
    {
        if (_skinBusy || AnotherWindowOwnsPreview || _viewModel is null || _viewModel.IsSavingSkinSelection || !_viewModel.CanRevertSkin) return;
        _skinBusy = true; CancelSkinEditorPreview();
        if (!TryAcquireSkinPreview()) { _skinBusy = false; return; }
        _skinMessageKey = "Skin_Saving";
        PaintSkinEditorState();
        try
        {
            var saved = await _viewModel.RevertSkinAsync(_skinPreviewOwner, _skinDraftSession);
            ResetSkinDraftFromCurrent();
            _skinMessageKey = saved ? "Skin_Saved" : "Skin_SaveFailed";
        }
        finally { _skinBusy = false; if (IsLoaded) SyncFromViewModel(); }
    }

    private bool IsCurrentSkinFileRequest(long request, long selectionRevision) => IsLoaded
        && request == _skinFileRequest && selectionRevision == _viewModel?.SkinSelectionRevision;

    private async void SkinImport_Click(object sender, RoutedEventArgs e)
    {
        if (_skinBusy || AnotherWindowOwnsPreview || _viewModel is null || _viewModel.IsSavingSkinSelection || App.WindowForElement(this) is not { } window) return;
        var request = ++_skinFileRequest;
        var selectionRevision = _viewModel.SkinSelectionRevision;
        _skinBusy = true; PaintSkinEditorState();
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, window.NativeHandle);
            _nativeSkinPickerOpen = true;
            StartSkinFocusWatch();
            StorageFile? file;
            try { file = await picker.PickSingleFileAsync(); }
            finally { _nativeSkinPickerOpen = false; }
            if (file is null || !IsCurrentSkinFileRequest(request, selectionRevision)) return;
            var text = await ReadBoundedSkinAsync(file);
            var skin = SkinPackageSerializer.Deserialize(text);
            if (!IsCurrentSkinFileRequest(request, selectionRevision) || AnotherWindowOwnsPreview) return;
            _skinDraft = skin; _invalidSkinName = _invalidSkinColor = false; _skinMessageKey = "Skin_Imported";
            SyncSkinColorInputs(); QueueSkinPreview(); FlushSkinPreview();
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException)
        { if (IsCurrentSkinFileRequest(request, selectionRevision)) _skinMessageKey = "Skin_InvalidPackage"; }
        catch (Exception error) { App.LogFailure("SkinImport", error); if (IsCurrentSkinFileRequest(request, selectionRevision)) _skinMessageKey = "Skin_FileFailed"; }
        finally
        {
            _skinBusy = false;
            if (IsLoaded) { await CheckSkinFocusAsync(); PaintSkinEditorState(); }
        }
    }

    private static async Task<string> ReadBoundedSkinAsync(StorageFile file)
    {
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > SkinPackageSerializer.MaximumUtf8Bytes) throw new InvalidDataException("Skin exceeds limit.");
        using var stream = await file.OpenStreamForReadAsync();
        var bytes = new byte[SkinPackageSerializer.MaximumUtf8Bytes + 1];
        var used = 0;
        while (used < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(used));
            if (count == 0) break;
            used += count;
        }
        if (used > SkinPackageSerializer.MaximumUtf8Bytes) throw new InvalidDataException("Skin exceeds limit.");
        var text = new UTF8Encoding(false, true).GetString(bytes, 0, used);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private async void SkinExport_Click(object sender, RoutedEventArgs e)
    {
        if (_skinBusy || _viewModel is null || _viewModel.IsSavingSkinSelection || AnotherWindowOwnsPreview || _skinDraft is null || _invalidSkinColor || _invalidSkinName
            || App.WindowForElement(this) is not { } window) return;
        var request = ++_skinFileRequest;
        var selectionRevision = _viewModel.SkinSelectionRevision;
        _skinBusy = true; PaintSkinEditorState();
        var package = _skinDraft; // Snapshot only the portable package, never the full appearance/profile.
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "FilesMate-skin" };
            picker.FileTypeChoices.Add(StringTable.Get("Skin_Package"), new List<string> { ".json" });
            InitializeWithWindow.Initialize(picker, window.NativeHandle);
            _nativeSkinPickerOpen = true;
            StartSkinFocusWatch();
            StorageFile? file;
            try { file = await picker.PickSaveFileAsync(); }
            finally { _nativeSkinPickerOpen = false; }
            if (file is null || !IsCurrentSkinFileRequest(request, selectionRevision)) return;
            await FileIO.WriteTextAsync(file, SkinPackageSerializer.Serialize(package), Windows.Storage.Streams.UnicodeEncoding.Utf8);
            if (IsCurrentSkinFileRequest(request, selectionRevision)) _skinMessageKey = "Skin_Exported";
        }
        catch (Exception error) { App.LogFailure("SkinExport", error); if (IsCurrentSkinFileRequest(request, selectionRevision)) _skinMessageKey = "Skin_FileFailed"; }
        finally
        {
            _skinBusy = false;
            if (IsLoaded) { await CheckSkinFocusAsync(); PaintSkinEditorState(); }
        }
    }
}
