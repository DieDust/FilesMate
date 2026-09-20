using FilesMate.App.Icons;
using FilesMate.Core.Icons;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace FilesMate.App.Controls.FileSurface;

internal static class FileDragPreview
{
#if FILESMATE_UI_TEST
    private static bool _smokeStarted;
    internal static async Task ExportSmokeAsync(Canvas host)
    {
        if (_smokeStarted) return;
        _smokeStarted = true;
        var output = Path.Combine(AppContext.BaseDirectory, "drag-preview-smoke");
        Directory.CreateDirectory(output);
        try
        {
            var path = AppContext.BaseDirectory;
            var icon = await LoadIconAsync(path, true, GridSizePreset.Default.IconSize, host.XamlRoot);
            var script = Path.Combine(output, "sample.ps1");
            var archive = Path.Combine(output, "sample.zip");
            File.WriteAllText(script, "# drag preview fixture");
            File.WriteAllBytes(archive, []);
            string[] selection = [path, script, Environment.ProcessPath!, archive, Path.Combine(path, "Assets", "FileIcons", "image.png")];
            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                host.RequestedTheme = theme;
                foreach (var count in new[] { 1, 2, 12, 1000 })
                {
                    using var bitmap = await RenderAsync(host, Enumerable.Repeat(icon, Math.Min(count, 3)).ToArray(), count, GridSizePreset.Default.IconSize);
                    await SaveSmokeAsync(bitmap, Path.Combine(output, $"{theme}-{count}.png"));
                }
                foreach (var first in selection.Skip(1))
                {
                    List<ImageSource?> icons = [];
                    var orderedSelection = new[] { first }.Concat(selection.Where(path => path != first)).ToArray();
                    foreach (var selected in DragPreviewSelection.Paths(orderedSelection))
                        icons.Add(await LoadIconAsync(selected, Directory.Exists(selected), GridSizePreset.Default.IconSize, host.XamlRoot));
                    using var mixed = await RenderAsync(host, icons, selection.Length, GridSizePreset.Default.IconSize);
                    await SaveSmokeAsync(mixed, Path.Combine(output, $"{theme}-mixed-{Path.GetExtension(first).TrimStart('.')}.png"));
                    using var single = await RenderAsync(host, [icons[0]], 1, GridSizePreset.Default.IconSize);
                    await SaveSmokeAsync(single, Path.Combine(output, $"{theme}-single-{Path.GetExtension(first).TrimStart('.')}.png"));
                }
            }
            File.WriteAllText(Path.Combine(output, "passed.txt"), "Rendered both themes; counts 1, 2, 12, 1000; single and mixed PS1, EXE, ZIP, PNG selections.");
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); }
        finally { host.RequestedTheme = ElementTheme.Default; }
    }
    private static string CreateOutput(string path) { File.WriteAllBytes(path, []); return path; }
    private static async Task SaveSmokeAsync(SoftwareBitmap bitmap, string path)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(CreateOutput(path));
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
    }
#endif

    internal static async Task<ImageSource?> LoadIconAsync(string path, bool directory, double iconSize, XamlRoot? root)
    {
        var pixels = ShellIconBinder.RasterizePixelSize(root, (int)iconSize);
        var kind = FileTypeIconCatalog.ClassifyPath(path, directory);
        if (ShellIconBinder.UseBundledIcons && kind is { } known && !FileTypeIconCatalog.PrefersShell(kind))
        {
            // Use the same project-owned art as the file grid. Await the small
            // packaged SVG before taking the snapshot, avoiding an empty icon.
            var uri = new Uri(FileTypeIconCatalog.AssetUri(known));
            var asset = Path.Combine(AppContext.BaseDirectory, uri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(asset);
            using var stream = await file.OpenReadAsync();
            var svg = new SvgImageSource { RasterizePixelWidth = pixels, RasterizePixelHeight = pixels };
            if (await svg.SetSourceAsync(stream) == SvgImageSourceLoadStatus.Success) return svg;
        }
        var key = IconKey.ForPath(path, directory, pixels);
        var bitmap = ShellIconBinder.Service.TryGetCached(key) ?? await ShellIconBinder.Service.GetAsync(
            key, path, directory ? FileAttributes.Directory : FileAttributes.Normal, directory, CancellationToken.None);
        return bitmap is null ? null : ShellIconBinder.ToBitmap(bitmap);
    }

    public static async Task<SoftwareBitmap> RenderAsync(Canvas host, IReadOnlyList<ImageSource?> icons, int count, double iconSize)
    {
        var dark = host.ActualTheme == ElementTheme.Dark;
        var face = new SolidColorBrush(dark ? ColorHelper.FromArgb(255, 47, 50, 57) : Colors.White);
        var label = "×" + count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        var badgeWidth = Math.Max(26, 12 + label.Length * 7);
        var layers = Math.Min(Math.Min(count, icons.Count), 3);
        var step = Math.Clamp(iconSize / 8, 3, 8);
        var frontTop = 4 + step * (layers - 1);
        var badgeLeft = iconSize - 10;
        var visual = new Canvas
        {
            Width = Math.Max(iconSize + step * (layers - 1) + 4, count > 1 ? badgeLeft + badgeWidth + 2 : 0),
            Height = iconSize + frontTop + 4, IsHitTestVisible = false,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        for (var layer = layers - 1; layer >= 0; layer--)
        {
            // The foreground image has exactly the grid's IconHost dimensions.
            // Behind it, show at most two other selected items.
            var icon = icons[layer];
            FrameworkElement image = icon is not null
                ? new Image { Source = icon, Width = iconSize, Height = iconSize, Stretch = Stretch.Uniform }
                : new FontIcon { Glyph = "\uE8A5", FontSize = iconSize * 0.5, Width = iconSize, Height = iconSize };
            Canvas.SetLeft(image, 2 + layer * step);
            Canvas.SetTop(image, frontTop - layer * step);
            visual.Children.Add(image);
        }
        if (count > 1)
        {
            var badge = new Border
            {
                MinWidth = badgeWidth, Height = 18, Padding = new Thickness(5, 0, 5, 0),
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 36, 103, 192)),
                BorderBrush = face, BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Canvas.SetLeft(badge, badgeLeft);
            Canvas.SetTop(badge, 2);
            visual.Children.Add(badge);
        }
        Canvas.SetLeft(visual, -10000);
        host.Children.Add(visual);
        try
        {
            visual.Measure(new Size(visual.Width, visual.Height));
            visual.Arrange(new Rect(0, 0, visual.Width, visual.Height));
            visual.UpdateLayout();
            var rendered = new RenderTargetBitmap();
            // An asynchronously decoded SVG needs a layout/render turn after it
            // is attached, even when SetSourceAsync has already completed.
            await Task.Delay(16);
            // Render at the source grid's natural raster scale exactly once.
            await rendered.RenderAsync(visual, (int)Math.Ceiling(visual.Width), (int)Math.Ceiling(visual.Height));
            var pixels = await rendered.GetPixelsAsync();
            if (rendered.PixelWidth == 0 || rendered.PixelHeight == 0)
                throw new InvalidOperationException($"Drag preview empty: layout={visual.ActualWidth}x{visual.ActualHeight}, requested={visual.Width}x{visual.Height}, loaded={visual.IsLoaded}, layers={layers}");
            return SoftwareBitmap.CreateCopyFromBuffer(pixels, BitmapPixelFormat.Bgra8,
                rendered.PixelWidth, rendered.PixelHeight, BitmapAlphaMode.Premultiplied);
        }
        finally { host.Children.Remove(visual); }
    }
}
