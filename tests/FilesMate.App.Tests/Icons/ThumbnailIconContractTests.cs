using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Icons;

public sealed class ThumbnailIconContractTests
{
    [Fact]
    public void Omnibar_uses_handled_pointer_events_for_click_to_edit()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Omnibar", "Omnibar.xaml"));
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Omnibar", "Omnibar.xaml.cs"));

        Assert.DoesNotContain("PointerPressed=\"PathHost_PointerPressed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AddHandler", code, StringComparison.Ordinal);
        Assert.Contains("handledEventsToo: true", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_tiles_request_shell_thumbnails_before_format_and_shell_fallbacks()
    {
        var binder = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Icons", "ShellIconBinder.cs"));
        var thumbnail = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Icons", "ShellThumbnailService.cs"));

        Assert.Contains("ShellThumbnailService", binder, StringComparison.Ordinal);
        Assert.Contains("FileTypeIconCatalog", binder, StringComparison.Ordinal);
        Assert.Contains("TryGetFormatAsset", binder, StringComparison.Ordinal);
        Assert.Contains("GetThumbnailAsync", thumbnail, StringComparison.Ordinal);
        Assert.Contains("ThumbnailMode.PicturesView", thumbnail, StringComparison.Ordinal);
        Assert.Contains("ThumbnailOptions.ResizeThumbnail", thumbnail, StringComparison.Ordinal);

        var thumbnailIndex = binder.IndexOf("ShellThumbnailService", StringComparison.Ordinal);
        var formatIndex = binder.IndexOf("FileTypeIconCatalog", StringComparison.Ordinal);
        var immediateIndex = binder.IndexOf("TryGetFormatAsset", StringComparison.Ordinal);
        var shellIndex = binder.LastIndexOf("Service.GetAsync", StringComparison.Ordinal);
        Assert.True(thumbnailIndex >= 0 && formatIndex > thumbnailIndex && immediateIndex > formatIndex && shellIndex > immediateIndex);
    }

    [Fact]
    public void Recycled_tiles_cancel_obsolete_thumbnail_and_shell_work()
    {
        var binder = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Icons", "ShellIconBinder.cs"));

        Assert.Contains("CancellationTokenSource", binder, StringComparison.Ordinal);
        Assert.Contains("CancelBinding(image)", binder, StringComparison.Ordinal);
        Assert.Contains("state.Cancellation.Token", binder, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(image.Tag, state)", binder, StringComparison.Ordinal);
        Assert.Contains("state.Dispose()", binder, StringComparison.Ordinal);
        Assert.Contains("finally", binder, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationToken.None", binder, StringComparison.Ordinal);
    }

    [Fact]
    public void Dynamic_thumbnail_sources_use_a_bounded_cache()
    {
        var binder = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Icons", "ShellIconBinder.cs"));

        Assert.Contains("MaxDynamicImageCacheEntries", binder, StringComparison.Ordinal);
        Assert.Contains("MaxDynamicImageCacheBytes", binder, StringComparison.Ordinal);
        Assert.Contains("ByteBudgetCache<ImageSource>", binder, StringComparison.Ordinal);
        Assert.Contains("new(MaxDynamicImageCacheBytes, MaxDynamicImageCacheEntries)", binder, StringComparison.Ordinal);
        Assert.Contains("DynamicImages.Set(cacheKey, created, bitmap.Bgra.LongLength)", binder, StringComparison.Ordinal);
    }

    [Fact]
    public void Thumbnail_cache_uses_decoded_bytes_and_file_versioned_keys()
    {
        var service = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Icons", "ShellThumbnailService.cs"));
        var binder = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Icons", "ShellIconBinder.cs"));

        Assert.Contains("MaxCacheBytes", service, StringComparison.Ordinal);
        Assert.Contains("IconLoadCache", service, StringComparison.Ordinal);
        Assert.Contains("LastWriteTimeUtc", service, StringComparison.Ordinal);
        Assert.Contains("info.Length", service, StringComparison.Ordinal);
        Assert.Contains("ShellThumbnailService.TryCreateKey", binder, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(rasterPixels, 32, 512)", binder, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Clamp(rasterPixels * 2", binder, StringComparison.Ordinal);
    }


    [Fact]
    public void Duplicate_empty_activation_toggles_the_existing_window()
    {
        var app = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "App.xaml.cs"));
        var window = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));

