using FilesMate.App.Localization;
using FilesMate.App.Theming;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace FilesMate.App.Views;

/// <summary>One-click conflict choices; metadata and previews are revealed on request.</summary>
internal sealed class FileConflictBody : Grid, IAsyncDisposable
{
    private readonly ContentDialog _dialog;
    private readonly FrameworkElement _host;
    private readonly FileConflict _conflict;
    private readonly FileConflictDialog.Session _session;
    private readonly CancellationToken _token;
    private readonly StackPanel _header = new() { Spacing = 8 };
    private readonly Border _headerBand;
    private readonly StackPanel _actions = new() { Spacing = 8 };
    private readonly StackPanel _footer = new() { Spacing = 6 };
    private readonly Border _footerDivider = new() { Height = 1, Margin = new Thickness(0, 4, 0, 4) };
    private readonly ScrollViewer _actionScroll;
    private readonly Grid _decisions = new() { ColumnSpacing = 8, RowSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly List<Button> _decisionButtons = [];
    private readonly CheckBox _applyRemaining;
    private readonly Grid _applyScope;
    private Button? _cancel;
    private FileConflictComparisonView? _comparison;
    private bool _comparing;
    public FileConflictChoice Choice { get; private set; } = new(FileConflictAction.Cancel);

    public FileConflictBody(ContentDialog dialog, FrameworkElement host, FileConflict conflict, FileConflictDialog.Session session, CancellationToken token)
    {
        _dialog = dialog; _host = host; _conflict = conflict; _session = session; _token = token;
        RowSpacing = 12;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        dialog.Title = StringTable.Get("Conflict_Title");
        _headerBand = new Border { Child = _header, HorizontalAlignment = HorizontalAlignment.Stretch };
        Children.Add(_headerBand);
        _actionScroll = new ScrollViewer { Content = _actions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(_actionScroll, "ConflictActions");
        Grid.SetRow(_actionScroll, 1); Children.Add(_actionScroll);
        var footerBand = new Border { Child = _footer };
        ThemeResources.Bind(_footerDivider, Border.BackgroundProperty, "FilesMate.Card.BorderBrush");
        Grid.SetRow(footerBand, 2); Children.Add(footerBand);
        _header.Children.Add(ConflictInfo(conflict));
        AddNecessaryNotice();
        var batch = conflict.IsBatch && !conflict.ChangedSinceDecision;
        if (conflict.CanMerge) Action("Conflict_Merge", "Conflict_MergeHint", "\uE8B7", FileConflictAction.Merge, batch);
        if (conflict.CanReplace)
            Action("Conflict_ReplaceFile",
                conflict.BackupUnavailable ? "Backup_DestructiveHint" : "Conflict_ReplaceHint", "\uE8AB",
                conflict.BackupUnavailable ? FileConflictAction.ReplaceWithoutUndo : FileConflictAction.Replace,
                batch);
        Action("Conflict_SkipFile", "Conflict_SkipHint", "\uE72A", FileConflictAction.Skip, batch);
        Action("Conflict_KeepFiles", "Conflict_KeepHint", "\uE8C8", FileConflictAction.KeepBoth, batch);
        var scopeLabel = Label(StringTable.Get("Conflict_ApplyRemaining"), 12);
        scopeLabel.VerticalAlignment = VerticalAlignment.Center;
        _applyRemaining = new CheckBox { Style = (Style)Application.Current.Resources["FilesMate.FileSelectionCheckBoxStyle"],
            IsTabStop = true, IsChecked = batch && !conflict.BackupUnavailable && session.ApplyRemaining != false, VerticalAlignment = VerticalAlignment.Center };
        _applyRemaining.Checked += (_, _) => _session.ApplyRemaining = true;
        _applyRemaining.Unchecked += (_, _) => { _session.ApplyRemaining = false; _session.DecideIndividually = batch; };
        _applyScope = new Grid { ColumnSpacing = 10, MinHeight = 32, Margin = new Thickness(0, 4, 0, 0),
            Visibility = batch ? Visibility.Visible : Visibility.Collapsed };
        _applyScope.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _applyScope.ColumnDefinitions.Add(new());
        _applyScope.Children.Add(_applyRemaining); Grid.SetColumn(scopeLabel, 1); _applyScope.Children.Add(scopeLabel);
        scopeLabel.Tapped += (_, args) => { _applyRemaining.IsChecked = _applyRemaining.IsChecked != true; args.Handled = true; };
        AutomationProperties.SetAutomationId(_applyRemaining, "ConflictApplyRemaining");
        AutomationProperties.SetName(_applyRemaining, StringTable.Get("Conflict_ApplyRemaining"));
        AutomationProperties.SetLabeledBy(_applyRemaining, scopeLabel);
        ToolTipService.SetToolTip(_applyRemaining, StringTable.Get("Conflict_BatchScope"));
        _footer.Children.Add(_applyScope);
        _footer.Children.Add(_footerDivider);
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (!conflict.IsSameItem)
        {
            var label = StringTable.Get("Conflict_Details");
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new FontIcon { Glyph = "\uE70D", FontSize = 12 });
            content.Children.Add(Label(label));
            var compare = new Button { Content = content, MinHeight = 32, Padding = new Thickness(0, 6, 8, 6),
                HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)Application.Current.Resources["QuietButtonStyle"] };
            AutomationProperties.SetAutomationId(compare, "ConflictCompare");
            AutomationProperties.SetName(compare, label);
            ToolTipService.SetToolTip(compare, StringTable.Get("Conflict_CompareHint"));
            compare.Click += (_, _) => { _session.DecideIndividually = batch; Compare(); };
            footer.Children.Add(compare);
        }
        var cancel = DecisionButton("Cancel", FileConflictAction.Cancel, () => false);
        Grid.SetColumn(cancel, 1); footer.Children.Add(cancel);
        _footer.Children.Add(footer);
        _host.SizeChanged += HostSizeChanged;
        _headerBand.SizeChanged += (_, _) => Resize();
        footerBand.SizeChanged += (_, _) => Resize();
        Resize();
        dialog.AddHandler(KeyDownEvent, new KeyEventHandler((_, e) =>
        { if (e.Key == Windows.System.VirtualKey.Escape) { Choice = new(FileConflictAction.Cancel); dialog.Hide(); e.Handled = true; } }), true);
    }

