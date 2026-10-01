#if FILESMATE_UI_TEST
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Icons;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunThumbnailSettingsSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        FileDetailsSurface? surface = null;
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000)); AppWindow.Resize(new(2350, 1500));
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } page && !page.ViewModel.IsLoading, "initial navigator");
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Dark);
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with
            { ThumbnailQuality = ThumbnailQuality.Standard, ShowFullThumbnails = false, ShowGridFileSizes = false, ShowFolderSizes = false });

            var root = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "thumbnail-fixture-" + Guid.NewGuid().ToString("N"))).FullName;
            await WriteImage("长图.png", 300, 2400);
            await WriteImage("宽图.png", 2400, 300);
            await WriteImage("透明圆图.png", 1200, 1200, circle: true);
            await WriteImage("细节图.png", 3072, 2048);
            File.WriteAllText(Path.Combine(root, "报告.txt"), new string('a', 4096));
            File.WriteAllText(Path.Combine(root, "空文件.txt"), "");
            var folder = Directory.CreateDirectory(Path.Combine(root, "文件夹")).FullName;
            File.Copy(Path.Combine(root, "长图.png"), Path.Combine(folder, "cover.png"));
            var names = new[] { "长图.png", "宽图.png", "透明圆图.png", "细节图.png", "报告.txt", "空文件.txt", "文件夹" };
            var store = new EntryStore();
            store.Append(names.Select((name, id) => new FileEntryCore(id, name,
                id == 6 ? 0 : (ulong)new FileInfo(Path.Combine(root, name)).Length, 1, 1,
                id == 6 ? FileAttributes.Directory : FileAttributes.Normal, id == 6 ? EntryKind.Directory : EntryKind.File)).ToArray());
            surface = new FileDetailsSurface { ResolvePath = entry => Path.Combine(root, entry.Name),
                ResolveTags = entry => entry.Id == 4 ? [new TagDefinition(1, "资料", "#6A9AFF", 0)] : [] };
            TabHost.Content = surface;
            await Wait(() => surface.IsLoaded, "surface load");
            surface.SetLayout(FileLayoutKind.Grid); surface.SetGridSize(GridSizePreset.Maximum);
            surface.Bind(store, EntryViewIndex.InSourceOrder(store, 1), 1);
            await Wait(() => Tiles().Length == names.Length && names.Take(4).All(name => Image(name).Source is WriteableBitmap), "standard thumbnails");
            var standard = Bitmap("细节图.png");
            Require(standard.PixelWidth <= 512 && standard.PixelHeight <= 512, "Standard no longer uses the existing resolution limit.");
            Require(names.Take(4).All(name => Image(name).Stretch == Stretch.UniformToFill), "Default crop behavior changed.");
            Require(Tiles().All(tile => Size(tile).Visibility == Visibility.Collapsed), "File sizes should default off.");
            report["Defaults"] = new { standard.PixelWidth, standard.PixelHeight, Crop = true, Sizes = false };
            await Capture(surface, "thumbnail-default-Dark.png");

            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowFullThumbnails = true });
            await Wait(() => names.Take(4).All(name => Image(name).Source is WriteableBitmap)
                && Bitmap("长图.png").PixelHeight == 512 && Bitmap("长图.png").PixelWidth == 64, "complete tall image");
            Require(Bitmap("宽图.png").PixelWidth == 512 && Bitmap("宽图.png").PixelHeight == 64, "Wide image proportions were lost.");
            Require(names.Take(4).All(name => Image(name).Stretch == Stretch.Uniform), "Full images are still cropped.");
            var circle = Bitmap("透明圆图.png");
            using (var pixels = circle.PixelBuffer.AsStream())
            {
                var bytes = new byte[circle.PixelWidth * circle.PixelHeight * 4]; pixels.ReadExactly(bytes);
                Require(bytes[3] == 0 && bytes[((circle.PixelHeight / 2 * circle.PixelWidth) + circle.PixelWidth / 2) * 4 + 3] == 255,
                    "Transparent circle did not preserve its alpha channel.");
            }
            var cover = (Image)Tile("文件夹").FindName("FolderPreviewCover");
            await Wait(() => cover.Source is WriteableBitmap, "folder cover");
            Require(cover.Stretch == Stretch.Uniform, "Folder covers did not adopt the full-image option.");
            report["CompleteImages"] = new { Tall = new[] { Bitmap("长图.png").PixelWidth, Bitmap("长图.png").PixelHeight },
                Wide = new[] { Bitmap("宽图.png").PixelWidth, Bitmap("宽图.png").PixelHeight }, TransparentCorners = true };
            await Capture(surface, "thumbnail-full-Dark.png");

            var settings = new FilesAndFoldersSettingsPage();
            var settingsHost = new ScrollViewer { Content = settings };
            TabHost.Content = settingsHost;
            await Wait(() => settings.IsLoaded, "settings load");
            var quality = (ComboBox)settings.FindName("ThumbnailQualityBox");
            var full = (ToggleSwitch)settings.FindName("FullThumbnailsToggle");
            var sizes = (ToggleSwitch)settings.FindName("GridFileSizesToggle");
            var hint = (TextBlock)settings.FindName("ThumbnailResourceHint");
            Require(quality.SelectedIndex == 0 && full.IsOn && !sizes.IsOn && hint.Visibility == Visibility.Collapsed, "Settings did not synchronize.");
            quality.SelectedIndex = 1;
            await Wait(() => Reload().ThumbnailQuality == ThumbnailQuality.High && hint.Visibility == Visibility.Visible, "high-quality setting");
            var section = (FrameworkElement)settings.FindName("ThumbnailSettingsSection");
            section.StartBringIntoView(); await Task.Delay(150);
            Require(Bounds(hint, section).Bottom <= Bounds((FrameworkElement)settings.FindName("ThumbnailQualityDivider"), section).Top + .5,
                "Resource hint is below the quality section divider.");
            await Capture(section, "thumbnail-settings-High.png");
            TabHost.Content = surface;
            await Wait(() => surface.IsLoaded && Bitmap("细节图.png").PixelWidth == 1024, "high-resolution bitmap");
            var high = Bitmap("细节图.png");
            Require(high.PixelWidth > standard.PixelWidth && high.PixelHeight <= 1024, "High did not increase detail.");
            report["HighQuality"] = new { high.PixelWidth, high.PixelHeight, ResourceHint = true };

            TabHost.Content = settingsHost; await Wait(() => settings.IsLoaded, "settings reload");
            quality.SelectedIndex = 2; sizes.IsOn = true;
            await Wait(() => Reload().ThumbnailQuality == ThumbnailQuality.Ultra && Reload().ShowGridFileSizes, "ultra and size save");
            TabHost.Content = surface;
            await Wait(() => surface.IsLoaded && Bitmap("细节图.png").PixelWidth == 2048 && Size(Tile("报告.txt")).Visibility == Visibility.Visible, "ultra and size display");
            var ultra = Bitmap("细节图.png");
            Require(ultra.PixelHeight <= 2048 && ultra.PixelWidth > high.PixelWidth, "Ultra did not increase bounded detail.");
            Require(Size(Tile("报告.txt")).Text == "4 KB" && Size(Tile("空文件.txt")).Text == "0 B", "File size formatting failed.");
            Require(Size(Tile("文件夹")).Visibility == Visibility.Collapsed, "Folder size should not appear under the name.");
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowFolderSizes = true });
            var expectedFolder = DriveCapacity.FormatBytes(new FileInfo(Path.Combine(folder, "cover.png")).Length);
            await Wait(() => Size(Tile("文件夹")).Visibility == Visibility.Visible && Size(Tile("文件夹")).Text == expectedFolder, "folder size under icon");
            Require(FolderSizeCache.TryGet(folder, out var cachedFolder) && (long)cachedFolder == new FileInfo(Path.Combine(folder, "cover.png")).Length,
                "Grid folder size did not populate the shared cache.");
            Require(ReferenceEquals(ultra, Bitmap("细节图.png")), "Changing size display reloaded an unchanged Ultra thumbnail.");
            ValidateSizeLayout();
            report["UltraQuality"] = new { ultra.PixelWidth, ultra.PixelHeight, Cache = ShellIconBinder.CacheStatistics };
            await Capture(surface, "thumbnail-full-and-sizes-Dark.png");

            foreach (var preset in GridSizePreset.All)
            {
                surface.SetGridSize(preset); await Task.Delay(100);
                Require(Tiles().Where(tile => tile.Entry.Kind == EntryKind.File)
                    .All(tile => (Size(tile).Visibility == Visibility.Visible) == (preset.IconSize >= GridSizePreset.Large.IconSize)),
                    "Size visibility is wrong for preset " + preset.Slot);
                if (preset.IconSize >= GridSizePreset.Large.IconSize) ValidateSizeLayout();
            }
            surface.SetGridSize(GridSizePreset.Large);
            await App.AppearanceViewModel.SetFileTypographyAsync("Microsoft YaHei UI", 24, 20);
            await Task.Delay(120); ValidateSizeLayout();
            Require(Size(Tile("报告.txt")).FontSize == 20, "Size typography did not update.");
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Light);
            await Capture(surface, "thumbnail-full-and-sizes-Light.png");
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            report["Sizes"] = new { PresetsChecked = GridSizePreset.All.Length, EmptyFile = "0 B", Folder = expectedFolder, SharedFolderCache = true,
                RetainedUltraThumbnail = true, LargeFont = true, Tags = true, HintAboveDivider = true };

            TabHost.Content = settingsHost; await Wait(() => settings.IsLoaded, "settings reset");
            quality.SelectedIndex = 0; full.IsOn = false; sizes.IsOn = false;
            await Wait(() => Reload().ThumbnailQuality == ThumbnailQuality.Standard && !Reload().ShowFullThumbnails && !Reload().ShowGridFileSizes,
                "reset controls persisted");
            TabHost.Content = surface; await Wait(() => surface.IsLoaded, "live reset surface");
            await Wait(() => Tiles().All(tile => Size(tile).Visibility == Visibility.Collapsed)
                && names.Take(4).All(name => Image(name).Stretch == Stretch.UniformToFill), "live option reset");
            surface.SetGridSize(GridSizePreset.Maximum);
            await Wait(() => Bitmap("细节图.png").PixelWidth <= 512, "standard cache restored");
            TabHost.Content = settingsHost; await Wait(() => settings.IsLoaded, "settings reset load");
            // Recreate the page to verify a new settings session reads persisted choices.
            settings = new FilesAndFoldersSettingsPage(); settingsHost.Content = settings;
            await Wait(() => settings.IsLoaded, "fresh settings session");
            Require(((ComboBox)settings.FindName("ThumbnailQualityBox")).SelectedIndex == 0
                && !((ToggleSwitch)settings.FindName("FullThumbnailsToggle")).IsOn
                && !((ToggleSwitch)settings.FindName("GridFileSizesToggle")).IsOn
                && ((TextBlock)settings.FindName("ThumbnailResourceHint")).Visibility == Visibility.Collapsed, "Defaults did not restore.");
            report["ResetAndPersistence"] = new { AllThreeSettingsControls = true, RetainedSurfaceReload = true };
            report["Passed"] = true;

            FileTile[] Tiles() => PolishDescendants(surface).OfType<FileTile>().Where(tile => tile.EntryId >= 0 && tile.IsLoaded).ToArray();
            FileTile Tile(string name) => Tiles().Single(tile => tile.Entry.Name == name);
            Image Image(string name) => (Image)Tile(name).FindName("IconImage");
            WriteableBitmap Bitmap(string name) => (WriteableBitmap)Image(name).Source;
            static TextBlock Size(FileTile tile) => (TextBlock)tile.FindName("SizeText");
            ExplorerPreferences Reload() => new ExplorerPreferencesService(Path.Combine(AppContext.BaseDirectory, "test-profile", "explorer.json")).Load();
            void ValidateSizeLayout()
            {
                surface.UpdateLayout();
                foreach (var tile in Tiles().Where(tile => Size(tile).Visibility == Visibility.Visible))
                {
                    var name = (TextBlock)tile.FindName("NameText"); var size = Size(tile);
                    var tags = (StackPanel)tile.FindName("TagHost"); var chrome = (Border)tile.FindName("Root");
                    var nameBounds = Bounds(name, tile); var sizeBounds = Bounds(size, tile);
                    Require(sizeBounds.Top >= nameBounds.Bottom + 1 && sizeBounds.Bottom <= chrome.ActualHeight + .5,
                        $"Size overlaps name/chrome for {tile.Entry.Name}: name={nameBounds}, size={sizeBounds}, chrome={chrome.ActualHeight}.");
                    Require(tags.ActualHeight == 0 || Bounds(tags, tile).Top > sizeBounds.Bottom, "Size overlaps tags.");
                    Require(chrome.ActualHeight < tile.ActualHeight, "Size line overflows its grid cell.");
                }
            }
            async Task WriteImage(string name, uint width, uint height, bool circle = false)
            {
                var bytes = await Task.Run(() =>
                {
                    var pixels = new byte[checked((int)(width * height * 4))];
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var p = (int)((y * width + x) * 4);
                        if (circle && Math.Pow(x - width / 2d, 2) + Math.Pow(y - height / 2d, 2) > Math.Pow(width * .46, 2)) continue;
                        pixels[p] = (byte)(80 + x * 150 / width); pixels[p + 1] = (byte)(50 + y * 150 / height);
                        pixels[p + 2] = y < height / 8 ? (byte)255 : y > height * 7 / 8 ? (byte)45 : (byte)120;
                        pixels[p + 3] = 255;
                    }
                    return pixels;
                });
                using var file = File.Open(Path.Combine(root, name), FileMode.Create);
                using var stream = file.AsRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, bytes);
                await encoder.FlushAsync();
            }
        }
        catch (Exception error)
        {
            report["Passed"] = false; report["Error"] = error.ToString();
            report["Preferences"] = App.ExplorerPreferences;
            report["Surface"] = new { Loaded = surface?.IsLoaded, Cache = ShellIconBinder.CacheStatistics,
                Tiles = surface is null ? [] : PolishDescendants(surface).OfType<FileTile>().Select(tile => new
                {
                    tile.EntryId, tile.Entry.Name, tile.IsLoaded, IconSize = ((FrameworkElement)tile.FindName("IconHost")).Width,
                    Scale = tile.XamlRoot?.RasterizationScale, Source = ((Image)tile.FindName("IconImage")).Source?.GetType().Name,
                    Pixels = ((Image)tile.FindName("IconImage")).Source is WriteableBitmap bitmap ? new[] { bitmap.PixelWidth, bitmap.PixelHeight } : []
                }).ToArray() };
        }
        finally
        {
            surface?.ReleaseResources();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "thumbnail-settings-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }

        static Rect Bounds(FrameworkElement element, UIElement parent) => element.TransformToVisual(parent)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Wait(Func<bool> ready, string step)
        {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline)
            {
                try { if (ready()) return; }
                catch (Exception error) when (error is InvalidOperationException or InvalidCastException or NullReferenceException) { }
                await Task.Delay(30);
            }
            throw new TimeoutException("Thumbnail settings timed out: " + step);
        }
    }
}
#endif
