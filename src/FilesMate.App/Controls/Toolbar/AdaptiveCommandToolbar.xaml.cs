using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.Core.Entries;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
namespace FilesMate.App.Controls.Toolbar;

public sealed partial class AdaptiveCommandToolbar : UserControl
{
    public static readonly DependencyProperty ShowCommandLabelsProperty = DependencyProperty.Register(
        nameof(ShowCommandLabels),
        typeof(bool),
        typeof(AdaptiveCommandToolbar),
        new PropertyMetadata(true, OnShowCommandLabelsChanged));

    private CommandContext _context = CommandContext.ForToolbar(0);
    private bool _overflowPending;
    public bool SearchMode { get; set; }
    private readonly List<MenuFlyoutItem> _extraOverflowSortItems = [];
    private readonly List<(AppCommandId Id, MenuFlyoutItem Item)> _overflowFileItems = [];
    private readonly MenuFlyoutSeparator _fileOverflowSeparator = new();
    private readonly List<(AppCommandId Id, MenuFlyoutItem Item)> _newDocumentItems = [];
    private readonly MenuFlyoutSubItem _overflowNew = new();
    private EntrySort _sort = EntrySort.Name;
    private readonly MenuFlyoutSubItem _overflowDirection;
    private readonly MenuFlyoutSubItem _overflowGrouping;
    public event EventHandler<bool>? SortDirectionRequested;
    public event EventHandler<EntryGrouping>? GroupingRequested;
    public void SetSort(EntrySort sort) { _sort = sort; SyncGroupingIcon(); }