    private static TextBlock FileName(string path)
    {
        var name = Label(Path.GetFileName(path), 16); name.MaxLines = 2; name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        name.TextTrimming = TextTrimming.CharacterEllipsis; ToolTipService.SetToolTip(name, path); return name;
    }

    private void AddNecessaryNotice()
    {
        if (_conflict.ChangedSinceDecision) _header.Children.Add(Label(StringTable.Get("Conflict_Changed"), 12));
        if (_conflict.BackupUnavailable) _header.Children.Add(Label(StringTable.Get("Conflict_BackupUnavailable"), 12));
        if (_conflict.IsSameItem) _header.Children.Add(Label(StringTable.Get("Transfer_SameItemHint"), 12));
        else if (!_conflict.CanMerge && !_conflict.CanReplace)
            _header.Children.Add(Label(StringTable.Get(_conflict.DestinationIsLink ? "Transfer_LinkHint" : "Transfer_TypeHint"), 12));
    }

    private void Action(string title, string hint, string glyph, FileConflictAction action, bool all)
    {
        var text = action == FileConflictAction.KeepBoth
            ? StringTable.Format("Transfer_NumberedName", _conflict.NumberedName) : StringTable.Get(hint);
        var button = ActionButton(StringTable.Get(title), text, glyph,
            action == FileConflictAction.ReplaceWithoutUndo ? StringTable.Get("Backup_ReplaceWithoutUndo")
                : action == FileConflictAction.KeepBoth ? StringTable.Get(hint) : null);
        AutomationProperties.SetAutomationId(button, "Conflict" + action);
        button.Click += (_, _) => Complete(action, all); _actions.Children.Add(button);
    }

    private void Complete(FileConflictAction action, bool all)
    {
        _session.ApplyRemaining = _applyRemaining.IsChecked == true;
        Choice = new(action, action != FileConflictAction.Cancel && all && _applyRemaining.IsChecked == true);
        _dialog.Hide();
    }

