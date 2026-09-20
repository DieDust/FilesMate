using Loc = FilesMate.App.Localization.StringTable;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FilesMate.App.Icons;
using FilesMate.Core.Icons;
using FilesMate.Platform.Windows.Icons;
using FilesMate.Search;

namespace FilesMate.SearchHost;

internal sealed class SearchResultIcons
{
    internal bool UseBundledIcons { get; set; } = true;
    private static readonly ImageSource Empty = CreateEmpty();
    private static ImageSource CreateEmpty()
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        image.Freeze();
        return image;
    }
    private readonly Dictionary<FileIconKind, ImageSource> _artwork = new();
    private readonly Dictionary<string, CachedIcon> _shellImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Thumbnails decode real image/video data, which is heavier than a shell icon lookup but parallelizes a
    // little; without a gate, scrolling a media-heavy result list would start one WIC/Shell job per visible row.
    private readonly SemaphoreSlim _mediaGate = new(2, 2);
    private ImageSource? _brand;
    internal SearchMediaPreview Media { get; } = new();
    private sealed record CachedIcon(ImageSource Image, long Created, long Used);
    private long _access;
    internal void ClearDynamicCache()
    {
        Media.Clear();
        // Expire individual icons instead of emptying the entire cache on a
        // capacity boundary. Recently opened applications stay warm.
        var now = Environment.TickCount64;
        foreach (var pair in _shellImages.ToArray())
            if (now - pair.Value.Created >= 60000) _shellImages.Remove(pair.Key);
    }

    private bool TryCached(string path, out ImageSource? image)
    {
        if (_shellImages.TryGetValue(path, out var cached))
        {
            if (Environment.TickCount64 - cached.Created < 60000)
            {
                _shellImages[path] = cached with { Used = ++_access };
                image = cached.Image;
                return true;
            }
            _shellImages.Remove(path);
        }
        image = null;
        return false;
    }

    private void Cache(string path, ImageSource image)
    {
        if (_shellImages.Count >= 128 && !_shellImages.ContainsKey(path))
            _shellImages.Remove(_shellImages.MinBy(pair => pair.Value.Used).Key);
        _shellImages[path] = new(image, Environment.TickCount64, ++_access);
    }

    internal ImageSource Fallback(NameHit hit)
    {
        if (hit.Application?.IsFilesMate == true)
        {
            if (_brand is not null) return _brand;
            var brand = new BitmapImage(new Uri("pack://application:,,,/FilesMate.SearchHost;component/Assets/FilesMate.png"));
            brand.Freeze();
            return _brand = brand;
        }
        var kind = FileTypeIconCatalog.ClassifyPath(hit.Path, hit.IsDirectory) ?? FileIconKind.Generic;
        if (!UseBundledIcons) return TryCached(hit.Path, out var system) ? system! : Empty;
        if (_artwork.TryGetValue(kind, out var cached)) return cached;
        var name = Path.GetFileNameWithoutExtension(FileTypeIconCatalog.AssetUri(kind));
        var image = new BitmapImage(new Uri($"pack://application:,,,/FilesMate.SearchHost;component/Assets/FileIcons/{name}.png"));
        image.Freeze();
        _artwork[kind] = image;
        return image;
    }

    internal async Task LoadAsync(SearchRow row, CancellationToken token)
    {
        if (row.Hit.Application?.IsFilesMate == true) return;
        var kind = FileTypeIconCatalog.ClassifyPath(row.Path, row.Hit.IsDirectory);
        if (!row.IsApplication && kind is FileIconKind.Image or FileIconKind.Video)
        {
            var enteredMedia = false;
            try
            {
                await _mediaGate.WaitAsync(token);
                enteredMedia = true;
                token.ThrowIfCancellationRequested();
                var thumbnail = await Media.LoadAsync(row.Path, 96, token);
                token.ThrowIfCancellationRequested();
                if (thumbnail is not null) { row.Icon = thumbnail; return; }
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException or Win32Exception or System.Runtime.InteropServices.COMException or ArgumentException or NotSupportedException) { }
            finally { if (enteredMedia) _mediaGate.Release(); }
        }
        if (UseBundledIcons && !row.IsApplication && kind is not null && !FileTypeIconCatalog.PrefersShell(kind)) return;
        if (TryCached(row.Path, out var cached)) { row.Icon = cached!; return; }
        var entered = false;
        try
        {
            // The same gate covers every query in this window. Canceled or
            // blocked native calls cannot create an unbounded worker queue.
            await _gate.WaitAsync(token);
            entered = true;
            token.ThrowIfCancellationRequested();
            if (TryCached(row.Path, out cached)) { row.Icon = cached!; return; }
            if (row.IsApplication)
            {
                var appIcon = await Task.Run(() => ApplicationShell.Icon(row.Hit.Application!.LaunchPath), token);
                token.ThrowIfCancellationRequested();
                if (appIcon is not null)
                {
                    Cache(row.Path, appIcon);
                    row.Icon = appIcon;
                }
                return;
            }
            IconBitmap? icon = null;
            for (var attempt = 0; attempt < 2 && icon is null; attempt++)
            {
                if (attempt > 0) await Task.Delay(80, token);
                icon = await new WindowsSystemIconService().GetAsync(IconKey.ForPath(row.Path, row.Hit.IsDirectory, 64), row.Path,
                    FileAttributes.Normal, row.Hit.IsDirectory, token);
            }
            token.ThrowIfCancellationRequested();
            if (icon is null) return;
            var image = BitmapSource.Create(icon.Width, icon.Height, 96, 96, PixelFormats.Pbgra32, null, icon.Bgra, icon.Width * 4);
            image.Freeze();
            Cache(row.Path, image);
            row.Icon = image;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException or Win32Exception or System.Runtime.InteropServices.COMException) { }
        finally { if (entered) _gate.Release(); }
    }
}

public sealed class SearchRow(NameHit hit, ImageSource icon) : INotifyPropertyChanged
{
    internal bool IconLoaded { get; set; }
    private ImageSource _icon = icon;
    public NameHit Hit { get; } = hit;
    public string Name => Hit.Name;
    public bool IsApplication => Hit.Application is not null;
    public string? FilePath => Hit.Application is { } app ? app.LocationPath : Path;
    public bool CanLocate => !string.IsNullOrEmpty(FilePath);
    public string DisplayName => System.IO.Path.GetExtension(Name).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetFileNameWithoutExtension(Name) : Name;
    public string Location => IsApplication ? Hit.Application!.IsFilesMate ? Loc.Get("App_TrayDescription") : Loc.Get("LaunchApplication") : System.IO.Path.GetDirectoryName(Path) ?? Path;
    public string Path => Hit.Path;
    public string Kind => IsApplication ? Loc.Get("Application") : Hit.IsDirectory ? Loc.Get("Type_Folder") : System.IO.Path.GetExtension(Hit.Name).ToLowerInvariant() switch
    { ".lnk" => Loc.Get("SearchRankShortcut"), ".exe" or ".com" => Loc.Get("ProgramFile"), _ => System.IO.Path.GetExtension(Hit.Name).TrimStart('.').ToUpperInvariant() };
    public ImageSource Icon { get => _icon; set { _icon = value; PropertyChanged?.Invoke(this, new(nameof(Icon))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