    public AdaptiveCommandToolbar()
    {
        InitializeComponent();
        InitializeHiddenFiles();
        NewFolderItem.Text = StringTable.Get("Command_NewFolder");
        NewFileItem.Text = StringTable.Get("NewDocument_Text");
        NewLabel.Text = StringTable.Get("Command_New");
        SortLabel.Text = StringTable.Get("Sort");
        Caption(SortButton, "Sort");
        Caption(ShelfButton, "Shelf_Title");
        ShelfLabel.Text = StringTable.Get("Shelf_Short");
        ViewLabel.Text = StringTable.Get("View_Options");
        Caption(MoreButton, "Nav_More");
        Caption(FolderSizesButton, "ShowFolderSizesTitle");
        FolderSizesLabel.Text = StringTable.Get("Column_Size");
        Caption(DualPaneButton, "DualPane");
        Caption(PaneArrangementButton, "Pane_Arrangement");
        Caption(PreviewButton, "PreviewPaneTitle");
        OverflowSortName.Text = StringTable.Get("SortByName");
        OverflowSortModified.Text = StringTable.Get("SortByModified");
        OverflowSortType.Text = StringTable.Get("SortByType");
        OverflowSortSize.Text = StringTable.Get("SortBySize");
        OverflowDetails.Text = StringTable.Get("DetailsView");
        OverflowGrid.Text = StringTable.Get("Layout_LargeIcons");
        OverflowList.Text = StringTable.Get("Layout_List");
        SortNameItem.Text = StringTable.Get("Sort_Name");
        SortModifiedItem.Text = StringTable.Get("Sort_Modified");
        SortTypeItem.Text = StringTable.Get("Sort_Type");
        SortSizeItem.Text = StringTable.Get("Sort_Size");
        var sortMenu = (MenuFlyout)SortButton.Flyout;
        var moreMenu = (MenuFlyout)MoreButton.Flyout;
        _overflowNew.Text = StringTable.Get("Command_New");
        var newFolderOverflow = new MenuFlyoutItem { Text = StringTable.Get("Command_NewFolder") };
        newFolderOverflow.Click += NewFolderItem_Click;
        _overflowNew.Items.Add(newFolderOverflow);
        foreach (var id in NewDocumentCommands.All)
        {
            MenuFlyoutItem CreateItem()
            {
                var item = new MenuFlyoutItem { Text = StringTable.Get(NewDocumentCommands.LabelKey(id)) };
                item.Click += (_, _) => CommandInvoked?.Invoke(this, id);
                _newDocumentItems.Add((id, item));
                return item;
            }
            if (id != AppCommandId.NewFile) ((MenuFlyout)NewButton.Flyout).Items.Add(CreateItem());
            _overflowNew.Items.Add(CreateItem());
        }
        FilesMate.App.Theming.FlyoutTheme.FollowHost(NewButton.Flyout);
        FilesMate.App.Theming.FlyoutTheme.FollowHost(moreMenu);
        FilesMate.App.Theming.FlyoutTheme.FollowHost(sortMenu);
        foreach (var column in DetailsColumn.Defaults().Skip(4).Where(c => c.CanSort))
        {
            var item = new MenuFlyoutItem { Text = column.Title };
            item.Click += (_, _) => SortRequested?.Invoke(this, column.Sort);
            sortMenu.Items.Add(item);
            var overflowItem = new MenuFlyoutItem { Name = "OverflowSort" + column.Id, Text = column.Title };
            overflowItem.Click += (_, _) => SortRequested?.Invoke(this, column.Sort);
            moreMenu.Items.Insert(4 + _extraOverflowSortItems.Count, overflowItem);
            _extraOverflowSortItems.Add(overflowItem);
        }
        foreach (var id in new[] { AppCommandId.NewFolder, AppCommandId.NewFile, AppCommandId.Cut, AppCommandId.Copy,
            AppCommandId.Paste, AppCommandId.Rename, AppCommandId.Share, AppCommandId.Recycle, AppCommandId.CopyPath })
        {
            var item = new MenuFlyoutItem { Name = "OverflowCommand" + id };
            item.Click += (_, e) => InvokeOverflowFileCommand(id, e);
            moreMenu.Items.Insert(_overflowFileItems.Count, item);
            _overflowFileItems.Add((id, item));
        }
        moreMenu.Items.Insert(_overflowFileItems.Count, _fileOverflowSeparator);
        moreMenu.Items.Insert(0, _overflowNew);
        sortMenu.Items.Add(new MenuFlyoutSeparator());
        sortMenu.Items.Add(Menus.SortDirectionMenu.Create(() => _sort, ascending => SortDirectionRequested?.Invoke(this, ascending)));
        _overflowDirection = Menus.SortDirectionMenu.Create(() => _sort, ascending => SortDirectionRequested?.Invoke(this, ascending));
        moreMenu.Items.Insert(moreMenu.Items.IndexOf(OverflowSortSize) + 1, _overflowDirection);
        sortMenu.Items.Add(Menus.EntryGroupingMenu.Create(() => _sort, grouping => GroupingRequested?.Invoke(this, grouping)));
        _overflowGrouping = Menus.EntryGroupingMenu.Create(() => _sort, grouping => GroupingRequested?.Invoke(this, grouping));
        moreMenu.Items.Insert(moreMenu.Items.IndexOf(_overflowDirection) + 1, _overflowGrouping);
        InitializeViewControls();
        Loaded += (_, _) => ScheduleOverflow();
        SetLayout(FileLayoutKind.Grid);
        ApplyContext(CommandContext.ForToolbar(0));
    }

    public event EventHandler<AppCommandId>? CommandInvoked;
    public event EventHandler<DragEventArgs>? ShelfDragOver;
    public event EventHandler<DragEventArgs>? ShelfDragLeave;
    public event EventHandler<DragEventArgs>? ShelfDrop;
    internal FrameworkElement ShelfAnchor => ShelfButton;

    private void ShelfButton_DragOver(object sender, DragEventArgs e) => ShelfDragOver?.Invoke(this, e);
    private void ShelfButton_DragLeave(object sender, DragEventArgs e) => ShelfDragLeave?.Invoke(this, e);
    private void ShelfButton_Drop(object sender, DragEventArgs e) => ShelfDrop?.Invoke(this, e);

    public event RoutedEventHandler? CopyPathClicked;

    public event EventHandler<FileLayoutKind>? LayoutChanged;

