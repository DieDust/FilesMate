#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunFileConflictChecksAsync(FrameworkElement host, string fixture, Dictionary<string, object> report)
    {
        var samples = new List<object>();
        var longName = "这是一份名称比较长的文件用于确认操作卡片和来源目标信息的边缘始终对齐.txt";
        var incoming = Directory.CreateDirectory(Path.Combine(fixture, "来源文件夹")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(fixture, "目标文件夹")).FullName;
        foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
        foreach (var narrow in new[] { false, true })
        {
            await App.AppearanceViewModel!.SetThemeAsync(theme);
            var scale = host.XamlRoot.RasterizationScale;
            AppWindow.Resize(new((int)((narrow ? 420 : 760) * scale), (int)((narrow ? 520 : 740) * scale)));
            await Task.Delay(150);
            var conflict = new FileConflict(Path.Combine(incoming, longName), Path.Combine(destination, longName), false,
                Path.GetFileNameWithoutExtension(longName) + " (2).txt") { CanReplace = true, IsBatch = true };
            var pending = FileConflictDialog.For(host)(conflict, CancellationToken.None);
            var dialog = await Dialog();
            try
            {
                dialog.UpdateLayout();
                await CaptureCard(dialog, $"conflict-cards-{theme}-{(narrow ? "narrow" : "wide")}.png");
                var body = (FileConflictBody)dialog.Content;
                var locations = Descendants(body).OfType<Border>().Single(element => AutomationProperties.GetAutomationId(element) == "ConflictLocations");
                var cards = Descendants(body).OfType<Button>().Where(element => AutomationProperties.GetAutomationId(element)
                    is "ConflictReplace" or "ConflictSkip" or "ConflictKeepBoth").ToArray();
                Require(cards.Length == 3, "Conflict action cards missing");
                var origin = locations.TransformToVisual(body).TransformPoint(new());
                var actionScroll = Descendants(body).OfType<ScrollViewer>().Single(element => AutomationProperties.GetAutomationId(element) == "ConflictActions");
                foreach (var card in cards)
                {
                    var point = card.TransformToVisual(body).TransformPoint(new());
                    Require(Math.Abs(point.X - origin.X) < 1 && Math.Abs(card.ActualWidth - locations.ActualWidth) < 1,
                        "Conflict locations and action cards are not aligned/full width");
                    var labels = ((Grid)card.Content).Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().ToArray();
                    Require(card.ActualHeight >= 63 && labels.Length == 2 && labels.All(text => text.ActualHeight > 0 && !string.IsNullOrWhiteSpace(text.Text)),
                        $"Conflict action description is not visible: height={card.ActualHeight}, labels={labels.Length}");
                    var boundsInScroll = card.TransformToVisual(actionScroll).TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
                    Require(boundsInScroll.Y >= -1 && boundsInScroll.Bottom <= actionScroll.ViewportHeight + 1,
                        $"Conflict action card is clipped: action={AutomationProperties.GetAutomationId(card)}, bounds={boundsInScroll}, viewport={actionScroll.ViewportHeight}, body={body.ActualHeight}, maximum={body.MaxHeight}");
                    var frame = Descendants(card).OfType<Border>().Single(element => element.Name == "ActionFrame");
                    var normalFill = ((SolidColorBrush)frame.Background).Color;
                    Require(Contrast(Argb(normalFill), Argb(((SolidColorBrush)frame.BorderBrush).Color)) >= 3,
                        "Conflict card outline merges with its fill");
                    if (theme == AppThemeKind.Dark)
                        Require(Contrast(Argb(normalFill), SkinPalette.DarkSkin.Card) >= 1.2,
                            "Conflict card fill disappears into the dark dialog background");
                    Require(VisualStateManager.GoToState(card, "PointerOver", false), "Conflict card has no hover state");
                    Require(((SolidColorBrush)frame.Background).Color != normalFill
                        && Contrast(Argb(((SolidColorBrush)frame.Background).Color), Argb(((SolidColorBrush)frame.BorderBrush).Color)) >= 3
                        && Contrast(Argb(((SolidColorBrush)frame.Background).Color), Argb(((SolidColorBrush)labels[1].Foreground).Color)) >= 4.5,
                        "Conflict card hover does not have distinct, legible colors");
                    VisualStateManager.GoToState(card, "Normal", false);
                }
                if (!narrow)
                {
                    VisualStateManager.GoToState(cards[0], "PointerOver", false); await Task.Delay(80);
                    await CaptureCard(dialog, $"conflict-cards-{theme}-hover.png");
                    VisualStateManager.GoToState(cards[0], "Normal", false);
                }
                var bounds = dialog.TransformToVisual(Content).TransformBounds(new Rect(0, 0, dialog.ActualWidth, dialog.ActualHeight));
                Require(bounds.X >= -1 && bounds.Y >= -1 && bounds.Right <= host.XamlRoot.Size.Width + 1
                    && bounds.Bottom <= host.XamlRoot.Size.Height + 1, "Conflict dialog extends beyond the window");
                var apply = Descendants(dialog).OfType<CheckBox>().Single(element => AutomationProperties.GetAutomationId(element) == "ConflictApplyRemaining");
                Require(apply.IsChecked == true, "Ordinary batch scope was not preserved");
                apply.IsChecked = !narrow;
                var sample = new { Theme = theme.ToString(), Narrow = narrow, Width = body.ActualWidth, CardWidth = cards[0].ActualWidth, Viewport = host.XamlRoot.Size };
                Choose(dialog, FileConflictAction.KeepBoth);
                var choice = await pending.WaitAsync(TimeSpan.FromSeconds(10));
                Require(choice.Action == FileConflictAction.KeepBoth && choice.ApplyToAll == !narrow, "Card returned the wrong action/scope");
                samples.Add(sample);
            }
            finally { if (dialog.IsLoaded) dialog.Hide(); }
        }
        var source = Path.Combine(incoming, "asset-manifest.json");
        var target = Path.Combine(destination, "asset-manifest.json");
        File.WriteAllText(source, "incoming version"); File.WriteAllText(target, "original version");
        foreach (var cancel in new[] { true, false })
        {
            var budget = new ReplacementBackupBudget(0, 0);
            var pending = WindowsFileTransfer.RunAsync(new WindowsLocalFileOperations(), [new(source, target)], false,
                FileConflictDialog.For(host), backupBudget: budget);
            var dialog = await Dialog();
            try
            {
                Require(dialog.DefaultButton == ContentDialogButton.None && File.ReadAllText(target) == "original version", "Replacement ran before a user choice");
                Require(Descendants(dialog).OfType<TextBlock>().Any(text => text.Text == Localization.StringTable.Get("Conflict_BackupUnavailable")),
                    "Undo/history warning is missing from the initial dialog");
                if (!cancel) await CaptureCard(dialog, "conflict-replace-without-undo.png");
                Choose(dialog, cancel ? FileConflictAction.Cancel : FileConflictAction.ReplaceWithoutUndo);
                var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
                Require(result.Errors.Count == 0 && result.Undo is null && budget.UsedBytes == 0, "Unexpected transfer failure or backup");
                Require(cancel ? result.Cancelled && File.ReadAllText(target) == "original version"
                    : result.WithoutUndo == 1 && File.ReadAllText(target) == "incoming version", "Single-choice replacement/cancellation failed");
                Require(!VisualTreeHelper.GetOpenPopupsForXamlRoot(host.XamlRoot).SelectMany(popup => Descendants(popup.Child))
                    .OfType<ContentDialog>().Any(element => element.IsLoaded), "A second confirmation remained open");
            }
            finally { if (dialog.IsLoaded) dialog.Hide(); }
        }
        foreach (var applyToRemaining in new[] { false, true })
        {
            var pairs = Enumerable.Range(0, 2).Select(index => new FilePathPair(Path.Combine(incoming, $"batch-{index}.txt"),
                Path.Combine(destination, $"batch-{index}.txt"))).ToArray();
            foreach (var pair in pairs) { File.WriteAllText(pair.Source, "incoming"); File.WriteAllText(pair.Destination, "original"); }
            var pending = WindowsFileTransfer.RunAsync(new WindowsLocalFileOperations(), pairs, false, FileConflictDialog.For(host),
                backupBudget: new ReplacementBackupBudget(0, 0));
            var prompts = 0;
            for (var index = 0; index < (applyToRemaining ? 1 : 2); index++)
            {
                var dialog = await Dialog(); prompts++;
                try
                {
                    var scope = Descendants(dialog).OfType<CheckBox>().Single(element => AutomationProperties.GetAutomationId(element) == "ConflictApplyRemaining");
                    Require(scope.IsChecked == false, "Unprotected replacement silently opted into remaining conflicts");
                    scope.IsChecked = applyToRemaining;
                    Choose(dialog, FileConflictAction.ReplaceWithoutUndo);
                    for (var attempt = 0; dialog.IsLoaded && attempt < 100; attempt++) await Task.Delay(20);
                }
                finally { if (dialog.IsLoaded) dialog.Hide(); }
            }
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Require(result.Errors.Count == 0 && result.WithoutUndo == 2 && result.Undo is null
                && pairs.All(pair => File.ReadAllText(pair.Destination) == "incoming"), "Batch replacement scope/result failed");
            samples.Add(new { BatchApplyRemaining = applyToRemaining, Prompts = prompts });
        }
        report["FileConflicts"] = new { LayoutCases = samples, ReplacementAfterOneChoice = true, FirstDialogCancellation = true, WarningVisibleBeforeChoice = true, BatchScopeRespected = true };

        async Task<ContentDialog> Dialog()
        {
            for (var attempt = 0; attempt < 150; attempt++)
            {
                var found = VisualTreeHelper.GetOpenPopupsForXamlRoot(host.XamlRoot).SelectMany(popup => Descendants(popup.Child))
                    .OfType<ContentDialog>().FirstOrDefault(element => element.IsLoaded);
                if (found is not null) { await Task.Delay(350); return found; }
                await Task.Delay(40);
            }
            throw new TimeoutException("Conflict dialog did not open");
        }
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
        static Task CaptureCard(ContentDialog dialog, string name) => Capture(
            Descendants(dialog).OfType<Border>().Single(element => element.Name == "BackgroundElement"), name);
        static uint Argb(Windows.UI.Color color) => (uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
        static double Contrast(uint first, uint second)
        {
            static double Luminance(uint color)
            {
                static double Linear(uint channel)
                { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
                return .2126 * Linear((color >> 16) & 255) + .7152 * Linear((color >> 8) & 255) + .0722 * Linear(color & 255);
            }
            var a = Luminance(first); var b = Luminance(second);
            return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
        }
        static void Choose(ContentDialog dialog, FileConflictAction action)
        {
            var button = Descendants(dialog).OfType<Button>().First(element => AutomationProperties.GetAutomationId(element)
                is var id && (id == "Conflict" + action || id == "ConflictChoose" + action));
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        }
        static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
    }
}
#endif
