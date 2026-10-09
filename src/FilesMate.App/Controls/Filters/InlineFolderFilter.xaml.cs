using System.Globalization;
using System.Runtime.CompilerServices;
using FilesMate.App.Navigation;
using FilesMate.App.Models;
using FilesMate.App.Theming;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App.Controls.Filters;

public sealed partial class InlineFolderFilter : UserControl
{
    private sealed class Draft
    {
        public EntryMetadataFilter Applied = EntryMetadataFilter.None;
        public string Path = "", Extensions = "", Minimum = "", Maximum = "", Error = "";
        public DateTimeOffset? First, Last;
        public int DatePreset, SizePreset, Unit;
        public bool Expanded;
    }
    private readonly ConditionalWeakTable<PaneViewModel, Draft> _drafts = new();
    private PaneViewModel? _pane;
    private Draft? _draft;
    private bool _syncing;
    private readonly Button _types = new(), _dates = new(), _sizes = new();
    private readonly Flyout _typeFlyout = new(), _dateFlyout = new(), _sizeFlyout = new();
    private readonly List<(CheckBox Box, EntryTypeCategory Category, string? Extension)> _typeChoices = [];
    private readonly TextBox _extensions = new();
    private readonly CalendarDatePicker _first = new(), _last = new();
    private readonly TextBox _minimum = new(), _maximum = new();
    private readonly ComboBox _units = new();
    private readonly List<Button> _datePresets = [], _sizePresets = [];
    private readonly List<Button> _chips = [];
    private readonly TextBlock _activeCount = new() { FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _typeCaption = new(), _dateCaption = new(), _sizeCaption = new();
    private readonly FontIcon _expandChevron = new() { FontSize = 9, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons") };
    private readonly ScrollViewer _extensionScroll = new() { MaxHeight = 165, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static readonly ulong[] Multipliers = [1, 1024, 1024 * 1024, 1024UL * 1024 * 1024];

    public InlineFolderFilter()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(_types, "InlineTypes");
        AutomationProperties.SetAutomationId(_extensions, "InlineExtensions");
        AutomationProperties.SetAutomationId(_dates, "InlineDatePreset");
        AutomationProperties.SetAutomationId(_first, "InlineStartDate");
        AutomationProperties.SetAutomationId(_last, "InlineEndDate");
        AutomationProperties.SetAutomationId(_sizes, "InlineSizePreset");
        AutomationProperties.SetAutomationId(_minimum, "InlineMinimum");
        AutomationProperties.SetAutomationId(_maximum, "InlineMaximum");
        AutomationProperties.SetAutomationId(_units, "InlineUnit");

        ExpandButton.Content = CreateToolbarHeaderContent(_activeCount, _expandChevron);
        ClearButton.Content = Loc.Get("Inline_ClearFilters");
        AutomationProperties.SetName(ExpandButton, Loc.Get("Inline_Filter"));
        SetupTrigger(_types, _typeCaption, "\uE8D2", _typeFlyout, TypeHost);
        SetupTrigger(_dates, _dateCaption, "\uE787", _dateFlyout, DateHost);
        SetupTrigger(_sizes, _sizeCaption, "\uE838", _sizeFlyout, SizeHost);

        var typeBody = Popup(Loc.Get("Column_Type"));
        typeBody.Children.Add(Note(Loc.Get("Inline_MultiSelect")));
        var categories = ChoicesGrid(6, 6);
        foreach (var (key, category) in new[] {
            ("Inline_Folders", EntryTypeCategory.Directories), ("Inline_Documents", EntryTypeCategory.Documents),
            ("Inline_Images", EntryTypeCategory.Images), ("Inline_Audio", EntryTypeCategory.Audio),
            ("Inline_Video", EntryTypeCategory.Video), ("Inline_Archives", EntryTypeCategory.Archives),
            ("Inline_Applications", EntryTypeCategory.Applications), ("Inline_NoExtension", EntryTypeCategory.None) })
            AddType(categories, Loc.Get(key), category, null);
        typeBody.Children.Add(categories);
        typeBody.Children.Add(Note(Loc.Get("Inline_ExtensionChoices")));
        var extensions = ChoicesGrid(7, 4);
        foreach (var extension in new[] { ".pdf", ".txt", ".jpg", ".png", ".zip" }) AddType(extensions, extension, EntryTypeCategory.None, extension);
        _extensionScroll.Content = extensions; typeBody.Children.Add(_extensionScroll);
        var custom = new Grid { ColumnSpacing = 7 };
        custom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        custom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _extensions.PlaceholderText = "jpg; .png; *.pdf";
        AutomationProperties.SetName(_extensions, Loc.Get("Inline_Extensions"));
        custom.Children.Add(_extensions);
        var add = new Button { Content = Loc.Get("Inline_Add"), Style = Choice, VerticalAlignment = VerticalAlignment.Center };
        add.Click += (_, _) => CommitExtensions(); Grid.SetColumn(add, 1); custom.Children.Add(add);
        typeBody.Children.Add(custom);
        _extensions.TextChanged += (_, _) => { if (!_syncing && _draft is not null) _draft.Extensions = _extensions.Text; };
        _extensions.KeyDown += (_, e) => CompleteOnEnter(e, CommitExtensions);
        ConfigurePopup(_typeFlyout, typeBody, Footer(Loc.Get("Inline_TypeHint"), CommitExtensions, _typeFlyout));
        _typeFlyout.Opening += (_, _) => _extensionScroll.MaxHeight = Math.Min(165, Math.Max(32, (XamlRoot?.Size.Height ?? 700) - 350));

        var dateBody = Popup(Loc.Get("SearchPage_Modified"));
        var dateChoices = ChoicesGrid(6, 6);
        var dateKeys = new[] { "AnyDate", "Today", "Yesterday", "Last7Days", "Last30Days", "Last90Days", "CustomDates" };
        for (var i = 0; i < dateKeys.Length; i++)
        {
            var index = i; var button = Preset(Loc.Get("SearchPage_" + dateKeys[i]), "InlineDateChoice" + i);
            button.Click += (_, _) => DatePresetChanged(index); _datePresets.Add(button); AddCell(dateChoices, button, i);
        }
        dateBody.Children.Add(dateChoices);
        _first.Header = Loc.Get("SearchPage_StartDate"); _last.Header = Loc.Get("SearchPage_EndDate");
        _first.Language = _last.Language = CultureInfo.CurrentUICulture.Name;
        dateBody.Children.Add(Range(_first, _last));
        ConfigurePopup(_dateFlyout, dateBody, Footer(Loc.Get("Inline_DateHint"), CommitDates, _dateFlyout));
        _first.DateChanged += (_, _) => { if (!_syncing && _draft is not null) { _draft.DatePreset = 6; _draft.First = _first.Date; if (_draft.Last is not null) CommitDates(); } };
        _last.DateChanged += (_, _) => { if (!_syncing && _draft is not null) { _draft.DatePreset = 6; _draft.Last = _last.Date; if (_draft.First is not null) CommitDates(); } };

        var sizeBody = Popup(Loc.Get("SearchPage_Size"));
        var sizeChoices = ChoicesGrid(6, 6);
        var sizes = new[] { Loc.Get("SearchPage_AnySize"), "\u2264 1 MB", "1\u2013100 MB", "\u2265 100 MB", "\u2265 1 GB", Loc.Get("Inline_CustomSize") };
        for (var i = 0; i < sizes.Length; i++)
        {
            var index = i; var button = Preset(sizes[i], "InlineSizeChoice" + i);
            button.Click += (_, _) => SizePresetChanged(index); _sizePresets.Add(button); AddCell(sizeChoices, button, i);
        }
        sizeBody.Children.Add(sizeChoices);
        _minimum.Header = Loc.Get("SearchPage_Minimum"); _maximum.Header = Loc.Get("SearchPage_Maximum");
        _minimum.PlaceholderText = _maximum.PlaceholderText = Loc.Get("SearchPage_NoLimit");
        sizeBody.Children.Add(Range(_minimum, _maximum));
        foreach (var unit in new[] { "B", "KB", "MB", "GB" }) _units.Items.Add(unit);
        AutomationProperties.SetName(_units, Loc.Get("SearchPage_Unit"));
        _units.Header = Loc.Get("SearchPage_Unit"); _units.Width = 100; _units.HorizontalAlignment = HorizontalAlignment.Left;
        sizeBody.Children.Add(_units);
        ConfigurePopup(_sizeFlyout, sizeBody, Footer(Loc.Get("Inline_BytesNote"), CommitSize, _sizeFlyout));
        _minimum.TextChanged += (_, _) => { if (!_syncing && _draft is not null) { _draft.Minimum = _minimum.Text; _draft.SizePreset = 5; } };
        _maximum.TextChanged += (_, _) => { if (!_syncing && _draft is not null) { _draft.Maximum = _maximum.Text; _draft.SizePreset = 5; } };
        _units.SelectionChanged += (_, _) => { if (!_syncing && _draft is not null) _draft.Unit = _units.SelectedIndex; };
        _minimum.KeyDown += (_, e) => CompleteOnEnter(e, CommitSize); _maximum.KeyDown += (_, e) => CompleteOnEnter(e, CommitSize);
    }

    // Shared by the live header and the inert toolbar customization preview.
    private static StackPanel CreateToolbarHeaderContent(TextBlock activeCount, FontIcon chevron)
    {
        var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        entry.Children.Add(Icon(ToolbarToolCatalog.Get(ToolbarTool.Filters).Glyph));
        entry.Children.Add(new TextBlock { Text = Loc.Get("Inline_Filter"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        entry.Children.Add(activeCount);
        entry.Children.Add(chevron);
        return entry;
    }

    internal static FrameworkElement CreateToolbarHeaderPreview()
    {
        var button = new Button { Height = 32, Padding = new Thickness(9, 0, 9, 0),
            Style = (Style)Application.Current.Resources["CommandBarButtonStyle"], IsTabStop = false,
            Content = CreateToolbarHeaderContent(
                new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center },
                new FontIcon { Glyph = "\uE70D", FontSize = 9, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons") }) };
        AutomationProperties.SetName(button, Loc.Get("Inline_Filter"));
        return button;
    }

    public FrameworkElement DetachToolbarHeader() { Root.Children.Remove(Header); return Header; }
    private static Style Quiet => (Style)Application.Current.Resources["QuietButtonStyle"];
    private static Style Choice => (Style)Application.Current.Resources["FilesMate.FilterChoiceButtonStyle"];
    private static Style ActiveChoice => (Style)Application.Current.Resources["FilesMate.ActiveFilterChoiceButtonStyle"];
    private static FontIcon Icon(string glyph) => new() { Glyph = glyph, FontSize = 15, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons") };
    private static TextBlock Note(string text) => new() { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = .75 };
    private static StackPanel Popup(string title)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        return panel;
    }
    private static Grid ChoicesGrid(double columns, double rows)
    {
        var grid = new Grid { ColumnSpacing = columns, RowSpacing = rows, Tag = "InlineChoicesGrid" };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }
    private static void AddCell(Grid grid, FrameworkElement item, int index, int columns = 3)
    {
        while (grid.RowDefinitions.Count <= index / columns) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(item, index % columns); Grid.SetRow(item, index / columns); if (!grid.Children.Contains(item)) grid.Children.Add(item);
    }
    private static Button Preset(string label, string id)
    {
        var button = new Button { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Style = new Style(typeof(TextBlock)) },
            Style = Choice, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, label); return button;
    }
    private static Grid Range(FrameworkElement first, FrameworkElement last)
    {
        var grid = new Grid { ColumnSpacing = 7 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        first.MinWidth = last.MinWidth = 0; first.HorizontalAlignment = last.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(last, 1); grid.Children.Add(first); grid.Children.Add(last); return grid;
    }
    private Grid Footer(string note, Action commit, Flyout flyout)
    {
        // Help text and the action have separate rows. The action never borrows the remaining text width.
        var grid = new Grid { RowSpacing = 8 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(Note(note));
        var done = new Button { Content = Loc.Get("Inline_Done"), Style = Choice,
            MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
        done.Click += (_, _) => { commit(); if (_draft?.Error.Length == 0) flyout.Hide(); };
        Grid.SetRow(done, 1); grid.Children.Add(done); return grid;
    }
    private static void ConfigurePopup(Flyout flyout, StackPanel body, FrameworkElement footer)
    {
        var title = body.Children[0]; body.Children.RemoveAt(0);
        var root = new Grid { RowSpacing = 8, MinWidth = 0 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(title);
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        var style = new Style(typeof(FlyoutPresenter)) { BasedOn = (Style)Application.Current.Resources["FilesMate.RoundedFlyoutPresenterStyle"] };
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(9)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 360d));
        style.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        style.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        flyout.FlyoutPresenterStyle = style;
        flyout.Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft;
        flyout.Content = root; FlyoutTheme.FollowHost(flyout);
        XamlRoot? observedRoot = null;
        void Bounds()
        {
            var available = flyout.Target?.XamlRoot?.Size ?? new Windows.Foundation.Size(440, 700);
            // XamlRoot dimensions are logical pixels: padding, border and edge clearance share the same units.
            root.Width = Math.Min(320, Math.Max(0, available.Width - 64));
            root.MaxHeight = Math.Max(0, available.Height - 64);
            var columns = root.Width >= 300 ? 3 : 2;
            foreach (var grid in body.Children.OfType<Grid>().Concat(body.Children.OfType<ScrollViewer>()
                .Select(s => s.Content).OfType<Grid>()).Where(g => Equals(g.Tag, "InlineChoicesGrid")))
            {
                if (grid.ColumnDefinitions.Count == columns) continue;
                grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
                for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var cells = grid.Children.OfType<FrameworkElement>().ToArray();
                for (var i = 0; i < cells.Length; i++) AddCell(grid, cells[i], i, columns);
            }
        }
        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Bounds();
        flyout.Opening += (_, _) => Bounds();
        flyout.Opened += (_, _) => { observedRoot = flyout.Target?.XamlRoot; if (observedRoot is not null) observedRoot.Changed += RootChanged; };
        flyout.Closed += (_, _) => { if (observedRoot is not null) observedRoot.Changed -= RootChanged; observedRoot = null; };
    }
    private static void SetupTrigger(Button button, TextBlock caption, string glyph, Flyout flyout, StackPanel host)
    {
        button.Style = Quiet; button.Height = 37; button.Padding = new Thickness(9, 0, 9, 0); button.CornerRadius = new CornerRadius(5);
        button.HorizontalAlignment = HorizontalAlignment.Left; button.MaxWidth = 250;
        caption.MaxWidth = 185; caption.TextTrimming = TextTrimming.CharacterEllipsis; caption.VerticalAlignment = VerticalAlignment.Center;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        content.Children.Add(Icon(glyph)); content.Children.Add(caption);
        content.Children.Add(new FontIcon { Glyph = "\uE70D", FontSize = 9, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons") });
        button.Content = content; button.Flyout = flyout; host.Children.Add(button);
    }
    private void Editor_SizeChanged(object sender, SizeChangedEventArgs e) => ArrangeEditor();
    private void ArrangeEditor()
    {
        var available = Math.Max(0, Editor.ActualWidth > 0 ? Editor.ActualWidth : ActualWidth - 18);
        var hosts = new List<FrameworkElement> { TypeHost, DateHost, SizeHost };
        hosts.AddRange(_chips);
        if (ClearButton.Visibility == Visibility.Visible) hosts.Add(ClearButton);
        var used = 0d; var row = 0;
        foreach (var host in hosts)
        {
            // Bound each item before measuring it. A long condition can occupy
            // its own row without extending past the editor's right edge.
            host.MaxWidth = host is Button ? Math.Min(250, available) : available;
            host.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var needed = host.DesiredSize.Width - host.Margin.Left;
            if (used > 0 && used + 7 + needed > available) { row++; used = 0; }
            while (Editor.RowDefinitions.Count <= row) Editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(host, row); Grid.SetColumn(host, 0);
            host.HorizontalAlignment = HorizontalAlignment.Left;
            var offset = used == 0 ? 0 : used + 7;
            host.Margin = new Thickness(offset, 0, 0, 0);
            used = offset + needed;
        }
        while (Editor.RowDefinitions.Count > row + 1) Editor.RowDefinitions.RemoveAt(Editor.RowDefinitions.Count - 1);
    }
    private static void CompleteOnEnter(KeyRoutedEventArgs e, Action commit) { if (e.Key == VirtualKey.Enter) { commit(); e.Handled = true; } }
    private void AddType(Grid host, string title, EntryTypeCategory category, string? extension)
    {
        var box = new CheckBox { Content = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Style = new Style(typeof(TextBlock)) },
            MinWidth = 0, MinHeight = 32, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4, 8, 4) };
        AutomationProperties.SetName(box, title);
        box.Click += (_, _) =>
        {
            if (_syncing || _pane is null) return;
            var filter = _pane.MetadataFilter;
            if (extension is not null)
            {
                var values = filter.Extensions.IsDefault ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : filter.Extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (box.IsChecked == true) values.Add(extension); else values.Remove(extension);
                Apply(filter with { Extensions = [.. values.Order(StringComparer.OrdinalIgnoreCase)] });
                if (_draft is not null) _draft.Extensions = string.Join(';', _pane.MetadataFilter.Extensions);
            }
            else if (category == EntryTypeCategory.None) Apply(filter with { NoExtension = box.IsChecked == true });
            else Apply(filter with { Categories = box.IsChecked == true ? filter.Categories | category : filter.Categories & ~category });
            Sync();
        };
        box.Style = (Style)Application.Current.Resources["FilesMate.FilterChoiceCheckBoxStyle"];
        AddCell(host, box, host.Children.Count); _typeChoices.Add((box, category, extension));
    }

    public void SetPane(PaneViewModel pane)
    {
        var changed = !ReferenceEquals(_pane, pane);
        if (changed) { _typeFlyout.Hide(); _dateFlyout.Hide(); _sizeFlyout.Hide(); }
        _pane = pane; _draft = _drafts.GetValue(pane, _ => new Draft());
        if (_draft.Path != pane.AddressText || _draft.Applied != pane.MetadataFilter)
        {
            var expanded = _draft.Expanded;
            _drafts.Remove(pane); _draft = new Draft { Path = pane.AddressText, Applied = pane.MetadataFilter, Expanded = expanded };
            _draft.Extensions = string.Join(';', pane.MetadataFilter.Extensions.IsDefault ? [] : pane.MetadataFilter.Extensions);
            if (pane.MetadataFilter.HasDate)
            {
                _draft.DatePreset = 6;
                _draft.First = ToCalendar(pane.MetadataFilter.ModifiedStartDate); _draft.Last = ToCalendar(pane.MetadataFilter.ModifiedEndDate);
            }
            if (pane.MetadataFilter.HasSize)
            {
                _draft.SizePreset = 5; _draft.Minimum = pane.MetadataFilter.MinimumBytes?.ToString(CultureInfo.CurrentCulture) ?? "";
                _draft.Maximum = pane.MetadataFilter.MaximumBytes?.ToString(CultureInfo.CurrentCulture) ?? "";
            }
            _drafts.Add(pane, _draft);
        }
        Header.Visibility = HomeLocation.IsHome(pane.AddressText) ? Visibility.Collapsed : Visibility.Visible; Sync();
    }
    private static DateTimeOffset? ToCalendar(DateOnly? date) => date is { } value ? new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue)) : null;
    private void Sync()
    {
        if (_pane is null || _draft is null) return;
        _syncing = true;
        try
        {
            var filter = _pane.MetadataFilter;
            Visibility = _draft.Expanded && !HomeLocation.IsHome(_pane.AddressText) ? Visibility.Visible : Visibility.Collapsed;
            var count = (filter.HasTypes ? 1 : 0) + (filter.HasDate ? 1 : 0) + (filter.HasSize ? 1 : 0);
            _activeCount.Text = count == 0 ? "" : count.ToString(CultureInfo.CurrentCulture);
            _activeCount.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetName(ExpandButton, Loc.Get("Inline_Filter") + (count == 0 ? "" : " (" + count.ToString(CultureInfo.CurrentCulture) + ")"));
            _expandChevron.Glyph = _draft.Expanded ? "\uE70E" : "\uE70D";
            ClearButton.Visibility = filter.IsActive ? Visibility.Visible : Visibility.Collapsed;
            foreach (var chip in _chips) Editor.Children.Remove(chip);
            _chips.Clear();
            if (filter.HasTypes) AddChip(TypeCaption(filter), ClearTypes);
            if (filter.HasDate) AddChip(Loc.Get("SearchPage_Modified") + ": " + (filter.ModifiedStartDate?.ToString("yyyy-MM-dd") ?? "…") + " – " + (filter.ModifiedEndDate?.ToString("yyyy-MM-dd") ?? "…"), ClearDates);
            if (filter.HasSize) AddChip(Loc.Get("SearchPage_Size") + ": " + SizeCaption(filter), ClearSize,
                Loc.Get("SearchPage_Size") + ": " + (filter.MinimumBytes?.ToString("N0") ?? "0") + " – " + (filter.MaximumBytes?.ToString("N0") ?? "∞") + " B");
            foreach (var chip in _chips) Editor.Children.Add(chip);
            foreach (var (box, category, extension) in _typeChoices)
            {
                box.IsChecked = extension is not null ? !filter.Extensions.IsDefault && filter.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                    : category == EntryTypeCategory.None ? filter.NoExtension : (filter.Categories & category) != 0;
                box.Style = (Style)Application.Current.Resources[box.IsChecked == true ? "FilesMate.ActiveFilterChoiceCheckBoxStyle" : "FilesMate.FilterChoiceCheckBoxStyle"];
            }
            _typeCaption.Text = Loc.Get("Column_Type") + " \u00B7 " + (filter.HasTypes ? TypeCaption(filter) : Loc.Get("Inline_All"));
            _dateCaption.Text = Loc.Get("SearchPage_Modified") + " \u00B7 " + (filter.HasDate ? Loc.Get("SearchPage_" + new[] { "AnyDate", "Today", "Yesterday", "Last7Days", "Last30Days", "Last90Days", "CustomDates" }[_draft.DatePreset]) : Loc.Get("SearchPage_NoLimit"));
            _sizeCaption.Text = Loc.Get("SearchPage_Size") + " \u00B7 " + (filter.HasSize ? SizeCaption(filter) : Loc.Get("SearchPage_NoLimit"));
            foreach (var button in new[] { _types, _dates, _sizes })
                AutomationProperties.SetName(button, ((TextBlock)((StackPanel)button.Content).Children[1]).Text);
            _extensions.Text = _draft.Extensions;
            _first.Date = _draft.First; _last.Date = _draft.Last; _minimum.Text = _draft.Minimum; _maximum.Text = _draft.Maximum; _units.SelectedIndex = _draft.Unit;
            HighlightPresets(_datePresets, _draft.DatePreset); HighlightPresets(_sizePresets, _draft.SizePreset);
            ArrangeEditor();
            Validation.Text = _draft.Error; Validation.Visibility = !_draft.Expanded || _draft.Error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _syncing = false; }
    }
    private static string TypeCaption(EntryMetadataFilter filter)
    {
        var values = new List<string>();
        foreach (var (key, category) in new[] { ("Folders", EntryTypeCategory.Directories), ("Documents", EntryTypeCategory.Documents), ("Images", EntryTypeCategory.Images), ("Audio", EntryTypeCategory.Audio), ("Video", EntryTypeCategory.Video), ("Archives", EntryTypeCategory.Archives), ("Applications", EntryTypeCategory.Applications) })
            if ((filter.Categories & category) != 0) values.Add(Loc.Get("Inline_" + key));
        if (filter.NoExtension) values.Add(Loc.Get("Inline_NoExtension"));
        if (!filter.Extensions.IsDefaultOrEmpty) values.AddRange(filter.Extensions);
        return string.Join(", ", values);
    }
    private void AddChip(string text, Action clear, string? details = null)
    {
        var content = new Grid { ColumnSpacing = 7 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center });
        var close = new TextBlock { Text = "\u00D7", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(close, 1); content.Children.Add(close);
        var chip = new Button { Content = content, MaxWidth = 250,
            Style = (Style)Application.Current.Resources["FilesMate.FilterChipButtonStyle"] };
        AutomationProperties.SetName(chip, Loc.Get("Inline_ClearCondition") + " " + (details ?? text));
        ToolTipService.SetToolTip(chip, details ?? text);
        chip.Click += (_, _) => { clear(); SetPane(_pane!); }; _chips.Add(chip);
    }
    private void Apply(EntryMetadataFilter filter)
    {
        if (_pane is null || _draft is null) return;
        _draft.Applied = filter; _draft.Error = ""; _pane.SetMetadataFilter(filter); Sync();
    }
    private void Invalid(string key) { if (_draft is null) return; _draft.Error = Loc.Get(key) + " " + Loc.Get("Inline_PreviousRetained"); Sync(); }
    private void CommitExtensions()
    {
        if (_pane is null || _draft is null) return;
        if (!EntryMetadataFilter.TryNormalizeExtensions(_draft.Extensions, out var values)) { Invalid("Inline_InvalidExtensions"); return; }
        Apply(_pane.MetadataFilter with { Extensions = values }); _draft.Extensions = string.Join(';', values); Sync();
    }
    private void DatePresetChanged(int index)
    {
        if (_syncing || _draft is null || _pane is null) return;
        _draft.DatePreset = index;
        if (_draft.DatePreset == 6) { Sync(); return; }
        if (_draft.DatePreset == 0) { _draft.First = _draft.Last = null; Apply(_pane.MetadataFilter with { ModifiedStartDate = null, ModifiedEndDate = null, ModifiedFromUtcTicks = null, ModifiedUntilUtcTicks = null }); return; }
        var today = DateOnly.FromDateTime(DateTime.Now);
        var last = _draft.DatePreset == 2 ? today.AddDays(-1) : today;
        var first = _draft.DatePreset switch { 3 => today.AddDays(-6), 4 => today.AddDays(-29), 5 => today.AddDays(-89), _ => last };
        _draft.First = ToCalendar(first); _draft.Last = ToCalendar(last); CommitDates();
    }
    private void CommitDates()
    {
        if (_syncing || _pane is null || _draft is null) return;
        if (_draft.DatePreset == 0 && _draft.First is null && _draft.Last is null) { ClearDates(); return; }
        if (_draft.First is not { } first || _draft.Last is not { } last) { Invalid("Inline_IncompleteDates"); return; }
        var start = DateOnly.FromDateTime(first.Date);
        var end = DateOnly.FromDateTime(last.Date);
        if (!EntryMetadataFilter.TryDateRange(start, end, TimeZoneInfo.Local, out var from, out var until)) { Invalid("SearchPage_DateOrder"); return; }
        Apply(_pane.MetadataFilter with { ModifiedStartDate = start, ModifiedEndDate = end, ModifiedFromUtcTicks = from, ModifiedUntilUtcTicks = until });
    }
    private void SizePresetChanged(int index)
    {
        if (_syncing || _draft is null || _pane is null) return;
        _draft.SizePreset = index;
        if (_draft.SizePreset == 5) { Sync(); return; }
        const ulong mb = 1024 * 1024;
        var (min, max) = _draft.SizePreset switch { 1 => ((ulong?)null, (ulong?)mb), 2 => ((ulong?)mb, (ulong?)(100 * mb)), 3 => ((ulong?)(100 * mb), (ulong?)null), 4 => ((ulong?)(1024 * mb), (ulong?)null), _ => ((ulong?)null, (ulong?)null) };
        _draft.Unit = _draft.SizePreset == 4 ? 3 : _draft.SizePreset == 0 ? 0 : 2;
        _draft.Minimum = min is { } lower ? (lower / Multipliers[_draft.Unit]).ToString(CultureInfo.CurrentCulture) : "";
        _draft.Maximum = max is { } upper ? (upper / Multipliers[_draft.Unit]).ToString(CultureInfo.CurrentCulture) : "";
        Apply(_pane.MetadataFilter with { MinimumBytes = min, MaximumBytes = max });
    }
    private static string SizeCaption(EntryMetadataFilter filter)
    {
        static string Bytes(ulong? value) => value is { } bytes ? bytes >= 1024UL * 1024 * 1024 ? (bytes / (1024d * 1024 * 1024)).ToString("0.#") + " GB" : bytes >= 1024 * 1024 ? (bytes / (1024d * 1024)).ToString("0.#") + " MB" : bytes.ToString("N0") + " B" : "\u221E";
        return Bytes(filter.MinimumBytes ?? 0) + "\u2013" + Bytes(filter.MaximumBytes);
    }
    private static void HighlightPresets(List<Button> buttons, int selected)
    {
        for (var i = 0; i < buttons.Count; i++)
            buttons[i].Style = i == selected ? ActiveChoice : Choice;
    }
    private void CommitSize()
    {
        if (_pane is null || _draft is null) return;
        if (_draft.SizePreset == 0 && string.IsNullOrWhiteSpace(_draft.Minimum) && string.IsNullOrWhiteSpace(_draft.Maximum)) { ClearSize(); return; }
        if (_draft.Unit is < 0 or > 3 || !EntryMetadataFilter.TryByteRange(_draft.Minimum, _draft.Maximum, Multipliers[_draft.Unit], CultureInfo.CurrentCulture, out var min, out var max)) { Invalid("Inline_InvalidSize"); return; }
        Apply(_pane.MetadataFilter with { MinimumBytes = min, MaximumBytes = max });
    }
    private void Expand_Click(object sender, RoutedEventArgs e) { if (_draft is null) return; _draft.Expanded = !_draft.Expanded; Sync(); }
    public void ShowEditor() { if (_draft is null) return; _draft.Expanded = true; Sync(); }
    private void Clear_Click(object sender, RoutedEventArgs e) { if (_pane is null) return; _pane.SetMetadataFilter(EntryMetadataFilter.None); SetPane(_pane); }
    private void ClearTypes()
    {
        if (_pane is null || _draft is null) return;
        _draft.Extensions = "";
        Apply(_pane.MetadataFilter with { Categories = EntryTypeCategory.None, Extensions = [], NoExtension = false });
    }
    private void ClearDates()
    {
        if (_pane is null || _draft is null) return;
        _draft.DatePreset = 0; _draft.First = _draft.Last = null;
        Apply(_pane.MetadataFilter with { ModifiedStartDate = null, ModifiedEndDate = null, ModifiedFromUtcTicks = null, ModifiedUntilUtcTicks = null });
    }
    private void ClearSize()
    {
        if (_pane is null || _draft is null) return;
        _draft.SizePreset = 0; _draft.Minimum = _draft.Maximum = ""; _draft.Unit = 0;
        Apply(_pane.MetadataFilter with { MinimumBytes = null, MaximumBytes = null });
    }
}
