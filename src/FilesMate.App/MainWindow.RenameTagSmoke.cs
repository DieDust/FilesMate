#if FILESMATE_UI_TEST
using FilesMate.App.Commands;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Controls.Tags;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.App.Views;
using FilesMate.Core.Metadata;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunRenameTagSmokeAsync()
    {
        var results = new List<object>();
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1900, 1300));
            await Task.Delay(1200);
            var fixture = Path.Combine(AppContext.BaseDirectory, "rename-fixtures");
            Directory.CreateDirectory(fixture);
            var paths = new[] { "旅行照片-上海.JPG", "旅行照片-杭州.JPG", "会议纪要.docx", "预算表.xlsx", "一个很长的文件名称用来验证左右两栏的省略位置以及提示信息.pdf", "Report.txt", "report-02.txt", "report-03.txt", "report-04.txt", "report-05.txt" }
                .Select(name => Path.Combine(fixture, name)).ToArray();
            foreach (var path in paths) File.WriteAllText(path, "preview fixture");
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark, AppThemeKind.Light })
            {
                await App.AppearanceViewModel!.SetThemeAsync(theme);
                await App.AppearanceViewModel.SetBackdropAsync(BackdropKind.Solid);
                var editor = new BatchRenameDialog();
                editor.SetSources(paths);
                var dialog = editor.CreateDialog(((FrameworkElement)Content).XamlRoot);
                ContentDialogTheme.Apply(dialog, (FrameworkElement)Content);
                var showing = dialog.ShowAsync();
                await Task.Delay(650);
                if (dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Unchanged names enabled execution.");
                var applyButton = FindDescendant<Button>(dialog, b => b.Name == "PrimaryButton")!;
                if (applyButton.ActualWidth > 220) throw new InvalidOperationException("Rename action stretches across the workspace.");
                var rules = FindDescendant<StackPanel>(editor, p => p.Name == "RulesPanel")!;
                for (var parent = VisualTreeHelper.GetParent(rules); parent is not null && parent != editor; parent = VisualTreeHelper.GetParent(parent))
                    if (parent is ScrollViewer) throw new InvalidOperationException("Rules panel is independently scrollable.");
                await Capture(dialog, $"rename-{theme}-empty.png");
                var mode = FindDescendant<ComboBox>(editor, b => b.Name == "ModeBox")!;
                var baseName = FindDescendant<TextBox>(editor, b => b.Name == "BaseNameBox")!;
                mode.SelectedIndex = 1;
                baseName.Text = "项目资料";
                await Task.Delay(450);
                if (!dialog.IsPrimaryButtonEnabled || !editor.CurrentPlan.Entries[0].Target.EndsWith("项目资料 001.JPG", StringComparison.Ordinal))
                    throw new InvalidOperationException("Numbering did not update preview.");
                await Capture(dialog, $"rename-{theme}-numbering.png");
                var newName = Descendants(editor).OfType<TextBlock>().First(t => t.Text == "项目资料 001.JPG");
                var ink = ((SolidColorBrush)newName.Foreground).Color;
                var background = ((SolidColorBrush)dialog.Background).Color;
                var table = FindDescendant<Border>(editor, b => b.Name == "PreviewTable")!;
                var tableFill = ((SolidColorBrush)table.Background).Color;
                var tableLine = ((SolidColorBrush)table.BorderBrush).Color;
                if (theme == AppThemeKind.Dark && (tableFill.R > 80 || tableLine.R > 100))
                    throw new InvalidOperationException("Dark preview table retained a light fill or divider.");
                if ((theme == AppThemeKind.Dark) != (ink.R > 180 && background.R < 80))
                    throw new InvalidOperationException("Preview text or dialog background did not follow the theme.");
                var rootBrush = ((Panel)Content).Background as SolidColorBrush;
                if (rootBrush is null || (theme == AppThemeKind.Dark) != (rootBrush.Color.R < 80))
                    throw new InvalidOperationException("Solid window background retained the old theme.");
                results.Add(new { Area = "rename", Theme = theme.ToString(), editor.ActualWidth, editor.ActualHeight, Changes = editor.CurrentPlan.Entries.Count(e => e.RequiresRename) });
                baseName.Text = "bad:";
                if (dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Stale plan enabled during debounce.");
                await Task.Delay(350);
                if (dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Invalid names enabled execution.");
                await Capture(dialog, $"rename-{theme}-invalid.png");
                mode.SelectedIndex = 2;
                FindDescendant<TextBox>(editor, b => b.Name == "PrefixBox")!.Text = "归档-";
                await Task.Delay(350);
                if (!dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Prefix did not enable execution.");
                AppWindow.Resize(new Windows.Graphics.SizeInt32(1150, 1000));
                await Task.Delay(500);
                foreach (var ruleMode in new[] { 0, 1, 2, 3 })
                {
                    mode.SelectedIndex = ruleMode;
                    await Task.Delay(250);
                    var bottom = rules.TransformToVisual(editor).TransformPoint(new Windows.Foundation.Point(0, rules.ActualHeight)).Y;
                    if (bottom > editor.ActualHeight + 1)
                        throw new InvalidOperationException($"Rules are clipped in mode {ruleMode}: {bottom} > {editor.ActualHeight}.");
                }
                mode.SelectedIndex = 2;
                await Task.Delay(600);
                await Capture(dialog, $"rename-{theme}-compact.png");
                var reset = FindDescendant<Button>(editor, b => b.Name == "ResetRulesButton")!;
                ((IInvokeProvider)new ButtonAutomationPeer(reset).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(250);
                if (dialog.IsPrimaryButtonEnabled || editor.CurrentPlan.Entries.Any(e => e.RequiresRename))
                    throw new InvalidOperationException("Reset did not clear the active rename rule.");
                dialog.Hide();
                await showing;
                AppWindow.Resize(new Windows.Graphics.SizeInt32(1900, 1300));
                await Task.Delay(350);

                await using var store = new SqliteFileMetadataStore(Path.Combine(fixture, "tags-" + theme + "-" + Guid.NewGuid().ToString("N") + ".db"));
                var identity = FileIdentity.FromNormalizedPath(paths[0]);
                var otherIdentity = FileIdentity.FromNormalizedPath(paths[1]);
                var work = await store.CreateTagAsync("工作", "#5B8DEF");
                var review = await store.CreateTagAsync("待整理", "#E9A23B");
                await store.CreateTagAsync("名称很长的标签应该在容器边缘省略", "#32A873");
                await store.SetTagsAsync(identity, [work.Id]);
                await store.SetTagsAsync(otherIdentity, [review.Id]);
                var panel = TagPickerFlyout.CreatePanel(store, [identity, otherIdentity], null);
                var menu = FileContextFlyout.Create(FileContextMenuBuilder.BuildLayout(CommandContext.Multi with
                    { TagsAvailable = true, BatchRenameAvailable = true, FolderPath = fixture }), _ => { }, tagPicker: panel);
                menu.ShowAt((FrameworkElement)Content, new FlyoutShowOptions { Position = new Windows.Foundation.Point(50, 90) });
                await Task.Delay(400);
                var column = (FrameworkElement)menu.Content;
                await CapturePopupAsync(column, $"menu-{theme}.png");
                var add = Descendants(column).OfType<Button>().First(b => Descendants(b).OfType<TextBlock>().Any(t => t.Text == Localization.StringTable.Get("Command_AddTags")));
                ((IInvokeProvider)new ButtonAutomationPeer(add).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(650);
                var filter = FindDescendant<TextBox>(panel, _ => true)!;
                if (filter.ActualTheme.ToString() != theme.ToString()) throw new InvalidOperationException("Tag input theme mismatch.");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "tag-layout.json"), JsonSerializer.Serialize(new { panel.ActualWidth, panel.ActualHeight, panel.IsLoaded, menu.IsOpen }));
                await CapturePopupAsync(panel, $"tags-{theme}.png");
                results.Add(new { Area = "tags", Theme = filter.ActualTheme.ToString(), Background = (filter.Background as SolidColorBrush)?.Color.ToString(), panel.ActualWidth, panel.ActualHeight });
                var removeWork = Descendants(panel).OfType<Button>().Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b)
                    == Localization.StringTable.Get("Tag_Remove") + " " + work.Name);
                if (removeWork.Opacity != 1 || !removeWork.IsEnabled || !removeWork.IsHitTestVisible || !removeWork.IsTabStop
                    || removeWork.ActualWidth < 32 || removeWork.ActualHeight < 32)
                    throw new InvalidOperationException("A tag present on part of the selection has no usable remove target.");
                ((IInvokeProvider)new ButtonAutomationPeer(removeWork).GetPattern(PatternInterface.Invoke)).Invoke();
                await WaitForWorkTagAsync(false);
                if (!(await store.GetTagsAsync(otherIdentity)).Any(t => t.Id == review.Id) || (await store.ListTagsAsync()).Count != 3)
                    throw new InvalidOperationException("Removing a file tag changed an unrelated tag or deleted its definition.");
                var addWork = Descendants(panel).OfType<Button>().Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == work.Name);
                ((IInvokeProvider)new ButtonAutomationPeer(addWork).GetPattern(PatternInterface.Invoke)).Invoke();
                await WaitForWorkTagAsync(true);
                if (removeWork.Opacity != 1) throw new InvalidOperationException("Applying a tag did not restore its remove button.");
                ((IInvokeProvider)new ButtonAutomationPeer(removeWork).GetPattern(PatternInterface.Invoke)).Invoke();
                await WaitForWorkTagAsync(false);
                results.Add(new { Area = "tag-actions", Theme = theme.ToString(), MixedRemove = true, ApplyAll = true, RemoveAll = true, DefinitionsPreserved = true });

                async Task WaitForWorkTagAsync(bool expected)
                {
                    for (var attempt = 0; attempt < 100; attempt++)
                    {
                        var firstHas = (await store.GetTagsAsync(identity)).Any(t => t.Id == work.Id);
                        var otherHas = (await store.GetTagsAsync(otherIdentity)).Any(t => t.Id == work.Id);
                        var accessibility = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAccessibilityView(removeWork);
                        if (firstHas == expected && otherHas == expected && (removeWork.Opacity == 1) == expected
                            && removeWork.IsEnabled == expected && removeWork.IsTabStop == expected && removeWork.IsHitTestVisible == expected
                            && accessibility == (expected ? AccessibilityView.Content : AccessibilityView.Raw)) return;
                        await Task.Delay(30);
                    }
                    throw new TimeoutException("Tag row/remove action did not update the selected files and button state.");
                }
                filter.Text = "新标签";
                await Task.Delay(250);
                await CapturePopupAsync(panel, $"tags-{theme}-create.png");
                menu.Hide();
                await Task.Delay(300);
                OpenSettings("appearance");
                await Task.Delay(450);
                await Capture((UIElement)Content, $"theme-settings-{theme}.png");
                CloseSettings();
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "rename-tag-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "rename-tag-error.txt"), error.ToString());
        }
    }

    private static async Task CapturePopupAsync(FrameworkElement content, string name)
    {
        DependencyObject? ancestor = content;
        while (ancestor is not null && ancestor is not FlyoutPresenter) ancestor = VisualTreeHelper.GetParent(ancestor);
        if (ancestor is not FlyoutPresenter presenter) { await Capture(content, name); return; }
        var surface = content as Panel ?? (content as UserControl)?.Content as Panel;
        if (surface is null) { await Capture(content, name); return; }
        var brush = surface.Background;
        // RenderTargetBitmap does not capture the system acrylic backdrop.
        surface.Background = presenter.Background is AcrylicBrush acrylic ? new SolidColorBrush(acrylic.FallbackColor) : presenter.Background;
        try { await Capture(content, name); }
        finally { surface.Background = brush; }
    }
}
#endif
