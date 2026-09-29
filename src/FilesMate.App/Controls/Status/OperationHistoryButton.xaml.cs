using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Status;

public sealed partial class OperationHistoryButton : UserControl
{
    private bool _busy;
    public OperationHistoryButton()
    {
        InitializeComponent();
        FlyoutTheme.FollowHost(HistoryFlyout);
        Unloaded += (_, _) => { HistoryFlyout.Hide(); App.FileUndo.Changed -= HistoryChanged; };
    }
    private void History_Opening(object sender, object e)
    {
        App.FileUndo.Changed -= HistoryChanged;
        App.FileUndo.Changed += HistoryChanged;
        ErrorText.Visibility = Visibility.Collapsed;
        Refresh();
    }
    private void History_Closed(object sender, object e) => App.FileUndo.Changed -= HistoryChanged;
    private void HistoryChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Refresh);
    private void Refresh()
    {
        var selected = HistoryList.SelectedItem as OperationHistoryEntry;
        var entries = OperationHistoryEntry.Snapshot(App.FileUndo);
        HistoryList.ItemsSource = entries;
        HistoryList.SelectedItem = entries.FirstOrDefault(entry => ReferenceEquals(entry.Record, selected?.Record)) ?? entries.FirstOrDefault();
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.Visibility = RedoButton.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HistoryList.Visibility = FileActions.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UndoButton.IsEnabled = !_busy && App.FileUndo.CanUndo;
        RedoButton.IsEnabled = !_busy && App.FileUndo.CanRedo;
        ToolTipService.SetToolTip(UndoButton, App.FileUndo.Latest is { } undo ? new OperationHistoryEntry(undo, false).Title : StringTable.Get("History_Empty"));
        ToolTipService.SetToolTip(RedoButton, App.FileUndo.LatestRedo is { } redo ? new OperationHistoryEntry(redo, true).Title : null);
    }
    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PathsBox.ItemsSource = (HistoryList.SelectedItem as OperationHistoryEntry)?.Paths;
        PathsBox.SelectedIndex = PathsBox.Items.Count > 0 ? 0 : -1;
        PathsBox.Visibility = PathsBox.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        RefreshFileActions();
    }
    private void Path_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshFileActions();
    private void RefreshFileActions()
    {
        if (OpenButton is null || LocateButton is null) return;
        var path = PathsBox.SelectedItem as string;
        OpenButton.IsEnabled = !_busy && path is not null && Path.Exists(path);
        LocateButton.IsEnabled = !_busy && path is not null && Directory.Exists(Path.GetDirectoryName(path));
    }
    private async void Undo_Click(object sender, RoutedEventArgs e) => await ApplyAsync(false);
    private async void Redo_Click(object sender, RoutedEventArgs e) => await ApplyAsync(true);
    private async Task ApplyAsync(bool redo)
    {
        if (_busy) return;
        if (FileOperationLifetime.IsBusy) { ShowError(StringTable.Get("Files_Busy")); return; }
        _busy = true;
        Refresh();
        RefreshFileActions();
        try { if (App.WindowForElement(this) is { } window) await window.ApplyHistoryAsync(redo, ShowError); }
        finally { _busy = false; Refresh(); RefreshFileActions(); }
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (PathsBox.SelectedItem is not string path) return;
        try
        {
            if (!Path.Exists(path)) { ShowError(StringTable.Get("History_Missing")); RefreshFileActions(); return; }
            if (Directory.Exists(path)) App.WindowForElement(this)?.OpenFolderInNewTab(path);
            else await FilesMate.Platform.Windows.Operations.ShellOperationWorker.RunAsync(() =>
                FilesMate.Platform.Windows.Processes.DetachedProcess.Open(path));
            HistoryFlyout.Hide();
        }
        catch (Exception error) { ShowError(error.Message); }
    }
    private void Locate_Click(object sender, RoutedEventArgs e)
    {
        if (PathsBox.SelectedItem is not string path) return;
        if (!Directory.Exists(Path.GetDirectoryName(path))) { ShowError(StringTable.Get("History_Missing")); RefreshFileActions(); return; }
        App.WindowForElement(this)?.LocateHistoryPath(path);
        HistoryFlyout.Hide();
    }
    private void ShowError(string message) { ErrorText.Text = message; ErrorText.Visibility = Visibility.Visible; }
}
