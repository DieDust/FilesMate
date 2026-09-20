using Loc = FilesMate.App.Localization.StringTable;
using System.IO;
using FilesMate.App.Commands;
using FilesMate.App.Controls.Tags;
using FilesMate.Platform.Windows.Operations;
using FilesMate.Platform.Windows.Shell;
using FilesMate.Search;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    internal async Task RunSearchActionAsync(SearchFileAction request)
    {
        // Capture the search selection; the currently displayed folder and its
        // selection must never become the target of a redirected command.
        var paths = request.Paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Any(path => !Path.Exists(path))) throw new IOException(Loc.Get("Search_StaleFiles"));
        var folder = Path.GetDirectoryName(paths[0]);
        if (request.Command == "ShowMore")
        {
            if (!ShellContextMenu.TryShow(App.WindowForElement(this)!.NativeHandle, paths,
                folder, false, 100, 100, XamlRoot.RasterizationScale, true))
                throw new IOException(Loc.Get("Menu_SystemFailed"));
            return;
        }
        var command = Enum.Parse<AppCommandId>(request.Command);
        if (command == AppCommandId.AddToFavorites)
        {
            await Favorites.AddPathsAsync(paths);
        }
        else if (command == AppCommandId.AddToShelf)
        {
            await App.FileShelf.AddAsync(paths);
            await ShowShelfAsync();
        }
        else if (command == AppCommandId.AddTags)
        {
            var store = App.MetadataStore ?? throw new InvalidOperationException(Loc.Get("Tags_NotReady"));
            var provider = App.FileIdentityProvider ?? throw new InvalidOperationException(Loc.Get("Tags_NotReady"));
            TagPickerFlyout.Show(Commands, store, paths.Select(provider.Resolve).ToArray(), () =>
            {
                lock (_tagCacheGate) foreach (var path in paths) _tagCache.Remove(path);
                _hasTagDefinitions = true;
                FileSurface.RefreshRealizedTags();
                _rightSurface?.RefreshRealizedTags();
            });
        }
        else if (command == AppCommandId.Share)
        {
            var share = App.ShareService ?? throw new InvalidOperationException(Loc.Get("Share_NotReady"));
            await share.ShareAsync(paths);
        }
        else
        {
            var actions = new PaneFileActions(new WindowsLocalFileOperations(), () => paths, () => folder, () => paths[0],
                message => ViewModel.ReportUserError(message), RefreshFilePanes, App.VacateFoldersAsync, this);
            await actions.RunAsync(command);
        }
    }
}
