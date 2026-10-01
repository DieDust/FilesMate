#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Omnibar;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Shortcuts;
using FilesMate.App.Views;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunWindowShortcutSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        var checks = new List<object>();
        var keys = new List<object>();
        var invocations = new List<object>();
        ((UIElement)Content).AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) =>
            keys.Add(new { Key = args.Key.ToString(), args.Handled,
                Alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).ToString(),
                Control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).ToString() })), true);
        foreach (var accelerator in ((UIElement)Content).KeyboardAccelerators)
            accelerator.Invoked += (sender, args) => invocations.Add(new { Key = sender.Key.ToString(), Modifiers = sender.Modifiers.ToString(), args.Handled, Blocked = FileShortcutRoutingBlocked, TextFocused = TextInputFocused });
        var fixture = Path.Combine(AppContext.BaseDirectory, "window-shortcut-fixture-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(fixture, "parent");
        var child = Path.Combine(parent, "child");
        var history = Path.Combine(fixture, "history");
        var foreground = BrowsingGetForegroundWindow();
        nint keyboardTarget = 0;
        var originalShortcut = App.Shortcuts[ShortcutAction.Refresh];
        NavigatorPage? page = null;
        try
        {
            Require(App.PinnedLocations.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase), "An isolated test profile is required");
            Directory.CreateDirectory(child); Directory.CreateDirectory(history);
            await File.WriteAllTextAsync(Path.Combine(child, "original.txt"), "shortcut fixture");
            await File.WriteAllTextAsync(Path.Combine(child, "second.txt"), "second fixture");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000)); AppWindow.Resize(new(2600, 1600));
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowFolderSizes = false, DualPane = false, PaneCount = 1 });
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } ready && !ready.ViewModel.IsLoading, "Initial page");
            AddNavigatorTab(child);
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } ready && !ready.ViewModel.IsLoading && ready.ViewModel.AddressText == child, "Fixture page");
            page = (NavigatorPage)TabHost.Content;
            var surface = (FileDetailsSurface)page.FindName("FileSurface");
            var omni = (Omnibar)page.FindName("Omni");
            var toolbar = (Controls.NavigationToolbar)omni.FindName("Toolbar");
            Control[] targets = [(TabViewItem)Tabs.SelectedItem, NewTabButton,
                (Button)NavigationSidebar.FindName("SettingsButton"), (Button)toolbar.FindName("RefreshButton"), surface];
            foreach (var target in targets)
            {
                await Navigate(history); await Navigate(child);
                var before = page.ViewModel.Navigation.CurrentGeneration;
                await Key(target, VirtualKey.Back);
                await Wait(() => At(parent), "Backspace from " + target.GetType().Name + "/" + target.Name);
                Require(page.ViewModel.Navigation.CurrentGeneration == before + 1, "Backspace navigated more than once");
                await Key(target, VirtualKey.Left, alt: true);
                await Wait(() => At(child), "Alt+Left");
                await Key(target, VirtualKey.Right, alt: true);
                await Wait(() => At(parent), "Alt+Right");
                await Key(target, VirtualKey.Left, alt: true);
                await Wait(() => At(child), "History return");
                await Key(target, VirtualKey.Up, alt: true);
                await Wait(() => At(parent), "Alt+Up");
                await Navigate(child);
                before = page.ViewModel.Navigation.CurrentGeneration;
                await Key(target, VirtualKey.F5);
                await Wait(() => !page.ViewModel.IsLoading && page.ViewModel.Navigation.CurrentGeneration == before + 1, "Refresh");
                surface.SelectAll();
                await Key(target, VirtualKey.Escape);
                Require(surface.Selection.Count == 0, "Escape outside file area");
                await Key(target, VirtualKey.A, control: true);
                Require(surface.Selection.Count == 2, "Select All outside file area");
                await Key(target, VirtualKey.L, control: true);
                var address = (TextBox)omni.FindName("PathBox");
                await Wait(() => address.FocusState != FocusState.Unfocused && omni.IsEditing, "Edit address");
                address.Text = "unsubmitted path";
                address.Select(address.Text.Length, 0);
                await Key(address, VirtualKey.Back);
                Require(At(child) && address.Text == "unsubmitted pat", "Backspace in address navigated instead of deleting text");
                Require(target.Focus(FocusState.Programmatic), "Cannot leave address editor");
                await Wait(() => !omni.IsEditing, "Address did not end editing after focus left");
                Require(address.Text.Equals(child, StringComparison.OrdinalIgnoreCase) && At(child), "Unsubmitted address was retained/applied");
                await Key(target, VirtualKey.F, control: true);
                var search = (TextBox)omni.FindName("SearchBox");
                await Wait(() => search.FocusState != FocusState.Unfocused, "Search shortcut");
                await Key(search, VirtualKey.Escape);
                checks.Add(new { Focus = target.GetType().Name + "/" + target.Name,
                    BackspaceUp = true, AltNavigation = true, RefreshOnce = true, SelectAllAndEscape = true,
                    AddressAndSearch = true, AddressTextEditing = true, AddressBlurRestores = true });
            }
            var custom = new ShortcutGesture(ShortcutKey.K, ShortcutModifiers.Control | ShortcutModifiers.Shift);
            Require(await App.UpdateShortcutAsync(ShortcutAction.Refresh, custom) is null, "Custom Refresh shortcut conflicts");
            var generation = page.ViewModel.Navigation.CurrentGeneration;
            await Key(NewTabButton, VirtualKey.K, control: true, shift: true);
            await Wait(() => !page.ViewModel.IsLoading && page.ViewModel.Navigation.CurrentGeneration == generation + 1, "Custom Refresh outside file area");
            report["CustomShortcut"] = true;
            await App.UpdateShortcutAsync(ShortcutAction.Refresh, originalShortcut);

            // Real F2 enters rename from outside the file area; Escape restores the old name.
            Require(surface.TrySelectByName("original.txt"), "Rename selection");
            await Key(NewTabButton, VirtualKey.F2);
            await Wait(() => surface.IsRenaming && Descendants(surface).OfType<TextBox>().Any(), "Rename shortcut");
            var rename = Descendants(surface).OfType<TextBox>().Single();
            rename.Text = "cancelled.txt";
            await Key(rename, VirtualKey.Escape);
            Require(!surface.IsRenaming && File.Exists(Path.Combine(child, "original.txt")) && !File.Exists(Path.Combine(child, "cancelled.txt")), "Escape did not cancel rename");
            await Key(NewTabButton, VirtualKey.F2);
            await Wait(() => surface.IsRenaming, "Second rename");
            rename = Descendants(surface).OfType<TextBox>().Single();
            rename.Text = "accepted.txt";
            Require(NewTabButton.Focus(FocusState.Programmatic), "Cannot leave rename");
            await Wait(() => !surface.IsRenaming && File.Exists(Path.Combine(child, "accepted.txt")), "Rename blur did not commit");
            await Wait(() => surface.TrySelectByName("accepted.txt"), "Renamed item update");
            Require(NewTabButton.FocusState != FocusState.Unfocused, "Rename stole focus from the clicked control");
            generation = page.ViewModel.Navigation.CurrentGeneration;
            await Key(NewTabButton, VirtualKey.F5);
            await Wait(() => !page.ViewModel.IsLoading && page.ViewModel.Navigation.CurrentGeneration == generation + 1, "Shortcut after leaving rename");
            report["RenameFromChromeEscapeAndBlur"] = true;

            var sidebar = (Button)NavigationSidebar.FindName("SettingsButton");
            var initialDirectories = Directory.GetDirectories(child).Length;
            await Key(sidebar, VirtualKey.N, control: true, shift: true);
            await Wait(() => surface.IsRenaming && Directory.GetDirectories(child).Length == initialDirectories + 1, "New folder from sidebar");
            rename = Descendants(surface).OfType<TextBox>().Single();
            rename.Text = "created-by-keyboard";
            await Key(rename, VirtualKey.Enter);
            var created = Path.Combine(child, "created-by-keyboard");
            await Wait(() => Directory.Exists(created) && !surface.IsRenaming && !PaneFileActions.IsBusy, "Confirm created folder name");
            await Key(NewTabButton, VirtualKey.Z, control: true);
            await Wait(() => !Directory.Exists(created) && Directory.GetDirectories(child).Length == initialDirectories + 1
                && !PaneFileActions.IsBusy, "Undo rename from tab strip");
            await Key(sidebar, VirtualKey.Y, control: true);
            await Wait(() => Directory.Exists(created) && !PaneFileActions.IsBusy, "Redo rename from sidebar");
            report["NewFolderUndoRedoOutsideFiles"] = true;

            await Key(NewTabButton, VirtualKey.P, alt: true);
            await Wait(() => ((FrameworkElement)page.FindName("PreviewContainer")).Visibility == Visibility.Visible, "Preview from tab strip");
            await Key(NewTabButton, VirtualKey.P, alt: true);
            await Wait(() => ((FrameworkElement)page.FindName("PreviewContainer")).Visibility == Visibility.Collapsed, "Close preview from tab strip");
            report["PreviewFromChrome"] = true;
            var paneCount = typeof(NavigatorPage).GetField("_paneCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await Key(sidebar, VirtualKey.S, control: true, shift: true);
            await Wait(() => (int)paneCount.GetValue(page)! == 2, "Dual pane from sidebar");
            await Key(sidebar, VirtualKey.S, control: true, shift: true);
            await Wait(() => (int)paneCount.GetValue(page)! == 3, "Triple pane from sidebar");
            await Key(sidebar, VirtualKey.S, control: true, shift: true);
            await Wait(() => (int)paneCount.GetValue(page)! == 1, "Single pane from sidebar");
            report["DualPaneFromChrome"] = true;

            var input = new TextBox { Text = "protected" };
            var dialog = new ContentDialog { Title = "快捷键验证", Content = input, CloseButtonText = "关闭", XamlRoot = surface.XamlRoot };
            Theming.ContentDialogTheme.Apply(dialog, surface);
            var shown = dialog.ShowAsync();
            await Wait(() => input.IsLoaded && VisualTreeHelper.GetOpenPopupsForXamlRoot(surface.XamlRoot).Count > 0, "Dialog");
            generation = page.ViewModel.Navigation.CurrentGeneration;
            await Key(input, VirtualKey.A, control: true);
            Require(input.SelectionLength == input.Text.Length, "Dialog text did not receive Select All");
            await Key(input, VirtualKey.Back);
            await Key(input, VirtualKey.F5);
            await Key(input, VirtualKey.Up, alt: true);
            Require(input.Text.Length == 0 && generation == page.ViewModel.Navigation.CurrentGeneration && At(child), "Dialog keys changed the underlying folder");
            await Key(input, VirtualKey.Escape); await shown;
            report["DialogProtected"] = true;
            report["FocusScopes"] = checks;
            report["Passed"] = true;

            bool At(string path) => !page.ViewModel.IsLoading && page.ViewModel.AddressText.Equals(path, StringComparison.OrdinalIgnoreCase);
            async Task Navigate(string path)
            {
                page.ViewModel.Navigate(path);
                await Wait(() => !page.ViewModel.IsLoading, "Navigate loading");
                Require(At(path), $"Navigate expected={path}; actual={page.ViewModel.AddressText}; error={page.ViewModel.ErrorText}; status={page.ViewModel.StatusText}");
                await Task.Delay(100);
            }
        }
        catch (Exception error) { report["Passed"] = false; report["Error"] = error.ToString(); report["FocusScopes"] = checks;
            report["PathAtFailure"] = page?.ViewModel.AddressText ?? ""; report["KeyEvents"] = keys; report["Invocations"] = invocations; }
        finally
        {
            await App.UpdateShortcutAsync(ShortcutAction.Refresh, originalShortcut);
            if (page is not null) { page.ViewModel.Navigate(HomeLocation.Uri); await page.ViewModel.WhenFolderReleased; }
            App.FileUndo.Clear();
            var fixturePath = Path.GetFullPath(fixture);
            Require(fixturePath.StartsWith(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase), "Fixture path escaped test output");
            if (Directory.Exists(fixturePath)) Directory.Delete(fixturePath, recursive: true);
            if (foreground != 0) BrowsingSetForegroundWindow(foreground);
        }
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "window-shortcut-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        async Task Key(Control target, VirtualKey key, bool control = false, bool shift = false, bool alt = false)
        {
            Activate();
            var thread = BrowsingGetCurrentThreadId();
            var foregroundThread = BrowsingGetWindowThreadProcessId(BrowsingGetForegroundWindow(), out _);
            var attached = thread != foregroundThread && BrowsingAttachThreadInput(thread, foregroundThread, true);
            try { BrowsingSetForegroundWindow(NativeHandle); }
            finally { if (attached) BrowsingAttachThreadInput(thread, foregroundThread, false); }
            Require(target.Focus(FocusState.Programmatic), "Cannot focus " + target.Name);
            await Task.Delay(70);
            var focus = BrowsingGetFocus();
            // The user can return to Codex while the off-screen fixture runs.
            // Its XAML focus still exists; keep posting to its own input island.
            if (focus == 0) focus = keyboardTarget;
            BrowsingGetWindowThreadProcessId(focus, out var processId);
            Require(processId == Environment.ProcessId, "Keyboard target is outside the test process");
            keyboardTarget = focus;
            var prior = new byte[256]; Require(MutationGetKeyboardState(prior), "Cannot read keyboard state");
            var state = (byte[])prior.Clone();
            foreach (var modifier in new[] { VirtualKey.Control, VirtualKey.LeftControl, VirtualKey.RightControl,
                VirtualKey.Shift, VirtualKey.LeftShift, VirtualKey.RightShift, VirtualKey.Menu, VirtualKey.LeftMenu, VirtualKey.RightMenu,
                VirtualKey.LeftWindows, VirtualKey.RightWindows }) state[(int)modifier] = 0;
            state[(int)VirtualKey.Control] = control ? (byte)0x80 : (byte)0;
            state[(int)VirtualKey.Shift] = shift ? (byte)0x80 : (byte)0;
            state[(int)VirtualKey.Menu] = alt ? (byte)0x80 : (byte)0;
            Require(MutationSetKeyboardState(state), "Cannot set test-thread keyboard state");
            try
            {
                // Post only to this process's focused native HWND; no physical keyboard input.
                var context = alt ? 1 << 29 : 0;
                Require(BrowsingPostMessage(focus, 0x100, (nuint)key, 1 | context), "Key down rejected");
                Require(BrowsingPostMessage(focus, 0x101, (nuint)key, unchecked((nint)(int)0xC0000001) | context), "Key up rejected");
                await Task.Delay(180);
            }
            finally { MutationSetKeyboardState(prior); }
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static async Task Wait(Func<bool> ready, string description)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(25);
            Require(ready(), description + " did not settle");
        }
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i); yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}
#endif
