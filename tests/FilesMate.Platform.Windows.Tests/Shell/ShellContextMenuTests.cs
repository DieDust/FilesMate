using System.Runtime.Versioning;

using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

[SupportedOSPlatform("windows")]
public sealed class ShellContextMenuTests
{
    [Fact]
    public void Native_shell_creates_item_multi_selection_and_background_menus()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var text = Path.Combine(root, "菜单 测试.txt");
        var image = Path.Combine(root, "图片.png");
        var folder = Path.Combine(root, "子文件夹");
        File.WriteAllText(text, "menu fixture");
        File.WriteAllBytes(image, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5WQAAAAASUVORK5CYII="));
        Directory.CreateDirectory(folder);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var initialized = OleInitialize(0) >= 0;
            try
            {
                foreach (var paths in new[] { new[] { text }, new[] { image }, new[] { folder }, new[] { text, image } })
                {
                    Assert.True(ShellContextMenu.TryCreate(GetDesktopWindow(), paths, root,
                        background: false, extendedVerbs: false, out var menu));
                    using (menu)
                    {
                        Assert.NotNull(menu);
                        Assert.NotEmpty(menu.Items);
                    }
                }

                Assert.True(ShellContextMenu.TryCreate(GetDesktopWindow(), [], root,
                    background: true, extendedVerbs: false, out var background));
                using (background)
                {
                    Assert.NotNull(background);
                    Assert.NotEmpty(background.Items);
                }
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                if (initialized) OleUninitialize();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Native shell menu creation timed out.");
            if (failure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            if (!thread.IsAlive) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_window_does_not_show_a_menu()
    {
        Assert.False(ShellContextMenu.TryShow(
            hwnd: 0,
            itemPaths: [Path.GetTempPath()],
            folderPath: Path.GetTempPath(),
            background: false,
            clientDipX: 0,
            clientDipY: 0,
            rasterizationScale: 1,
            extendedVerbs: false));
    }

    [Fact]
    public void Missing_paths_do_not_show_a_menu()
    {
        Assert.False(ShellContextMenu.TryShow(
            hwnd: 1,
            itemPaths: [],
            folderPath: null,
            background: false,
            clientDipX: 0,
            clientDipY: 0,
            rasterizationScale: 1,
            extendedVerbs: false));
    }

    [Fact]
    public void Host_window_class_is_a_win32_window_not_the_xaml_hwnd()
    {
        Assert.Equal("FilesMate.ShellContextMenuHost", ShellContextMenu.HostClassName);
    }

    [Fact]
    public void Missing_window_does_not_create_a_session()
    {
        Assert.False(ShellContextMenu.TryCreate(
            hwnd: 0,
            itemPaths: [Path.GetTempPath()],
            folderPath: Path.GetTempPath(),
            background: true,
            extendedVerbs: false,
            out var session));
        Assert.Null(session);
    }

    [Fact]
    public void Display_label_strips_accelerators_and_shortcuts()
    {
        Assert.Equal("Open with", ShellContextMenu.DisplayLabel("Open &with\tO"));
        Assert.Equal(string.Empty, ShellContextMenu.DisplayLabel("   "));
    }

    [Fact]
    public void Read_items_keeps_labels_separators_and_nested_entries()
    {
        var menu = CreatePopupMenu();
        var nested = CreatePopupMenu();
        try
        {
            Assert.True(AppendMenuW(nested, 0, 40, "&Nested"));
            Assert.True(AppendMenuW(menu, 0, 10, "&Open\tO"));
            Assert.True(AppendMenuW(menu, 0x800, 0, null));
            Assert.True(AppendMenuW(menu, 0x10, (nuint)nested, "More"));
            var items = ShellContextMenu.ReadItems(menu);
            Assert.Equal(3, items.Count);
            Assert.Equal("Open", items[0].Label);
            Assert.Equal(10u, items[0].CommandId);
            Assert.True(items[1].IsSeparator);
            Assert.Equal("More", items[2].Label);
            Assert.Equal("Nested", Assert.Single(items[2].Children).Label);
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint CreatePopupMenu();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetDesktopWindow();

    [System.Runtime.InteropServices.DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int OleInitialize(nint reserved);

    [System.Runtime.InteropServices.DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void OleUninitialize();
}
