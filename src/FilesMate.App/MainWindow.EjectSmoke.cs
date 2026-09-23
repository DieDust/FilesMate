#if FILESMATE_UI_TEST
using FilesMate.App.Controls.Navigation;
using FilesMate.App.Controls.Home;
using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunEjectSmokeAsync()
    {
        var evidence = new Dictionary<string, object>();
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            await Until(() => TabHost.Content is NavigatorPage { IsLoaded: true });
            var page = (NavigatorPage)TabHost.Content;
            var sidebar = (NavigationSidebar)page.FindName("Sidebar");
            var item = new NavigationItem("drive:Z:", "Test device", "\uEDA2", @"Z:\");
            var query = new TaskCompletionSource<DeviceEjectTarget?>();
            var result = new TaskCompletionSource<DeviceEjectResult>();
            var requests = 0;
            DeviceEjectUI.TestQuery = _ => query.Task;
            DeviceEjectUI.TestRequest = _ => { requests++; return result.Task; };
            void Click() => sidebar.InvokePlaceAction(SidebarContextAction.Eject, item, item.Target!);
            ContentDialog? Dialog() => VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
                .Select(p => p.Child as ContentDialog ?? FindDescendant<ContentDialog>(p.Child, _ => true)).FirstOrDefault(d => d is not null);
            bool HasText(string key) => Dialog() is { } d && FindDescendant<TextBlock>(d, t => t.Text == StringTable.Get(key)) is not null;

            using (FileOperationLifetime.Begin())
            {
                Click();
                await Until(() => HasText("Device_EjectBusy"));
                if (requests != 0) throw new IOException("Ejected during a file transfer");
                Dialog()!.Hide();
                await Until(() => Dialog() is null && !DeviceEjectUI.IsShowingForTest);
            }
            evidence["ActualTransferBlocksRemoval"] = true;
            await Task.Delay(100);
            Click();
            await Until(() => HasText("Device_EjectPreparing"));
            evidence["FirstClickImmediatelyShowsProgress"] = true;
            Click();
            await Task.Delay(100);
            if (!HasText("Device_EjectPreparing")) throw new IOException("Second click replaced progress with busy error");
            query.SetResult(new DeviceEjectTarget(@"Z:\", "fake-device", []));
            await Until(() => requests == 1 && HasText("Device_EjectRemoving"));
            Dialog()!.Hide();
            await Task.Delay(100);
            if (Dialog() is null || !FileOperationLifetime.IsBusy) throw new IOException("Pending Windows request lost its dialog or lifetime");
            evidence["PendingNativeRequestCannotBeDismissed"] = true;
            Click();
            await Task.Delay(100);
            if (requests != 1) throw new IOException("Duplicate native removal");
            result.SetResult(new(false, 23, 5));
            await Until(() => HasText("Device_EjectDenied"));
            if (FileOperationLifetime.IsBusy) throw new IOException("Result dialog retained operation lease");
            if (Dialog()?.PrimaryButtonText != StringTable.Get("Device_EjectShowBlockers"))
                throw new IOException("Denied drive did not offer file-user inspection");
            evidence["RepeatedClicksKeepOneRequest"] = true;
            evidence["WindowsDenialRemainsVisibleAndReleasesLease"] = true;
            var inspect = Dialog()!;
            var inspectButton = FindDescendant<Button>(inspect,
                button => Equals(button.Content, StringTable.Get("Device_EjectShowBlockers")))
                ?? throw new IOException("File-user button was not rendered");
            var inspectPeer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(inspectButton);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)inspectPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await Until(() => FindDescendant<FileLockDialog>(page, _ => true) is { IsLoaded: true });
            var blockerPanel = FindDescendant<FileLockDialog>(page, _ => true)!;
            if (blockerPanel.FindName("DeleteButton") is not Button { Visibility: Visibility.Collapsed }
                || blockerPanel.FindName("OtherButton") is not Button { Visibility: Visibility.Collapsed }
                || blockerPanel.FindName("RetryEjectButton") is not Button { Visibility: Visibility.Visible })
                throw new IOException("Eject inspection exposed destructive actions or hid retry");
            evidence["EjectFailureOpensReadOnlyFileUserView"] = true;
            DeviceEjectUI.TestQuery = _ => Task.FromResult<DeviceEjectTarget?>(new(@"Z:\", "fake-device", []));
            DeviceEjectUI.TestRequest = _ => Task.FromResult(new DeviceEjectResult(true));
            var retry = (Button)blockerPanel.FindName("RetryEjectButton");
            var retryPeer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(retry);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)retryPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await Until(() => HasText("Device_EjectSuccess"));
            evidence["FileUserViewRetriesNativeEject"] = true;
            Dialog()!.Hide();
            await Until(() => Dialog() is null && !DeviceEjectUI.IsShowingForTest);
            await Task.Delay(100);

            DeviceEjectUI.TestQuery = _ => Task.FromException<DeviceEjectTarget?>(new IOException("Test disconnected"));
            Click();
            await Until(() => Dialog() is { } d && FindDescendant<TextBlock>(d, t => t.Text == "Test disconnected") is not null);
            if (FileOperationLifetime.IsBusy) throw new IOException("Failure retained operation lease");
            evidence["DisconnectFailureReleasesLease"] = true;
            Dialog()!.Hide();
            await Until(() => Dialog() is null && !DeviceEjectUI.IsShowingForTest);
            await Task.Delay(100);
            DeviceEjectUI.TestQuery = _ => Task.FromResult<DeviceEjectTarget?>(new(@"Z:\", "fake-device", []));
            DeviceEjectUI.TestRequest = _ => Task.FromResult(new DeviceEjectResult(true));
            Click();
            await Until(() => HasText("Device_EjectSuccess"));
            if (FileOperationLifetime.IsBusy) throw new IOException("Success retained operation lease");
            evidence["RetryAfterFailureSucceeds"] = true;
            Dialog()!.Hide();
            await Until(() => Dialog() is null && !DeviceEjectUI.IsShowingForTest);
            DeviceEjectUI.TestRequest = _ => Task.FromResult(new DeviceEjectResult(false, 23, 5)
                { BlockingUsbDebugging = true, BlockingDeviceName = "ADB Interface" });
            Click();
            await Until(() => Dialog() is { } d && FindDescendant<TextBlock>(d,
                t => t.Text.StartsWith(StringTable.Get("Device_EjectDebugging"), StringComparison.Ordinal)) is not null);
            evidence["UsbDebuggingVetoExplainsNextSteps"] = true;
            Dialog()!.Hide();
            await Until(() => Dialog() is null && !DeviceEjectUI.IsShowingForTest);

            page.ViewModel.Navigate(HomeLocation.Uri);
            await Until(() => FindDescendant<HomeDashboard>(page, dashboard => dashboard.IsLoaded) is not null);
            var home = FindDescendant<HomeDashboard>(page, dashboard => dashboard.IsLoaded)!;
            await Until(() => FindDescendant<Button>(home, b => b.Tag is HomeDriveItem { Letter: "D:" }) is not null);
            var driveButton = FindDescendant<Button>(home, b => b.Tag is HomeDriveItem { Letter: "D:" })!;
            // Compiled x:Bind templates do not guarantee an item DataContext.
            // Exercise the real card and its menu after clearing inherited data.
            driveButton.DataContext = null;
            string? requestedLocation = null;
            PlaceContextFlyout.TestEjectQuery = (location, _) => Task.FromResult<DeviceEjectTarget?>(
                new(location, "fake", []));
            DeviceEjectUI.TestQuery = location => { requestedLocation = location; return Task.FromResult<DeviceEjectTarget?>(new(location, "fake", [])); };
            DeviceEjectUI.TestRequest = _ => Task.FromResult(new DeviceEjectResult(true));
            home.ShowPlaceContext(driveButton);
            MenuFlyoutItem? EjectMenu() => VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
                .Select(p => FindDescendant<MenuFlyoutItem>(p.Child, i => i.Text == StringTable.Get("Sidebar_Eject")))
                .FirstOrDefault(i => i is not null);
            await Until(() => EjectMenu()?.IsEnabled == true);
            var peer = new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(EjectMenu()!);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await Until(() => HasText("Device_EjectSuccess"));
            if (requestedLocation != @"D:\") throw new IOException("Home card lost its drive identity");
            evidence["HomeUsbCardUsesSharedMenuAndRemovalCommand"] = true;
            Dialog()!.Hide();
            await Until(() => Dialog() is null && !DeviceEjectUI.IsShowingForTest);

            var phoneButton = FindDescendant<Button>(home, b => b.Tag is HomeFolderItem f && f.Id.StartsWith("device:", StringComparison.Ordinal));
            if (phoneButton is not null)
            {
                home.ShowPlaceContext(phoneButton);
                await Until(() => EjectMenu()?.IsEnabled == true);
                evidence["HomePhoneCardOffersSafeRemoval"] = true;
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)) popup.IsOpen = false;
            }
            evidence["PhysicalEjectExecuted"] = false;
            evidence["Success"] = true;
        }
        catch (Exception error) { evidence["Success"] = false; evidence["Error"] = error.ToString(); }
        finally
        {
            DeviceEjectUI.TestQuery = null;
            DeviceEjectUI.TestRequest = null;
            PlaceContextFlyout.TestEjectQuery = null;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "eject-smoke.json"),
                System.Text.Json.JsonSerializer.Serialize(evidence, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            RequestCloseAfterFileWork();
        }
        static async Task Until(Func<bool> condition)
        {
            for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(50); }
            throw new TimeoutException("Eject dialog condition timed out");
        }
    }
}
#endif
