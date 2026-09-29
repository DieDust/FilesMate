using FilesMate.App.Localization;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Menus;

internal static class EntryGroupingMenu
{
    internal static MenuFlyoutSubItem Create(Func<EntrySort> current, Action<EntryGrouping> change)
    {
        var menu = new MenuFlyoutSubItem { Text = StringTable.Get("Sort_Grouping") };
        var items = new List<(EntryGrouping Grouping, ToggleMenuFlyoutItem Item)>();
        void Sync() { foreach (var pair in items) pair.Item.IsChecked = pair.Grouping == current().EffectiveGrouping; }
        foreach (var grouping in new[] { EntryGrouping.Mixed, EntryGrouping.FilesFirst, EntryGrouping.FoldersFirst })
        {
            var item = new ToggleMenuFlyoutItem { Text = StringTable.Get("Sort_" + grouping) };
            item.Click += (_, _) => { change(grouping); Sync(); };
            items.Add((grouping, item));
            menu.Items.Add(item);
        }
        menu.PointerEntered += (_, _) => Sync();
        menu.GotFocus += (_, _) => Sync();
        Sync();
        return menu;
    }
}
