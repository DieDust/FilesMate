#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Views;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunVisualRefreshSmokeAsync()
    {
        var result = new Dictionary<string, object>();
        try
        {
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(1700, 1120));
            var fixture = Path.Combine(AppContext.BaseDirectory, "design-fixture");
            Directory.CreateDirectory(fixture);
            foreach (var name in new[] { "01 项目资料", "02 设计素材", "03 工作文档", "04 归档" })
                Directory.CreateDirectory(Path.Combine(fixture, name));
            foreach (var name in new[] { "产品说明.txt", "设计记录.md", "会议纪要.docx", "预算明细.xlsx", "演示文稿.pptx", "品牌手册.pdf", "界面草图.png", "封面.jpg", "项目备份.zip", "配置.json", "启动工具.exe", "背景音乐.mp3", "演示视频.mp4" })
                await File.WriteAllTextAsync(Path.Combine(fixture, name), "FilesMate visual fixture — display only.");
            App.SetFavoritesBarEnabled(true);
            await App.Favorites.AddAsync([(Path.Combine(fixture, "01 项目资料"), true), (Path.Combine(fixture, "02 设计素材"), true)]);
            AddNavigatorTab(fixture);
            for (var i = 0; i < 100 && (TabHost.Content is not NavigatorPage { IsLoaded: true } p || p.ViewModel.IsLoading); i++)
                await Task.Delay(100);
            if (TabHost.Content is not NavigatorPage page) throw new InvalidOperationException("No fixture page");
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            await App.AppearanceViewModel.SetAccentAsync(AccentKind.Default);
            var surface = FindDescendant<FileDetailsSurface>(page, _ => true)!;
            surface.RestoreSelectedNames(["会议纪要.docx"]);
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                await Task.Delay(600);
                await Capture((UIElement)Content, "design-files-" + theme + ".png");
            }
            result["BothThemesRendered"] = true;
            AppWindow.Resize(new(1150, 800));
            await Task.Delay(250);
            await Capture((UIElement)Content, "design-narrow.png");
            result["NarrowWindowRendered"] = true;
            AppWindow.Resize(new(1700, 1120));
            AddNavigatorTab(HomeLocation.Uri);
            await Task.Delay(700);
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                await Task.Delay(350);
                await Capture((UIElement)Content, "design-home-" + theme + ".png");
            }
            result["HomeRendered"] = true;
            await VerifyVisualRefreshControlsAsync(result);
            result["Passed"] = true;
        }
        catch (Exception error) { result["Passed"] = false; result["Error"] = error.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "visual-refresh-smoke.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task VerifyVisualRefreshControlsAsync(Dictionary<string, object> result)
    {
        var root = (Grid)Content;
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetRowSpan(host, 10);
        Grid.SetColumnSpan(host, 10);
        root.Children.Add(host);
        try
        {
            var row = new FileRow();
            host.Content = row;
            row.ApplyColumns(DetailsColumn.Defaults());
            var file = new FileEntryCore(1, "report.docx", 42, DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks, FileAttributes.Normal, EntryKind.File);
            var folder = new FileEntryCore(2, "archive.docx", 0, DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks, FileAttributes.Directory, EntryKind.Directory);
            row.Bind(1, file, false, null);
            await Task.Delay(150);
            var badge = (Border)row.FindName("TypeCell");
            var label = (TextBlock)row.FindName("TypeText");
            var stripe = (Border)row.FindName("Stripe");
            Require(label.Text == "DOCX" && badge.Background is SolidColorBrush, "File badge was not rendered");
            Require(stripe.Visibility == Visibility.Visible, "Odd unselected row is missing its stripe");
            row.SetSelected(true);
            Require(stripe.Visibility == Visibility.Collapsed, "Decorative stripe overlaid the selection");
            row.SetSelected(false);

            await App.AppearanceViewModel!.SetThemeAsync(AppThemeKind.Light);
            await Task.Delay(100);
            var lightInk = ((SolidColorBrush)label.Foreground).Color;
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Dark);
            await Task.Delay(100);
            Require(lightInk != ((SolidColorBrush)label.Foreground).Color, "Reused badge did not follow theme changes");
            row.Bind(0, folder, false, null);
            Require(label.Text != "DOCX" && badge.Background is not null && badge.Width == 68 && stripe.Visibility == Visibility.Collapsed,
                "Recycled folder retained file decoration");
            row.Bind(1, file with { Name = "README" }, false, null);
            Require(badge.Background is not null && badge.Width == 68 && label.Text != "DOCX", "Extensionless file retained its old badge");
            row.Bind(1, file with { Name = "long." + new string('a', 80) }, false, null);
            var columns = DetailsColumn.Defaults();
            columns[2] = columns[2] with { Width = 64 };
            row.ApplyColumns(columns);
            row.UpdateLayout();
            Require(badge.ActualWidth <= 48.5, "Long extension escaped the resized type column");
            columns = columns.Select(c => c.Id == DetailsColumnId.Type ? c with { Visible = false } : c).ToArray();
            row.ApplyColumns(columns);
            Require(badge.Visibility == Visibility.Collapsed, "Hidden type column remained visible");
            columns = columns.Select(c => c.Id == DetailsColumnId.Type ? c with { Visible = true } : c).Reverse().ToArray();
            row.ApplyColumns(columns);
            Require(badge.Visibility == Visibility.Visible && Grid.GetColumn(badge) != 4, "Reordered badge stayed in its old column");
            row.Clear();
            Require(badge.Background is null && label.Text.Length == 0 && stripe.Visibility == Visibility.Collapsed, "Cleared row retained decoration");
            result["RecyclingColumnsAndLiveTheme"] = true;

            foreach (var accent in new[] { "#FFF8C8", "#112233" })
            {
                await App.AppearanceViewModel.SetCustomAccentAsync(accent);
                var button = FindDescendant<Button>(TabHost, b => b.Name == "NewButton")!;
                var color = ((SolidColorBrush)button.Background).Color;
                Require(color.R == (accent == "#FFF8C8" ? 255 : 17), "Custom accent was lost");
                var ink = ((SolidColorBrush)button.Foreground).Color;
                Require(ink.R == (accent == "#FFF8C8" ? 0 : 255), "Primary button ink did not adapt to its fill");
            }
            await App.AppearanceViewModel.SetAccentAsync(AccentKind.Default);
            result["CustomAccentContrast"] = true;

            var surface = new FileDetailsSurface();
            host.Content = surface;
            surface.SetLayout(FileLayoutKind.Details);
            var store = new EntryStore();
            store.Append(Enumerable.Range(0, 10000).Select(i => new FileEntryCore(i, $"sample-{i:D5}.txt", 42, 1, 1, FileAttributes.Normal, EntryKind.File)).ToArray());
            var index = EntryViewIndex.Build(store, EntrySort.Name, new EntryFilter(), NaturalStringComparer.Instance, 1);
            surface.Bind(store, index, 1);
            await Task.Delay(250);
            var initialCount = surface.RealizedCount;
            surface.RestoreScrollOffset(5000 * FileColumnLayout.RowHeight);
            await Task.Delay(250);
            Require(surface.ScrollOffset > 10000, "Large-list fixture did not scroll");
            Require(initialCount is > 0 and < 160 && surface.RealizedCount is > 0 and < 160, "Row decorations disabled virtualization");
            var realized = FindDescendant<FileRow>(surface, r => r.ViewIndex >= 0)!;
            var realizedStripe = (Border)realized.FindName("Stripe");
            Require((realized.ViewIndex % 2 != 0) == (realizedStripe.Visibility == Visibility.Visible), "Recycled row stripe has the wrong parity");
            result["TenThousandEntries"] = new { InitialRows = initialCount, ScrolledRows = surface.RealizedCount, Offset = surface.ScrollOffset };
            surface.ReleaseResources();
        }
        finally { root.Children.Remove(host); }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