    public event RoutedEventHandler? PreviewClicked;

    public event RoutedEventHandler? DualPaneClicked;

    public event RoutedEventHandler? FolderSizesClicked;

    public event EventHandler<EntrySortColumn>? SortRequested;

    public bool ShowCommandLabels
    {
        get => (bool)GetValue(ShowCommandLabelsProperty);
        set => SetValue(ShowCommandLabelsProperty, value);
    }

    public void ApplyContext(CommandContext context)
    {
        _context = context with { Surface = CommandSurface.Toolbar };
        var states = ToolbarOverflowController.Present(_context);
        Apply(NewButton, Find(states, ToolbarCommandId.New));
        Apply(PasteButton, Find(states, ToolbarCommandId.Paste));
        Apply(CutButton, Find(states, ToolbarCommandId.Cut));
        Apply(CopyButton, Find(states, ToolbarCommandId.Copy));
        Apply(RenameButton, Find(states, ToolbarCommandId.Rename));
        Apply(ShareButton, Find(states, ToolbarCommandId.Share));
        Apply(DeleteButton, Find(states, ToolbarCommandId.Delete));
        Apply(CopyPathButton, Find(states, ToolbarCommandId.CopyPath));
        if (SearchMode) NewButton.Visibility = Visibility.Collapsed;
        Apply(SortButton, Find(states, ToolbarCommandId.Sort));
        NewFolderItem.IsEnabled = CommandCatalog.CanExecute(AppCommandId.NewFolder, _context);
        NewFileItem.IsEnabled = CommandCatalog.CanExecute(AppCommandId.NewFile, _context);
        HiddenFilesButton.Visibility = SearchMode ? Visibility.Collapsed : Visibility.Visible;
        ShelfButton.Visibility = PreviewButton.Visibility = Visibility.Visible;
        GroupingGroup.Visibility = GroupingButton.Visibility = Visibility.Visible;
        FolderSizesButton.Visibility = DualPaneButton.Visibility = PaneArrangementButton.Visibility = SearchMode ? Visibility.Collapsed : Visibility.Visible;
        ApplyHiddenTools();
        UpdateGroups();
        UpdateLabels();
        ApplyOverflow();
    }

    public void SetLayout(FileLayoutKind kind)
    {
        _viewKind = kind;
        ViewIcon.Glyph = kind switch { FileLayoutKind.List => "\uEA37", FileLayoutKind.Grid => "\uF0E2", _ => "\uE8FD" };
        var label = StringTable.Get(kind switch { FileLayoutKind.List => "Layout_List", FileLayoutKind.Grid => _viewSize.ZoomKey, _ => "Layout_Details" });
        ToolTipService.SetToolTip(ViewMenuButton, StringTable.Get("View_Options") + " · " + label);
        AutomationProperties.SetName(ViewMenuButton, StringTable.Get("View_Options") + " · " + label);
    }

    public void SetFolderSizesActive(bool active)
    {
        SetActive(FolderSizesButton, active);
    }

    public void SetDualPaneActive(bool active)
    {
        SetPaneCount(active ? 2 : 1);
    }

    public void SetPaneCount(int count)
    {
        _arrangementActive = count == 3;
        SetActive(DualPaneButton, count > 1);
        PaneDividerOne.Margin = new Thickness(count == 3 ? 6 : 9, 2, 0, 2);
        PaneDividerTwo.Visibility = count == 3 ? Visibility.Visible : Visibility.Collapsed;
        Caption(DualPaneButton, count == 1 ? "Pane_NextDual" : count == 2 ? "Pane_NextTriple" : "Pane_NextSingle");
    }

