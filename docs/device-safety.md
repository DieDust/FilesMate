# External devices and removal safety

## Device discovery and browsing

FilesMate listens for Windows device arrival/removal notifications and refreshes the sidebar and home page. Disk enumeration handles errors per drive so a disconnected drive cannot hide the other drives.

Portable devices such as an iPad have no drive letter. Their Windows Shell identities are carried in a separate `filesmate-device:` location. A saved location must resolve to a currently connected portable device under This PC. Each child is checked against its opaque identity, including when a display name matches. These locations never enter filesystem copy, move, delete or terminal commands.

Portable devices use the ordinary NavigatorPage and FileDetailsSurface through a directory-enumerator adapter. Breadcrumbs, back/forward/up navigation, details/grid views, sorting, multiple selection, clipboard, drag-and-drop and the preview pane share the folder UI. A folder opened in a new tab inherits the source tab's navigation history; the source tab stays unchanged. Device searches currently filter names in the current folder. Windows and the device determine what content is exposed; an iPad may require unlocking and trusting the computer. This does not expose the iPad's entire filesystem.

The adapter keeps opaque Shell identities in the current pane's snapshot, separate from display names used for sorting and icons. Canceled or superseded reads cannot replace a newer identity map. Closing the pane clears the map. Device paths do not start filesystem watchers or recursive folder-size reads. Unsupported filesystem commands are disabled or omitted in menus, the toolbar, the command palette and keyboard handlers.

Photo thumbnails come from the Windows Shell provider on a bounded background worker and use the existing byte-limited thumbnail caches. The shared preview pane shows metadata and an available thumbnail. Unsupported previews explain how to open the item. No whole-file staging cache is added. Shell automation wrappers can be shared across reads, so their lifetime is managed by the CLR; forcibly releasing them can break concurrent enumeration and thumbnails. Exclusively owned operation objects, PIDLs and bitmap handles are released explicitly.

## Portable-device transfers (1.1.90)

Transfers use Windows `IFileOperation` and live Shell items resolved from the connected device, in both directions. Device destinations must advertise `SFGAO_FOLDER | SFGAO_DROPTARGET` without `SFGAO_READONLY`, and are rechecked immediately before queuing the copy. The tested iPad photo folder allows export but does not accept writes. Writable Android/MTP folders use the same Windows interface; the phone must allow file transfers. This capability check is per folder, not an iPad-name blacklist. Apple Devices provides separate photo synchronization and app file-sharing workflows for sending content to an iPad; those are not the photo directory exposed through Windows Explorer.

Windows provides progress, cancellation, conflict resolution and error UI. FilesMate does not auto-confirm replacement or skip conflicts. Only successful published items count as copied. A batch with missing sources is rejected before execution. Interrupted and skipped operations are reported separately from success. There is no FilesMate undo record for device copies; source items are always retained. Cut/move and device deletion are not offered. A cut clipboard or Shift-drag is not silently converted into a move.

The clipboard/drag payload contains bounded device identities rather than file contents. It is supported between FilesMate tabs; local files copied or dragged from Explorer can be pasted into a writable device folder. Device items copied from Explorer may lack a filesystem path and are not silently discarded. They can be copied from a FilesMate device tab instead. Direct device export to an Explorer window via clipboard is not implemented. Use a destination folder in FilesMate, or copy to the other pane in dual-pane mode.

One background STA handles transfers, independently of the bounded enumeration worker. Large files are streamed by the Windows provider without a FilesMate whole-file memory/disk cache. The file-operation lifetime remains active until the native operation actually returns, even after cancellation or a tab closes. Application exit, language restart, updates and drive eject wait for that lifetime. Windows/device providers determine how partial destination files are handled after failure; FilesMate cannot remove a partial file from a disconnected device.

Shell enumeration runs on a background STA, with one operation at a time. Canceling a read releases the caller promptly. A blocked driver call retains the worker slot until it returns; repeated refreshes cannot create unlimited blocked threads. Device tabs follow the ordinary tab memory policy, and closed/disposed panes release their rows and cancel pending requests.

## Removal behavior

