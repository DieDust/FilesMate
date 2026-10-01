#if FILESMATE_UI_TEST
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Commands;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.App.Views;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunPendingPolishSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        FileDetailsSurface? surface = null;
        var preferences = App.ExplorerPreferences;
        var appearance = App.AppearanceViewModel!.Current;
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-12000, -12000)); AppWindow.Resize(new(2100, 1500));
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } p && !p.ViewModel.IsLoading, "navigator ready");
            var navigator = (NavigatorPage)TabHost.Content;
            var colors = new List<object>();
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            foreach (var accent in new[] { "#FFFFFF", "#000000", "#80C09A" })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                await App.AppearanceViewModel.SetCustomAccentAsync(accent);
                navigator.ShowPermanentDeletionFeedback(1, null);
                await Task.Delay(100);
                var bar = (InfoBar)Field(navigator, "_transferResultNotice")!;
                bar.UpdateLayout();
                var expected = ((SolidColorBrush)ThemeResources.Resolve(bar, "FilesMate.Text.PrimaryBrush")!).Color;
                var labels = PolishDescendants(bar).OfType<TextBlock>().Where(t => t.Text == bar.Title && t.ActualHeight > 0).ToArray();
                Require(labels.Length > 0 && labels.All(t => t.Foreground is SolidColorBrush b && b.Color == expected),
                    "Operation feedback foreground is incorrect for " + theme + " / " + accent);
                colors.Add(new { Theme = theme.ToString(), Accent = accent, Foreground = expected.ToString() });
                if (accent == "#80C09A") await Capture(bar, "feedback-" + theme + ".png");
            }
            report["OperationFeedbackThemeColors"] = colors;
            await VerifySidebarVisualsAsync(report);
            if (Environment.GetEnvironmentVariable("FILESMATE_REAL_POINTER_SMOKE") == "1")
                await VerifyMenuPointerAsync(report);
            if (Environment.GetEnvironmentVariable("FILESMATE_DISABLE_NATIVE_CURSORS") != "1")
                await VerifyNativeCursorsAsync(report);

            await App.SetExplorerPreferencesAsync(preferences with { AutoFitNameColumn = false, ShowFileExtensions = true });
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            surface = new FileDetailsSurface();
            surface.SetLayout(FileLayoutKind.Details);
            surface.SetColumns(DetailsColumn.Defaults().Select(c => c.Id == DetailsColumnId.Name ? c with { Width = 312 } : c).ToArray());
            TabHost.Content = surface;
            await Wait(() => surface.IsLoaded, "standalone surface");
            var store = Store(1000, i => i == 999 ? "Z-" + new string('W', 220) + ".txt" : $"a-{i:D4}.txt");
            surface.Bind(store, EntryViewIndex.InSourceOrder(store, 1), 1);
            await Wait(() => PolishDescendants(surface).OfType<FileRow>().Any(r => r.IsLoaded && r.EntryId >= 0), "details rows");
            Require(!PolishDescendants(surface).OfType<FileRow>().Any(r => r.Entry.Name.StartsWith("Z-")), "Longest name must be offscreen for this test");
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { AutoFitNameColumn = true });
            await Measured();
            Require(NameWidth() + FileColumnLayout.GlyphWidth == CompactListMetrics.MaximumColumnWidth, "Offscreen name did not reach the shared 800-DIP limit");
            Require(surface.GetPresentationColumns().Single(c => c.Id == DetailsColumnId.Name).Width == 312,
                "Automatic width replaced the saved manual width");
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { AutoFitNameColumn = false });
            Require(NameWidth() == 312, "Turning automatic sizing off did not restore the manual width");
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { AutoFitNameColumn = true });
            await Measured();
            report["OffscreenNameBoundAndManualRestore"] = true;

            store = Store(70, i => $"Project-research-{i:D3}.txt");
            surface.Bind(store, EntryViewIndex.InSourceOrder(store, 2), 2); await Measured();
            var normal = NameWidth();
            await App.AppearanceViewModel.SetFileTypographyAsync("Microsoft YaHei UI", 24, 20); await Measured();
            var enlarged = NameWidth();
            Require(enlarged > normal && enlarged + FileColumnLayout.GlyphWidth <= 800, "Font changes did not resize the name column");
            store = Store(5, i => $"{i}.txt");
            surface.Bind(store, EntryViewIndex.InSourceOrder(store, 3), 3); await Measured();
            Require(NameWidth() < enlarged, "A folder with short names did not shrink");
            var shortWidth = NameWidth();
            surface.SetLayout(FileLayoutKind.List); await Task.Delay(120);
            surface.SetLayout(FileLayoutKind.Details); await Measured();
            Require(NameWidth() == shortWidth, "View switching changed the automatic name width");
            report["FolderFontAndViewChanges"] = new { Normal = normal, Enlarged = enlarged, ShortNames = shortWidth };

            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            store = Store(100000, i => $"entry-{i:D6}-release-notes.txt");
            var watch = Stopwatch.StartNew();
            surface.Bind(store, EntryViewIndex.InSourceOrder(store, 4), 4);
            var heartbeat = DispatcherQueue.CreateTimer();
            heartbeat.Interval = TimeSpan.FromMilliseconds(20);
            var ticks = 0;
            heartbeat.Tick += (_, _) => ticks++;
            heartbeat.Start(); await Measured(); heartbeat.Stop();
            Require(ticks > 0 && PolishDescendants(surface).OfType<FileRow>().Count(r => r.EntryId >= 0) < 200,
                "Large-folder measurement blocked the dispatcher or expanded all rows");
            report["AutomaticWidth100k"] = new { Milliseconds = watch.Elapsed.TotalMilliseconds, DispatcherTicks = ticks,
                RealizedRows = PolishDescendants(surface).OfType<FileRow>().Count(r => r.EntryId >= 0), NameWidth = NameWidth() };

            var menuResults = new List<object>();
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                typeof(FileDetailsSurface).GetMethod("ShowColumnMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, [new Point(60, 20)]);
                await Task.Delay(150);
                var menu = (MenuFlyout)Field(surface, "_columnMenu")!;
                var labels = menu.Items.OfType<MenuFlyoutItem>().Where(item => item is not ToggleMenuFlyoutItem).Select(item =>
                {
                    var label = PolishDescendants(item).OfType<TextBlock>().Single(t => t.Name == "TextBlock");
                    return new { item.Text, X = label.TransformToVisual(item).TransformPoint(new()).X };
                }).Concat(menu.Items.OfType<ToggleMenuFlyoutItem>().Select(item =>
                {
                    var label = PolishDescendants(item).OfType<TextBlock>().Single(t => t.Name == "TextBlock");
                    return new { item.Text, X = label.TransformToVisual(item).TransformPoint(new()).X };
                })).ToArray();
                report["ColumnMenuLabels"] = labels;
                report["ColumnMenuGeometry"] = menu.Items.OfType<MenuFlyoutItem>().Select(item => new
                {
                    Type = item.GetType().Name, item.Padding, item.Margin,
                    Controls = PolishDescendants(item).OfType<FrameworkElement>().Where(e => e.Name is "TextBlock" or "CheckGlyph" or "LayoutRoot" or "IconRoot")
                        .Select(e => new { e.Name, Width = double.IsFinite(e.Width) ? (double?)e.Width : null, e.MinWidth, e.ActualWidth,
                            e.Margin, Padding = e is Grid grid ? grid.Padding : default }).ToArray()
                }).ToArray();
                Require(labels.Max(x => x.X) - labels.Min(x => x.X) < 1, "Column menu text starts are misaligned");
                Require(labels.All(x => Math.Abs(x.X - 36) < .6), "Column menu still reserves a second icon/check slot");
                Require(menu.Items.OfType<MenuFlyoutItem>().All(i => i.FontSize == 13 && Math.Abs(i.ActualHeight - 32) < .6)
                    && menu.Items.OfType<ToggleMenuFlyoutItem>().All(i => i.FontSize == 13 && Math.Abs(i.ActualHeight - 32) < .6),
                    "Column menu rows do not match the file context card");
                Require(menu.Items.OfType<MenuFlyoutItem>().Take(2).All(i => i.Icon is not null), "Fit-column actions have no icons");
                Require(menu.Items.OfType<ToggleMenuFlyoutItem>().All(item =>
                    PolishDescendants(item).OfType<FontIcon>().Single(i => i.Name == "CheckGlyph").Opacity == (item.IsChecked ? 1 : 0)),
                    "Visible-column checkmarks do not reflect the current columns");
                var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(surface.XamlRoot).Last();
                await Capture(popup.Child, "column-menu-" + theme + ".png");
                menuResults.Add(new { Theme = theme.ToString(), Labels = labels });
                menu.Hide();
                await Wait(() => Field(surface, "_columnMenu") is null, "column menu closed");
                var referenceLayout = FileContextMenuBuilder.BuildLayout(CommandContext.Blank with { FolderPath = AppContext.BaseDirectory });
                var reference = FileContextFlyout.Create(referenceLayout, _ => { });
                FileContextFlyout.ShowAt(reference, surface, new Point(60, 20));
                await Task.Delay(150);
                var referenceText = referenceLayout.Items.First(entry => !entry.IsSeparator).Label;
                var referenceItem = PolishDescendants(reference.Content).OfType<Button>().First(b => PolishDescendants(b).OfType<TextBlock>().Any(t => t.Text == referenceText));
                var referenceLabel = PolishDescendants(referenceItem).OfType<TextBlock>().First(t => t.Text == referenceText);
                Require(Math.Abs(referenceLabel.TransformToVisual(referenceItem).TransformPoint(new()).X - labels[0].X) < .6,
                    "Column menu icon/name spacing differs from the existing context card");
                Require(Math.Abs(referenceItem.ActualHeight - 32) < .6, "Reference context card row metric changed");
                await CapturePopupAsync((FrameworkElement)reference.Content, "file-context-reference-" + theme + ".png");
                reference.Hide();
                await Task.Delay(80);
            }
            report["ColumnMenuAlignment"] = menuResults;

            typeof(FileDetailsSurface).GetMethod("ShowColumnMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, [new Point(60, 20)]);
            await Task.Delay(150);
            var choices = (MenuFlyout)Field(surface, "_columnMenu")!;
            var modified = choices.Items.OfType<ToggleMenuFlyoutItem>().Single(item => item.Text == surface.GetColumns().Single(c => c.Id == DetailsColumnId.Modified).Title);
            var before = modified.IsChecked;
            ((IToggleProvider)new ToggleMenuFlyoutItemAutomationPeer(modified).GetPattern(PatternInterface.Toggle)).Toggle();
            await Wait(() => surface.GetColumns().Single(c => c.Id == DetailsColumnId.Modified).Visible != before, "column toggle applies");
            choices.Hide();
            await Wait(() => Field(surface, "_columnMenu") is null, "column toggle closed");
            typeof(FileDetailsSurface).GetMethod("ShowColumnMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, [new Point(60, 20)]);
            await Task.Delay(150);
            choices = (MenuFlyout)Field(surface, "_columnMenu")!;
            Require(choices.Items.OfType<ToggleMenuFlyoutItem>().Single(item => item.Text == modified.Text).IsChecked != before, "Reopened menu lost the column choice");
            Require(!choices.Items.OfType<ToggleMenuFlyoutItem>().Single(item => item.Text == surface.GetColumns().Single(c => c.Id == DetailsColumnId.Name).Title).IsEnabled,
                "Name column can be hidden");
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(choices.Items.OfType<MenuFlyoutItem>().Last()).GetPattern(PatternInterface.Invoke)).Invoke();
            await Wait(() => surface.GetColumns().Single(c => c.Id == DetailsColumnId.Modified).Visible, "reset restores columns");
            choices.Hide();
            await Wait(() => Field(surface, "_columnMenu") is null, "reset closed");
            report["ColumnMenuTogglePersistenceAndReset"] = true;

            var settings = new FilesAndFoldersSettingsPage();
            TabHost.Content = new ScrollViewer { Content = settings };
            await Wait(() => settings.IsLoaded, "settings page");
            var toggle = (ToggleSwitch)settings.FindName("AutoNameWidthToggle");
            Require(toggle.IsOn, "Automatic width setting did not synchronize");
            toggle.IsOn = false;
            await Wait(() => !new ExplorerPreferencesService(Path.Combine(AppContext.BaseDirectory, "test-profile", "explorer.json")).Load().AutoFitNameColumn,
                "automatic width setting persistence");
            toggle.IsOn = true;
            await Wait(() => new ExplorerPreferencesService(Path.Combine(AppContext.BaseDirectory, "test-profile", "explorer.json")).Load().AutoFitNameColumn,
                "automatic width setting saved on");
            report["AutomaticWidthSettingPersistence"] = true;
            report["Passed"] = true;

            double NameWidth() => surface.GetColumns().Single(c => c.Id == DetailsColumnId.Name).Width;
            async Task Measured() => await Wait(() => !(bool)Field(surface, "_autoNameMeasurePending")!, "automatic name measurement");
        }
        catch (Exception error)
        {
            report["Passed"] = false; report["Error"] = error.ToString();
            // Native input fixtures use a screen capture in their own failure path.
            // Rendering the whole island after popup teardown can block diagnostics.
            if (Environment.GetEnvironmentVariable("FILESMATE_REAL_POINTER_SMOKE") != "1")
                try { await Capture(Content, "pending-polish-failure.png"); } catch { }
        }
        finally
        {
            surface?.ReleaseResources();
            await App.SetExplorerPreferencesAsync(preferences);
            await App.AppearanceViewModel.SetThemeAsync(appearance.Theme);
            await App.AppearanceViewModel.SetFileTypographyAsync(appearance.FileFontFamily, appearance.FileNameFontSize, appearance.FileDetailsFontSize);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "pending-polish-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
        static EntryStore Store(int count, Func<int, string> name)
        {
            var result = new EntryStore();
            result.Append(Enumerable.Range(0, count).Select(i => new FileEntryCore(i, name(i), 0, 1, 1, FileAttributes.Normal, EntryKind.File)).ToArray());
            return result;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Wait(Func<bool> condition, string step)
        { for (var i = 0; i < 600; i++) { if (condition()) return; await Task.Delay(50); } throw new TimeoutException(step); }
    }
}
#endif