        Assert.Contains("ToggleMainWindowFromActivation()", app, StringComparison.Ordinal);
        Assert.Contains("window.DispatcherQueue.TryEnqueue", app, StringComparison.Ordinal);
        Assert.Contains("internal void ToggleTaskbarVisibility()", window, StringComparison.Ordinal);
        Assert.Contains("presenter.Minimize()", window, StringComparison.Ordinal);
        Assert.Contains("if (IsIconic(hwnd))", window, StringComparison.Ordinal);
        Assert.Contains("TryBringToForeground", window, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactMate_format_assets_are_bundled_in_the_app()
    {
        var assetRoot = Path.Combine(ThemeXaml.AppRoot, "Assets", "FileIcons");
        foreach (var name in new[] { "folder.png", "archive.png", "document.png", "image.png", "audio.png", "video.png" })
        {
            var path = Path.Combine(assetRoot, name);
            Assert.True(File.Exists(path), $"Missing bundled file icon: {path}");
            Assert.True(new FileInfo(path).Length > 128, $"Bundled icon is unexpectedly empty: {path}");
        }
    }

    [Fact]
    public void Vector_format_assets_are_bundled_and_routed_for_hidpi_rendering()
    {
        var assetRoot = Path.Combine(ThemeXaml.AppRoot, "Assets", "FileIcons");
        foreach (var name in new[]
        {
            "folder.svg", "link.svg", "archive.svg", "document.svg", "image.svg", "audio.svg", "video.svg",
            "database.svg", "configuration.svg", "system.svg", "generic.svg",
            "pdf.svg", "word.svg", "spreadsheet.svg", "presentation.svg", "text.svg", "markdown.svg",
            "json.svg", "xml.svg", "html.svg", "csv.svg", "log.svg", "ebook.svg", "code.svg",
            "javascript.svg", "typescript.svg", "csharp.svg", "cpp.svg", "python.svg", "rust.svg",
            "go.svg", "java.svg", "yaml.svg", "toml.svg", "zip.svg", "sevenzip.svg", "rar.svg", "tar.svg",
        })
        {
            var path = Path.Combine(assetRoot, name);
            Assert.True(File.Exists(path), $"Missing vector file icon: {path}");
            var xml = File.ReadAllText(path);
            Assert.Contains("<svg", xml, StringComparison.Ordinal);
            Assert.Contains("viewBox=\"0 0 128 128\"", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("data:image", xml, StringComparison.OrdinalIgnoreCase);
            if (name is not ("folder.svg" or "link.svg" or "image.svg" or "audio.svg" or "video.svg"))
            {
                Assert.Contains("id=\"badge-label\"", xml, StringComparison.Ordinal);
                Assert.DoesNotContain("v2.16h-", xml, StringComparison.Ordinal);
                Assert.DoesNotContain("h1.643v1.643", xml, StringComparison.Ordinal);
            }
        }

        var catalog = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Icons", "FileTypeIconCatalog.cs"));
        var binder = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Icons", "ShellIconBinder.cs"));
        Assert.Contains("}}.svg", catalog, StringComparison.Ordinal);
        Assert.Contains("SvgImageSource", binder, StringComparison.Ordinal);
        Assert.Contains("RasterizePixelWidth", binder, StringComparison.Ordinal);
        Assert.Contains("RasterizationScale", binder, StringComparison.Ordinal);
        Assert.Contains("RasterizePixelSize", binder, StringComparison.Ordinal);
        Assert.Contains("dipSize * scale * 2", binder, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Clamp(pixelSize * 2", binder, StringComparison.Ordinal);
        Assert.Contains("FileIconKind.Link", catalog, StringComparison.Ordinal);
        Assert.Contains("FileIconKind.Database", catalog, StringComparison.Ordinal);
        Assert.Contains("FileIconKind.Configuration", catalog, StringComparison.Ordinal);
        Assert.Contains("FileIconKind.System", catalog, StringComparison.Ordinal);
        Assert.Contains("FileIconKind.Generic", catalog, StringComparison.Ordinal);
        Assert.Contains("FileIconKind.Pdf", catalog, StringComparison.Ordinal);
        Assert.Contains("FileIconKind.Json", catalog, StringComparison.Ordinal);
        Assert.Contains("FileIconKind.Markdown", catalog, StringComparison.Ordinal);
        Assert.Contains("FileIconKind.SevenZip", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void Archive_assets_use_the_shared_double_page_compact_mark_instead_of_a_literal_zipper()
    {
        var assetRoot = Path.Combine(ThemeXaml.AppRoot, "Assets", "FileIcons");
        foreach (var name in new[] { "archive.svg", "zip.svg", "sevenzip.svg", "rar.svg", "tar.svg" })
        {
            var vector = File.ReadAllText(Path.Combine(assetRoot, name));
            Assert.Contains("id=\"compact-mark\"", vector, StringComparison.Ordinal);
            Assert.DoesNotContain("zipper", vector, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Shortcuts_prefer_the_real_windows_shell_icon_before_generic_vector_art()
    {
        var catalog = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Icons", "FileTypeIconCatalog.cs"));
        var binder = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Icons", "ShellIconBinder.cs"));

        Assert.Contains("PrefersShell", catalog, StringComparison.Ordinal);
        Assert.Contains("FileIconKind.Link", catalog, StringComparison.Ordinal);
        Assert.Contains("FileTypeIconCatalog.PrefersShell(formatKind)", binder, StringComparison.Ordinal);
        Assert.Contains("GetShellBitmapAsync", binder, StringComparison.Ordinal);

        var shellFirst = binder.IndexOf("if (FileTypeIconCatalog.PrefersShell(formatKind))", StringComparison.Ordinal);
        var formatFallback = binder.IndexOf("GetFormatAssetAsync(kind, rasterPixels, dispatcher)", shellFirst, StringComparison.Ordinal);
        Assert.True(shellFirst >= 0 && formatFallback > shellFirst, "Shortcut Shell extraction must precede bundled link artwork.");
    }

    [Fact]
    public void Hover_states_are_instant_fills_without_theme_shadow()
    {
        foreach (var file in new[] { "FileTile.xaml", "FileRow.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "FileSurface", file));
            Assert.Contains("GeneratedDuration=\"0:0:0\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("FilesMate.Item.HoverShadow", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("FilesMate.Motion.Hover", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Tab_icons_use_the_same_bundled_folder_art_as_file_tiles()
    {
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));

        Assert.Contains("new ImageIconSource", code, StringComparison.Ordinal);
        Assert.Contains("new FontIconSource", code, StringComparison.Ordinal);
        Assert.Contains("new SvgImageSource", code, StringComparison.Ordinal);
        Assert.Contains("FileTypeIconCatalog.AssetUri(FileIconKind.Folder)", code, StringComparison.Ordinal);
        Assert.Contains("RasterizePixelSize", code, StringComparison.Ordinal);
        Assert.Contains("RasterizeTabFolderIcons", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RasterizePixelWidth = 48", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Glyph = \"\\uE8B7\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Folder_art_does_not_have_a_hard_opaque_highlight_band()
    {
        var vectorPath = Path.Combine(ThemeXaml.AppRoot, "Assets", "FileIcons", "folder.svg");
        var vector = File.ReadAllText(vectorPath);
        Assert.Contains("id=\"back\"", vector, StringComparison.Ordinal);
        Assert.Contains("id=\"content\"", vector, StringComparison.Ordinal);
        Assert.Contains("id=\"front\"", vector, StringComparison.Ordinal);
        Assert.DoesNotContain("stroke=\"#FFF", vector, StringComparison.OrdinalIgnoreCase);

        var path = Path.Combine(ThemeXaml.AppRoot, "Assets", "FileIcons", "folder.png");
        var image = BrandImage.DecodePng(File.ReadAllBytes(path));
        var maxHighlightAlpha = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var offset = ((y * image.Width) + x) * 4;
                var alpha = image.Rgba[offset + 3];
                var red = image.Rgba[offset];
                var green = image.Rgba[offset + 1];
                var blue = image.Rgba[offset + 2];
                if (red > 245 && green > 245 && blue > 245)
                {
                    maxHighlightAlpha = Math.Max(maxHighlightAlpha, alpha);
                }
            }
        }

        Assert.True(maxHighlightAlpha <= 32, $"Folder highlight alpha is too strong: {maxHighlightAlpha}.");
    }
}
