using FilesMate.App.Localization;
using FilesMate.App.Theming;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace FilesMate.App.Views;

/// <summary>Compact first choice; metadata and previews are revealed only on request.</summary>
internal sealed class FileConflictBody : Grid, IAsyncDisposable
{
    private readonly ContentDialog _dialog;
    private readonly FrameworkElement _host;
    private readonly FileConflict _conflict;
    private readonly FileConflictDialog.Session _session;
    private readonly CancellationToken _token;
    private readonly StackPanel _header = new() { Spacing = 5 };
    private readonly Border _headerBand;
    private readonly StackPanel _actions = new() { Spacing = 2 };
    private readonly StackPanel _footer = new() { Spacing = 6 };
    private readonly ScrollViewer _actionScroll;
    private readonly Grid _decisions = new() { ColumnSpacing = 8, RowSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly List<Button> _decisionButtons = [];
    private Button? _cancel;
    private FileConflictComparisonView? _comparison;
    private bool _comparing;
    public FileConflictChoice Choice { get; private set; } = new(FileConflictAction.Cancel);

    public FileConflictBody(ContentDialog dialog, FrameworkElement host, FileConflict conflict, FileConflictDialog.Session session, CancellationToken token)
    {
        _dialog = dialog; _host = host; _conflict = conflict; _session = session; _token = token;
        RowSpacing = 8;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        _headerBand = new Border { Child = _header, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(8) };
        ThemeResources.Bind(_headerBand, Border.BackgroundProperty, "FilesMate.Compare.SourceFillBrush");
        Children.Add(_headerBand);
        _actionScroll = new ScrollViewer { Content = _actions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(_actionScroll, 1); Children.Add(_actionScroll);
        var footerBand = new Border { Child = _footer, Padding = new Thickness(0, FileConflictDialog.FooterInset, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0) };
        ThemeResources.Bind(footerBand, Border.BorderBrushProperty, "FilesMate.Card.BorderBrush");
        Grid.SetRow(footerBand, 2); Children.Add(footerBand);
        var title = Label(StringTable.Get(conflict.CanMerge ? "Files_FolderConflict" : "Conflict_Exists"), 13, true);
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; _header.Children.Add(title);
        _header.Children.Add(FileName(conflict.Destination));
        var location = Label(Path.GetDirectoryName(conflict.Destination) ?? conflict.Destination, 12, true);
        location.TextWrapping = TextWrapping.NoWrap; location.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(location, conflict.Destination); _header.Children.Add(location);
        AddNecessaryNotice();
        var batch = conflict.IsBatch && !conflict.ChangedSinceDecision;
        if (conflict.CanMerge) Action("Conflict_Merge", "Conflict_MergeHint", "\uE8B7", FileConflictAction.Merge, batch);
        if (conflict.CanReplace)
            Action(batch && !conflict.BackupUnavailable ? "Conflict_ReplaceAll" : "Conflict_ReplaceFile",
                conflict.BackupUnavailable ? "Backup_DestructiveHint" : "Conflict_ReplaceHint", "\uE8AB",
                conflict.BackupUnavailable ? FileConflictAction.ReplaceWithoutUndo : FileConflictAction.Replace,
                batch && !conflict.BackupUnavailable);
        Action(batch ? "Conflict_SkipAll" : "Conflict_SkipFile", "Conflict_SkipHint", "\uE72A", FileConflictAction.Skip, batch);
        if (!conflict.IsSameItem)
        {
            var compare = ActionButton(StringTable.Get(batch ? "Conflict_DecideEach" : "Conflict_Compare"), StringTable.Get("Conflict_CompareHint"), "\uE721");
            AutomationProperties.SetAutomationId(compare, "ConflictCompare");
            compare.Click += (_, _) => { _session.DecideIndividually = batch; Compare(); }; _actions.Children.Add(compare);
        }
        Action(batch ? "Conflict_KeepAll" : "Conflict_KeepFiles", StringTable.Format("Transfer_NumberedName", conflict.NumberedName),
            "\uE8C8", FileConflictAction.KeepBoth, batch, literalHint: true);
        var cancel = DecisionButton("Cancel", FileConflictAction.Cancel, () => false);
        cancel.HorizontalAlignment = HorizontalAlignment.Right; _footer.Children.Add(cancel);
        _host.SizeChanged += HostSizeChanged;
        _headerBand.SizeChanged += (_, _) => Resize();
        footerBand.SizeChanged += (_, _) => Resize();
        Resize();
        dialog.AddHandler(KeyDownEvent, new KeyEventHandler((_, e) =>
        { if (e.Key == Windows.System.VirtualKey.Escape) { Choice = new(FileConflictAction.Cancel); dialog.Hide(); e.Handled = true; } }), true);
    }

    private TextBlock FileName(string path)
    {
        var name = Label(Path.GetFileName(path), 14); name.MaxLines = 2; name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
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

    private void Action(string title, string hint, string glyph, FileConflictAction action, bool all, bool literalHint = false)
    {
        var button = ActionButton(StringTable.Get(title), literalHint ? hint : StringTable.Get(hint), glyph);
        AutomationProperties.SetAutomationId(button, "Conflict" + action);
        button.Click += (_, _) => Complete(action, all); _actions.Children.Add(button);
    }

    private void Complete(FileConflictAction action, bool all)
    {
        Choice = new(action, action is not (FileConflictAction.Cancel or FileConflictAction.ReplaceWithoutUndo) && all);
        _dialog.Hide();
    }

    public void Compare()
    {
        if (_comparing) return;
        _comparing = true; _header.Children.Clear();
        _header.Children.Add(FileName(_conflict.Destination));
        _header.Children.Add(Details(_conflict)); AddNecessaryNotice();
        _actionScroll.Visibility = Visibility.Collapsed; _footer.Children.Clear();
        _comparison = new(_conflict, _token); Grid.SetRow(_comparison, 1); Children.Add(_comparison);
        _comparison.PreferredHeightChanged += (_, _) => Resize();
        var all = new CheckBox { Content = StringTable.Get("Conflict_ApplyRemaining"), MinHeight = 28,
            IsEnabled = !_conflict.BackupUnavailable, Visibility = _conflict.IsBatch ? Visibility.Visible : Visibility.Collapsed };
        _footer.Children.Add(all);
        bool ApplyAll() => all.IsEnabled && all.IsChecked == true;
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
        Width = Math.Max(240, Math.Min(_comparing ? 680 : 340, root.Size.Width - 64));
        MaxHeight = Math.Max(200, root.Size.Height - 100);
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

    private static Button ActionButton(string title, string hint, string glyph)
    {
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new());
        var icon = new FontIcon { Glyph = glyph, FontSize = 16, Width = 20 };
        ThemeResources.Bind(icon, FontIcon.ForegroundProperty, "FilesMate.Selection.AccentBrush");
        content.Children.Add(icon);
        var text = Label(title, 13); text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1); content.Children.Add(text);
        var button = new Button { Content = content, Style = (Style)Application.Current.Resources["QuietButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 34, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(6) };
        AutomationProperties.SetName(button, title); AutomationProperties.SetHelpText(button, hint);
        ToolTipService.SetToolTip(button, hint); return button;
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
