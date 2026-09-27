using FilesMate.App.Controls.Favorites;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    internal FavoritesBar Favorites => WindowFavorites;

    private void InitializeFavoritesBar()
    {
        WindowFavorites.CurrentFolder = () => (TabHost.Content as NavigatorPage)?.ViewModel.Navigation.CurrentPath;
        WindowFavorites.OpenRequested += (_, entry) => (TabHost.Content as NavigatorPage)?.OpenFavorite(entry);
        App.FeaturesChanged += WindowFavorites_FeaturesChanged;
    }

    private void WindowFavorites_FeaturesChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_windowClosed && TabHost.Content is NavigatorPage page) UpdateFavoritesPlacement(page);
    });

    internal void UpdateFavoritesPlacement(NavigatorPage page)
    {
        if (_windowClosed || !ReferenceEquals(TabHost.Content, page)) return;
        WindowFavorites.RefreshCurrentFolder();
        if (!App.Features.FavoritesBarEnabled)
        {
            WindowFavorites.Visibility = Visibility.Collapsed;
            return;
        }
        var slot = page.FavoritesAnchor;
        // Keep the previous bar visible until the new page has its layout.
        if (!slot.IsLoaded || slot.ActualWidth <= 0 || slot.ActualHeight <= 0) return;
        var bounds = slot.TransformToVisual((UIElement)Content)
            .TransformBounds(new Rect(0, 0, slot.ActualWidth, slot.ActualHeight));
        var margin = new Thickness(bounds.X, bounds.Y, 0, 0);
        if (WindowFavorites.Margin != margin) WindowFavorites.Margin = margin;
        if (Math.Abs(WindowFavorites.Width - bounds.Width) > 0.1 || double.IsNaN(WindowFavorites.Width))
            WindowFavorites.Width = bounds.Width;
        WindowFavorites.Visibility = Visibility.Visible;
    }
}