    private static void AnimateChoice(object sender)
    {
        if (sender is not Button { Content: FrameworkElement content } || App.Motion.Fast <= TimeSpan.Zero) return;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(content);
        visual.CenterPoint = new System.Numerics.Vector3((float)content.ActualWidth / 2, (float)content.ActualHeight / 2, 0);
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0, new System.Numerics.Vector3(.84f, .84f, 1));
        animation.InsertKeyFrame(1, System.Numerics.Vector3.One);
        animation.Duration = App.Motion.Fast;
        visual.StartAnimation("Scale", animation);
    }

    public void SetPreviewActive(bool active)
    {
        SetActive(PreviewButton, active);
    }

    private static void OnShowCommandLabelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AdaptiveCommandToolbar toolbar)
        {
            toolbar.ScheduleOverflow();
        }
    }

    private static ToolbarCommandState Find(IReadOnlyList<ToolbarCommandState> states, ToolbarCommandId id) =>
        states.First(state => state.Id == id);

    private static void Apply(Control control, ToolbarCommandState state)
    {
        control.Visibility = state.Visible ? Visibility.Visible : Visibility.Collapsed;
        control.IsEnabled = state.Enabled;
        AutomationProperties.SetName(control, state.Label);
        ToolTipService.SetToolTip(control, string.IsNullOrWhiteSpace(state.Tooltip) ? state.Label : state.Tooltip);
    }

    private static void Caption(Button button, string key)
    {
        var text = StringTable.Get(key);
        AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, text);
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleOverflow();

    private void ShelfButton_Click(object sender, RoutedEventArgs e) => CommandInvoked?.Invoke(this, AppCommandId.ShowShelf);

    private void ScheduleOverflow()
    {
        if (_overflowPending)
        {
            return;
        }

        _overflowPending = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ApplyPendingOverflow))
        {
            _overflowPending = false;
        }
    }

    private void ApplyPendingOverflow()
    {
        _overflowPending = false;
        if (!IsLoaded)
        {
            return;
        }

        UpdateLabels();
        ApplyOverflow();
    }

    private void ApplyOverflow()
    {
        if (SortButton is null)
        {
            return;
        }

        if (ActualWidth <= 0) return;
        LeadingGroups.Visibility = Visibility.Visible;
        LeadingGroups.Measure(new Windows.Foundation.Size(double.PositiveInfinity, 40));
        SortButton.Visibility = ViewMenuButton.Visibility = Visibility.Collapsed;
        MoreButton.Visibility = Visibility.Visible;
        ViewGroup.Measure(new Windows.Foundation.Size(double.PositiveInfinity, 40));
        var fileOverflow = ActualWidth < LeadingGroups.DesiredSize.Width + ViewGroup.DesiredSize.Width;
        LeadingGroups.Visibility = fileOverflow ? Visibility.Collapsed : Visibility.Visible;
        var width = Math.Max(0, ActualWidth - (fileOverflow ? 0 : LeadingGroups.DesiredSize.Width));
        var overflow = ToolbarOverflow.ForWidth(width);
        var sortState = CommandCatalog.Resolve(AppCommandId.Sort, _context);
        SortButton.Visibility = overflow.ShowSort && sortState.Visible && !Hidden(ToolbarTool.Sort) ? Visibility.Visible : Visibility.Collapsed;
        ViewMenuButton.Visibility = overflow.ShowView && !Hidden(ToolbarTool.ViewMenu) ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.Visibility = overflow.ShowMore || fileOverflow ? Visibility.Visible : Visibility.Collapsed;

        // Account for labels, font scaling and the always-visible utility buttons.
        ViewGroup.Measure(new Windows.Foundation.Size(double.PositiveInfinity, 40));
        if (ViewGroup.DesiredSize.Width > width)
        {
            overflow = overflow with { ShowSort = false, ShowMore = true };
            SortButton.Visibility = Visibility.Collapsed;
            MoreButton.Visibility = Visibility.Visible;
            ViewGroup.Measure(new Windows.Foundation.Size(double.PositiveInfinity, 40));
            if (ViewGroup.DesiredSize.Width > width)
            {
                overflow = overflow with { ShowView = false };
                ViewMenuButton.Visibility = Visibility.Collapsed;
            }
        }
        foreach (var (id, item) in _overflowFileItems)
        {
            var command = CommandCatalog.Resolve(id, _context);
            item.Text = command.Label;
            item.IsEnabled = command.Enabled;
            item.Visibility = fileOverflow && (command.Visible || id == AppCommandId.NewFile) ? Visibility.Visible : Visibility.Collapsed;
        }
        _fileOverflowSeparator.Visibility = fileOverflow ? Visibility.Visible : Visibility.Collapsed;
        _overflowHiddenFiles.Visibility = fileOverflow && !SearchMode && !Hidden(ToolbarTool.HiddenFiles) ? Visibility.Visible : Visibility.Collapsed;

        var sortOverflow = overflow.ShowSort || Hidden(ToolbarTool.Sort) ? Visibility.Collapsed : Visibility.Visible;
        _overflowSortPanel.Visibility = SearchMode ? Visibility.Collapsed : sortOverflow;
        _overflowDirection.Visibility = _overflowGrouping.Visibility = Visibility.Collapsed;
        OverflowSortName.Visibility = OverflowSortModified.Visibility = OverflowSortType.Visibility = OverflowSortSize.Visibility = Visibility.Collapsed;
        foreach (var item in _extraOverflowSortItems) item.Visibility = Visibility.Collapsed;

        var viewOverflow = overflow.ShowView ? Visibility.Collapsed : Visibility.Visible;
        OverflowDetails.Visibility = OverflowGrid.Visibility = OverflowList.Visibility = Hidden(ToolbarTool.ViewMenu) ? Visibility.Collapsed : viewOverflow;
        _overflowNew.Visibility = fileOverflow && !SearchMode && !Hidden(ToolbarTool.New) ? Visibility.Visible : Visibility.Collapsed;
        _overflowNew.IsEnabled = _context.IsFolderWritable;
        foreach (var (id, item) in _newDocumentItems) item.IsEnabled = CommandCatalog.CanExecute(id, _context);
        foreach (var (id, item) in _overflowFileItems)
        {
            if (id is AppCommandId.NewFolder or AppCommandId.NewFile) item.Visibility = Visibility.Collapsed;
            var name = id == AppCommandId.Recycle ? nameof(ToolbarTool.Delete) : id.ToString();
            if (Enum.TryParse<ToolbarTool>(name, out var tool) && Hidden(tool)) item.Visibility = Visibility.Collapsed;
        }
        OverflowSeparator.Visibility = !overflow.ShowSort && !overflow.ShowView
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (SearchMode)
        {
            SortButton.Visibility = FolderSizesButton.Visibility = DualPaneButton.Visibility = PaneArrangementButton.Visibility = Visibility.Collapsed;
            OverflowSortName.Visibility = OverflowSortModified.Visibility = OverflowSortType.Visibility = OverflowSortSize.Visibility = Visibility.Collapsed;
            foreach (var item in _extraOverflowSortItems) item.Visibility = Visibility.Collapsed;
            foreach (var (id, item) in _overflowFileItems)
                if (id is AppCommandId.NewFolder or AppCommandId.NewFile) item.Visibility = Visibility.Collapsed;
        }
        if (!((MenuFlyout)MoreButton.Flyout).Items.Any(item => item is not MenuFlyoutSeparator && item.Visibility == Visibility.Visible))
            MoreButton.Visibility = Visibility.Collapsed;
        ApplyHiddenTools();
    }

    private void UpdateGroups()
    {
        var create = NewButton.Visibility == Visibility.Visible;
        var clipboard = AnyVisible(CutButton, CopyButton, PasteButton);
        var organize = AnyVisible(RenameButton, ShareButton, DeleteButton, CopyPathButton, HiddenFilesButton);
        CreateGroup.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
        ClipboardGroup.Visibility = clipboard ? Visibility.Visible : Visibility.Collapsed;
        OrganizeGroup.Visibility = organize ? Visibility.Visible : Visibility.Collapsed;
        CreateSeparator.Visibility = create && (clipboard || organize) ? Visibility.Visible : Visibility.Collapsed;
        ClipboardSeparator.Visibility = clipboard && organize ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateLabels()
    {
        if (NewLabel is null || SortLabel is null || FolderSizesLabel is null)
        {
            return;
        }

        var labels = ShowCommandLabels && (ActualWidth == 0 || ActualWidth >= 700) ? Visibility.Visible : Visibility.Collapsed;
        NewLabel.Visibility = labels;
        SortLabel.Visibility = labels;
        FolderSizesLabel.Visibility = labels;
        ViewLabel.Visibility = labels;
    }

    private static bool AnyVisible(params UIElement[] elements)
    {
        foreach (var element in elements)
        {
            if (element.Visibility == Visibility.Visible)
            {
                return true;
            }
        }

        return false;
    }

    private void CopyPathItem_Click(object sender, RoutedEventArgs e) => CopyPathClicked?.Invoke(this, e);

    private void InvokeOverflowFileCommand(AppCommandId id, RoutedEventArgs e)
    {
        if (id == AppCommandId.CopyPath) CopyPathClicked?.Invoke(this, e);
        else CommandInvoked?.Invoke(this, id);
    }

    private void NewFolderItem_Click(object sender, RoutedEventArgs e) =>
        CommandInvoked?.Invoke(this, AppCommandId.NewFolder);

    private void NewFileItem_Click(object sender, RoutedEventArgs e) =>
        CommandInvoked?.Invoke(this, AppCommandId.NewFile);

    private void CutButton_Click(object sender, RoutedEventArgs e) =>
        CommandInvoked?.Invoke(this, AppCommandId.Cut);

    private void CopyButton_Click(object sender, RoutedEventArgs e) =>
        CommandInvoked?.Invoke(this, AppCommandId.Copy);

    private void PasteButton_Click(object sender, RoutedEventArgs e) =>
        CommandInvoked?.Invoke(this, AppCommandId.Paste);

    private void RenameButton_Click(object sender, RoutedEventArgs e) =>
        CommandInvoked?.Invoke(this, AppCommandId.Rename);

    private void ShareButton_Click(object sender, RoutedEventArgs e) =>
        CommandInvoked?.Invoke(this, AppCommandId.Share);

    private void DeleteButton_Click(object sender, RoutedEventArgs e) =>
        CommandInvoked?.Invoke(this, AppCommandId.Recycle);

    private void DetailsViewButton_Click(object sender, RoutedEventArgs e)
    { LayoutChanged?.Invoke(this, FileLayoutKind.Details); }

    private void GridViewButton_Click(object sender, RoutedEventArgs e)
    { GridSizeRequested?.Invoke(this, GridSizePreset.Large); LayoutChanged?.Invoke(this, FileLayoutKind.Grid); }

    private void ListViewButton_Click(object sender, RoutedEventArgs e)
    { LayoutChanged?.Invoke(this, FileLayoutKind.List); }

    private void FolderSizesButton_Click(object sender, RoutedEventArgs e) =>
        FolderSizesClicked?.Invoke(this, e);

    private void DualPaneButton_Click(object sender, RoutedEventArgs e)
    { DualPaneClicked?.Invoke(this, e); AnimateChoice(DualPaneButton); }

    private void PreviewButton_Click(object sender, RoutedEventArgs e) =>
        PreviewClicked?.Invoke(this, e);

    private void SortName_Click(object sender, RoutedEventArgs e) =>
        SortRequested?.Invoke(this, EntrySortColumn.Name);

    private void SortModified_Click(object sender, RoutedEventArgs e) =>
        SortRequested?.Invoke(this, EntrySortColumn.Modified);

    private void SortType_Click(object sender, RoutedEventArgs e) =>
        SortRequested?.Invoke(this, EntrySortColumn.Type);

    private void SortSize_Click(object sender, RoutedEventArgs e) =>
        SortRequested?.Invoke(this, EntrySortColumn.Size);

    private static void SetActive(Button button, bool active) => button.Style =
        (Style)Application.Current.Resources[active ? "SelectedCommandBarButtonStyle" : "CommandBarButtonStyle"];
}
