#if FILESMATE_UI_TEST
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Omnibar;

public sealed partial class Omnibar
{
    private async Task RunBreadcrumbSmokeAsync()
    {
        var samples = new List<object>();
        var originalWidth = Width;
        try
        {
            await Task.Delay(700);
            foreach (var width in new[] { 540d, 800d, 1100d, 1800d, 800d })
            {
                Width = width;
                UpdateLayout();
                await Task.Delay(120);
                var layout = _breadcrumbLayout!;
                var view = _crumbViews[^1];
                var label = (TextBlock)((Grid)((Button)view.Children[0]).Content).Children.Last();
                var position = view.TransformToVisual(PathViewport).TransformPoint(default);
                samples.Add(new { Width = width, Viewport = PathViewport.ActualWidth, Layout = layout,
                    Natural = _crumbWidths, Actual = view.ActualWidth, TextWidth = label.ActualWidth,
                    TextDesired = label.DesiredSize.Width, label.IsTextTrimmed, X = position.X });
                if (position.X < -1 || position.X + view.ActualWidth > PathViewport.ActualWidth + 1)
                    throw new InvalidOperationException("Current directory outside address viewport.");
                if (layout.CurrentWidth >= _crumbWidths[^1] && label.IsTextTrimmed)
                    throw new InvalidOperationException("Current directory trimmed despite enough space.");
            }
            ShowHiddenAncestors();
            await Task.Delay(100);
            if (!CrumbFolderPopup.IsOpen || CrumbFolderItems.Children.Count == 0)
                throw new InvalidOperationException("Missing ancestor menu.");
            string? chosen = null;
            void OnChosen(object? sender, string path) => chosen = path;
            CrumbClicked += OnChosen;
            try
            {
                var target = (Button)CrumbFolderItems.Children[0];
                ((IInvokeProvider)new ButtonAutomationPeer(target).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(100);
                if (chosen != (string)target.Tag) throw new InvalidOperationException("Ancestor navigation target incorrect.");
            }
            finally { CrumbClicked -= OnChosen; }
            if (CrumbFolderPopup.IsOpen) FinishCrumbFolderClose();
            BeginPathEdit();
            if (PathBox.Text != Text || PathBox.SelectionLength != Text.Length)
                throw new InvalidOperationException("Full path editing lost text.");
            CancelMode();
            var navigatedPath = Text;
            foreach (var path in new[] { @"D:\", @"D:\资料\项目\" + new string('长', 90), @"\\server\share\资料\归档\当前目录" })
            {
                Text = path;
                await Task.Delay(100);
                UpdateLayout();
                var view = _crumbViews[^1];
                var point = view.TransformToVisual(PathViewport).TransformPoint(default);
                if (point.X < -1 || point.X + view.ActualWidth > PathViewport.ActualWidth + 1)
                    throw new InvalidOperationException("Long/UNC/root path overflowed.");
                BeginPathEdit();
                if (PathBox.Text != path) throw new InvalidOperationException("Path altered by abbreviation.");
                CancelMode();
            }
            Text = navigatedPath;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "breadcrumb-smoke.json"),
                JsonSerializer.Serialize(new { Passed = true, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "breadcrumb-smoke.json"),
                JsonSerializer.Serialize(new { Passed = false, Error = error.ToString(), Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Width = originalWidth; }
    }
}
#endif
