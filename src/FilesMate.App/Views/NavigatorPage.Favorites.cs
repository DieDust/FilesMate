using FilesMate.App.Controls.Favorites;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    internal FrameworkElement FavoritesAnchor => FavoritesSlot;
    private FavoritesBar Favorites => App.WindowForElement(this)!.Favorites;
    private void FavoritesSlot_LayoutUpdated(object? sender, object e)
    {
        if (IsLoaded) App.WindowForElement(this)?.UpdateFavoritesPlacement(this);
    }
}
