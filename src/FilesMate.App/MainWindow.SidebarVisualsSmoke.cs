#if FILESMATE_UI_TEST
using System.Reflection;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Navigation;
using FilesMate.App.Icons;
using FilesMate.App.Navigation;
using FilesMate.App.Theming;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task VerifySidebarVisualsAsync(Dictionary<string, object> report)
    {
        var sidebar = WindowNavigation.Sidebar;
        var preferences = App.ExplorerPreferences;
        var originalContent = TabHost.Content;
        var originalWidth = sidebar.Width;
        var originalCompact = sidebar.IsCompact;
        var originalIcons = ShellIconBinder.UseBundledIcons;
        var root = Path.Combine(AppContext.BaseDirectory, "sidebar-visual-fixture");
        var folder = Path.Combine(root, "Download");
        Directory.CreateDirectory(folder);
        var row = new FileRow();
        try
        {
            await Until(() => sidebar.IsLoaded && PolishDescendants(sidebar).OfType<PlaceGlyph>().Any(), "sidebar loaded");
            var items = new[]
            {
                new NavigationItem("folder", "Download", "\uE8B7", folder, selected: true, badge: "5"),
                new NavigationItem("documents", "文档", "\uE8A5", Path.Combine(root, "Documents")),
                new NavigationItem("downloads", "下载", "\uE896", Path.Combine(root, "Downloads")),
                new NavigationItem("cloud", "WPS云盘", "\uE753", Path.Combine(root, "Cloud")),
                new NavigationItem("drive", "系统 (C:)", "\uEDA2", Path.GetPathRoot(root)),
                new NavigationItem("tag", "资料", "\uE8EC", TagLocation.Uri(1)),
            };
            var home = new NavigationItem("home", "主页", "\uE80F", HomeLocation.Uri);
            typeof(NavigationSidebar).GetMethod("PublishPlaces", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(sidebar,
                [NavigationCatalog.Build(items.Take(4).ToArray(), [items[4]], tags: [items[5]],
                    home: [home])]);
            var host = new Grid { Background = (Brush)ThemeResources.Resolve(sidebar, "FilesMate.FileContent.BackgroundBrush")! };
            row.VerticalAlignment = VerticalAlignment.Top;
            row.Margin = new Thickness(12, 24, 0, 0);
            host.Children.Add(row);
            TabHost.Content = host;
            await Until(() => row.IsLoaded && Glyphs().Length == 7, "reference row and sidebar items");
            ShellIconBinder.SetUseBundledIcons(true);
            row.Bind(0, new FileEntryCore(0, "Download", 0, 1, 1, FileAttributes.Directory, EntryKind.Directory), false, folder);
            row.UpdateLayout();
            var referenceImage = PolishDescendants(row).OfType<Image>().Single(image => image.Name == "IconImage");
            var referenceFrame = PolishDescendants(row).OfType<FrameworkElement>().Single(element => element.Name == "IconFrame");
            var referenceName = PolishDescendants(row).OfType<TextBlock>().Single(text => text.Name == "NameText");
            var referenceGap = Left(referenceName, row) - Left(referenceFrame, row) - referenceFrame.ActualWidth;
            Check(Math.Abs(referenceGap - FileColumnLayout.NameCellPad) < .7, "File list reference gap");
            var geometry = new List<object>();
            foreach (var theme in new[] { Models.AppThemeKind.Light, Models.AppThemeKind.Dark })
            {
                await App.AppearanceViewModel!.SetThemeAsync(theme);
                sidebar.IsCompact = false;
                await Task.Delay(100);
                sidebar.UpdateLayout(); row.UpdateLayout();
                foreach (var glyph in Glyphs())
                {
                    var itemRoot = (FrameworkElement)VisualTreeHelper.GetParent(glyph);
                    Check(Math.Abs(itemRoot.ActualHeight - 32) < .7, "Sidebar row height was not tightened");
                    var labelText = items.Append(home).Single(item => item.Target == glyph.Target).Label;
                    var label = PolishDescendants(itemRoot).OfType<TextBlock>().First(text => text.Text == labelText);
                    var gap = Left(label, itemRoot) - Left(glyph, itemRoot) - glyph.ActualWidth;
                    Check(Math.Abs(gap - referenceGap) < .7 && Math.Abs(glyph.ActualWidth - referenceFrame.ActualWidth) < .7,
                        "Sidebar icon and label metrics: " + label.Text);
                    var image = Image(glyph);
                    if (glyph.Target?.StartsWith(root, StringComparison.OrdinalIgnoreCase) == true)
                        Check(image.Source is not null && ReferenceEquals(image.Source, referenceImage.Source), "Shared folder image: " + label.Text);
                    else Check(image.Source is null, "Virtual or drive location must retain its symbol");
                    geometry.Add(new { Theme = theme.ToString(), Label = label.Text, Width = glyph.ActualWidth, Gap = gap, RowHeight = itemRoot.ActualHeight });
                }
                var settings = PolishDescendants(sidebar).OfType<Button>().Single(button => button.Name == "SettingsButton");
                var settingGlyph = PolishDescendants(settings).OfType<FontIcon>().Single();
                var settingName = PolishDescendants(settings).OfType<TextBlock>().Single(text => text.Name == "SettingsLabel");
                Check(Math.Abs(Left(settingName, settings) - Left(settingGlyph, settings) - settingGlyph.ActualWidth - referenceGap) < .7,
                    "Settings icon and label gap");
                await Capture(WindowNavigation, "sidebar-icons-" + theme + ".png");
            }
            report["SidebarMatchesFileListMetrics"] = new { ReferenceIconWidth = referenceFrame.ActualWidth, ReferenceGap = referenceGap, Items = geometry };
            var tracked = Glyphs().Single(glyph => glyph.Target == folder);
            tracked.Target = HomeLocation.Uri;
            Check(Image(tracked).Source is null, "Rebinding a folder icon to a virtual location retained its image");
            tracked.Target = folder;
            Check(ReferenceEquals(Image(tracked).Source, referenceImage.Source), "Rebinding a folder did not reuse its image");
            ShellIconBinder.SetUseBundledIcons(false);
            try
            {
                await Until(() => Image(tracked).Source is not null && Image(tracked).Source is not Microsoft.UI.Xaml.Media.Imaging.SvgImageSource,
                    "system icon preference");
                Check(ReferenceEquals(Image(tracked).Source, referenceImage.Source), "System folder image differs from the file list");
            }
            finally
            {
                report["SidebarSystemIcon"] = new { Bundled = ShellIconBinder.UseBundledIcons,
                    Source = Image(tracked).Source?.GetType().Name, Loaded = tracked.IsLoaded };
            }
            ShellIconBinder.SetUseBundledIcons(true);
            Check(ReferenceEquals(Image(tracked).Source, referenceImage.Source), "Restoring bundled icons did not restore the shared image");
            report["SidebarIconPreferenceAndRebind"] = true;
            sidebar.Width = 52; sidebar.IsCompact = true;
            await Task.Delay(100); sidebar.UpdateLayout();
            foreach (var glyph in Glyphs())
            {
                var itemRoot = (FrameworkElement)VisualTreeHelper.GetParent(glyph);
                Check(Math.Abs(Left(glyph, itemRoot) + glyph.ActualWidth / 2 - itemRoot.ActualWidth / 2) < .7, "Compact icon is not centered");
            }
            await Capture(WindowNavigation, "sidebar-icons-Compact.png");
            report["SidebarCompactIconsCentered"] = true;
        }
        finally
        {
            ShellIconBinder.SetUseBundledIcons(originalIcons);
            row.Clear(); TabHost.Content = originalContent;
            sidebar.Width = originalWidth; sidebar.IsCompact = originalCompact;
            await App.SetExplorerPreferencesAsync(preferences);
            sidebar.Reload();
        }
        PlaceGlyph[] Glyphs() => PolishDescendants(sidebar).OfType<PlaceGlyph>().Where(glyph => glyph.IsLoaded).ToArray();
        static Image Image(PlaceGlyph glyph) => (Image)typeof(PlaceGlyph).GetField("_image", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(glyph)!;
        static double Left(UIElement element, UIElement relative) => element.TransformToVisual(relative).TransformPoint(new Point()).X;
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Until(Func<bool> condition, string message)
        { for (var i = 0; i < 160; i++) { if (condition()) return; await Task.Delay(50); } throw new TimeoutException(message); }
    }
}
#endif
