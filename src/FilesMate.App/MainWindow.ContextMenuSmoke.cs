#if FILESMATE_UI_TEST
using FilesMate.App.Commands;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Controls.Tags;
using FilesMate.App.Services;
using FilesMate.Core.Metadata;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using System.Text.Json;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunContextMenuSmokeAsync()
    {
        var measurements = new List<object>();
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 1100));
            await Task.Delay(1200);
            var root = (FrameworkElement)Content;
            var layout = FileContextMenuBuilder.BuildLayout(CommandContext.Blank with { FolderPath = @"D:\FilesMate" });
            foreach (var theme in new[] { Models.AppThemeKind.Light, Models.AppThemeKind.Dark })
            {
                await App.AppearanceViewModel!.SetThemeAsync(theme);
                foreach (var location in new[] { "center", "top", "bottom", "right" })
                {
                    var point = new Point(location == "right" ? root.ActualWidth - 10 : root.ActualWidth * .55,
                        location == "top" ? 10 : location == "bottom" ? root.ActualHeight - 10 : root.ActualHeight / 2);
                    AppCommandId? invoked = null;
                    var menu = FileContextFlyout.Create(layout, id => invoked = id);
                    FileContextFlyout.ShowAt(menu, root, point);
                    await Task.Delay(350);
                    DependencyObject? ancestor = (DependencyObject)menu.Content;
                    while (ancestor is not null && ancestor is not FlyoutPresenter) ancestor = VisualTreeHelper.GetParent(ancestor);
                    var presenter = (FlyoutPresenter?)ancestor ?? throw new InvalidOperationException("Missing presenter.");
                    var bounds = presenter.TransformToVisual(root).TransformBounds(new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));
                    measurements.Add(new { Theme = theme.ToString(), location, point.X, point.Y, bounds.Top, bounds.Bottom, bounds.Left, bounds.Right, bounds.Height });
                    if (location == "center" && Math.Abs((bounds.Top + bounds.Bottom) / 2 - point.Y) > 20)
                        throw new InvalidOperationException("Menu was not vertically centered at the click.");
                    if (bounds.Top < -2 || bounds.Bottom > root.ActualHeight + 2)
                        throw new InvalidOperationException("Menu escaped the window bounds.");
                    if (location == "bottom" && root.ActualHeight - bounds.Bottom > 24)
                        throw new InvalidOperationException("Bottom-edge menu jumped away from the pointer.");
                    if (location == "center")
                    {
                        await CapturePopupAsync((FrameworkElement)menu.Content, $"context-menu-{theme}.png");
                        var appearance = FindDescendant<Button>((DependencyObject)menu.Content,
                            b => FindDescendant<TextBlock>(b, t => t.Text == Localization.StringTable.Get("Menu_FolderAppearance")) is not null)!;
                        ((IInvokeProvider)new ButtonAutomationPeer(appearance).GetPattern(PatternInterface.Invoke)).Invoke();
                        await Task.Delay(350);
                        var item = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Select(p => p.Child)
                            .SelectMany(p => Descendants(p).Prepend(p)).OfType<MenuFlyoutItem>()
                            .First(i => i.Text == Localization.StringTable.Get("Cover_Choose"));
                        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
                        await Task.Delay(100);
                        if (invoked != AppCommandId.ChooseFolderCover) throw new InvalidOperationException("Submenu invoked the wrong action.");
                    }
                    menu.Hide();
                    await Task.Delay(100);
                }
            }
            var tagDatabase = Path.Combine(AppContext.BaseDirectory, "tag-placement-" + Guid.NewGuid().ToString("N") + ".db");
            await using var store = new SqliteFileMetadataStore(tagDatabase);
            await store.CreateTagAsync("测试", "#5288DD");
            foreach (var theme in new[] { Models.AppThemeKind.Light, Models.AppThemeKind.Dark })
            {
                await App.AppearanceViewModel!.SetThemeAsync(theme);
                var panel = TagPickerFlyout.CreatePanel(store, [], null);
                var menu = FileContextFlyout.Create(FileContextMenuBuilder.BuildLayout(CommandContext.Multi with
                    { TagsAvailable = true, BatchRenameAvailable = true, FolderPath = AppContext.BaseDirectory }),
                    _ => { }, tagPicker: panel);
                FileContextFlyout.ShowAt(menu, root, new Point(root.ActualWidth - 10, root.ActualHeight / 2));
                await Task.Delay(350);
                var add = FindDescendant<Button>(menu.Content,
                    b => FindDescendant<TextBlock>(b, t => t.Text == Localization.StringTable.Get("Command_AddTags")) is not null)!;
                ((IInvokeProvider)new ButtonAutomationPeer(add).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(600);
                var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                    .First(p => FindDescendant<TagPickerPanel>(p.Child, _ => true) is not null);
                if (popup.ShouldConstrainToRootBounds) throw new InvalidOperationException("Tag submenu still constrained to app bounds.");
                var bounds = panel.TransformToVisual(root).TransformBounds(new Rect(0, 0, panel.ActualWidth, panel.ActualHeight));
                measurements.Add(new { Theme = theme.ToString(), TagMenu = true, ConstrainedToApp = popup.ShouldConstrainToRootBounds,
                    bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, RootWidth = root.ActualWidth });
                if (bounds.Right <= root.ActualWidth) throw new InvalidOperationException("Right-edge tag submenu did not extend beyond app bounds.");
                menu.Hide();
                await Task.Delay(100);
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "context-menu-results.json"), JsonSerializer.Serialize(new { Passed = true, measurements }));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "context-menu-results.json"), JsonSerializer.Serialize(new { Passed = false, Error = error.ToString(), measurements }));
        }
    }
}
#endif
