#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunTransferRedesignSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        var retained = new List<FileUndoRecord>();
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            await Wait(() => Content is FrameworkElement { IsLoaded: true }, "window");
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } page && !page.ViewModel.IsLoading, "navigator settled");
            await Task.Delay(300);
            AppWindow.Move(new(-12000, -12000)); Activate();
            await App.AppearanceViewModel!.SetThemeAsync(AppThemeKind.Light);
            var host = (FrameworkElement)Content;
            var root = Path.Combine(AppContext.BaseDirectory, "transfer-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var operations = new WindowsLocalFileOperations();
            string Write(string relative, string content)
            {
                var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content); return path;
            }
            Task<ShelfTransferResult> Move(string[] paths, string target) => FileShelfTransfer.RunAsync(operations, paths, target, true,
                resolveConflict: FileConflictDialog.For(host));
            void Keep(ShelfTransferResult result) { if (result.Undo is { } undo) retained.Add(undo); }

            var source = Write("single/source/metadata.json", "{\n  \"name\": \"FilesMate\",\n  \"version\": 2,\n  \"preview\": true\n}\n");
            var target = Write("single/target/metadata.json", "{\n  \"name\": \"FilesMate\",\n  \"version\": 1\n}\n");
            var copy = await FileShelfTransfer.RunAsync(operations, [source], Path.GetDirectoryName(source)!, false,
                allowSameDirectoryCopy: true,
                resolveConflict: (_, _) => throw new InvalidOperationException("Copying a file onto itself asked for a conflict decision."));
            Require(copy.Completed.Count == 1 && copy.Errors.Count == 0 && File.Exists(source), "copy duplicate");
            report["SameFolderCopyAutoNumbersWithoutPrompt"] = true;
            var pending = Move([source], Path.GetDirectoryName(target)!);
            var dialog = await Dialog(); await Task.Delay(300);
            var focused = FocusManager.GetFocusedElement(host.XamlRoot) as DependencyObject;
            report["InitialFocus"] = focused is null ? "none" : focused.GetType().Name + ":" + AutomationProperties.GetAutomationId(focused);
            Require(focused is Button && AutomationProperties.GetAutomationId(focused) == "ConflictChooseCancel", "initial focus must be cancel");
            var body = PolishDescendants(dialog).OfType<FileConflictBody>().Single();
            var keepBothButton = PolishDescendants(dialog).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "ConflictKeepBoth");
            Require(keepBothButton.TransformToVisual(body).TransformPoint(new(0, keepBothButton.ActualHeight)).Y <= body.ActualHeight + 1,
                "a main conflict action is outside the visible body");
            var replaceButton = PolishDescendants(dialog).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "ConflictReplace");
            var skipButton = PolishDescendants(dialog).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "ConflictSkip");
            Require(skipButton.TransformToVisual(body).TransformPoint(default).Y >= replaceButton.TransformToVisual(body).TransformPoint(new(0, replaceButton.ActualHeight)).Y,
                "main conflict choices must be vertical");
            report["VerticalActionsWithExplorerWording"] = true;
            var cancelButton = PolishDescendants(dialog).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "ConflictChooseCancel");
            var footerBand = body.Children.OfType<Border>().Single(border => Grid.GetRow(border) == 2);
            var cardBorder = PolishDescendants(dialog).OfType<Border>().Single(border => border.Name == "BackgroundElement");
            var above = cancelButton.TransformToVisual(footerBand).TransformPoint(default).Y - footerBand.BorderThickness.Top;
            var below = cardBorder.ActualHeight - cancelButton.TransformToVisual(cardBorder).TransformPoint(new(0, cancelButton.ActualHeight)).Y - cardBorder.BorderThickness.Bottom;
            report["FooterButtonInsets"] = new { Above = above, Below = below };
            Require(above >= 0 && below <= 9.1
                && cancelButton.TransformToVisual(body).TransformPoint(new(0, cancelButton.ActualHeight)).Y <= body.ActualHeight + 1,
                "footer cancel button needs a compact inset and must remain visible");
            await CaptureCard(dialog, "transfer-single-light.png");
            Click(dialog, "ConflictCompare");
            await Wait(() => PolishDescendants(dialog).OfType<ListView>().Any(l => l.Items.Count > 0), "text diff");
            Require(PolishDescendants(dialog).OfType<ListView>().Single().ActualHeight >= 64, "diff area too short");
            var bounds = new List<object>();
            for (DependencyObject? ancestor = body; ancestor is FrameworkElement element; ancestor = VisualTreeHelper.GetParent(ancestor))
                bounds.Add(new { Type = element.GetType().Name, element.Name, element.ActualHeight,
                    Viewport = element is ScrollViewer ancestorScroll ? ancestorScroll.ViewportHeight : 0 });
            report["ComparisonLayout"] = bounds;
            Require(PolishDescendants(dialog).OfType<TextBlock>().Any(t => t.Text.Contains("version")), "diff not rendered");
            await Task.Delay(180);
            var initialLines = PolishDescendants(dialog).OfType<ListView>().Single();
            var originalVersion = PolishDescendants(initialLines).OfType<TextBlock>().Single(t => t.Text.Contains("\"version\": 1"));
            var newVersion = PolishDescendants(initialLines).OfType<TextBlock>().Single(t => t.Text.Contains("\"version\": 2"));
            Require(originalVersion.TransformToVisual(initialLines).TransformPoint(default).X < initialLines.ActualWidth / 2
                && newVersion.TransformToVisual(initialLines).TransformPoint(default).X > initialLines.ActualWidth / 2, "original must be left; new must be right");
            var originalCell = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(originalVersion));
            var newCell = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(newVersion)));
            Require(originalCell.Background is SolidColorBrush { Color.A: 0 } && newCell.Background is SolidColorBrush { Color.A: > 0 }, "change highlights must be on the new side only");
            report["OriginalLeftNewRightWithOneSidedHighlight"] = true;
            var toolbarMetrics = new List<object>(); report["ComparisonToolbarMetrics"] = toolbarMetrics;
            foreach (var tabButton in PolishDescendants(dialog).OfType<Button>().Where(b => AutomationProperties.GetAutomationId(b) is "ConflictTextChanges" or "ConflictPreview" or "ConflictVerify"))
            {
                var text = PolishDescendants(tabButton).OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));
                var natural = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, UseLayoutRounding = false };
                natural.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                toolbarMetrics.Add(new { text.Text, text.FontSize, text.ActualHeight, NaturalHeight = natural.DesiredSize.Height, ButtonHeight = tabButton.ActualHeight });
                Require(!text.IsTextTrimmed && text.ActualHeight + 1 >= natural.DesiredSize.Height
                    && tabButton.ActualHeight >= natural.DesiredSize.Height + 8, "comparison toolbar clips text");
            }
            report["ComparisonToolbarTextFits"] = true;
            await Task.Delay(160); await CaptureCard(dialog, "transfer-compare-light.png");
            Choose(dialog, FileConflictAction.KeepBoth);
            var single = await pending.WaitAsync(TimeSpan.FromSeconds(15)); Keep(single);
            Require(single.Completed.Count == 1 && single.Errors.Count == 0 && !File.Exists(source), "keep both move");
            Require(Path.GetFileName(single.Completed[0].Destination) == "metadata (2).json", "duplicate collision allocation");
            Require(File.ReadAllText(target).Contains("\"version\": 1"), "keep both damaged target");
            report["SingleCompareAndKeepBoth"] = true;

            var incomingLines = new List<string>(); var existingLines = new List<string>();
            for (var i = 0; i < 60; i++)
            {
                if (i % 10 == 1) incomingLines.Add($"新增设置 {i}");
                if (i % 10 == 3) existingLines.Add($"删除旧设置 {i}");
                incomingLines.Add(i % 10 == 5 ? $"修改后的设置 {i}" : $"共同内容 {i}");
                existingLines.Add(i % 10 == 5 ? $"修改前的设置 {i}" : $"共同内容 {i}");
            }
            const string lastIncoming = "最后一行 · 完整显示";
            incomingLines.Add(lastIncoming); existingLines.Add("最后一行 · 原内容");
            var scrollSource = Write("scroll/source/settings.txt", string.Join('\n', incomingLines));
            var scrollTarget = Write("scroll/target/settings.txt", string.Join('\n', existingLines));
            pending = Move([scrollSource], Path.GetDirectoryName(scrollTarget)!);
            dialog = await Dialog(); Click(dialog, "ConflictCompare");
            await Wait(() => PolishDescendants(dialog).OfType<ListView>().Any(l => l.Items.Count > 20), "scrollable diff");
            var lines = PolishDescendants(dialog).OfType<ListView>().Single();
            Require(new[] { "Added", "Deleted", "Modified" }.All(kind => lines.Items.Cast<FileComparisonLine>().Any(row => row.Change == kind)), "diff semantic classification");
            await Task.Delay(240);
            var removed = PolishDescendants(lines).OfType<TextBlock>().Where(t => t.Text == "删除旧设置 3").ToArray();
            Require(removed.Length == 2 && removed.Count(t => t.TextDecorations == Windows.UI.Text.TextDecorations.Strikethrough) == 1,
                "deletion must remain plain on the original and be struck out on the new side");
            await CaptureCard(dialog, "transfer-semantic-light.png");
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Dark);
            await Task.Delay(240); await CaptureCard(dialog, "transfer-semantic-dark.png");
            var scroll = PolishDescendants(lines).OfType<ScrollViewer>().Single();
            Require(scroll.ScrollableHeight > 0, "diff does not expose vertical scroll");
            lines.ScrollIntoView(lines.Items.Last(), ScrollIntoViewAlignment.Leading);
            await Wait(() => scroll.VerticalOffset > 0 && PolishDescendants(lines).OfType<TextBlock>().Any(t => t.Text == lastIncoming), "diff bottom reachable");
            await Task.Delay(200);
            // Virtualized rows refine the extent after realization, as with a user
            // continuing to scroll to the end after a long wrapped row appears.
            scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
            await Task.Delay(160);
            var last = PolishDescendants(lines).OfType<TextBlock>().Single(t => t.Text == lastIncoming);
            var endOfLast = last.TransformToVisual(lines).TransformPoint(new(0, last.ActualHeight)).Y;
            report["DiffScroll"] = new { scroll.VerticalOffset, scroll.ScrollableHeight, LastLineBottom = endOfLast, Viewport = lines.ActualHeight };
            Require(!last.IsTextTrimmed && endOfLast <= lines.ActualHeight + 1, "last line clipped by footer");
            await CaptureCard(dialog, "transfer-scroll-bottom-dark.png");
            Choose(dialog, FileConflictAction.Cancel);
            var scrollResult = await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Require(scrollResult.Cancelled && File.Exists(scrollSource) && File.ReadAllText(scrollTarget) == string.Join('\n', existingLines), "scroll changed files");
            report["SemanticColorsAndLastLineAccessible"] = true;
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Light);

            var a = Write("batch/source/a.txt", "new a"); var b = Write("batch/source/b.txt", "new b");
            var ta = Write("batch/target/a.txt", "old a"); var tb = Write("batch/target/b.txt", "old b");
            pending = Move([a, b], Path.GetDirectoryName(ta)!);
            dialog = await Dialog(); await Task.Delay(180);
            Require(PolishDescendants(dialog).OfType<TextBlock>().Any(t => t.Text == StringTable.Get("Conflict_ReplaceFile")), "batch actions");
            await CaptureCard(dialog, "transfer-batch-light.png");
            Click(dialog, "ConflictCompare");
            PolishDescendants(dialog).OfType<CheckBox>().Single(box => AutomationProperties.GetAutomationId(box) == "ConflictApplyRemaining").IsChecked = false;
            Choose(dialog, FileConflictAction.Replace);
            var firstDialog = dialog;
            dialog = await Dialog(firstDialog);
            await Wait(() => PolishDescendants(dialog).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "ConflictChooseReplace"), "individual comparison persists");
            Choose(dialog, FileConflictAction.Skip);
            var batch = await pending.WaitAsync(TimeSpan.FromSeconds(15)); Keep(batch);
            Require(batch.Completed.Count == 1 && batch.Skipped == 1 && batch.Errors.Count == 0, "mixed decisions");
            Require(!File.Exists(a) && File.Exists(b) && File.ReadAllText(tb) == "old b", "skipped source/target");
            FileUndoApplier.Undo(operations, batch.Undo!);
            Require(File.ReadAllText(a) == "new a" && File.ReadAllText(ta) == "old a", "replacement undo");
            FileUndoApplier.Redo(operations, batch.Undo!);
            Require(!File.Exists(a) && File.ReadAllText(ta) == "new a", "replacement redo");
            report["BatchIndividualDecisionsAndUndoRedo"] = true;

            var allA = Write("all/source/a.txt", "new a"); var allB = Write("all/source/b.txt", "new b");
            var allTarget = Write("all/target/a.txt", "old a"); Write("all/target/b.txt", "old b");
            pending = Move([allA, allB], Path.GetDirectoryName(allTarget)!);
            dialog = await Dialog(); Click(dialog, "ConflictReplace");
            var all = await pending.WaitAsync(TimeSpan.FromSeconds(15)); Keep(all);
            Require(all.Completed.Count == 2 && all.Errors.Count == 0 && all.Undo?.Replacements.Count == 2, "replace all");
            report["BatchReplaceAll"] = true;

            var changing = Write("changed/source/a.txt", "new"); var changingTarget = Write("changed/target/a.txt", "old");
            pending = Move([changing], Path.GetDirectoryName(changingTarget)!);
            dialog = await Dialog(); Click(dialog, "ConflictCompare");
            await Wait(() => PolishDescendants(dialog).OfType<ListView>().Any(l => l.Items.Count > 0), "comparison done");
            File.WriteAllText(changingTarget, "edited during comparison");
            Choose(dialog, FileConflictAction.Replace);
            firstDialog = dialog; dialog = await Dialog(firstDialog);
            Require(PolishDescendants(dialog).OfType<TextBlock>().Any(t => t.Text == StringTable.Get("Conflict_Changed")), "changed file notice");
            dialog.Hide(); var cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Require(cancelled.Cancelled && File.Exists(changing) && File.ReadAllText(changingTarget) == "edited during comparison", "cancel after changed file");
            report["ChangedFileRequiresFreshDecision"] = true;

            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Dark);
            var pngA = Path.Combine(root, "images/source/photo.png"); var pngB = Path.Combine(root, "images/target/photo.png");
            await ImageFixture(pngA, false); await ImageFixture(pngB, true);
            pending = Move([pngA], Path.GetDirectoryName(pngB)!);
            dialog = await Dialog(); Click(dialog, "ConflictCompare");
            await Wait(() => PolishDescendants(dialog).OfType<Image>().Count(i => i.Name == "ImageContent" && i.Source is not null) == 2, "paired image preview");
            await Task.Delay(200); await CaptureCard(dialog, "transfer-images-dark.png");
            Require(PolishDescendants(dialog).OfType<Image>().Where(i => i.Name == "ImageContent").All(i => i.ActualHeight >= 64), "image preview clipped");
            dialog.Hide(); cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Require(cancelled.Cancelled && File.Exists(pngA) && File.Exists(pngB), "cancel image comparison");
            report["PairedImagePreviewAndCancel"] = true;

            var batchPairs = Enumerable.Range(0, 3).Select(i => new FilePathPair(Path.Combine(root, $"image-batch/source/photo-{i}.png"),
                Path.Combine(root, $"image-batch/target/photo-{i}.png"))).ToArray();
            foreach (var pair in batchPairs) { await ImageFixture(pair.Source, false); await ImageFixture(pair.Destination, true); }
            var batchPending = WindowsFileTransfer.RunAsync(operations, batchPairs, false, FileConflictDialog.For(host));
            ContentDialog? previousBatchDialog = null;
            for (var index = 0; index < batchPairs.Length; index++)
            {
                var batchDialog = await Dialog(previousBatchDialog);
                if (index == 0) Click(batchDialog, "ConflictCompare");
                await Wait(() => PolishDescendants(batchDialog).OfType<Image>().Count(i => i.Name == "ImageContent" && i.Source is not null) == 2,
                    "batch image previews " + index);
                var applyRemaining = PolishDescendants(batchDialog).OfType<CheckBox>()
                    .Single(c => AutomationProperties.GetAutomationId(c) == "ConflictApplyRemaining");
                Require(applyRemaining.IsChecked == (index == 0), "Unchecked batch scope was reset on the next conflict");
                applyRemaining.IsChecked = false;
                Click(batchDialog, "ConflictChooseReplace");
                previousBatchDialog = batchDialog;
            }
            var batchImages = await batchPending.WaitAsync(TimeSpan.FromSeconds(15));
            if (batchImages.Undo is { } imageUndo) retained.Add(imageUndo);
            Require(!batchImages.Cancelled && batchImages.Errors.Count == 0 && batchImages.Completed.Count == 3
                && batchPairs.All(p => File.ReadAllBytes(p.Source).SequenceEqual(File.ReadAllBytes(p.Destination))),
                "Individual batch preview decisions failed");
            report["ThreeSequentialImagePreviewsAndUncheckedScope"] = true;

            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Light);
            var budgetSource = Write("budget/source/a.txt", "incoming"); var budgetTarget = Write("budget/target/a.txt", "existing");
            var operation = WindowsFileTransfer.RunAsync(operations, [new(budgetSource, budgetTarget)], true,
                FileConflictDialog.For(host), backupBudget: new ReplacementBackupBudget(0, 0));
            dialog = await Dialog();
            Require(dialog.DefaultButton == ContentDialogButton.None && File.ReadAllText(budgetTarget) == "existing"
                && PolishDescendants(dialog).OfType<TextBlock>().Any(text => text.Text == StringTable.Get("Conflict_BackupUnavailable")),
                "irreversible warning must precede the first choice");
            dialog.Hide(); var unprotected = await operation.WaitAsync(TimeSpan.FromSeconds(15));
            Require(unprotected.Cancelled && File.Exists(budgetSource), "cancel unprotected replacement");
            report["BackupLimitCancellationFromFirstDialog"] = true;

            var normalSize = AppWindow.Size;
            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
            var minimumWidth = presenter.PreferredMinimumWidth;
            // Detached file windows already support a 640-pixel minimum.
            presenter.PreferredMinimumWidth = 640;
            AppWindow.Resize(new((int)(500 * host.XamlRoot.RasterizationScale), normalSize.Height));
            await Wait(() => host.XamlRoot.Size.Width < 540, "narrow window");
            var longName = "project-release-notes-and-review-comments-with-a-long-filename.txt";
            var longLine = "incoming: " + string.Concat(Enumerable.Repeat("这是一段需要完整显示的修改内容，不能只留下省略号。", 8));
            var narrowSource = Write("narrow/source/" + longName, longLine + "\nsecond line");
            var narrowTarget = Write("narrow/target/" + longName, "existing\nsecond line");
            pending = Move([narrowSource], Path.GetDirectoryName(narrowTarget)!);
            dialog = await Dialog(); Click(dialog, "ConflictCompare");
            await Wait(() => PolishDescendants(dialog).OfType<ListView>().Any(l => l.Items.Count > 0), "narrow text diff");
            await Task.Delay(180);
            body = PolishDescendants(dialog).OfType<FileConflictBody>().Single();
            var wrapped = PolishDescendants(dialog).OfType<TextBlock>().Single(t => t.Text == longLine);
            Require(!wrapped.IsTextTrimmed && wrapped.TextWrapping == TextWrapping.Wrap && wrapped.ActualHeight > 30, "long diff line was truncated");
            foreach (var button in PolishDescendants(dialog).OfType<Button>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("ConflictChoose")))
            {
                var end = button.TransformToVisual(body).TransformPoint(new(button.ActualWidth, button.ActualHeight));
                Require(end.X <= body.ActualWidth + 1 && end.Y <= body.ActualHeight + 1, "narrow action outside body");
            }
            await CaptureCard(dialog, "transfer-compare-narrow-light.png");
            dialog.Hide(); cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Require(cancelled.Cancelled && File.Exists(narrowSource) && File.ReadAllText(narrowTarget).StartsWith("existing"), "narrow cancel");
            AppWindow.Resize(normalSize);
            presenter.PreferredMinimumWidth = minimumWidth;
            report["NarrowComparisonActionsVisible"] = true;
            report["Passed"] = true;

            async Task<ContentDialog> Dialog(ContentDialog? previous = null)
            {
                ContentDialog? found = null;
                await Wait(() => (found = VisualTreeHelper.GetOpenPopupsForXamlRoot(host.XamlRoot)
                    .SelectMany(p => PolishDescendants(p.Child)).OfType<ContentDialog>()
                    .FirstOrDefault(d => d.IsLoaded && !ReferenceEquals(previous, d))) is not null, "conflict dialog");
                return found!;
            }
        }
        catch (Exception error)
        {
            report["Passed"] = false; report["Error"] = error.ToString();
            try
            {
                var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)Content).XamlRoot).FirstOrDefault();
                if (popup?.Child is UIElement child) await Capture(child, "transfer-failure.png");
            }
            catch { }
            foreach (var dialog in VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)Content).XamlRoot)
                .SelectMany(p => PolishDescendants(p.Child)).OfType<ContentDialog>()) dialog.Hide();
            await Task.Delay(200);
        }
        finally { foreach (var replacement in retained.SelectMany(r => r.Replacements)) replacement.Dispose(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "transfer-redesign-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static void Require(bool ok, string step) { if (!ok) throw new InvalidOperationException(step); }
        static Task CaptureCard(ContentDialog dialog, string name) => Capture(
            PolishDescendants(dialog).OfType<Border>().First(b => b.Name == "BackgroundElement"), name);
        static async Task Wait(Func<bool> condition, string step)
        { for (var i = 0; i < 300; i++) { if (condition()) return; await Task.Delay(50); } throw new TimeoutException(step); }
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        static void Click(ContentDialog dialog, string id) => Invoke(PolishDescendants(dialog).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == id));
        static void Choose(ContentDialog dialog, FileConflictAction action) =>
            Click(dialog, "ConflictChoose" + action);
        static async Task ImageFixture(string path, bool original)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            var pixels = new byte[320 * 200 * 4];
            for (var y = 0; y < 200; y++) for (var x = 0; x < 320; x++)
            { var n = (y * 320 + x) * 4; pixels[n] = (byte)(original ? x * 180 / 320 : 50); pixels[n + 1] = (byte)(70 + y * 130 / 200); pixels[n + 2] = (byte)(original ? 70 : 180); pixels[n + 3] = 255; }
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Straight, 320, 200, 96, 96, pixels);
            await encoder.FlushAsync(); stream.Seek(0);
            using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size); var bytes = new byte[stream.Size]; reader.ReadBytes(bytes); File.WriteAllBytes(path, bytes);
        }
    }
}
#endif
