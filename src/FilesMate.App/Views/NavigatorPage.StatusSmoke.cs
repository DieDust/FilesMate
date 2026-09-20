#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using Windows.Foundation;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private async Task RunStatusSmokeAsync()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "status-smoke.json");
        try
        {
            var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "status-fixture");
            var fixture = Path.Combine(fixtureRoot, "content");
            Directory.CreateDirectory(fixture);
            Directory.CreateDirectory(Path.Combine(fixtureRoot, "empty"));
            for (var i = 0; i < 12; i++)
                File.WriteAllBytes(Path.Combine(fixture, $"file-{i:D2}.bin"), new byte[1024]);
            _leftVm.Navigate(fixture);
            await Until(() => !_leftVm.IsLoading && _leftVm.ItemCount == 12);
            FileSurface.SetLayout(FileLayoutKind.Details);
            await Task.Delay(200);
            var surfaceType = typeof(FileDetailsSurface);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var range = (Microsoft.UI.Xaml.FrameworkElement)surfaceType.GetField("AlphabetRange", flags)!.GetValue(FileSurface)!;
            Require(range.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed, "Idle alphabet range is visible");
            surfaceType.GetField("_alphabetHovered", flags)!.SetValue(FileSurface, true);
            surfaceType.GetMethod("ShowAlphabet", flags)!.Invoke(FileSurface, null);
            Require(range.Visibility == Microsoft.UI.Xaml.Visibility.Visible, "Hover did not show range");
            surfaceType.GetField("_alphabetHovered", flags)!.SetValue(FileSurface, false);
            surfaceType.GetMethod("ScheduleAlphabetHide", flags)!.Invoke(FileSurface, null);
            surfaceType.GetMethod("UpdateAlphabetPosition", flags)!.Invoke(FileSurface, [true]);
            Require(range.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed, "Scrolling after pointer exit restored range");
            surfaceType.GetMethod("HideAlphabet", flags)!.Invoke(FileSurface, null);
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowFolderSizes = false });
            UpdateFolderStatus(_leftVm);
            var expectedCapacity = DriveCapacity.FormatFreeSpace(_leftVm.AddressText) ?? string.Empty;
            await Until(() => PaneChrome.ZoomText == expectedCapacity);
            Require(!_folderStatusRequests.ContainsKey(_leftVm),
                "Disabled folder sizes still started a recursive walk");
            var marquee = surfaceType.GetMethod("ApplyMarqueeSelection", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var lost = surfaceType.GetMethod("OnPointerCaptureLost", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
            surfaceType.GetField("_dragging", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(FileSurface, true);
            marquee.Invoke(FileSurface, [new Point(10, 2), new Point(200, 130)]);
            var selected = FileSurface.Selection.Count;
            Require(selected > 1 && selected < 12, "Fixture was not partially selected");
            Require(PaneChrome.SelectionText == StringTable.Format("Status_Selected", selected),
                "Selection count was not published during marquee");
            lost.Invoke(FileSurface, [FileSurface, null]);
            await Task.Delay(100);
            Require(PaneChrome.SelectionText == StringTable.Format("Status_Selected", selected),
                "Capture loss dropped selection status");
            var selectionText = PaneChrome.SelectionText;
            marquee.Invoke(FileSurface, [new Point(10000, 2), new Point(11000, 130)]);
            Require(FileSurface.Selection.Count == 0 && string.IsNullOrEmpty(PaneChrome.SelectionText),
                "Blank-space selection did not immediately clear status");

            var expectedSize = StringTable.Format("Status_FolderSize", DriveCapacity.FormatBytes(12288));
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowFolderSizes = true });
            UpdateFolderStatus(_leftVm);
            await Until(() => PaneChrome.ZoomText == expectedSize);
            var folderText = PaneChrome.ZoomText;
            SetDualPane(true, persist: false);
            await Until(() => _rightVm is not null && !_rightVm.IsLoading);
            var empty = Path.Combine(_leftVm.AddressText, "..", "empty");
            _rightVm!.Navigate(empty);
            await Until(() => _rightChrome!.ZoomText == StringTable.Format("Status_FolderSize", "0 B"));
            Require(PaneChrome.ZoomText == folderText, "Second pane overwrote first pane's total");
            var emptyText = _rightChrome!.ZoomText;
            // Two navigations in the same dispatcher turn must cancel the old request.
            _rightVm.Navigate(_leftVm.AddressText);
            _rightVm.Navigate(empty);
            await Until(() => !_rightVm.IsLoading && _rightChrome.ZoomText == emptyText);
            await Task.Delay(400);
            Require(_rightChrome.ZoomText == emptyText, "Stale folder total won navigation race");
            SetDualPane(false, persist: false);
            Require(!_folderStatusRequests.ContainsKey(_rightVm), "Hidden pane retained its size request");
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Passed = true, SelectionDuringMarquee = selectionText, CaptureLossPreserved = true,
                BlankSpaceCleared = true, FolderSize = folderText, EmptyFolderSize = emptyText,
                DualPaneIndependent = true, RapidNavigationSafe = true, HiddenPaneCanceled = true,
                AlphabetRangeOnlyOnHover = true,
                FolderSizeDisabledUsesCapacity = true,
                MouseKeyboardInputUsed = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = false, Error = ex.ToString() }));
        }

        static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException(error);
        }
        static async Task Until(Func<bool> condition)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (condition()) return;
                await Task.Delay(100);
            }
            throw new TimeoutException("Status smoke condition did not become true");
        }
    }
}
#endif