    public void Compare()
    {
        if (_comparing) return;
        _comparing = true; _header.Children.Clear();
        _header.Children.Add(FileName(_conflict.Destination));
        _header.Children.Add(Details(_conflict));
        var numberedName = Label(StringTable.Format("Transfer_NumberedName", _conflict.NumberedName), 12, true);
        numberedName.MaxLines = 2; numberedName.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(numberedName, numberedName.Text);
        _header.Children.Add(numberedName); AddNecessaryNotice();
        _actionScroll.Visibility = Visibility.Collapsed; _footer.Children.Clear();
        _comparison = new(_conflict, _token); Grid.SetRow(_comparison, 1); Children.Add(_comparison);
        _comparison.PreferredHeightChanged += (_, _) => Resize();
        _applyScope.Visibility = _conflict.IsBatch ? Visibility.Visible : Visibility.Collapsed;
        _footer.Children.Add(_applyScope);
        _footer.Children.Add(_footerDivider);
        bool ApplyAll() => _applyRemaining.IsChecked == true;
        void Add(string key, FileConflictAction action)
        { var button = DecisionButton(key, action, ApplyAll); _decisionButtons.Add(button); _decisions.Children.Add(button); }
        if (_conflict.CanMerge) Add("Files_Merge", FileConflictAction.Merge);
        if (_conflict.CanReplace) Add("Transfer_ReplaceButton", _conflict.BackupUnavailable ? FileConflictAction.ReplaceWithoutUndo : FileConflictAction.Replace);
        Add("Files_Skip", FileConflictAction.Skip);
        Add("Transfer_KeepBothButton", FileConflictAction.KeepBoth);
        Add("Cancel", FileConflictAction.Cancel);
        _footer.Children.Add(_decisions);
        Resize(); AppTypography.Apply(this); _comparison.Start();
    }

    private Button DecisionButton(string key, FileConflictAction action, Func<bool> applyAll)
    {
        var button = new Button { Content = StringTable.Get(key), MinHeight = 30, MinWidth = 64, FontSize = 13,
            Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(6) };
        if (action == FileConflictAction.Cancel)
        {
            _cancel = button;
            button.TabIndex = 0;
            button.Loaded += (_, _) => FocusCancel();
        }
        AutomationProperties.SetAutomationId(button, "ConflictChoose" + action);
        var hint = action switch
        {
            FileConflictAction.KeepBoth => StringTable.Format("Transfer_NumberedName", _conflict.NumberedName),
            FileConflictAction.Replace => StringTable.Get("Conflict_ReplaceHint"),
            FileConflictAction.ReplaceWithoutUndo => StringTable.Get("Backup_DestructiveHint"),
            FileConflictAction.Skip => StringTable.Get("Conflict_SkipHint"),
            _ => StringTable.Get(key),
        };
        ToolTipService.SetToolTip(button, hint); AutomationProperties.SetHelpText(button, hint);
        button.Click += (_, _) => Complete(action, applyAll()); return button;
    }

    public void FocusCancel() => _cancel?.Focus(FocusState.Programmatic);

    public async ValueTask DisposeAsync()
    {
        _host.SizeChanged -= HostSizeChanged;
        if (_comparison is { } comparison) { _comparison = null; await comparison.DisposeAsync(); }
    }

