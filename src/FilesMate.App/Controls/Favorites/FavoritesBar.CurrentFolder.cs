using FilesMate.App.Services;
using FilesMate.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App.Controls.Favorites;

public sealed partial class FavoritesBar
{
    private readonly Button _bookmark = ButtonFor(Loc.Get("Favorites_AddFolder"), "\uE734");
    private IReadOnlyList<FavoriteEntry>? _observedFavorites;
    private string? _observedFolder;
    private bool _openingFolderEditor;
    private Flyout? _currentFolderFlyout;

    internal void RefreshCurrentFolder()
    {
        var folder = CurrentFolder?.Invoke();
        var entries = App.Favorites.Entries;
        // Layout updates are frequent; only inspect the collection after a
        // navigation or a new immutable favorites snapshot.
        if (folder == _observedFolder && ReferenceEquals(entries, _observedFavorites)) return;
        if (folder != _observedFolder) _currentFolderFlyout?.Hide();
        _observedFolder = folder;
        _observedFavorites = entries;
        var canSave = FavoritesStore.FolderKey(folder) is not null;
        var saved = canSave && App.Favorites.FindFolders(folder).Count > 0;
        ((FontIcon)_bookmark.Content).Glyph = saved ? "\uE735" : "\uE734";
        var name = Loc.Get(!canSave ? "OpenFolderFirst" : saved ? "Favorites_EditFolder" : "Favorites_AddFolder");
        AutomationProperties.SetAutomationId(_bookmark, "CurrentFolderFavorite");
        AutomationProperties.SetName(_bookmark, name);
        ToolTipService.SetToolTip(_bookmark, name);
        _bookmark.IsEnabled = canSave && !_openingFolderEditor;
    }

    private async Task EditCurrentFolderAsync()
    {
        if (_openingFolderEditor) return;
        var path = FavoritesStore.FolderKey(CurrentFolder?.Invoke());
        if (path is null) return;
        _openingFolderEditor = true;
        _bookmark.IsEnabled = false;
        try
        {
            var existing = App.Favorites.FindFolders(path).FirstOrDefault();
            var saved = existing ?? await App.Favorites.SaveFolderAsync(path);
            if (!IsLoaded || XamlRoot is null
                || !string.Equals(path, FavoritesStore.FolderKey(CurrentFolder?.Invoke()), StringComparison.OrdinalIgnoreCase)) return;
            _currentFolderFlyout?.Hide();
            ShowFolderEditor(saved, existing is null);
        }
        catch (Exception error) { ShowError(error.Message); }
        finally
        {
            _openingFolderEditor = false;
            _observedFavorites = null;
            RefreshCurrentFolder();
        }
    }

    private void ShowFolderEditor(FavoriteEntry saved, bool added)
    {
        var name = new TextBox { Text = saved.Name, MaxLength = 120 };
        AutomationProperties.SetAutomationId(name, "FavoriteName");
        AutomationProperties.SetName(name, Loc.Get("Favorites_Name"));
        var destination = new FavoriteGroupPicker(saved.GroupId);
        AutomationProperties.SetAutomationId(destination, "FavoriteGroup");
        AutomationProperties.SetName(destination, Loc.Get("Favorites_AddTo"));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var panel = new StackPanel { Spacing = 16, Width = Math.Max(200, Math.Min(320, XamlRoot.Size.Width - 64)) };
        AutomationProperties.SetAutomationId(panel, "FavoriteEditor");
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        heading.Children.Add(new FontIcon { Glyph = "\uE735", FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        heading.Children.Add(new TextBlock { Text = Loc.Get(added ? "Favorites_Added" : "Favorites_Edit"), FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(heading);
        void Field(string label, UIElement content)
        {
            var field = new StackPanel { Spacing = 6 };
            field.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.75 });
            field.Children.Add(content); panel.Children.Add(field);
        }
        Field(Loc.Get("Favorites_Name"), name);
        Field(Loc.Get("Favorites_AddTo"), destination);
        if (App.Favorites.FindFolders(saved.Path).Count > 1)
            panel.Children.Add(new TextBlock { Text = Loc.Get("Favorites_RemoveFolderCopies"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
        panel.Children.Add(error);
        var actions = new Grid { ColumnSpacing = 8 };
        actions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var remove = new Button { Content = Loc.Get("Favorites_Remove"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 5, 10, 5), Background = null, BorderThickness = new Thickness(0) };
        var save = new Button { Content = Loc.Get("Save"), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, Padding = new Thickness(16, 5, 16, 5), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        AutomationProperties.SetAutomationId(remove, "FavoriteRemove");
        AutomationProperties.SetAutomationId(save, "FavoriteSave");
        Grid.SetColumn(save, 1);
        actions.Children.Add(remove);
        actions.Children.Add(save);
        panel.Children.Add(actions);
        var scroll = new ScrollViewer { Content = panel, MaxHeight = Math.Max(180, XamlRoot.Size.Height - 100),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var flyout = new Flyout { Content = scroll, AreOpenCloseAnimationsEnabled = false };
        FlyoutTheme.FollowHost(flyout);
        var busy = false;
        async Task ApplyAsync(bool removing)
        {
            if (busy || destination.IsSaving) return;
            busy = true;
            name.IsEnabled = destination.IsEnabled = remove.IsEnabled = save.IsEnabled = false;
            error.Visibility = Visibility.Collapsed;
            try
            {
                if (removing) await App.Favorites.RemoveFolderAsync(saved.Path!);
                else await App.Favorites.UpdateAsync(saved.Id, name.Text, destination.SelectedGroupId);
                RefreshCurrentFolder();
                flyout.Hide();
            }
            catch (Exception failure)
            {
                error.Text = failure.Message;
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                busy = false;
                name.IsEnabled = destination.IsEnabled = remove.IsEnabled = save.IsEnabled = true;
            }
        }
        save.Click += async (_, _) => await ApplyAsync(removing: false);
        remove.Click += async (_, _) => await ApplyAsync(removing: true);
        flyout.Closed += (_, _) => { if (ReferenceEquals(_currentFolderFlyout, flyout)) _currentFolderFlyout = null; };
        _currentFolderFlyout = flyout;
        _bookmark.IsEnabled = true;
        flyout.ShowAt(_bookmark, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
    }
}
