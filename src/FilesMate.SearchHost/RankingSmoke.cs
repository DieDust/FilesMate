#if FILESMATE_UI_TEST
using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FilesMate.App.Models;
using FilesMate.Search;

namespace FilesMate.SearchHost;

internal sealed partial class SearchHost
{
    internal async Task RunRankingSmokeAsync()
    {
        _window ??= new PaletteWindow(this, _searchProvider);
        await _window.RunRankingSmokeAsync();
    }
}

public partial class PaletteWindow
{
    internal async Task RunRankingSmokeAsync()
    {
        var result = new Dictionary<string, object>();
        var path = Path.Combine(_host.Profile, "ranking-smoke.json");
        try
        {
            SearchRankingConfiguration.Save(SearchHitKinds.DefaultOrder, _host.Profile);
            SearchExecutableConfiguration.Save(true, _host.Profile);
            Open(true, "");
            Topmost = false;
            Left = -10000;
            Top = -10000;
            ShowSettings(true, "");
            Ranking_Click(this, new RoutedEventArgs());
            await Until(() => RankingPanel.IsVisible && RankingItems.ItemContainerGenerator.ContainerFromIndex(0) is not null);
            UpdateLayout();
            var container = (DependencyObject)RankingItems.ItemContainerGenerator.ContainerFromIndex(0);
            var row = FindRankRow(container) ?? throw new IOException("Ranking row missing.");
            if (row.CornerRadius.TopLeft != 12 || row.CornerRadius.BottomRight != 12)
                throw new IOException($"Ranking hover is not rounded: {row.CornerRadius}");
            if (!RankingScroll.AllowDrop || !row.AllowDrop)
                throw new IOException("Ranking list does not accept row and gap drops.");
            result["RoundedRowsAndDropTargets"] = true;

            MoveRank(_rankItems[2], -1);
            if (SearchRankingConfiguration.Load(_host.Profile)[1] != SearchHitKind.Folder)
                throw new IOException("Reordering did not save to shared settings.");
            result["ReorderWritesSharedSettings"] = true;

            UpdateLayout();
            var targetContainer = (DependencyObject)RankingItems.ItemContainerGenerator.ContainerFromIndex(2);
            var target = FindRankRow(targetContainer) ?? throw new IOException("Drop row missing.");
            var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var drop = (DragEventArgs)constructor.Invoke([
                new DataObject(typeof(RankOption), _rankItems[0]), DragDropKeyStates.LeftMouseButton,
                DragDropEffects.Move, target, new Point(10, target.ActualHeight - 2),
            ]);
            drop.RoutedEvent = DragDrop.DropEvent;
            Rank_Drop(target, drop);
            if (SearchRankingConfiguration.Load(_host.Profile)[2] != SearchHitKind.Program)
                throw new IOException("Dropping a row did not save the new position.");
            result["DropWritesSharedSettings"] = true;

            await Task.Run(() => SearchRankingConfiguration.Save([SearchHitKind.Video], _host.Profile));
            await Until(() => _rankItems.Count > 0 && _rankItems[0].Kind == SearchHitKind.Video);
            await Task.Run(() => SearchExecutableConfiguration.Save(false, _host.Profile));
            await Until(() => _rankItems.FirstOrDefault(item => item.Kind == SearchHitKind.Executable)?.ExecutablesEnabled == false);
            result["ExternalOrderAndToggleRefresh"] = true;
            result["Passed"] = true;
        }
        catch (Exception error) { result["Passed"] = false; result["Error"] = error.ToString(); }
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

        static async Task Until(Func<bool> condition)
        {
            for (var i = 0; i < 100; i++) { if (condition()) return; await Task.Delay(50); }
            throw new TimeoutException("Search ranking UI did not reach the expected state.");
        }
    }
}
#endif
