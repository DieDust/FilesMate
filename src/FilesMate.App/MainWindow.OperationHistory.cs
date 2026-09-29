using FilesMate.App.Views;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    internal Task ApplyHistoryAsync(bool redo, Action<string> reportError) => TabHost.Content switch
    {
        NavigatorPage navigator => navigator.ApplyHistoryAsync(redo, reportError),
        SearchResultsPage search => search.ApplyHistoryAsync(redo, reportError),
        _ => Task.CompletedTask
    };
    internal void LocateHistoryPath(string path) => AddNavigatorTab(Path.GetDirectoryName(path), Path.Exists(path) ? path : null);
}