- A volume-removal notification invalidates the affected pane's generation, cancels its directory read/watch, clears stale entries and previews, and preserves the address for retry. Other volumes remain open.
- Safe removal is available from the sidebar and home-page context menus when Windows exposes a removable hardware node. Drive letters are mapped to storage device numbers and PnP identities, including USB disks reported as `Fixed`. All mounted volumes belonging to that removal node are included. Portable devices are matched to their connected Windows device interface. System volumes and USB hubs/controllers are excluded.
- FilesMate releases its own folder handles first, including tabs at affected drive roots. An ongoing file operation prevents initiating eject from FilesMate. The identity is resolved again immediately before `CM_Request_Device_EjectW` to avoid removing a different device after drive-letter reuse. Windows handles query-remove, flush and veto; failure leaves the device connected and asks the user to close files/programs using it. FilesMate never forces a dismount, terminates owners or changes write-cache policy. Optical media use the Windows Shell `eject` verb on a worker.
- Filesystem copies use an owned sibling staging file. The existing implementation flushes it to disk before publishing the final name. A cross-volume move removes the leased source object only after publishing the copied destination. Failed/canceled copies are not added to completed/undo results. Replacements prepare incoming data before replacing the existing version.
- If a device physically disappears, removing a partial staging file on that device may also fail. FilesMate cannot guarantee cleanup on an absent device or filesystem integrity after interrupted writes. Successfully flushing data also depends on the filesystem, driver and hardware honoring the request.

Checking safe-removal capability runs off the UI thread and is canceled when the menu closes. A driver that does not expose an eligible removable node has no enabled eject command; this does not prevent ordinary browsing.

## Verification

Tests cover portable URI validation, rejecting unrelated children and non-drive eject targets, cancellation before entering a driver, unrelated-volume isolation, releasing entries after removal, refresh recovery, and interrupted cross-volume moves retaining their source. Existing transfer tests also cover replacement cancellation and source identity changes.

The isolated UI smoke test reads a connected iPad, checks sidebar discovery, nested navigation, hidden/closed tab release and rejection of a missing device with the same display name. It sends a volume-removal message only to the test window; it does not physically unplug or eject any user drive. Hardware power-loss behavior is not simulated by this test.

Local verification on 2026-09-22: 297 platform tests and 868 app tests passed. A connected iPad exposed one storage root and 48 child folders; nested reads succeeded. The native-window smoke test passed discovery on both home/sidebar, simulated removal and refresh, releasing the drive root before eject, tab reactivation and cleanup. Version 1.1.89 was installed locally; all 1,519 payload files matched and 17 settings files were preserved. The separate desktop automation connection was unavailable; these UI checks ran through the application's isolated test build.

Transfer verification for 1.1.90: 318 platform tests and 868 app tests passed. Tests include real Shell copies with nested folders and Unicode names, byte-for-byte content checks, pre-cancellation, missing sources, and skipped children inside a folder merge. A connected iPad file was copied through the device page's actual Copy command, the Windows clipboard (including `Flush`), and the ordinary folder's Paste handler. The copied size matched, the source remained present, and the file-operation lease ended. The device's read-only destination was rejected before any write was queued. No writable Android phone was available for a physical upload test. No device was unplugged during a write.

The same UI smoke verified deferred drag payloads, local-file clipboard input, rendered row labels, tab reactivation and disposal. Version 1.1.90 was installed locally on 2026-09-22; all 1,519 payload files matched, 17 settings files were preserved, and both the main application and independent search host restarted successfully.

Verification for 1.1.91: 875 app tests and 327 platform tests passed. A native UI test on the connected iPad passed ordinary-pane browsing, new-tab back/forward history, shared selection/sort/layout, photo thumbnail and shared preview, eight rounds of concurrent metadata/list/thumbnail reads, clipboard copy and paste to a temporary local folder, missing-device recovery and tab disposal. It checked enabled safe-eject context-menu entries for the iPad and USB X: disk, and verified that the system volume was excluded. It did not invoke physical removal, disconnect hardware during a write or upload to an Android device. The temporary exported file was removed and the original device file was retained. The iPad photo folder's read-only status came from its live Windows Shell capabilities.

Version 1.1.91 was installed locally on 2026-09-22. All 1,519 payload files matched their packaged hashes, all 17 settings files were preserved, and the main application and independent search host restarted and responded normally. The installation verification is saved under `artifacts/local-upgrade-1.1.91`.

