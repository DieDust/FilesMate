#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    internal async Task RunNavigationViewportSmokeAsync()
    {
        var samples = new List<object>();
        var failures = new List<string>();
        var fixture = Path.Combine(AppContext.BaseDirectory, "navigation-viewport-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(fixture);
            for (var i = 0; i < 384; i++) Directory.CreateDirectory(Path.Combine(fixture, $"Folder-{i:D3}"));
            SetPaneCount(1, persist: false);
            ActivatePane(0);
            _leftVm.Navigate(fixture);
            await Ready(_leftVm, FileSurface, fixture);
            foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.Grid, FileLayoutKind.List })
            {
                await SetView(_leftVm, layout);
                var (name, before) = await SelectMiddle(_leftVm, FileSurface, 150);
                if (layout == FileLayoutKind.Details) await MainWindow.Capture(FileSurface, "navigation-parent-before.png");
                Enter(_leftVm, FileSurface, name);
                await Ready(_leftVm, FileSurface, Path.Combine(fixture, name));
                Toolbar_UpClicked(Omni, new RoutedEventArgs());
                await VerifyReturn(_leftVm, FileSurface, name, before, layout + "/Up");
                if (layout == FileLayoutKind.Details) await MainWindow.Capture(FileSurface, "navigation-parent-after.png");
                _leftVm.Back();
                await Ready(_leftVm, FileSurface, Path.Combine(fixture, name));
                _leftVm.Forward();
                await VerifyReturn(_leftVm, FileSurface, name, before, layout + "/Forward");
            }

            await SetView(_leftVm, FileLayoutKind.Details);
            var (rapidName, rapidOffset) = await SelectMiddle(_leftVm, FileSurface, 170);
            var child = Path.Combine(fixture, rapidName);
            _leftVm.Navigate(child);
            _leftVm.Up();
            _leftVm.Navigate(child);
            _leftVm.Up();
            await VerifyReturn(_leftVm, FileSurface, rapidName, rapidOffset, "Rapid navigation");

            SetPaneCount(2, persist: false);
            _rightVm!.Navigate(fixture);
            await Ready(_rightVm, _rightSurface!, fixture);
            await SetView(_leftVm, FileLayoutKind.Details);
            await SetView(_rightVm, FileLayoutKind.Details);
            var (leftName, leftOffset) = await SelectMiddle(_leftVm, FileSurface, 100);
            var (rightName, rightOffset) = await SelectMiddle(_rightVm, _rightSurface!, 250);
            Enter(_leftVm, FileSurface, leftName);
            await Ready(_leftVm, FileSurface, Path.Combine(fixture, leftName));
            Enter(_rightVm, _rightSurface!, rightName);
            await Ready(_rightVm, _rightSurface!, Path.Combine(fixture, rightName));
            Toolbar_UpClicked(Omni, new RoutedEventArgs());
            await VerifyReturn(_rightVm, _rightSurface!, rightName, rightOffset, "Right pane/Up");
            Require(_leftVm.AddressText == Path.Combine(fixture, leftName), "Right Up navigated the left pane");
            ActivatePane(0);
            FileSurface_UpRequested(FileSurface, EventArgs.Empty);
            await VerifyReturn(_leftVm, FileSurface, leftName, leftOffset, "Left pane/Up");
            Require(Math.Abs(Offset(_rightSurface!) - rightOffset) <= 2, "Left Up changed the right pane position");
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "navigation-viewport-smoke.json"),
            JsonSerializer.Serialize(new { Passed = failures.Count == 0, Failures = failures, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));

        async Task SetView(PaneViewModel vm, FileLayoutKind layout)
        {
            var view = new FolderViewSettings(layout == FileLayoutKind.Details, GridSizePreset.Large.Slot,
                EntrySort.Name, SurfaceOf(vm).GetColumns(), layout == FileLayoutKind.List, 100);
            await App.FolderCustomizations.SetViewAsync(fixture, view);
            ApplyFolderView(vm, view);
            await Ready(vm, SurfaceOf(vm), fixture);
        }
        async Task<(string Name, double Offset)> SelectMiddle(PaneViewModel vm, FileDetailsSurface surface, int number)
        {
            var name = $"Folder-{number:D3}";
            Require(surface.TrySelectByName(name), "Cannot select the fixture folder");
            await Task.Delay(350);
            var before = Offset(surface);
            Require(before > 300, "Fixture did not scroll away from the top");
            return (name, before);
        }
        void Enter(PaneViewModel vm, FileDetailsSurface surface, string name) =>
            FileSurface_OpenRequested(surface, vm.Store!.Snapshot().Single(entry => entry.Name == name));
        async Task VerifyReturn(PaneViewModel vm, FileDetailsSurface surface, string name, double before, string action)
        {
            await Ready(vm, surface, fixture);
            for (var i = 0; i < 60 && Math.Abs(Offset(surface) - before) > 2; i++) await Task.Delay(25);
            var after = Offset(surface);
            var selected = surface.SelectedPaths().Select(Path.GetFileName).ToArray();
            samples.Add(new { Action = action, Before = before, After = after, Selected = selected });
            Require(Math.Abs(after - before) <= 2, $"{action}: expected {before}, got {after}");
            Require(selected.SequenceEqual(new[] { name }), $"{action}: entered folder was not reselected");
        }
        async Task Ready(PaneViewModel vm, FileDetailsSurface surface, string path)
        {
            for (var i = 0; i < 200; i++)
            {
                if (vm.AddressText == path && !vm.IsLoading && !_restoringViews.ContainsKey(vm)
                    && vm.ViewIndex is { } index && index.Generation == vm.Navigation.CurrentGeneration
                    && index.Sort == vm.Sort && surface.IsBoundTo(vm.Store, index))
                { await Task.Delay(150); return; }
                await Task.Delay(50);
            }
            throw new TimeoutException($"Folder did not finish loading: {path}; actual={vm.AddressText}; current={vm.Navigation.CurrentPath}; loading={vm.IsLoading}; restoring={_restoringViews.ContainsKey(vm)}; generation={vm.Navigation.CurrentGeneration}; index={vm.ViewIndex?.Generation}; sortMatches={vm.ViewIndex?.Sort == vm.Sort}");
        }
        static double Offset(FileDetailsSurface surface)
        {
            var scroller = (ScrollViewer)surface.FindName("Scroller");
            return surface.LayoutKind == FileLayoutKind.List ? scroller.HorizontalOffset : scroller.VerticalOffset;
        }
        static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
    }
}
#endif
