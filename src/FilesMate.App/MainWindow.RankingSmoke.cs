#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Search;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunRankingSmokeAsync()
    {
        var result = new Dictionary<string, object>();
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            var profile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-profile"));
            var store = App.SearchIndexSettingsStore ?? throw new IOException("Search settings unavailable.");
            if (!Path.GetFullPath(store.FilePath).StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Refusing ranking smoke outside the private UI-test profile.");

            OpenSettings("search");
            await Until(() => _settingsPage is not null
                && FindDescendant<SearchSettingsPage>(_settingsPage, page => page.IsLoaded) is not null);
            var page = FindDescendant<SearchSettingsPage>(_settingsPage!, page => page.IsLoaded)!;
            var list = (ListView)page.FindName("RankList");
            await Until(() => list.Items.Count == SearchHitKinds.DefaultOrder.Count && list.ContainerFromIndex(0) is ListViewItem);
            list.UpdateLayout();
            var row = (ListViewItem)list.ContainerFromIndex(0);
            var presenter = FindDescendant<ListViewItemPresenter>(row, _ => true)
                ?? throw new IOException("Ranking row presenter missing.");
            if (presenter.CornerRadius.TopLeft != 12 || presenter.CornerRadius.BottomRight != 12)
                throw new IOException($"Ranking selection is not rounded: {presenter.CornerRadius}");
            if (!list.AllowDrop || !list.CanDragItems || !list.CanReorderItems)
                throw new IOException("Ranking list is missing native drag reorder support.");
            result["RoundedSelection"] = true;

            await Task.Run(() => SearchRankingConfiguration.Save([SearchHitKind.Video], profile));
            await Until(() => list.Items.Count > 0 && list.Items[0] is SearchRankItem { Kind: SearchHitKind.Video });
            await Task.Run(() => SearchExecutableConfiguration.Save(false, profile));
            await Until(() => list.Items.OfType<SearchRankItem>()
                .Any(item => item.Kind == SearchHitKind.Executable && !item.ExecutablesEnabled));
            result["ExternalOrderAndToggleRefresh"] = true;

            var executable = list.Items.OfType<SearchRankItem>().Single(item => item.Kind == SearchHitKind.Executable);
            list.ScrollIntoView(executable);
            await Until(() => list.ContainerFromItem(executable) is ListViewItem);
            var toggle = FindDescendant<ToggleSwitch>((ListViewItem)list.ContainerFromItem(executable), _ => true)
                ?? throw new IOException("Executable toggle missing.");
            if (toggle.IsOn) throw new IOException("Executable toggle did not reflect external disabled state.");
            var toggled = 0;
            toggle.Toggled += (_, _) => toggled++;
            toggle.IsOn = true;
            if (toggled != 1) throw new IOException("Executable toggle did not raise one user change event.");
            await Until(() => SearchExecutableConfiguration.Load(profile));
            result["ToggleWritesSharedSettings"] = true;
            result["Passed"] = true;
        }
        catch (Exception error) { result["Passed"] = false; result["Error"] = error.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ranking-smoke.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Close();

        static async Task Until(Func<bool> condition)
        {
            for (var i = 0; i < 100; i++) { if (condition()) return; await Task.Delay(50); }
            throw new TimeoutException("Ranking UI did not reach the expected state.");
        }
    }
}
#endif
