#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Core.Operations;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunCompatibilityPolishSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(1360, 960));
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            await Wait(() => TabHost.Content is NavigatorPage p && p.IsLoaded && !p.ViewModel.IsLoading, "initial navigator");
            var fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "polish-fixture-" + Guid.NewGuid().ToString("N"))).FullName;
            for (var i = 0; i < 320; i++) File.WriteAllText(Path.Combine(fixture, $"Document-{i:D3}.txt"), "Sample " + i);
            AddNavigatorTab(fixture);
            await Wait(() => TabHost.Content is NavigatorPage p && p.ViewModel.AddressText == fixture && p.IsLoaded && !p.ViewModel.IsLoading && p.ViewModel.ItemCount == 320, "fixture load");
            var navigator = (NavigatorPage)TabHost.Content;
            var surface = (FileDetailsSurface)navigator.FindName("FileSurface");
            var scroller = (ScrollViewer)surface.FindName("Scroller");
            surface.SetLayout(FileLayoutKind.List);
            await Wait(() => scroller.ScrollableWidth > 0 && PolishDescendants(surface).OfType<FileRow>().Count(r => r.EntryId >= 0) > 10, "list layout");
            await Task.Delay(300);
            var rows = PolishDescendants(surface).OfType<FileRow>().Where(r => r.EntryId >= 0).OrderBy(r => r.ViewIndex).ToArray();
            Require(rows.Length < 320, "List is not virtualized.");
            var first = rows.First();
            var second = rows.Single(r => r.ViewIndex == first.ViewIndex + 1);
            var firstPoint = first.TransformToVisual(surface).TransformPoint(new Point());
            var secondPoint = second.TransformToVisual(surface).TransformPoint(new Point());
            Require(Math.Abs(firstPoint.X - secondPoint.X) < 1 && secondPoint.Y > firstPoint.Y, "List is not column-major.");
            Require(rows.Any(r => r.TransformToVisual(surface).TransformPoint(new Point()).X > firstPoint.X + 100), "List does not wrap to another column.");
            Require(((FrameworkElement)surface.FindName("DetailsHeader")).Visibility == Visibility.Collapsed, "List retained details header.");
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                await Task.Delay(220);
                await Capture(Content, $"compact-list-{theme}.png");
            }
            var nearEnd = 0;
            surface.NearEndReached += (_, _) => nearEnd++;
            Require(surface.TrySelectByName("Document-319.txt"), "Cannot select last list item.");
            await Wait(() => scroller.HorizontalOffset > 0, "list reveal");
            await Wait(() => nearEnd > 0, "horizontal search pagination");
            var savedOffset = surface.ScrollOffset;
            scroller.ChangeView(0, 0, null, true);
            await Task.Delay(100);
            surface.RestoreScrollOffset(savedOffset);
            await Wait(() => Math.Abs(scroller.HorizontalOffset - savedOffset) < 2, "horizontal scroll restoration");
            report["ListColumnMajorVirtualizationAndReveal"] = true;

            foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.Grid, FileLayoutKind.List })
            {
                surface.SetLayout(layout);
                await Task.Delay(200);
                Require(surface.TrySelectByName("Document-010.txt"), "Rename fixture not found.");
                surface.BeginInlineRename();
                await Wait(() => surface.IsRenaming, "rename start");
                var editor = (TextBox)Field(surface, "_renameEditor")!;
                editor.Text = "Blur-" + layout + ".txt";
                surface.CommitRenameOutside(editor);
                Require(surface.IsRenaming, "Click inside rename editor cancelled it.");
                surface.CommitRenameOutside(scroller);
                await Wait(() => !surface.IsRenaming && File.Exists(Path.Combine(fixture, "Blur-" + layout + ".txt")), "outside rename commit");
                File.Move(Path.Combine(fixture, "Blur-" + layout + ".txt"), Path.Combine(fixture, "Document-010.txt"));
                navigator.ViewModel.Refresh();
                await Wait(() => !navigator.ViewModel.IsLoading && surface.TrySelectByName("Document-010.txt"), "blur rename reset");
                surface.BeginInlineRename();
                await Wait(() => surface.IsRenaming, "rename restart");
                ((TextBox)Field(surface, "_renameEditor")!).Text = "Confirmed-" + layout + ".txt";
                await (Task)Call(surface, "CommitInlineRenameAsync", 0)!;
                await Wait(() => File.Exists(Path.Combine(fixture, "Confirmed-" + layout + ".txt")), "rename commit");
                File.Move(Path.Combine(fixture, "Confirmed-" + layout + ".txt"), Path.Combine(fixture, "Document-010.txt"));
                navigator.ViewModel.Refresh();
                await Wait(() => !navigator.ViewModel.IsLoading && surface.TrySelectByName("Document-010.txt"), "rename reset");
            }
            report["RenameOutsideCommitInsideEditAndCommitAllViews"] = true;

            surface.SetLayout(FileLayoutKind.Details);
            var actions = (PaneFileActions)Field(navigator, "_fileActions")!;
            foreach (var command in NewDocumentCommands.All)
            {
                await actions.RunAsync(command);
                await Wait(() => surface.IsRenaming, "new document rename " + command);
                var path = surface.SelectedPaths().Single();
                var kind = NewDocumentCommands.Kind(command);
                var createdBytes = File.ReadAllBytes(path);
                Require(Path.GetExtension(path) == NewDocumentTemplate.Extension(kind), "Wrong new document type.");
                Require(File.ReadAllBytes(path).SequenceEqual(NewDocumentTemplate.Content(kind)) || kind is NewDocumentKind.Word or NewDocumentKind.Spreadsheet or NewDocumentKind.Presentation, "Document template was not written.");
                surface.CommitRenameOutside(scroller);
                await Wait(() => !surface.IsRenaming && !PaneFileActions.IsBusy, "initial rename completion");
                Require(File.Exists(path), "Completing initial rename removed the newly created document.");
                await actions.ApplyUndoAsync(false);
                await Wait(() => !File.Exists(path), "undo new document");
                await actions.ApplyUndoAsync(true);
                await Wait(() => File.Exists(path), "redo new document");
                Require(File.ReadAllBytes(path).SequenceEqual(createdBytes), "Redo lost document content.");
            }
            report["NineNewDocumentTypesAndUndoRedo"] = true;

            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Light);
            var counts = new Dictionary<AppCommandId, int>();
            var layoutModel = FileContextMenuBuilder.BuildLayout(CommandContext.SingleFile with { ShareAvailable = true });
            var menu = FileContextFlyout.Create(layoutModel, id => counts[id] = counts.GetValueOrDefault(id) + 1);
            foreach (var command in layoutModel.Primary.Where(command => command.Enabled))
            {
                FileContextFlyout.ShowAt(menu, surface, new Point(30, 0));
                await Task.Delay(140);
                Require(_inputPopups.Count > 0, "Popup did not suspend caption hit testing.");
                var regions = InputNonClientPointerSource.GetForWindowId(AppWindow.Id).GetRegionRects(NonClientRegionKind.Passthrough);
                Require(regions.Any(r => r.X == 0 && r.Y == 0 && r.Width >= Content.XamlRoot.Size.Width * Content.XamlRoot.RasterizationScale - 1), "Popup commands can intersect the native caption.");
                var button = PolishDescendants((FrameworkElement)menu.Content).OfType<Button>().First(b => (b.Tag as AppCommandId?) == command.Id);
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(100);
                Require(counts.GetValueOrDefault(command.Id) == 1, "Context command did not run exactly once: " + command.Id);
            }
            await Task.Delay(150);
            Require(_inputPopups.Count == 0, "Popup caption region was not restored.");
            var newMenu = FileContextFlyout.Create(FileContextMenuBuilder.BuildLayout(CommandContext.SingleFile with
                { IsBackground = true, FolderPath = fixture, IsFolderWritable = true }), _ => { });
            FileContextFlyout.ShowAt(newMenu, surface, new Point(100, 100));
            await Task.Delay(160);
            await CapturePopupAsync((FrameworkElement)newMenu.Content, "new-menu-Light.png");
            var newButton = PolishDescendants((FrameworkElement)newMenu.Content).OfType<Button>()
                .First(b => PolishDescendants(b).OfType<TextBlock>().Any(t => t.Text == Localization.StringTable.Get("Menu_New")));
            ((IInvokeProvider)new ButtonAutomationPeer(newButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(180);
            var newPresenter = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(surface.XamlRoot)
                .SelectMany(p => PolishDescendants(p.Child)).OfType<MenuFlyoutPresenter>().First();
            Require(PolishDescendants(newPresenter).OfType<MenuFlyoutItem>().Count() == 10, "New submenu does not contain folder and nine documents.");
            await CapturePopupAsync(newPresenter, "new-types-Light.png");
            newMenu.Hide();
            report["ContextCommandsAndCaptionPassthrough"] = true;
            report["ExternalApplications"] = await RunExternalLaunchSmokeAsync();
            report["Passed"] = true;
        }
        catch (Exception error) { report["Passed"] = false; report["Error"] = error.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "compatibility-polish-smoke.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
        static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
        static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
        static async Task Wait(Func<bool> condition, string step)
        { for (var i = 0; i < 160; i++) { if (condition()) return; await Task.Delay(50); } throw new TimeoutException(step); }
    }
}
#endif
