#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunSelectionVisualsSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        var failures = new List<string>();
        var samples = new List<object>();
        var multiTagSamples = new List<object>();
        var selectionSamples = new List<object>();
        var interactionSamples = new List<object>();
        FileDetailsSurface? surface = null;
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(2200, 1500));
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            await App.AppearanceViewModel.SetAccentAsync(AccentKind.Default);
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with
            {
                ShowAlternatingRows = true, ShowGridFileSizes = false,
                ShowFullThumbnails = false, ThumbnailQuality = ThumbnailQuality.Standard,
            });
            for (var attempt = 0; attempt < 100 && TabHost.Content is not NavigatorPage { IsLoaded: true }; attempt++) await Task.Delay(50);
            var store = new EntryStore();
            string[] names = ["tools", "Project planning documents September", "publish"];
            store.Append(names.Select((name, id) => new FileEntryCore(id, name, 0, 0, 0, FileAttributes.Directory, EntryKind.Directory)).ToArray());
            surface = new FileDetailsSurface { Width = Math.Min(920, TabHost.ActualWidth), Height = 420,
                ResolveTags = entry => entry.Id is 1 or 2 ? [new TagDefinition(1, "项目", "#4F786C", 0)] : [] };
            var host = new Border { Width = surface.Width, Height = surface.Height, Child = surface };
            Theming.ThemeResources.Bind(host, Border.BackgroundProperty, "FilesMate.FileContent.BackgroundBrush");
            var fixtureRoot = new Grid();
            fixtureRoot.Children.Add(host);
            TabHost.Content = fixtureRoot;
            surface.Bind(store, EntryViewIndex.InSourceOrder(store, 1), 1);
            var opened = 0;
            surface.OpenRequested += (_, _) => opened++;

            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                foreach (var mode in Enum.GetValues<ItemOpeningMode>())
                {
                    await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { FileOpeningMode = mode, FolderOpeningMode = mode });
                    foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.List })
                    {
                        surface.SetLayout(layout);
                        await Task.Delay(140);
                        foreach (var selected in new[] { new[] { names[0] }, names[..2], Array.Empty<string>(), new[] { names[1] } })
                        {
                            surface.RestoreSelectedNames(selected);
                            surface.RebindVisibleEntries();
                            await Task.Delay(45);
                            var rows = PolishDescendants(surface).OfType<FileRow>().Where(row => row.EntryId >= 0).ToArray();
                            Check(rows.Length == 3, $"{theme}/{layout}: rows missing");
                            foreach (var row in rows)
                            {
                                var check = row.FindName("SelectionBox") as CheckBox;
                                Check(check is null || check.Visibility == Visibility.Collapsed, $"{theme}/{layout}/{mode}: row checkbox visible");
                                Check(row.FindName("AccentBar") is null, "File row retained a selection bar");
                                selectionSamples.Add(new { Theme = theme.ToString(), Layout = layout.ToString(), Mode = mode.ToString(),
                                    Selected = selected.Contains(row.Entry.Name), SelectionCount = selected.Length, row.Entry.Name });
                            }
                            if (mode == ItemOpeningMode.DoubleClick && selected.Length is 1 or 2)
                                await CaptureSample($"selection-{layout}-{theme}-{selected.Length}.png", 600, 128);
                        }
                    }
                }
                foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.List, FileLayoutKind.Grid })
                    await CaptureInteractionSample(theme, layout);
                surface.SetLayout(FileLayoutKind.Grid);
                foreach (var font in new[] { 13d, 20d })
                {
                    await App.AppearanceViewModel.SetFileTypographyAsync(null, font, 12);
                    foreach (var preset in GridSizePreset.All)
                    {
                        surface.SetGridSize(preset);
                        surface.RestoreSelectedNames(names);
                        await Task.Delay(140);
                        var tiles = PolishDescendants(surface).OfType<FileTile>().Where(tile => tile.EntryId >= 0).OrderBy(tile => tile.EntryId).ToArray();
                        Check(tiles.Length == 3, $"{theme}/{preset.Slot}/{font}: tiles missing");
                        var sizes = tiles.Select(tile =>
                        {
                            var root = Bounds((FrameworkElement)tile.FindName("Root"), tile);
                            var name = Bounds((FrameworkElement)tile.FindName("NameText"), tile);
                            var tag = Bounds((FrameworkElement)tile.FindName("TagHost"), tile);
                            return new { tile.Entry.Name, root.Width, root.Height, NameHeight = name.Height,
                                NameBottomPadding = root.Bottom - name.Bottom, TagGap = tag.Top - root.Bottom };
                        }).ToArray();
                        if (sizes.Length > 0)
                            Check(sizes.Max(size => size.Width) - sizes.Min(size => size.Width) <= 1 && sizes.Max(size => size.Height) - sizes.Min(size => size.Height) <= 1,
                                $"{theme}/{preset.Slot}/{font}: different highlight sizes: {JsonSerializer.Serialize(sizes)}");
                        foreach (var tile in tiles)
                        {
                            Check(tile.FindName("SelectionBox") is CheckBox { Visibility: Visibility.Collapsed }, $"{theme}/{preset.Slot}/{font}: idle grid checkbox visible");
                            var root = (FrameworkElement)tile.FindName("Root");
                            Check(root.ActualHeight <= tile.ActualHeight + 1 && root.ActualWidth <= tile.ActualWidth + 1, "Highlight exceeds its grid cell");
                            var name = Bounds((FrameworkElement)tile.FindName("NameText"), tile);
                            var highlight = Bounds(root, tile);
                            var nameAreaBottom = name.Top + preset.WithFontSize(font).TextHeight;
                            Check(highlight.Bottom - nameAreaBottom is >= 0 and <= 4, "Highlight reserves empty space below the two-line filename area");
                            Check(root is Border { Background: SolidColorBrush fill } && fill.Color.A >= 0x2B,
                                "Selected tile background is missing or too faint");
                            var tags = (StackPanel)tile.FindName("TagHost");
                            if (tags.Children.Count > 0)
                            {
                                var label = Bounds(tags, tile);
                                Check(label.Top - highlight.Bottom is >= 5 and <= 7, "Tag is too close to the name or detached from the tile");
                                Check(label.Bottom <= tile.ActualHeight + 1, "Tag overlaps the next grid row");
                            }
                        }
                        samples.Add(new { Theme = theme.ToString(), preset.Slot, Font = font, Sizes = sizes });
                        if (font == 13 && preset == GridSizePreset.Large)
                        {
                            await CaptureSample($"selection-Grid-{theme}.png", preset.ItemWidth * 3 + preset.Gutter * 2 + 16, preset.ItemHeight + 12);
                            var originalTags = surface.ResolveTags;
                            TagDefinition[][] tagSets =
                            [
                                [new(1, "项目", "#4F786C", 0)],
                                [new(1, "项目", "#4F786C", 0), new(2, "待处理", "#C08A3C", 1)],
                                [new(3, "本季度归档资料", "#678BC3", 0), new(1, "项目", "#4F786C", 1), new(2, "待处理", "#C08A3C", 2), new(4, "共享", "#9976B9", 3)]
                            ];
                            surface.ResolveTags = entry => tagSets[entry.Id];
                            surface.RebindVisibleEntries();
                            await Task.Delay(140);
                            foreach (var tile in tiles)
                            {
                                var tags = (StackPanel)tile.FindName("TagHost");
                                var overflow = tags.Children.OfType<TextBlock>().SingleOrDefault()?.Text;
                                var count = tagSets[tile.EntryId].Length;
                                Check(overflow == (count > 1 ? $"+{count - 1}" : null), $"{theme}: wrong multi-tag overflow count");
                                var fullText = ToolTipService.GetToolTip(tags) as string;
                                Check(tagSets[tile.EntryId].All(tag => fullText?.Contains(tag.Name, StringComparison.Ordinal) == true), "Tag tooltip omits a hidden label");
                                Check(tags.Children.OfType<FrameworkElement>().All(child => Bounds(child, tile).Right <= tile.ActualWidth + 1), "Multi-tag label overflows its tile");
                                multiTagSamples.Add(new { Theme = theme.ToString(), tile.Entry.Name, Count = count, Overflow = overflow, Tooltip = fullText });
                            }
                            await CaptureSample($"selection-multiple-tags-{theme}.png", preset.ItemWidth * 3 + preset.Gutter * 2 + 16, preset.ItemHeight + 12);
                            surface.ResolveTags = originalTags;
                            surface.RebindVisibleEntries();
                        }
                    }
                }
            }
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            surface.SetGridSize(GridSizePreset.Large);
            foreach (var mode in Enum.GetValues<ItemOpeningMode>())
            {
                await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { FileOpeningMode = mode, FolderOpeningMode = mode });
                surface.RestoreSelectedNames([]);
                await Task.Delay(150);
                surface.RestoreSelectedNames(names[..2]);
                Check(surface.Selection.Count == 2, $"{mode}: grid multi-selection failed");
                Check(!PolishDescendants(surface).OfType<CheckBox>().Any(box => box.Visibility == Visibility.Visible), $"{mode}: idle grid checkbox visible");
            }
            Check(opened == 0, "Selecting items opened an item");
            report["GridSizes"] = samples;
            report["MultipleTags"] = multiTagSamples;
            report["FileRowSelectionSamples"] = selectionSamples;
            report["Interactions"] = interactionSamples;

            async Task CaptureInteractionSample(AppThemeKind theme, FileLayoutKind layout)
            {
                await App.AppearanceViewModel!.SetFileTypographyAsync(null, 13, 12);
                var oldWidth = host.Width;
                var oldHeight = host.Height;
                var grid = layout == FileLayoutKind.Grid;
                var width = grid ? GridSizePreset.Large.ItemWidth * 3 + GridSizePreset.Large.Gutter * 2 + 16 : 600;
                var height = grid ? GridSizePreset.Large.ItemHeight + 12 : 128;
                surface.Width = host.Width = width;
                surface.Height = host.Height = height;
                surface.SetLayout(layout);
                surface.SetGridSize(GridSizePreset.Large);
                surface.RestoreSelectedNames([names[2]]);
                await Task.Delay(150);
                var items = PolishDescendants(surface).Where(item => grid ? item is FileTile { EntryId: >= 0 } : item is FileRow { EntryId: >= 0 })
                    .Cast<Control>().OrderBy(item => grid ? ((FileTile)item).EntryId : ((FileRow)item).EntryId).ToArray();
                Check(items.Length == 3, $"{theme}/{layout}: interaction fixture items missing");
                if (items.Length != 3) return;
                foreach (var item in items) Pointer(item, false);
                if (!grid && layout == FileLayoutKind.Details)
                    Check(((Border)items[1].FindName("Stripe")).Visibility == Visibility.Visible, "Normal odd row has no stripe");
                Pointer(items[1], true);
                await Task.Delay(60);
                var normal = Fill(items[0]);
                var hover = Fill(items[1]);
                var selected = Fill(items[2]);
                var colors = SkinPalette.For(theme == AppThemeKind.Dark).FileItemColors();
                Check(Argb(normal.Background) >> 24 == 0, "Normal item retained an interaction fill");
                Check(Argb(hover.Background) == colors["FilesMate.FileItem.HoverBrush"], $"{theme}/{layout}: neutral hover fill missing");
                Check(Argb(hover.BorderBrush) == colors["FilesMate.FileItem.HoverBorderBrush"] && hover.BorderThickness.Left == 1, "Hover outline missing");
                Check(Argb(selected.Background) == colors["FilesMate.FileItem.SelectedBrush"], $"{theme}/{layout}: selection fill missing");
                Check(Argb(selected.BorderBrush) == colors["FilesMate.FileItem.SelectionBorderBrush"] && selected.BorderThickness.Left == 1, "Selection outline missing");
                Check(Argb(((TextBlock)items[2].FindName("SizeText")).Foreground) == colors["FilesMate.FileItem.SelectedForegroundBrush"], "Selected details did not use legible ink");
                if (!grid) Check(((Border)items[1].FindName("Stripe")).Visibility == Visibility.Collapsed, "Stripe remains behind hover");
                interactionSamples.Add(new { Theme = theme.ToString(), Layout = layout.ToString(),
                    HoverFill = $"{Argb(hover.Background):X8}", HoverBorder = $"{Argb(hover.BorderBrush):X8}",
                    SelectedFill = $"{Argb(selected.Background):X8}", SelectedBorder = $"{Argb(selected.BorderBrush):X8}" });
                await Capture(host, $"hover-selection-{layout}-{theme}.png");
                Pointer(items[2], true);
                Check(Argb(Fill(items[2]).Background) == colors["FilesMate.FileItem.SelectedHoverBrush"], "Hovered selection lost its accent fill");
                Pointer(items[1], false);
                surface.RestoreSelectedNames(names[1..]);
                await Task.Delay(70);
                Pointer(items[2], true);
                foreach (var item in items) Check(item.FindName("AccentBar") is null, "File item retained a selection bar");
                await Capture(host, $"hover-selection-{layout}-{theme}-multi.png");
                foreach (var accent in new[] { "#0078D4", "#FFFFFF", "#000000" })
                {
                    await App.AppearanceViewModel!.SetCustomAccentAsync(accent);
                    await Task.Delay(70);
                    var expected = (SkinPalette.For(theme == AppThemeKind.Dark) with { Accent = AccentPalette.Resolve(AccentKind.Custom, accent) }).FileItemColors();
                    Check(Argb(Fill(items[2]).Background) == expected["FilesMate.FileItem.SelectedHoverBrush"], $"{theme}/{layout}/{accent}: live selected-hover accent was not updated");
                    Check(Argb(Fill(items[2]).BorderBrush) == expected["FilesMate.FileItem.SelectionBorderBrush"], $"{theme}/{layout}/{accent}: live selection outline was not updated");
                }
                await App.AppearanceViewModel!.SetAccentAsync(AccentKind.Default);
                foreach (var item in items) Pointer(item, false);
                surface.RestoreSelectedNames([]);
                await Task.Delay(60);
                foreach (var item in items)
                {
                    Check(Argb(Fill(item).Background) >> 24 == 0, "Clearing selection left a fill behind");
                    Check(Argb(((TextBlock)item.FindName("SizeText")).Foreground) == SkinPalette.For(theme == AppThemeKind.Dark).Muted, "Clearing selection retained selected detail ink");
                }
                surface.Width = host.Width = oldWidth;
                surface.Height = host.Height = oldHeight;
                await Task.Delay(80);

                Border Fill(Control item) => (Border)item.FindName(grid ? "Root" : "Fill");
            }

            static void Pointer(Control item, bool entered) =>
                item.GetType().GetMethod(entered ? "OnPointerEntered" : "OnPointerExited", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(item, [item, null]);

            static uint Argb(Brush brush)
            {
                var color = ((SolidColorBrush)brush).Color;
                return (uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
            }

            async Task CaptureSample(string filename, double width, double height)
            {
                var oldWidth = host.Width;
                var oldHeight = host.Height;
                surface.Width = host.Width = Math.Min(width, TabHost.ActualWidth);
                surface.Height = host.Height = height;
                await Task.Delay(120);
                await Capture(host, filename);
                surface.Width = host.Width = oldWidth;
                surface.Height = host.Height = oldHeight;
                await Task.Delay(100);
            }
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally { surface?.ReleaseResources(); }
        report["Failures"] = failures;
        report["Passed"] = failures.Count == 0;
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "selection-visuals-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        void Check(bool passed, string message) { if (!passed) failures.Add(message); }
        static Windows.Foundation.Rect Bounds(FrameworkElement element, UIElement relativeTo) =>
            element.TransformToVisual(relativeTo).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
    }
}
#endif
