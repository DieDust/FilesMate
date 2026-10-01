#if FILESMATE_UI_TEST
using System.Runtime.InteropServices;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Shortcuts;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunFolderMutationSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        var samples = new List<object>();
        var fixture = Path.Combine(AppContext.BaseDirectory, "folder-mutation-fixture-" + Guid.NewGuid().ToString("N"));
        var originalForeground = BrowsingGetForegroundWindow();
        var originalShortcut = App.Shortcuts[ShortcutAction.NewFolder];
        try
        {
            if (!App.PinnedLocations.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated test profile is required");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000)); AppWindow.Resize(new(2800, 1600));
            await App.AppearanceViewModel!.SetThemeAndAccentAsync(AppThemeKind.Light, AccentKind.Default);
            await App.AppearanceViewModel.SetBackdropAsync(BackdropKind.Solid);
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowFolderSizes = true, ShowHiddenFiles = false,
                ShowAlphabetNavigation = false, DualPane = false, PaneCount = 1, ShowAlternatingRows = true });
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } ready
                && !ready.ViewModel.IsLoading && !string.IsNullOrEmpty(ready.ViewModel.AddressText));
            var page = (NavigatorPage)TabHost.Content;
            var surface = (FileDetailsSurface)page.FindName("FileSurface");
            Theming.ThemeResources.Bind((Grid)surface.Content, Panel.BackgroundProperty, "FilesMate.FileContent.BackgroundBrush");
            Directory.CreateDirectory(fixture);
            report["FolderMutations"] = await page.RunFolderMutationSmokeAsync(fixture, async () =>
            {
                surface.SetColumns(DetailsColumn.Defaults());
                report["DetailsBlank"] = await surface.RunDetailsBlankSmokeAsync();
                await Capture(surface, "details-right-blank-Light.png");
                await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Dark);
                report["DetailsBlankDark"] = await surface.RunDetailsBlankSmokeAsync();
                await Capture(surface, "details-right-blank-Dark.png");
                await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Light);
                var omni = (Controls.Omnibar.Omnibar)page.FindName("Omni");
                var navigation = (Controls.NavigationToolbar)omni.FindName("Toolbar");
                Control[] focusTargets = [NewTabButton, (Button)NavigationSidebar.FindName("SettingsButton"), (Button)navigation.FindName("RefreshButton")];
                foreach (var target in focusTargets)
                {
                    surface.SelectAll();
                    Require(surface.Selection.Count == 2, "File selection fixture is incomplete");
                    await Key(target, VirtualKey.Escape);
                    Require(surface.Selection.Count == 0, "Escape outside the file area did not clear selection: " + target.Name);
                    await Key(target, VirtualKey.A, control: true);
                    Require(surface.Selection.Count == 2, "Select All outside the file area did not select files: " + target.Name);
                    samples.Add(new { Focus = target.Name, EscapeClears = true, SelectAllWorks = true });
                }
                var input = new TextBox { Text = "不要改变文件选择" };
                var dialog = new ContentDialog { Title = "输入快捷键验证", Content = input, CloseButtonText = "关闭", XamlRoot = surface.XamlRoot };
                Theming.ContentDialogTheme.Apply(dialog, surface);
                var shown = dialog.ShowAsync();
                await Wait(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(surface.XamlRoot).Count > 0 && input.IsLoaded);
                await Key(input, VirtualKey.A, control: true);
                Require(input.SelectionLength == input.Text.Length && surface.Selection.Count == 2, "Text selection affected the files behind the dialog");
                await Key(input, VirtualKey.Escape);
                await shown;
                Require(surface.Selection.Count == 2, "Closing a dialog also cleared file selection");
                report["TextAndDialogFocusProtected"] = true;
                var custom = new ShortcutGesture(ShortcutKey.K, ShortcutModifiers.Control | ShortcutModifiers.Shift);
                Require(await App.UpdateShortcutAsync(ShortcutAction.NewFolder, custom) is null, "Custom shortcut conflicts");
                await Key(NewTabButton, VirtualKey.K, control: true, shift: true);
                await Wait(() => page.ViewModel.ItemCount == 3 && surface.IsRenaming);
                Require(Directory.GetDirectories(fixture).Length == 3, "One keyboard press created multiple folders");
                report["CustomShortcutAtWindowLevel"] = new { Action = "NewFolder", Key = "Ctrl+Shift+K", Executions = 1 };
            });
            report["FocusScopes"] = samples;
            report["Passed"] = true;
        }
        catch (Exception error) { report["Passed"] = false; report["Error"] = error.ToString(); }
        finally
        {
            await App.UpdateShortcutAsync(ShortcutAction.NewFolder, originalShortcut);
            if (TabHost.Content is NavigatorPage page)
            {
                var surface = (FileDetailsSurface)page.FindName("FileSurface");
                surface.CancelRenameForMutationSmoke(); page.ViewModel.Navigate(HomeLocation.Uri);
                await page.ViewModel.WhenFolderReleased;
            }
            App.FileUndo.Clear();
            if (Path.GetFullPath(fixture).StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase) && Directory.Exists(fixture))
                Directory.Delete(fixture, recursive: true);
            if (originalForeground != 0) BrowsingSetForegroundWindow(originalForeground);
        }
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "folder-mutation-smoke.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        async Task Key(Control target, VirtualKey key, bool control = false, bool shift = false)
        {
            Activate();
            var thread = BrowsingGetCurrentThreadId();
            var foregroundThread = BrowsingGetWindowThreadProcessId(BrowsingGetForegroundWindow(), out _);
            var attached = thread != foregroundThread && BrowsingAttachThreadInput(thread, foregroundThread, true);
            try { BrowsingSetForegroundWindow(NativeHandle); }
            finally { if (attached) BrowsingAttachThreadInput(thread, foregroundThread, false); }
            Require(target.Focus(FocusState.Programmatic), "Cannot focus " + target.Name);
            await Task.Delay(100);
            var focus = BrowsingGetFocus();
            BrowsingGetWindowThreadProcessId(focus, out var processId);
            Require(processId == Environment.ProcessId, "Native keyboard target is outside the test process");
            var prior = new byte[256]; Require(MutationGetKeyboardState(prior), "Cannot read keyboard state");
            var current = (byte[])prior.Clone();
            current[(int)VirtualKey.Control] = control ? (byte)0x80 : (byte)0;
            current[(int)VirtualKey.Shift] = shift ? (byte)0x80 : (byte)0;
            current[(int)VirtualKey.Menu] = 0;
            Require(MutationSetKeyboardState(current), "Cannot set test-thread keyboard state");
            try
            {
                Require(BrowsingPostMessage(focus, 0x100, (nuint)key, 1), "Native key-down rejected");
                Require(BrowsingPostMessage(focus, 0x101, (nuint)key, unchecked((nint)(int)0xC0000001)), "Native key-up rejected");
                await Task.Delay(180);
            }
            finally { MutationSetKeyboardState(prior); }
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static async Task Wait(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(25);
            if (!ready()) throw new TimeoutException("Native mutation check did not settle");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetKeyboardState")] private static extern bool MutationGetKeyboardState(byte[] state);
    [DllImport("user32.dll", EntryPoint = "SetKeyboardState")] private static extern bool MutationSetKeyboardState(byte[] state);
}
#endif
