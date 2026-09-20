#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Models;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private bool _navigationSmokeStarted;

    private async Task RunNavigationSmokeAsync()
    {
        try
        {
            await Task.Delay(1000);
            var fixture = ViewModel.AddressText;
            foreach (var alias in new[] { "shell:Startup", "桌面" })
            {
                var expected = await ShellLocation.ResolveFileSystemPathAsync(Navigation.SpecialLocation.ShellName(alias)!);
                Omni.BeginPathEdit();
                ((TextBox)Omni.FindName("PathBox")).Text = alias;
                Omni_PathSubmitted(Omni, alias);
                for (var i = 0; i < 100 && (ViewModel.AddressText != expected || ViewModel.IsLoading); i++) await Task.Delay(50);
                if (ViewModel.AddressText != expected) throw new InvalidOperationException("Alias did not navigate: " + alias);
            }
            ViewModel.Navigate(fixture);
            await Task.Delay(700);
            FileSurface.SetColumns(DetailsColumn.Defaults().Select(c => c with { Visible = true }).ToArray());
            UpdateLayout();
            await Task.Delay(150);
            var texts = Descendants(FileSurface).OfType<TextBlock>().Select(t => t.Text).ToArray();
            if (!texts.Contains(Path.Combine(fixture, "验证.txt")) || !texts.Contains(fixture))
                throw new InvalidOperationException("Path columns did not render actual file locations.");
            if (FileSurface.GetColumns().Count(c => c.Visible) != 10)
                throw new InvalidOperationException("Optional columns unavailable.");
            foreach (var width in new[] { 160d, 400d, 240d })
            {
                FileSurface.SetColumns(FileSurface.GetColumns().Select(c => c.Id == DetailsColumnId.Name ? c with { Width = width } : c).ToArray());
                UpdateLayout();
                await Task.Delay(100);
                var names = Descendants(FileSurface).OfType<TextBlock>().Where(t => t.Name == "NameText" && t.Text.Length > 0).ToArray();
                if (names.Length == 0 || names.Any(t => t.TextWrapping != TextWrapping.NoWrap
                    || Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(t).Width < width - 24
                    || Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(t).Width > width))
                    throw new InvalidOperationException("File names do not use the resized column width: " + width + " "
                        + JsonSerializer.Serialize(names.Select(t => new { t.Text, t.ActualWidth, t.TextWrapping, t.Visibility })));
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "navigation-smoke.json"),
                JsonSerializer.Serialize(new { Passed = true, AddressAliases = true, PathCells = true, OptionalColumns = 10, FileNameResizing = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "navigation-smoke.json"),
                JsonSerializer.Serialize(new { Passed = false, Error = error.ToString() }));
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
#endif
