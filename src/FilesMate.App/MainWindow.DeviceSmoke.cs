#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Navigation;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunDeviceSmokeAsync()
    {
        var evidence = new Dictionary<string, object>();
        DataPackage? savedClipboard = null;
        uint ownClipboard = 0;
        var stage = "startup";
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            await Until(() => TabHost.Content is NavigatorPage { IsLoaded: true });
            var page = (NavigatorPage)TabHost.Content;
            var sidebar = (NavigationSidebar)page.FindName("Sidebar");
            var device = (await PortableDeviceCatalog.LoadAsync()).First(d => d.Name.Contains("iPad", StringComparison.OrdinalIgnoreCase));
            stage = "sidebar";
            await Until(() => sidebar.PlaceFor(device.Uri, true) is not null);
            evidence["SidebarDetectsIpad"] = true;
            typeof(NavigatorPage).GetMethod("Places_PlaceChosen", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, [sidebar, device.Uri]);
            await Loaded(page, device.Uri);
            Require(ReferenceEquals(TabHost.Content, page), "Device replaced the ordinary navigator");
            Require(page.ViewModel.CanGoBack, "Device root lost navigation origin");
            page.ViewModel.Back();
            await Until(() => HomeLocation.IsHome(page.ViewModel.AddressText));
            page.ViewModel.Forward();
            await Loaded(page, device.Uri);
            evidence["DeviceUsesOrdinaryNavigatorAndHistory"] = true;
            stage = "device-storage";
            page.ViewModel.Open(page.ViewModel.Store!.Snapshot().First(e => e.Kind == EntryKind.Directory));
            await Until(() => !page.ViewModel.IsLoading && page.ViewModel.ItemCount > 0 && page.ViewModel.AddressText != device.Uri);
            var storageUri = page.ViewModel.AddressText;
            var child = page.ViewModel.Store!.Snapshot().First(e => e.Kind == EntryKind.Directory);
            var childUri = page.ViewModel.FullPath(child);
            var surface = (FileDetailsSurface)page.FindName("FileSurface");
            typeof(NavigatorPage).GetMethod("FileSurface_OpenInNewTabRequested", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, [surface, child]);
            await Until(() => TabHost.Content is NavigatorPage n && !ReferenceEquals(n, page) && n.IsLoaded);
            var childPage = (NavigatorPage)TabHost.Content;
            var childTab = (TabViewItem)Tabs.SelectedItem;
            await Loaded(childPage, childUri);
            Require(childPage.ViewModel.CanGoBack, "New child tab has no back history");
            childPage.ViewModel.Back();
            await Loaded(childPage, storageUri);
            childPage.ViewModel.Forward();
            await Loaded(childPage, childUri);
            evidence["ChildTabBackAndForward"] = true;
            surface = (FileDetailsSurface)childPage.FindName("FileSurface");
            Require(surface.IsPortableDevice && !surface.IsFolderWritable, "iPad write capability was incorrect");
            var sample = childPage.ViewModel.Store!.Snapshot().First(e => e.Kind == EntryKind.File && e.Size is > 0 and < 10 * 1024 * 1024);
            var sampleUri = childPage.ViewModel.FullPath(sample);
            surface.RestoreSelectedNames([sample.Name]);
            Require(surface.Selection.Count == 1, "Shared file surface selection failed");
            childPage.ViewModel.SetSortColumn(EntrySortColumn.Size);
            await Until(() => childPage.ViewModel.ViewIndex?.Sort.Column == EntrySortColumn.Size);
            surface.SetLayout(FileLayoutKind.Grid);
            await Task.Delay(250);
            surface.SetLayout(FileLayoutKind.Details);
            surface.RestoreSelectedNames([sample.Name]);
            evidence["SharedSelectionSortAndViews"] = true;
            evidence["IpadPhotoFolderReadOnly"] = !childPage.ViewModel.CanReceiveFiles;
            stage = "preview";
            var preview = await new Preview.Providers.DevicePreviewProvider().CreateAsync(new(sampleUri, 1));
            Require(preview is Preview.PreviewResult.Device { Thumbnail: not null }, "Device photo thumbnail unavailable");
            evidence["DevicePhotoThumbnail"] = true;
            for (var repetition = 0; repetition < 8; repetition++)
            {
                PortableDeviceLocation.TryParse(sampleUri, out var sampleLocation);
                PortableDeviceLocation.TryParse(childUri, out var sampleParent);
                await Task.WhenAll(
                    PortableDeviceService.GetDetailsAsync(sampleLocation, CancellationToken.None),
                    PortableDeviceService.ReadFolderAsync(sampleParent, CancellationToken.None),
                    PortableDeviceService.GetThumbnailAsync(sampleLocation, 96, CancellationToken.None));
            }
            evidence["ConcurrentDeviceReadsAndThumbnails"] = true;
            typeof(NavigatorPage).GetMethod("SetPreviewVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(childPage, [true]);
            await Until(() => FindDescendant<Image>(childPage, i => i.Name == "ImageContent" && i.Source is not null) is not null);
            evidence["SharedPreviewPane"] = true;
            stage = "copy";
            var previous = Clipboard.GetContent();
            savedClipboard = new DataPackage { RequestedOperation = previous.RequestedOperation };
            foreach (var format in previous.AvailableFormats) savedClipboard.SetData(format, await previous.GetDataAsync(format));
            AppWindow.Move(new Windows.Graphics.PointInt32(40, 40));
            Activate();
            var actions = (PaneFileActions)typeof(NavigatorPage).GetField("_fileActions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(childPage)!;
            await actions.RunAsync(AppCommandId.Copy);
            ownClipboard = GetDeviceTestClipboardSequenceNumber();
            var view = Clipboard.GetContent();
            Require((await DeviceTransferUI.ReadItemsAsync(view)).Single() == sampleUri, "Copy lost the selected Shell identity");
            var output = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "test-profile", "device-copy-" + Guid.NewGuid().ToString("N"))).FullName;
            var refreshed = false;
            var destinationActions = new PaneFileActions(new FilesMate.Platform.Windows.Operations.WindowsLocalFileOperations(),
                () => [], () => output, () => null, _ => { }, () => refreshed = true, _ => Task.CompletedTask, childPage);
            await destinationActions.PasteItemsAsync(view);
            var copied = Directory.GetFiles(output);
            Require(refreshed && copied.Length == 1 && (ulong)new FileInfo(copied[0]).Length == sample.Size, "Device copy size mismatch");
            Require(!FileOperationLifetime.IsBusy, "Copy retained a lifetime lease");
            evidence["SharedCopyClipboardAndPasteToDisk"] = true;
            File.Delete(copied[0]); Directory.Delete(output);
            stage = "eject-capabilities";
            var external = await SafeDeviceEject.QueryAsync(@"X:\");
            Require(external is not null && external.AffectedRoots.Contains(@"X:\"), "USB fixed disk has no safe removal target");
            Require(await SafeDeviceEject.QueryAsync(Path.GetPathRoot(Environment.SystemDirectory)!) is null, "System disk exposes eject");
            evidence["UsbFixedDiskSafeEjectRecognized"] = true;
            evidence["IpadSafeEjectRecognized"] = await SafeDeviceEject.QueryAsync(device.Uri) is not null;
            evidence["PhysicalEjectExecuted"] = false;
            var currentSidebar = (NavigationSidebar)childPage.FindName("Sidebar");
            await CheckEjectMenu(new NavigationItem("drive:X:", "USB disk", "\uEDA2", @"X:\"));
            await CheckEjectMenu(new NavigationItem("device:" + device.Root, device.Name, "\uE8EA", device.Uri));
            evidence["SafeEjectContextMenuEnabled"] = true;
            stage = "screenshot";
            surface.RestoreSelectedNames([sample.Name]);
            await Until(() => FindDescendant<TextBlock>(surface, t => t.Text == sample.Name) is not null);
            await Task.Delay(300);
            await Capture(childPage, "device-unified.png");
            stage = "missing-device";
            var absent = device with { Root = PortableDeviceLocation.ComputerPrefix + @"\\?\usb#absent#" + PortableDeviceLocation.InterfaceId };
            childPage.ViewModel.Navigate(absent.Uri);
            await Until(() => !childPage.ViewModel.IsLoading && childPage.ViewModel.ErrorText is not null);
            Require(childPage.ViewModel.ItemCount == 0 && childPage.ViewModel.CanGoBack, "Missing device retained stale rows or lost history");
            evidence["MissingDeviceClearsRowsAndPreservesHistory"] = true;
            CloseTab(childTab);
            await Until(() => childTab.Tag is null && childPage.Content is null);
            evidence["CloseReleasesContent"] = true;
            evidence["Success"] = true;

            async Task CheckEjectMenu(NavigationItem item)
            {
                PlaceContextFlyout.Show(currentSidebar, item, (_, _, _) => throw new IOException("The test must not eject hardware."));
                await Until(() => Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(childPage.XamlRoot)
                    .Select(p => FindDescendant<MenuFlyoutItem>(p.Child,
                        i => i.Text == Localization.StringTable.Get("Sidebar_Eject") && i.IsEnabled)).Any(i => i is not null));
                foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(childPage.XamlRoot)) popup.IsOpen = false;
            }
        }
        catch (Exception error) { evidence["Success"] = false; evidence["Stage"] = stage; evidence["Error"] = error.ToString(); }
        finally
        {
            if (savedClipboard is not null && ownClipboard != 0 && GetDeviceTestClipboardSequenceNumber() == ownClipboard)
            {
                try { Clipboard.SetContent(savedClipboard); Clipboard.Flush(); }
                catch (Exception error) { evidence["ClipboardRestoreError"] = error.Message; }
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "device-smoke.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            RequestCloseAfterFileWork();
        }
        static Task Loaded(NavigatorPage page, string path) => Until(() => page.ViewModel.AddressText == path && !page.ViewModel.IsLoading && page.ViewModel.ItemCount > 0);
        static async Task Until(Func<bool> condition)
        {
            for (var i = 0; i < 600; i++) { if (condition()) return; await Task.Delay(50); }
            throw new TimeoutException("Device UI condition timed out.");
        }
        static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber")]
    private static extern uint GetDeviceTestClipboardSequenceNumber();
}
#endif