## References reviewed (2026-09-22)

- [Files: drive eject via Windows Shell](https://github.com/files-community/Files/blob/main/src/Files.App/Utils/Storage/Helpers/DriveHelpers.cs)
- [Files: device watcher and removal events](https://github.com/files-community/Files/blob/main/src/Files.App/Utils/Global/WindowsStorageDeviceWatcher.cs)
- [Microsoft: detecting media insertion or removal](https://learn.microsoft.com/en-us/windows/win32/devio/detecting-media-insertion-or-removal)
- [Microsoft: Quick removal and Better performance policies](https://learn.microsoft.com/en-au/windows/client-management/client-tools/change-default-removal-policy-external-storage-media)
- [Microsoft: safely remove hardware](https://support.microsoft.com/en-au/windows/hardware/safely-remove-hardware-in-windows)
- [Apple: transfer photos and videos to a PC](https://support.apple.com/en-us/120267)
- [Microsoft: Shell file copying](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-copyitem)
- [Microsoft: operation flags and confirmation behavior](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)
- [Microsoft: recursive operation progress callbacks](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperationprogresssink)
- [Microsoft: custom clipboard stream formats](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.datapackage.setdata)
- [Microsoft: PnP safe removal and veto results](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_request_device_ejectw)
- [Microsoft: storage device numbers](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_get_device_number)
- [Microsoft: Shell thumbnail extraction](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemimagefactory-getimage)
- [Microsoft: shared COM wrapper lifetime](https://devblogs.microsoft.com/visualstudio/marshal-releasecomobject-considered-dangerous/)
- [Apple: app file sharing with Apple Devices](https://support.apple.com/guide/devices-windows/transfer-files-between-your-devices-mchl4bd77d3a/windows)
- [Apple: syncing photos to an iPad](https://support.apple.com/en-au/guide/devices-windows/mchl4af095d3/windows)
# Safe-removal feedback regression (1.1.92)

The first removal request previously held an application operation lease without
showing progress. A second click opened a generic busy dialog. When Windows
returned, the first request attempted to open another ContentDialog and failed:
`Only a single ContentDialog can be open at any time.` This was confirmed in the
local application's exception log on 2026-09-22. The Windows result was therefore
hidden behind an obsolete busy message; that message did not establish that a
file transfer was still running.

Removal now owns one dialog for preparation, the native request and the result.
The idle check and operation reservation are atomic; duplicate clicks do not
start another request. The operation lease ends when Windows returns, before
the user dismisses the result. A real transfer still blocks removal, and there
is no timeout that claims success or force-removes hardware.

The UI regression exercises the sidebar command with controlled native responses:
active transfer, delayed preparation, repeated clicks, delayed Windows response,
attempted dismissal while pending, Windows veto, disconnected device and retry.
It does not physically eject the user's hardware.

## Home menu and USB debugging veto (1.1.93)

Home drive and portable-device cards now retain their typed navigation identity in
the card itself. The shared context menu therefore offers safe removal on eligible
home cards, just as it does in the sidebar. The native UI smoke test exercised the
actual home drive card, its menu command and the resulting dialog with a simulated
Windows response; it did not eject a physical device.

The local phone removal attempt on 2026-09-23 returned `CR=23`,
`PNP_VetoOutstandingOpen`, with the blocking instance ending in `MI_01`. Windows
identified that exact interface as `ADB Interface` with USB debugging protocol
`Class_ff&SubClass_42&Prot_01`; `adb.exe` was also running. FilesMate now resolves
the vetoed device node and shows a specific USB debugging message only when that
protocol matches. It does not terminate ADB, disconnect a debugging session, or
claim the phone was removed. The user can exit the debugging/mirroring application
or turn off USB debugging on the phone, then retry. A current physical eject has
not been verified because the phone was disconnected during testing.

This follows Windows' documented query-remove veto rather than forcing removal:
[PnP veto types](https://learn.microsoft.com/en-us/windows/win32/api/cfg/ne-cfg-pnp_veto_type),
[CM_Request_Device_EjectW](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_request_device_ejectw),
and [Android USB debugging](https://developer.android.com/tools/adb).
