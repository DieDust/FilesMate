using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Models;
using FilesMate.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _tabMemoryTimer;
    private bool _tabMemorySweepRunning;
    internal bool IsMemoryReclamationBusy => !_windowClosed && (_tabDragging
        || TabHost.Content is Views.NavigatorPage { IsMemoryReclamationBusy: true });
    private void InitializeTabMemory()
    {
        var inputRoot = (UIElement)Content;
        PointerEventHandler pointerInput = (_, _) => Memory.TabResourceReclaimer.NotifyActivity();
        inputRoot.AddHandler(UIElement.PointerPressedEvent, pointerInput, true);
        inputRoot.AddHandler(UIElement.PointerMovedEvent, pointerInput, true);
        inputRoot.AddHandler(UIElement.PointerReleasedEvent, pointerInput, true);
        inputRoot.AddHandler(UIElement.PointerWheelChangedEvent, pointerInput, true);
        inputRoot.AddHandler(UIElement.KeyDownEvent,
            new KeyEventHandler((_, _) => Memory.TabResourceReclaimer.NotifyActivity()), true);
        _tabMemoryTimer = DispatcherQueue.CreateTimer();
        _tabMemoryTimer.Interval = TimeSpan.FromSeconds(30);
        _tabMemoryTimer.Tick += async (_, _) => await HibernateIdleTabsAsync();
        App.ExplorerPreferencesChanged += TabMemoryPreferencesChanged;
        TabMemoryPreferencesChanged(null, App.ExplorerPreferences);
    }

    private void ScheduleTabResourceRelease(IReadOnlyList<WeakReference> retiredResources)
    {
        Memory.TabResourceReclaimer.Request(DispatcherQueue, retiredResources);
    }

    private void TabMemoryPreferencesChanged(object? sender, ExplorerPreferences preferences)
    {
        if (preferences.TabMemory == TabMemoryMode.Off) _tabMemoryTimer?.Stop();
        else if (!_windowClosed) _tabMemoryTimer?.Start();
    }

    private void MarkSelectedTabActivity(TabViewItem selected)
    {
        foreach (var tab in Tabs.TabItems.OfType<TabViewItem>())
        {
            if (tab.Tag is not NavigatorTabContent state) continue;
            var active = ReferenceEquals(tab, selected);
            if (active || state.WasSelected) state.InactiveSince = Environment.TickCount64;
            state.WasSelected = active;
        }
        // Keep the current and most recently visited tab ready for quick switching.
        foreach (var state in Tabs.TabItems.OfType<TabViewItem>()
            .Where(tab => !ReferenceEquals(tab, selected))
            .Select(tab => tab.Tag).OfType<NavigatorTabContent>()
            .Where(state => state.Navigator is not null)
            .OrderByDescending(state => state.InactiveSince).Skip(1))
            state.Navigator!.ReleaseInactiveVisuals();
    }

    private async Task HibernateIdleTabsAsync()
    {
        if (_tabMemorySweepRunning || _windowClosed || FileOperationLifetime.IsBusy) return;
        _tabMemorySweepRunning = true;
        try
        {
            foreach (var tab in Tabs.TabItems.OfType<TabViewItem>().ToArray())
            {
                if (tab.Tag is not NavigatorTabContent state) continue;
                if (TabMemoryPolicy.ShouldHibernate(App.ExplorerPreferences.TabMemory,
                    TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - state.InactiveSince)),
                    ReferenceEquals(Tabs.SelectedItem, tab), state.KeepAlive, state.Navigator?.CanHibernate != true))
                    await HibernateTabAsync(tab);
            }
        }
        catch (Exception error) { App.LogFailure("HibernateTabs", error); }
        finally { _tabMemorySweepRunning = false; }
    }

    private async Task<bool> HibernateTabAsync(TabViewItem tab)
    {
        if (_windowClosed || !Tabs.TabItems.Contains(tab) || ReferenceEquals(Tabs.SelectedItem, tab)
            || tab.Tag is not NavigatorTabContent { Navigator: { } page, KeepAlive: false } old
            || ReferenceEquals(TabHost.Content, page) || !page.CanHibernate) return false;
        var snapshot = page.CaptureClosedTab();
        var sleeping = new NavigatorTabContent(snapshot.Left.Path, null, CreateLoadingContent())
        {
            RestoreState = snapshot,
            IsHibernated = true,
            InactiveSince = old.InactiveSince,
        };
        old.TakeNavigatorForDisposal();
        tab.Tag = sleeping;
        tab.Opacity = .65;
        ToolTipService.SetToolTip(tab, snapshot.Left.Path + Loc.Get("Tab_SuspendedHint"));
        await DisposeNavigatorAsync(page);
        return true;
    }

    private void AddTabMemoryMenu(MenuFlyout menu, TabViewItem tab)
    {
        if (tab.Tag is not NavigatorTabContent state) return;
        var keep = new ToggleMenuFlyoutItem { Text = Loc.Get("Tab_KeepActive"), IsChecked = state.KeepAlive };
        keep.Click += (_, _) =>
        {
            if (tab.Tag is not NavigatorTabContent current) return;
            current.KeepAlive = keep.IsChecked;
            if (current.KeepAlive && current.IsHibernated) Tabs.SelectedItem = tab;
        };
        menu.Items.Add(keep);
        var sleep = new MenuFlyoutItem { Text = Loc.Get("Tab_Suspend"),
            IsEnabled = !ReferenceEquals(Tabs.SelectedItem, tab) && !state.KeepAlive && state.Navigator?.CanHibernate == true };
        sleep.Click += async (_, _) =>
        {
            try { await HibernateTabAsync(tab); }
            catch (Exception error) { App.LogFailure("HibernateTab", error); }
        };
        menu.Items.Add(sleep);
    }
}
