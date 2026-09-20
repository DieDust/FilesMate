using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Shell;

[SupportedOSPlatform("windows")]
public static class ShellContextMenu
{
    public const string HostClassName = "FilesMate.ShellContextMenuHost";

    private const uint CmdFirst = 1;
    private const uint CmdLast = 0x7FFF;

    private static readonly User32.WndProc HostWndProc = HandleHostMessage;
    private static bool _hostClassRegistered;
    private static IContextMenu2? _menu2;
    private static IContextMenu3? _menu3;

    public static bool TryShow(
        nint hwnd,
        IReadOnlyList<string> itemPaths,
        string? folderPath,
        bool background,
        double clientDipX,
        double clientDipY,
        double rasterizationScale,
        bool extendedVerbs)
    {
        if (hwnd == 0)
        {
            return false;
        }

        nint host = 0;
        try
        {
            host = CreateHostWindow();
            if (host == 0)
            {
                return false;
            }

            var screen = ToScreen(hwnd, clientDipX, clientDipY, rasterizationScale);
            nint menuUnk;
            if (background)
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                {
                    return false;
                }

                menuUnk = CreateBackgroundMenu(host, folderPath);
            }
            else
            {
                var paths = ExistingPaths(itemPaths);
                if (paths.Count == 0)
                {
                    return false;
                }

                menuUnk = CreateItemMenu(host, paths);
            }

            return menuUnk != 0 && Popup(host, menuUnk, screen.X, screen.Y, extendedVerbs, itemMenu: !background);
        }
        catch
        {
            return false;
        }
        finally
        {
            _menu2 = null;
            _menu3 = null;
            if (host != 0)
            {
                _ = User32.DestroyWindow(host);
            }
        }
    }

    public static bool TryCreate(
        nint hwnd,
        IReadOnlyList<string> itemPaths,
        string? folderPath,
        bool background,
        bool extendedVerbs,
        out ShellContextMenuSession? session)
    {
        session = null;
        if (hwnd == 0)
        {
            return false;
        }

        nint host = 0;
        nint popup = 0;
        IContextMenu? menu = null;
        try
        {
            host = CreateHostWindow();
            if (host == 0)
            {
                return false;
            }

            nint menuUnk;
            if (background)
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                {
                    return false;
                }

                menuUnk = CreateBackgroundMenu(host, folderPath);
            }
            else
            {
                var paths = ExistingPaths(itemPaths);
                if (paths.Count == 0)
                {
                    return false;
                }

                menuUnk = CreateItemMenu(host, paths);
            }

            if (menuUnk == 0)
            {
                return false;
            }

            BindHandlers(menuUnk);
            menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuUnk);
            Marshal.Release(menuUnk);
            popup = User32.CreatePopupMenu();
            if (popup == 0)
            {
                return false;
            }

            var flags = Shell32.CmfExplore | Shell32.CmfCanRename;
            if (!background)
            {
                flags |= Shell32.CmfItemMenu;
            }

            if (extendedVerbs)
            {
                flags |= Shell32.CmfExtendedVerbs;
            }

            if (menu.QueryContextMenu(popup, 0, CmdFirst, CmdLast, flags) < 0)
            {
                return false;
            }

            var items = ReadItems(popup);
            if (items.Count == 0)
            {
                return false;
            }

            session = new ShellContextMenuSession(host, menu, popup, items);
            host = 0;
            popup = 0;
            menu = null;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (session is null)
            {
                _menu2 = null;
                _menu3 = null;
                if (popup != 0)
                {
                    _ = User32.DestroyMenu(popup);
                }

                Release(menu);
                if (host != 0)
                {
                    _ = User32.DestroyWindow(host);
                }
            }
        }
    }

    public static IReadOnlyList<ShellMenuItem> ReadItems(nint menu) => ReadMenu(menu, 0);

    public static string DisplayLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Replace("&", string.Empty, StringComparison.Ordinal);
        var tab = text.IndexOf('\t');
        if (tab >= 0)
        {
            text = text[..tab];
        }

        return text.Trim();
    }

    private static IReadOnlyList<ShellMenuItem> ReadMenu(nint menu, int depth)
    {
        if (menu == 0 || depth > 3)
        {
            return [];
        }

        var count = User32.GetMenuItemCount(menu);
        if (count <= 0)
        {
            return [];
        }

        var items = new List<ShellMenuItem>(count);
        var separatorPending = false;
        for (uint i = 0; i < (uint)count && items.Count < 80; i++)
        {
            var info = new MENUITEMINFOW
            {
                cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
                fMask = User32.MiimState | User32.MiimId | User32.MiimSubmenu | User32.MiimFtype,
            };
            if (!User32.GetMenuItemInfoW(menu, i, true, ref info))
            {
                continue;
            }

            if ((info.fType & User32.MftSeparator) != 0)
            {
                separatorPending = items.Count > 0;
                continue;
            }

            var label = ReadLabel(menu, i);
            var children = info.hSubMenu == 0 ? Array.Empty<ShellMenuItem>() : ReadMenu(info.hSubMenu, depth + 1);
            if (label.Length == 0 && children.Count == 0)
            {
                continue;
            }

            if (separatorPending)
            {
                items.Add(new ShellMenuItem(string.Empty, 0, true, false, []));
                separatorPending = false;
            }

            items.Add(new ShellMenuItem(
                label.Length == 0 ? "…" : label,
                info.wID,
                false,
                (info.fState & User32.MfsGrayed) == 0,
                children));
        }

        return items;
    }

    private static string ReadLabel(nint menu, uint index)
    {
        var info = new MENUITEMINFOW
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
            fMask = User32.MiimString,
            cch = 512,
        };
        info.dwTypeData = Marshal.AllocHGlobal(((int)info.cch + 1) * 2);
        try
        {
            if (!User32.GetMenuItemInfoW(menu, index, true, ref info) || info.dwTypeData == 0)
            {
                return string.Empty;
            }

            return DisplayLabel(Marshal.PtrToStringUni(info.dwTypeData));
        }
        finally
        {
            Marshal.FreeHGlobal(info.dwTypeData);
        }
    }

    private static List<string> ExistingPaths(IReadOnlyList<string> itemPaths)
    {
        var paths = new List<string>(itemPaths.Count);
        foreach (var path in itemPaths)
        {
            if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
            {
                paths.Add(path);
            }
        }

        if (paths.Count <= 1)
        {
            return paths;
        }

        var parent = Path.GetDirectoryName(paths[0]);
        paths.RemoveAll(path =>
            !string.Equals(Path.GetDirectoryName(path), parent, StringComparison.OrdinalIgnoreCase));
        return paths;
    }

    private static POINT ToScreen(nint hwnd, double clientDipX, double clientDipY, double scale)
    {
        var safeScale = scale <= 0 ? 1 : scale;
        var pt = new POINT
        {
            X = (int)Math.Round(clientDipX * safeScale),
            Y = (int)Math.Round(clientDipY * safeScale),
        };
        _ = User32.ClientToScreen(hwnd, ref pt);
        return pt;
    }

    private static nint CreateHostWindow()
    {
        var instance = Kernel32.GetModuleHandleW(null);
        if (instance == 0 || !EnsureHostClass(instance))
        {
            return 0;
        }

        return User32.CreateWindowExW(
            User32.WsExToolwindow | User32.WsExNoActivate,
            HostClassName,
            string.Empty,
            User32.WsPopup,
            0,
            0,
            1,
            1,
            0,
            0,
            instance,
            0);
    }

    private static bool EnsureHostClass(nint instance)
    {
        if (_hostClassRegistered)
        {
            return true;
        }

        var wndClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(HostWndProc),
            hInstance = instance,
            lpszClassName = HostClassName,
        };
        if (User32.RegisterClassExW(in wndClass) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != User32.ErrorClassAlreadyExists)
            {
                return false;
            }
        }

        _hostClassRegistered = true;
        return true;
    }

    private static nint CreateItemMenu(nint hwnd, IReadOnlyList<string> paths)
    {
        IShellFolder? parent = null;
        var pidls = new List<nint>(paths.Count);
        try
        {
            foreach (var path in paths)
            {
                if (Shell32.SHParseDisplayName(path, 0, out var absolute, 0, out _) != 0 || absolute == 0)
                {
                    continue;
                }

                var iid = Shell32.IidIShellFolder;
                var hr = Shell32.SHBindToParent(absolute, ref iid, out var folderUnk, out var last);
                if (hr != 0 || folderUnk == 0 || last == 0)
                {
                    Shell32.ILFree(absolute);
                    continue;
                }

                var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderUnk);
                Marshal.Release(folderUnk);
                if (parent is null)
                {
                    parent = folder;
                }
                else
                {
                    Release(folder);
                }

                pidls.Add(Shell32.ILClone(last));
                Shell32.ILFree(absolute);
            }

            if (parent is null || pidls.Count == 0)
            {
                return 0;
            }

            var iidMenu = Shell32.IidIContextMenu;
            var array = pidls.ToArray();
            if (parent.GetUIObjectOf(hwnd, (uint)array.Length, array, ref iidMenu, 0, out var menuUnk) != 0
                || menuUnk == 0)
            {
                return 0;
            }

            return menuUnk;
        }
        finally
        {
            foreach (var pidl in pidls)
            {
                if (pidl != 0)
                {
                    Shell32.ILFree(pidl);
                }
            }

            Release(parent);
        }
    }

    private static nint CreateBackgroundMenu(nint hwnd, string folderPath)
    {
        if (Shell32.SHGetDesktopFolder(out var desktopUnk) != 0 || desktopUnk == 0)
        {
            return 0;
        }

        var desktop = (IShellFolder)Marshal.GetObjectForIUnknown(desktopUnk);
        Marshal.Release(desktopUnk);
        nint folderPidl = 0;
        IShellFolder? folder = null;
        try
        {
            if (Shell32.SHParseDisplayName(folderPath, 0, out folderPidl, 0, out _) != 0 || folderPidl == 0)
            {
                return 0;
            }

            var iidFolder = Shell32.IidIShellFolder;
            if (desktop.BindToObject(folderPidl, 0, ref iidFolder, out var folderUnk) != 0 || folderUnk == 0)
            {
                return 0;
            }

            folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderUnk);
            Marshal.Release(folderUnk);
            var iidMenu = Shell32.IidIContextMenu;
            if (folder.CreateViewObject(hwnd, ref iidMenu, out var menuUnk) == 0 && menuUnk != 0)
            {
                return menuUnk;
            }

            var iidParent = Shell32.IidIShellFolder;
            if (Shell32.SHBindToParent(folderPidl, ref iidParent, out var parentUnk, out var last) != 0
                || parentUnk == 0
                || last == 0)
            {
                return 0;
            }

            var parent = (IShellFolder)Marshal.GetObjectForIUnknown(parentUnk);
            Marshal.Release(parentUnk);
            try
            {
                var child = Shell32.ILClone(last);
                try
                {
                    if (parent.GetUIObjectOf(hwnd, 1, [child], ref iidMenu, 0, out var fallback) != 0)
                    {
                        return 0;
                    }

                    return fallback;
                }
                finally
                {
                    Shell32.ILFree(child);
                }
            }
            finally
            {
                Release(parent);
            }
        }
        finally
        {
            if (folderPidl != 0)
            {
                Shell32.ILFree(folderPidl);
            }

            Release(folder);
            Release(desktop);
        }
    }

    private static bool Popup(
        nint host,
        nint menuUnk,
        int screenX,
        int screenY,
        bool extendedVerbs,
        bool itemMenu)
    {
        BindHandlers(menuUnk);
        var menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuUnk);
        Marshal.Release(menuUnk);
        var popup = User32.CreatePopupMenu();
        if (popup == 0)
        {
            Release(menu);
            return false;
        }

        try
        {
            var flags = Shell32.CmfExplore | Shell32.CmfCanRename;
            if (itemMenu)
            {
                flags |= Shell32.CmfItemMenu;
            }

            if (extendedVerbs)
            {
                flags |= Shell32.CmfExtendedVerbs;
            }

            if (menu.QueryContextMenu(popup, 0, CmdFirst, CmdLast, flags) < 0)
            {
                return false;
            }

            var command = User32.TrackPopupMenuEx(
                popup,
                Shell32.TpmLeftAlign | Shell32.TpmRightButton | Shell32.TpmReturnCmd,
                screenX,
                screenY,
                host,
                0);
            if (command == 0)
            {
                return true;
            }

            var invoke = new CMINVOKECOMMANDINFOEX
            {
                cbSize = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = Shell32.CmicMaskUnicode | Shell32.CmicMaskPtInvoke,
                hwnd = host,
                lpVerb = (nint)(command - CmdFirst),
                lpVerbW = (nint)(command - CmdFirst),
                nShow = (int)Shell32.SwShownormal,
                ptInvoke = new POINT { X = screenX, Y = screenY },
            };
            _ = menu.InvokeCommand(ref invoke);
            return true;
        }
        finally
        {
            _menu2 = null;
            _menu3 = null;
            _ = User32.DestroyMenu(popup);
            Release(menu);
        }
    }

    private static void BindHandlers(nint menuUnk)
    {
        var iid3 = Shell32.IidIContextMenu3;
        if (Marshal.QueryInterface(menuUnk, in iid3, out var p3) == 0)
        {
            _menu3 = (IContextMenu3)Marshal.GetObjectForIUnknown(p3);
            Marshal.Release(p3);
            return;
        }

        var iid2 = Shell32.IidIContextMenu2;
        if (Marshal.QueryInterface(menuUnk, in iid2, out var p2) == 0)
        {
            _menu2 = (IContextMenu2)Marshal.GetObjectForIUnknown(p2);
            Marshal.Release(p2);
        }
    }

    private static nint HandleHostMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (IsMenuMessage(msg))
        {
            try
            {
                if (_menu3 is not null)
                {
                    if (_menu3.HandleMenuMsg2(msg, wParam, lParam, out var result) == 0)
                    {
                        return result;
                    }
                }
                else if (_menu2 is not null && _menu2.HandleMenuMsg(msg, wParam, lParam) == 0)
                {
                    return 0;
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }

        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static bool IsMenuMessage(uint msg) =>
        msg is Shell32.WmInitMenu
            or Shell32.WmInitMenuPopup
            or Shell32.WmDrawItem
            or Shell32.WmMeasureItem
            or Shell32.WmMenuChar;

    internal static void InvokeVerb(IContextMenu menu, nint owner, uint commandId)
    {
        var invoke = new CMINVOKECOMMANDINFOEX
        {
            cbSize = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
            fMask = Shell32.CmicMaskUnicode | Shell32.CmicMaskPtInvoke,
            hwnd = owner,
            lpVerb = (nint)(commandId - CmdFirst),
            lpVerbW = (nint)(commandId - CmdFirst),
            nShow = (int)Shell32.SwShownormal,
        };
        _ = menu.InvokeCommand(ref invoke);
    }

    internal static void DropHandlers()
    {
        _menu2 = null;
        _menu3 = null;
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    internal static void ReleaseCom(object? value) => Release(value);
}