    private void HostSizeChanged(object sender, SizeChangedEventArgs args) => Resize();
    private void Resize()
    {
        if (_host.XamlRoot is not { } root) return;
        Width = Math.Max(240, Math.Min(_comparing ? 680 : 520, root.Size.Width - 64));
        MaxHeight = Math.Max(200, root.Size.Height - 80);
        Height = _comparing ? Math.Min(MaxHeight, Math.Max(72, _headerBand.ActualHeight) + Math.Max(30, _footer.ActualHeight) + 9
            + RowSpacing * 2 + (_comparison?.PreferredHeight ?? 330)) : double.NaN;
        _dialog.Resources["ContentDialogMaxWidth"] = Math.Max(280, Math.Min(980, root.Size.Width - 24));
        if (!_comparing) return;
        var columns = Width < 440 ? 2 : _decisionButtons.Count;
        _decisions.ColumnDefinitions.Clear(); _decisions.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) _decisions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        for (var i = 0; i < (_decisionButtons.Count + columns - 1) / columns; i++) _decisions.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < _decisionButtons.Count; i++) { Grid.SetColumn(_decisionButtons[i], i % columns); Grid.SetRow(_decisionButtons[i], i / columns); }
    }

    internal static TextBlock Label(string text, double size = 13, bool secondary = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        ThemeResources.Bind(label, TextBlock.ForegroundProperty, secondary ? "FilesMate.Text.SecondaryBrush" : "FilesMate.Text.PrimaryBrush");
        return label;
    }

    private static Button ActionButton(string title, string hint, string glyph, string? displayHint = null)
    {
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new());
        var icon = new FontIcon { Glyph = glyph, FontSize = 16 };
        ThemeResources.Bind(icon, FontIcon.ForegroundProperty, "FilesMate.Conflict.ActionHoverBorderBrush");
        var iconFrame = new Border { Child = icon, Width = 32, Height = 32, CornerRadius = new CornerRadius(16),
            VerticalAlignment = VerticalAlignment.Center };
        ThemeResources.Bind(iconFrame, Border.BackgroundProperty, "FilesMate.Conflict.IconFillBrush");
        content.Children.Add(iconFrame);
        var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var text = Label(title, 15); text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        labels.Children.Add(text);
        var description = Label(displayHint ?? hint, 12);
        ThemeResources.Bind(description, TextBlock.ForegroundProperty, "FilesMate.Conflict.DescriptionBrush");
        labels.Children.Add(description);
        Grid.SetColumn(labels, 1); content.Children.Add(labels);
        var button = new Button { Content = content, Style = (Style)Application.Current.Resources["FilesMate.ConflictActionButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 64, Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
        AutomationProperties.SetName(button, title); AutomationProperties.SetHelpText(button, hint);
        ToolTipService.SetToolTip(button, hint); return button;
    }

    private static Border ConflictInfo(FileConflict conflict)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new());
        void Side(int column, string key, string path)
        {
            var side = new Grid { ColumnSpacing = 6 };
            side.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); side.ColumnDefinitions.Add(new());
            var caption = Label(StringTable.Get(key), 11, true); caption.VerticalAlignment = VerticalAlignment.Center;
            side.Children.Add(caption);
            var folder = Path.GetDirectoryName(path) ?? path;
            var location = Label(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), 12);
            if (string.IsNullOrEmpty(location.Text)) location.Text = folder;
            location.TextWrapping = TextWrapping.NoWrap; location.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(location, path); AutomationProperties.SetHelpText(location, path);
            Grid.SetColumn(location, 1); side.Children.Add(location); Grid.SetColumn(side, column); grid.Children.Add(side);
        }
        Side(0, "Conflict_SourceLocation", conflict.Source);
        var arrow = new FontIcon { Glyph = "\uE72A", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        ThemeResources.Bind(arrow, FontIcon.ForegroundProperty, "FilesMate.Text.SecondaryBrush");
        Grid.SetColumn(arrow, 1); grid.Children.Add(arrow);
        Side(2, "Conflict_DestinationLocation", conflict.Destination);
        var information = new StackPanel { Spacing = 10 };
        information.Children.Add(FileName(conflict.Destination));
        information.Children.Add(grid);
        var band = new Border { Child = information, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(band, "ConflictLocations");
        ThemeResources.Bind(band, Border.BackgroundProperty, "FilesMate.Compare.SourceFillBrush");
        return band;
    }

    private static Grid Details(FileConflict conflict)
    {
        var grid = new Grid { ColumnSpacing = 12 }; grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        void Side(int column, string key, string path, FileConflictDetails? details)
        {
            var stack = new StackPanel { Spacing = 3 };
            var label = Label(StringTable.Get(key), 12); label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; stack.Children.Add(label);
            var folder = Path.GetDirectoryName(path) ?? path;
            var location = Label(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), 12, true);
            if (string.IsNullOrEmpty(location.Text)) location.Text = folder;
            location.TextWrapping = TextWrapping.NoWrap; location.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(location, path); AutomationProperties.SetHelpText(location, path); stack.Children.Add(location);
            if (details is not null) stack.Children.Add(Label(StringTable.Format("Transfer_Bytes", details.Length.ToString("N0")) + " · "
                + details.LastWriteTimeUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss"), 11, true));
            Grid.SetColumn(stack, column); grid.Children.Add(stack);
        }
        Side(0, "Conflict_OriginalItem", conflict.Destination, conflict.Existing);
        Side(1, "Conflict_NewItem", conflict.Source, conflict.Incoming);
        return grid;
    }
}
