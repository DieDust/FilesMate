using System.Collections.ObjectModel;
using FilesMate.App.Commands;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace FilesMate.App.Controls.Toolbar;

public sealed partial class AdaptiveCommandToolbar
{
    private readonly Dictionary<ToolbarTool, FrameworkElement> _toolbarUnits = [];
    private readonly MenuFlyoutItem _overflowFilters = new();
    private bool Hidden(ToolbarTool tool) => App.ExplorerPreferences.HiddenToolbarTools?.Contains(tool) == true;

    private void InitializeToolbarLayout()
    {
        if (!_isCustomizationPreview)
            AddHandler(UIElement.RightTappedEvent, new RightTappedEventHandler(Toolbar_RightTapped), handledEventsToo: true);
        foreach (var definition in ToolbarToolCatalog.All)
        {
            if (FindName(definition.ElementName) is not FrameworkElement unit)
                throw new InvalidOperationException("Missing toolbar command unit: " + definition.ElementName);
            _toolbarUnits.Add(definition.Id, unit);
        }
        CreateGroup.Children.Clear(); ClipboardGroup.Children.Clear(); OrganizeGroup.Children.Clear();
        LeadingGroups.Children.Clear(); ViewGroup.Children.Clear();
        ApplyToolbarOrder();
        if (_isCustomizationPreview) return;
        _overflowFilters.Text = StringTable.Get(ToolbarToolCatalog.Get(ToolbarTool.Filters).LabelKey);
        _overflowFilters.Icon = new FontIcon { Glyph = ToolbarToolCatalog.Get(ToolbarTool.Filters).Glyph };
        _overflowFilters.Click += (_, _) => CommandInvoked?.Invoke(this, AppCommandId.ShowFolderFilters);
        ((MenuFlyout)MoreButton.Flyout).Items.Add(_overflowFilters);
    }

    private void ApplyToolbarOrder()
    {
        foreach (var (zone, panel) in new[] { (ToolbarZone.FileCommands, LeadingGroups), (ToolbarZone.ViewTools, ViewGroup) })
        {
            var desired = ToolbarToolCatalog.Ordered(App.ExplorerPreferences.ToolbarOrder, zone, false)
                .Select(d => _toolbarUnits[d.Id]).Cast<UIElement>().ToList();
            if (zone == ToolbarZone.ViewTools) desired.Add(MoreButton);
            if (panel.Children.SequenceEqual(desired)) continue;
            panel.Children.Clear();
            foreach (var element in desired) panel.Children.Add(element);
        }
    }

