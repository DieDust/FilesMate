using FilesMate.App.Localization;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Menus;

internal static class SortDirectionMenu
{
    internal static MenuFlyoutSubItem Create(Func<EntrySort> current, Action<bool> change)
    {
        var menu = new MenuFlyoutSubItem { Text = StringTable.Get("Sort_Direction") };
        var ascending = new ToggleMenuFlyoutItem { Text = StringTable.Get("Sort_Ascending") };
        var descending = new ToggleMenuFlyoutItem { Text = StringTable.Get("Sort_Descending") };
        void Sync() { ascending.IsChecked = current().Ascending; descending.IsChecked = !ascending.IsChecked; }
        ascending.Click += (_, _) => { change(true); Sync(); };
        descending.Click += (_, _) => { change(false); Sync(); };
        menu.PointerEntered += (_, _) => Sync();
        menu.GotFocus += (_, _) => Sync();
        menu.Items.Add(ascending); menu.Items.Add(descending); Sync();
        return menu;
    }
}