    private void ApplyHiddenTools()
    {
        ApplyToolbarOrder();
        var headerVisible = !SearchMode && FilterHeader.Content is FrameworkElement { Visibility: Visibility.Visible };
        FilterHeader.Visibility = headerVisible && !Hidden(ToolbarTool.Filters) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var definition in ToolbarToolCatalog.All)
            if (Hidden(definition.Id) || SearchMode && !definition.AvailableInSearch)
                _toolbarUnits[definition.Id].Visibility = Visibility.Collapsed;
        if (Hidden(ToolbarTool.SplitView)) PaneArrangementButton.Visibility = Visibility.Collapsed;
        _toolbarUnits[ToolbarTool.SplitView].Visibility = !SearchMode && !Hidden(ToolbarTool.SplitView) ? Visibility.Visible : Visibility.Collapsed;
        GroupingGroup.Visibility = _recursiveViewEnabled || Hidden(ToolbarTool.Grouping) ? Visibility.Collapsed : Visibility.Visible;
        ViewSeparator.Visibility = Visibility.Collapsed;
    }

    private Action? _cancelToolbarEditing;

    private void BeginToolbarEditing()
    {
        if (_cancelToolbarEditing is not null) return;
        var draft = new ToolbarCustomizationDraft(App.ExplorerPreferences.ToolbarOrder, App.ExplorerPreferences.HiddenToolbarTools);
        var previews = CreateCustomizationPreviewUnits();
        var body = PanelBody(double.NaN);
        body.Padding = new Thickness(8, 2, 8, 4);
        body.Spacing = 2;

        // Replace the live row in place with the same command XAML units.
        // Independent viewports keep the two real zones clear on narrow windows.
        var strip = new Grid { ColumnSpacing = 8 };
        strip.ColumnDefinitions.Add(new() { Width = new GridLength(4, GridUnitType.Star) });
        strip.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        strip.ColumnDefinitions.Add(new() { Width = new GridLength(6, GridUnitType.Star) });
        var separator = new Border { Width = 1, Margin = new Thickness(0, 8, 0, 8),
            Background = (Brush)Application.Current.Resources["FilesMate.Divider.Brush"] };
        Theming.ThemeResources.Bind(separator, Border.BackgroundProperty, "FilesMate.Divider.Brush");
        Grid.SetColumn(separator, 1); strip.Children.Add(separator);
        var scrollers = new Dictionary<ToolbarZone, ScrollViewer>();
        var groups = new Dictionary<ToolbarZone, (ListView List, ObservableCollection<Grid> Items, TextBlock Empty)>();
        ToolbarTool? selected = null;
        ListView? draggingList = null;
        ToolbarTool? draggingHidden = null;
        ToolbarTool? pendingRemove = null;
        ScrollViewer? dragScroller = null;
        var refreshing = false;
        var saving = false;
        double dragScrollDelta = 0;
        var dragScrollTimer = DispatcherQueue.CreateTimer();
        dragScrollTimer.Interval = TimeSpan.FromMilliseconds(40);
        void StopDragScroll() { dragScrollDelta = 0; dragScrollTimer.Stop(); }
        dragScrollTimer.Tick += (_, _) =>
        {
            if (draggingList is null && draggingHidden is null || saving || dragScrollDelta == 0) { StopDragScroll(); return; }
            if (dragScroller is null) { StopDragScroll(); return; }
            var offset = Math.Clamp(dragScroller.HorizontalOffset + dragScrollDelta, 0, dragScroller.ScrollableWidth);
            if (Math.Abs(offset - dragScroller.HorizontalOffset) < .1) { StopDragScroll(); return; }
            dragScroller.ChangeView(offset, null, null, disableAnimation: true);
        };
        var moveLeft = EditorAction("Toolbar_MoveLeft", "ToolbarMoveLeft", "\uE72B");
        var moveRight = EditorAction("Toolbar_MoveRight", "ToolbarMoveRight", "\uE72A");
        var remove = EditorAction("Toolbar_Remove", "ToolbarRemove", "\uE738");
        SetEditorToolTip(strip, StringTable.Get("Toolbar_OrderHint"));
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var palette = new ToolbarPalettePanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var hiddenRow = new Grid { ColumnSpacing = 6 };
        hiddenRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        hiddenRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var hiddenHeading = new TextBlock { Text = StringTable.Get("Toolbar_Available"), FontFamily = Theming.AppTypography.Family, FontSize = 13, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0) };
        AutomationProperties.SetAutomationId(hiddenHeading, "ToolbarHiddenHeading");
        SetEditorToolTip(hiddenHeading, StringTable.Get("Toolbar_AddHint"));
        hiddenRow.Children.Add(hiddenHeading);
        Theming.ThemeResources.Bind(hiddenHeading, TextBlock.FontFamilyProperty, "FilesMate.UiFontFamily");
        Theming.ThemeResources.Bind(hiddenHeading, TextBlock.ForegroundProperty, "FilesMate.Text.SecondaryBrush");
        var hiddenTray = new Border { Child = hiddenRow, Padding = new Thickness(8, 4, 8, 4), MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1), CornerRadius = (CornerRadius)Application.Current.Resources["FilesMate.Corner.Small"], AllowDrop = true };
        AutomationProperties.SetAutomationId(hiddenTray, "ToolbarHiddenTray");
        AutomationProperties.SetName(hiddenTray, StringTable.Get("Toolbar_Available"));
        SetEditorToolTip(hiddenTray, StringTable.Get("Toolbar_AddHint"));
        void TrayFeedback(bool active)
        {
            Theming.ThemeResources.Bind(hiddenTray, Border.BackgroundProperty, active ? "FilesMate.Choice.InteractionHoverBrush" : "FilesMate.Glass.CardBrush");
            Theming.ThemeResources.Bind(hiddenTray, Border.BorderBrushProperty, active ? "FilesMate.Selection.AccentBrush" : "FilesMate.Card.BorderBrush");
        }
        TrayFeedback(false);
        var error = new TextBlock { Text = StringTable.Get("Toolbar_SaveFailed"), FontFamily = Theming.AppTypography.Family, FontSize = 13, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(error, "ToolbarSaveError");
        AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

        void UpdateSelection()
        {
            var definition = selected is { } id ? ToolbarToolCatalog.Get(id) : null;
            var visible = definition is null ? [] : draft.Visible(definition.Zone, SearchMode).Select(d => d.Id).ToList();
            var index = selected is { } current ? visible.IndexOf(current) : -1;
            moveLeft.IsEnabled = index > 0;
            moveRight.IsEnabled = index >= 0 && index < visible.Count - 1;
            remove.IsEnabled = index >= 0;
        }

        void Select(ToolbarTool? id, bool focus)
        {
            selected = id;
            refreshing = true;
            foreach (var (list, items, _) in groups.Values)
            {
                var item = id is { } current && items.Contains(previews[current]) ? previews[current] : null;
                list.SelectedItem = item;
                if (item is not null && focus)
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        void FocusContainer()
                        {
                            if (selected != id || !ReferenceEquals(list.SelectedItem, item) || !list.IsLoaded) return;
                            list.ScrollIntoView(item);
                            list.UpdateLayout();
                            if (list.ContainerFromItem(item) is ListViewItem container)
                            {
                                container.Focus(FocusState.Programmatic);
                                container.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                            }
                        }
                        FocusContainer();
                        if (list.ContainerFromItem(item) is null)
                            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FocusContainer);
                    });
            }
            refreshing = false;
            UpdateSelection();
        }

        void RefreshPalette()
        {
            palette.Children.Clear();
            var available = draft.Available(SearchMode);
            for (var i = 0; i < available.Count; i++)
            {
                var definition = available[i];
                var content = new Grid { ColumnSpacing = 8 };
                content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var gray = (Brush)Theming.ThemeResources.Resolve(this, "FilesMate.Text.SecondaryBrush")!;
                content.Children.Add(new FontIcon { Glyph = definition.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16, Foreground = gray });
                var label = new TextBlock { Text = StringTable.Get(definition.LabelKey), Foreground = gray, FontFamily = Theming.AppTypography.Family, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                label.MaxWidth = double.PositiveInfinity;
                Grid.SetColumn(label, 1); content.Children.Add(label);
                var plus = new FontIcon { Glyph = "\uE710", FontSize = 10, Foreground = gray };
                plus.Visibility = Visibility.Collapsed;
                Grid.SetColumn(plus, 2); content.Children.Add(plus);
                Theming.ThemeResources.Bind((FontIcon)content.Children[0], FontIcon.ForegroundProperty, "FilesMate.Text.SecondaryBrush");
                Theming.ThemeResources.Bind(label, TextBlock.ForegroundProperty, "FilesMate.Text.SecondaryBrush");
                Theming.ThemeResources.Bind(label, TextBlock.FontFamilyProperty, "FilesMate.UiFontFamily");
                Theming.ThemeResources.Bind(plus, FontIcon.ForegroundProperty, "FilesMate.Text.SecondaryBrush");
                var add = new Button { Content = content, Style = (Style)Application.Current.Resources["CommandBarButtonStyle"],
                    HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Center, MinWidth = 36, Height = (double)Application.Current.Resources["FilesMate.Control.Height.Row"], Padding = new Thickness(10, 0, 10, 0), CanDrag = true };
                var name = StringTable.Get("Toolbar_Add") + ": " + StringTable.Get(definition.LabelKey);
                AutomationProperties.SetName(add, name); AutomationProperties.SetAutomationId(add, "ToolbarAdd" + definition.Id);
                SetEditorToolTip(add, name + " · " + StringTable.Get(definition.Zone == ToolbarZone.FileCommands ? "Toolbar_FileCommands" : "Toolbar_ViewTools"));
                var pressed = false;
                var dragged = false;
                var origin = new Windows.Foundation.Point();
                add.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
                {
                    var point = args.GetCurrentPoint(add);
                    pressed = point.Properties.IsLeftButtonPressed;
                    dragged = false; origin = point.Position;
                }), true);
                add.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(async (_, args) =>
                {
                    var point = args.GetCurrentPoint(add);
                    if (!pressed || saving || !point.Properties.IsLeftButtonPressed || Math.Abs(point.Position.X - origin.X) + Math.Abs(point.Position.Y - origin.Y) < 6) return;
                    pressed = false; dragged = true; add.ReleasePointerCaptures();
                    try { await add.StartDragAsync(point); }
                    catch (Exception failure) { App.LogFailure("ToolbarCustomizationDrag", failure); }
                    finally { draggingHidden = null; StopDragScroll(); }
                }), true);
                add.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => pressed = false), true);
                add.KeyDown += (_, _) => dragged = false;
                add.Click += (_, _) => { if (dragged) return; draft.SetVisible(definition.Id, true); Refresh(definition.Id, true); };
                add.DragStarting += (_, args) =>
                {
                    pressed = false; dragged = true;
                    draggingHidden = definition.Id;
                    args.AllowedOperations = DataPackageOperation.Move;
                    args.Data.SetData("FilesMate.ToolbarTool", definition.Id.ToString());
                    args.Data.RequestedOperation = DataPackageOperation.Move;
                };
                add.DropCompleted += (_, _) => { draggingHidden = null; StopDragScroll(); };
                add.Loaded += (_, _) => FitHiddenTray();
                palette.Children.Add(add);
            }
            hiddenRow.Visibility = Visibility.Visible;
            palette.InvalidateMeasure();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FitHiddenTray);
        }

        void Refresh(ToolbarTool? selection, bool focus)
        {
            refreshing = true;
            foreach (var (zone, (list, items, empty)) in groups)
            {
                var desired = draft.Visible(zone, SearchMode).Select(d => previews[d.Id]).ToArray();
                foreach (var obsolete in items.Where(item => !desired.Contains(item)).ToArray()) items.Remove(obsolete);
                for (var i = 0; i < desired.Length; i++)
                {
                    var old = items.IndexOf(desired[i]);
                    if (old < 0) items.Insert(i, desired[i]);
                    else if (old != i) items.Move(old, i);
                }
                empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                list.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            refreshing = false;
            RefreshPalette();
            Select(selection, focus);
        }

        void MoveSelected(int direction)
        {
            if (selected is { } id && draft.Move(id, direction, SearchMode)) Refresh(id, true);
        }
        void RemoveSelected()
        {
            if (selected is not { } id) return;
            var zone = ToolbarToolCatalog.Get(id).Zone;
            var before = draft.Visible(zone, SearchMode).Select(d => d.Id).ToList();
            var position = before.IndexOf(id);
            draft.SetVisible(id, false);
            var after = draft.Visible(zone, SearchMode);
            Refresh(after.Count == 0 ? null : after[Math.Min(position, after.Count - 1)].Id, true);
        }
        moveLeft.Click += (_, _) => MoveSelected(-1);
        moveRight.Click += (_, _) => MoveSelected(1);
        remove.Click += (_, _) => RemoveSelected();

        foreach (var zone in Enum.GetValues<ToolbarZone>())
        {
            var group = new StackPanel { Spacing = 2 };

            var items = new ObservableCollection<Grid>();
            var list = new ListView { ItemsSource = items, ItemsPanel = (ItemsPanelTemplate)Resources["ToolbarHorizontalItemsPanel"],
                SelectionMode = ListViewSelectionMode.Single, CanDragItems = true, CanReorderItems = true, AllowDrop = true,
                IsSwipeEnabled = true, HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 40, Padding = new Thickness(0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
            ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Disabled);
            list.ItemContainerStyle = (Style)Resources["ToolbarEditingItemStyle"];
            AutomationProperties.SetAutomationId(list, "ToolbarOrder" + zone);
            AutomationProperties.SetName(list, StringTable.Get(zone == ToolbarZone.FileCommands ? "Toolbar_FileCommands" : "Toolbar_ViewTools"));
            list.SelectionChanged += (_, _) =>
            {
                if (!refreshing && list.SelectedItem is Grid { Tag: ToolbarTool id }) Select(id, false);
            };
            list.DragItemsStarting += (_, args) =>
            {
                draggingList = list;
                args.Data.RequestedOperation = DataPackageOperation.Move;
                if (args.Items.FirstOrDefault() is Grid { Tag: ToolbarTool id }) Select(id, false);
            };
            void RejectOtherZone(object sender, DragEventArgs args)
            {
                var sameZone = draggingHidden is { } hidden && ToolbarToolCatalog.Get(hidden).Zone == zone;
                if (ReferenceEquals(draggingList, list) || sameZone)
                {
                    args.AcceptedOperation = DataPackageOperation.Move;
                    if (sameZone) args.Handled = true;
                    return;
                }
                args.AcceptedOperation = DataPackageOperation.None;
                args.Handled = true;
            }
            list.AddHandler(UIElement.DragEnterEvent, new DragEventHandler(RejectOtherZone), true);
            list.AddHandler(UIElement.DragOverEvent, new DragEventHandler(RejectOtherZone), true);
            void DropInZone(object sender, DragEventArgs args)
            {
                if (draggingHidden is not { } id || ToolbarToolCatalog.Get(id).Zone != zone)
                {
                    if (!ReferenceEquals(draggingList, list)) { args.AcceptedOperation = DataPackageOperation.None; args.Handled = true; }
                    return;
                }
                var x = items.Count == 0 ? 0 : args.GetPosition(list).X;
                var order = draft.Visible(zone, SearchMode).Select(d => d.Id).ToList();
                var insertion = order.Count;
                for (var i = 0; i < items.Count; i++)
                {
                    if (list.ContainerFromItem(items[i]) is not ListViewItem container) continue;
                    var origin = container.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point());
                    if (x < origin.X + container.ActualWidth / 2) { insertion = i; break; }
                }
                draft.SetVisible(id, true);
                order.Insert(insertion, id);
                draft.SetVisibleOrder(zone, SearchMode, order);
                args.AcceptedOperation = DataPackageOperation.Move; args.Handled = true;
                draggingHidden = null; StopDragScroll(); Refresh(id, true);
            }
            list.AddHandler(UIElement.DropEvent, (DragEventHandler)DropInZone, true);
            list.DragItemsCompleted += (_, _) =>
            {
                // Native reorder mutates ItemsSource. Validate the exact zone's
                // permutation before adopting it; never save from a drag event.
                draft.SetVisibleOrder(zone, SearchMode, items.Select(item => (ToolbarTool)item.Tag));
                if (pendingRemove is { } removed) { draft.SetVisible(removed, false); selected = null; pendingRemove = null; }
                draggingList = null; StopDragScroll(); TrayFeedback(false);
                Refresh(selected, true);
            };
            list.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, args) =>
            {
                if (list.SelectedItem is not Grid { Tag: ToolbarTool id }) return;
                var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                if (control && args.Key is (VirtualKey.Left or VirtualKey.Right))
                {
                    Select(id, false); MoveSelected(args.Key == VirtualKey.Left ? -1 : 1); args.Handled = true;
                }
                else if (args.Key == VirtualKey.Delete)
                {
                    Select(id, false); RemoveSelected(); args.Handled = true;
                }
            }), true);
            var empty = new TextBlock { Text = StringTable.Get("Toolbar_EmptyGroup"), FontSize = 12, Margin = new Thickness(4, 8, 4, 8) };
            AutomationProperties.SetAutomationId(empty, "ToolbarEmpty" + zone);
            empty.AllowDrop = true;
            empty.AddHandler(UIElement.DragEnterEvent, (DragEventHandler)RejectOtherZone, true);
            empty.AddHandler(UIElement.DragOverEvent, (DragEventHandler)RejectOtherZone, true);
            empty.AddHandler(UIElement.DropEvent, (DragEventHandler)DropInZone, true);
            group.Children.Add(list); group.Children.Add(empty);
            var scroll = new ScrollViewer { Content = group, HorizontalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, IsTabStop = false, MinHeight = 40 };
            AutomationProperties.SetAutomationId(scroll, "ToolbarEditScroll" + zone);
            scroll.AddHandler(UIElement.DragOverEvent, new DragEventHandler((_, args) =>
            {
                if (draggingList is null && draggingHidden is null || scroll.ScrollableWidth <= 0) { StopDragScroll(); return; }
                var ownedZone = ReferenceEquals(draggingList, list) || draggingHidden is { } hidden && ToolbarToolCatalog.Get(hidden).Zone == zone;
                if (!ownedZone) { StopDragScroll(); return; }
                dragScroller = scroll;
                var x = args.GetPosition(scroll).X;
                dragScrollDelta = x < 32 ? -12 : x > scroll.ActualWidth - 32 ? 12 : 0;
                if (dragScrollDelta == 0) dragScrollTimer.Stop(); else if (!dragScrollTimer.IsRunning) dragScrollTimer.Start();
            }), true);
            scroll.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler((_, _) => StopDragScroll()), true);
            Grid.SetColumn(scroll, zone == ToolbarZone.FileCommands ? 0 : 2);
            strip.Children.Add(scroll); scrollers.Add(zone, scroll);
            groups.Add(zone, (list, items, empty));
        }

        var selectionActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        selectionActions.Children.Add(moveLeft); selectionActions.Children.Add(moveRight); selectionActions.Children.Add(remove);
        // The whole card, including empty space and child buttons, is one
        // hide target. Defer removal until the native drag has completed.
        void TrayDrag(object sender, DragEventArgs args)
        {
            var accept = !saving && draggingList is not null && selected is not null;
            args.AcceptedOperation = accept ? DataPackageOperation.Move : DataPackageOperation.None;
            args.Handled = true; StopDragScroll(); TrayFeedback(accept);
        }
        hiddenTray.AddHandler(UIElement.DragEnterEvent, (DragEventHandler)TrayDrag, true);
        hiddenTray.AddHandler(UIElement.DragOverEvent, (DragEventHandler)TrayDrag, true);
        hiddenTray.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler((_, args) =>
        {
            var point = args.GetPosition(hiddenTray);
            if (point.X < 0 || point.Y < 0 || point.X > hiddenTray.ActualWidth || point.Y > hiddenTray.ActualHeight) TrayFeedback(false);
        }), true);
        hiddenTray.AddHandler(UIElement.DropEvent, new DragEventHandler((_, args) =>
        {
            if (saving || draggingList is null || selected is not { } id) { args.AcceptedOperation = DataPackageOperation.None; args.Handled = true; TrayFeedback(false); return; }
            pendingRemove = id; args.AcceptedOperation = DataPackageOperation.Move; args.Handled = true; TrayFeedback(false);
        }), true);
        Grid.SetColumn(palette, 1); hiddenRow.Children.Add(palette);
        var cancel = new Button { Content = StringTable.Get("Cancel"), FontSize = 13,
            Style = (Style)Application.Current.Resources["CommandBarButtonStyle"], MinWidth = 56 };
        var save = new Button { Content = StringTable.Get("Done"), FontSize = 13,
            Style = (Style)Application.Current.Resources["CommandBarButtonStyle"], MinWidth = 56 };
        Theming.ThemeResources.Bind(save, Control.ForegroundProperty, "FilesMate.Selection.AccentBrush");
        AutomationProperties.SetAutomationId(cancel, "ToolbarCancel"); AutomationProperties.SetAutomationId(save, "ToolbarSave");
        footer.Children.Add(selectionActions);
        footer.Children.Add(new Border { Width = 1, Height = 16, Margin = new Thickness(4, 8, 4, 8),
            Background = (Brush)Theming.ThemeResources.Resolve(this, "FilesMate.Divider.Brush")! });
        footer.Children.Add(cancel); footer.Children.Add(save);
        var lowerRow = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        lowerRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        lowerRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        lowerRow.RowDefinitions.Add(new() { Height = GridLength.Auto });
        lowerRow.RowDefinitions.Add(new() { Height = GridLength.Auto });
        lowerRow.Children.Add(hiddenTray); Grid.SetColumn(footer, 1); lowerRow.Children.Add(footer);
        body.Children.Add(lowerRow); body.Children.Add(error);
        void FitHiddenTray()
        {
            if (body.ActualWidth <= 0) return;
            footer.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var stacked = body.ActualWidth < 600;
            Grid.SetRow(footer, stacked ? 1 : 0); Grid.SetColumn(footer, stacked ? 0 : 1); Grid.SetColumnSpan(footer, stacked ? 2 : 1);
            Grid.SetColumnSpan(hiddenTray, stacked ? 2 : 1);
            // One shared strip uses its actual layout slot. Its width must not
            // depend on the previous wrapped row or on an estimated item sum.
            // The footer's real width is reserved by lowerRow's Auto column.
            palette.InvalidateMeasure();
        }
        body.SizeChanged += (_, args) => { if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > .1) FitHiddenTray(); };
        var editor = new ContentControl { Content = body, FontFamily = Theming.AppTypography.Family, FontSize = 13, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var inputRoot = XamlRoot?.Content as UIElement ?? this;
        void EndEditing()
        {
            if (saving) return;
            _cancelToolbarEditing = null; draggingList = null; draggingHidden = null; StopDragScroll();
            inputRoot.RemoveHandler(UIElement.PreviewKeyDownEvent, (KeyEventHandler)EditorKey);
            SizeChanged -= EditorBounds; ActualThemeChanged -= EditorTheme; Unloaded -= EditorUnloaded; App.AppearanceChanged -= EditorAppearance;
            ToolbarEditingRow.Content = null; ToolbarEditingDetails.Content = null;
            ToolbarEditingRow.Visibility = ToolbarEditingDetails.Visibility = Visibility.Collapsed;
            LiveToolbarRow.Visibility = Visibility.Visible;
            ApplyContext(_context);
        }
        void EditorKey(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Escape) { EndEditing(); args.Handled = true; }
        }
        void EditorBounds(object sender, SizeChangedEventArgs args) { if (draggingList is null && draggingHidden is null) RefreshPalette(); }
        void EditorTheme(FrameworkElement sender, object args) { TrayFeedback(false); RefreshPalette(); }
        void EditorAppearance(object? sender, AppearanceSettings settings)
        {
            Theming.AppTypography.Apply(strip); Theming.AppTypography.Apply(editor);
            if (draggingList is null && draggingHidden is null) RefreshPalette();
            FitHiddenTray();
        }
        void EditorUnloaded(object sender, RoutedEventArgs args) => EndEditing();
        cancel.Click += (_, _) => EndEditing();
        save.Click += async (_, _) =>
        {
            if (saving) return;
            if (!draft.HasChanges) { EndEditing(); return; }
            if (!draft.TryMergeForSave(App.ExplorerPreferences.ToolbarOrder, App.ExplorerPreferences.HiddenToolbarTools, out var snapshot))
            {
                error.Text = StringTable.Get("Toolbar_ChangedElsewhere"); error.Visibility = Visibility.Visible; return;
            }
            error.Text = StringTable.Get("Toolbar_SaveFailed"); error.Visibility = Visibility.Collapsed;
            saving = true; editor.IsEnabled = false; ToolbarEditingRow.IsEnabled = false;
            var saved = await SaveToolbarToolsAsync(snapshot.Hidden, snapshot.Order);
            saving = false; editor.IsEnabled = true; ToolbarEditingRow.IsEnabled = true;
            if (saved) EndEditing(); else error.Visibility = Visibility.Visible;
        };
        _cancelToolbarEditing = EndEditing;
        inputRoot.AddHandler(UIElement.PreviewKeyDownEvent, (KeyEventHandler)EditorKey, true);
        SizeChanged += EditorBounds; ActualThemeChanged += EditorTheme; Unloaded += EditorUnloaded; App.AppearanceChanged += EditorAppearance;
        ToolbarEditingRow.Content = strip; ToolbarEditingDetails.Content = editor;
        LiveToolbarRow.Visibility = Visibility.Collapsed;
        ToolbarEditingRow.Visibility = ToolbarEditingDetails.Visibility = Visibility.Visible;
        Theming.AppTypography.Track(strip); Theming.AppTypography.Track(editor);
        Refresh(null, false);
        Theming.AppTypography.Apply(strip); Theming.AppTypography.Apply(editor);
    }

    private static void SetEditorToolTip(FrameworkElement owner, object text)
    {
        var tip = new ToolTip { Content = text, FontFamily = Theming.AppTypography.Family, FontSize = 13 };
        Theming.ThemeResources.Bind(tip, Control.FontFamilyProperty, "FilesMate.UiFontFamily");
        ToolTipService.SetToolTip(owner, tip);
    }

    // All hidden commands share one continuous strip, regardless of
    // their restore zone. Individual buttons keep their natural width; wrap
    // only when the next complete command exceeds the actual remaining width.
    private sealed class ToolbarPalettePanel : Panel
    {
        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
        {
            var width = double.IsFinite(available.Width) ? Math.Max(1, available.Width) : 600;
            double x = 0, y = 0, rowHeight = 0, used = 0;
            foreach (var child in Children)
            {
                if (child is Button { Content: Grid content } button)
                {
                    var label = content.Children.OfType<TextBlock>().First();
                    var icon = content.Children.OfType<FontIcon>().First();
                    icon.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                    label.MaxWidth = Math.Max(1, width - icon.DesiredSize.Width - content.ColumnSpacing
                        - button.Padding.Left - button.Padding.Right - button.BorderThickness.Left - button.BorderThickness.Right);
                }
                child.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
                var w = Math.Min(width, child.DesiredSize.Width); var h = child.DesiredSize.Height;
                if (x > 0 && x + w > width) { y += rowHeight + 4; x = 0; rowHeight = 0; }
                used = Math.Max(used, x + w); x += w + 4; rowHeight = Math.Max(rowHeight, h);
            }
            // Return the offered row width. Returning only the widest
            // wrapped row can shrink the Grid slot and wrap items a second time
            // during Arrange, even though the shared strip has room for them.
            return new Windows.Foundation.Size(double.IsFinite(available.Width) ? width : used, y + rowHeight);
        }
        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
        {
            double x = 0, y = 0, rowHeight = 0;
            foreach (var child in Children)
            {
                var w = Math.Min(final.Width, child.DesiredSize.Width); var h = child.DesiredSize.Height;
                if (x > 0 && x + w > final.Width) { y += rowHeight + 4; x = 0; rowHeight = 0; }
                child.Arrange(new Windows.Foundation.Rect(x, y, w, h)); x += w + 4; rowHeight = Math.Max(rowHeight, h);
            }
            return final;
        }
    }

    private static Button EditorAction(string key, string automationId, string glyph)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Style = (Style)Application.Current.Resources["CommandBarButtonStyle"], MinWidth = 32, Padding = new Thickness(8, 0, 8, 0) };
        AutomationProperties.SetName(button, StringTable.Get(key)); AutomationProperties.SetAutomationId(button, automationId);
        SetEditorToolTip(button, StringTable.Get(key));
        return button;
    }

    private async Task<bool> SaveToolbarToolsAsync(IReadOnlyList<ToolbarTool>? hidden, IReadOnlyList<ToolbarTool>? order)
    {
        try { await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { HiddenToolbarTools = hidden, ToolbarOrder = order }); }
        catch (Exception error) { App.LogFailure("ToolbarCustomization", error); return false; }
        ApplyContext(_context);
        return true;
    }

    private void Toolbar_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_isCustomizationPreview) return;
        e.Handled = true;
        if (_cancelToolbarEditing is not null) return;
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["FilesMate.MenuFlyoutPresenterStyle"] };
        var edit = new MenuFlyoutItem { Text = StringTable.Get("Toolbar_Customize"), Icon = new FontIcon { Glyph = "\uE70F" } };
        AutomationProperties.SetAutomationId(edit, "ToolbarEnterEdit");
        edit.Click += (_, _) => BeginToolbarEditing();
        menu.Items.Add(edit); Theming.FlyoutTheme.FollowHost(menu);
        FileContextPopupPlacement.ShowAt(menu, this, e.GetPosition(this));
    }
}
