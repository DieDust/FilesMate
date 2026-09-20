#Requires -Version 7
param([Parameter(Mandatory)][string]$PlatformAssembly, [string]$ImagePath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repoRoot ('artifacts\shell-menu-popup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$picture = Join-Path $fixture '菜单 图片.png'
[IO.File]::WriteAllBytes($picture, [Convert]::FromBase64String(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5WQAAAAASUVORK5CYII='))
Add-Type @'
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
public static class ShellMenuPopupProbe {
    [StructLayout(LayoutKind.Sequential)] struct GuiInfo {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint id, ref GuiInfo info);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr window, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("ole32.dll")] static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] static extern void OleUninitialize();
    public static void Run(string assembly, string image, string folder) {
        Exception failure = null;
        var ui = new Thread(() => {
            var initialized = OleInitialize(IntPtr.Zero) >= 0;
            try {
                var method = Assembly.LoadFrom(assembly)
                    .GetType("FilesMate.Platform.Windows.Shell.ShellContextMenu").GetMethod("TryShow");
                var ownerThread = GetCurrentThreadId();
                foreach (var background in new[] { false, true }) {
                    bool observed = false;
                    using var finished = new ManualResetEventSlim();
                    var cancel = new Thread(() => {
                        while (!finished.Wait(30)) {
                            var info = new GuiInfo { Size = (uint)Marshal.SizeOf<GuiInfo>() };
                            // Query only this probe's UI thread; never send input to other apps.
                            if (GetGUIThreadInfo(ownerThread, ref info) &&
                                (info.Flags & 4) != 0 && info.MenuOwner != IntPtr.Zero) {
                                observed = true;
                                Thread.Sleep(300);
                                PostMessageW(info.MenuOwner, 0x001F, IntPtr.Zero, IntPtr.Zero);
                                return;
                            }
                        }
                    }) { IsBackground = true };
                    cancel.Start();
                    bool shown;
                    try {
                        shown = (bool)method.Invoke(null, new object[] {
                            GetDesktopWindow(), background ? Array.Empty<string>() : new[] { image },
                            folder, background, 80d, 80d, 1d, false });
                    } finally { finished.Set(); cancel.Join(); }
                    if (!shown || !observed) throw new Exception("Native menu was not observed: background=" + background);
                }
            } catch (Exception error) { failure = error; }
            finally { if (initialized) OleUninitialize(); }
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        if (!ui.Join(TimeSpan.FromSeconds(25))) throw new TimeoutException("Native menu popup timed out.");
        if (failure != null) throw new Exception("Native menu popup failed.", failure);
    }
}
'@
[ShellMenuPopupProbe]::Run([IO.Path]::GetFullPath($PlatformAssembly), $picture, $fixture)
if ($ImagePath) {
    $additionalImage = (Get-Item -LiteralPath $ImagePath).FullName
    [ShellMenuPopupProbe]::Run([IO.Path]::GetFullPath($PlatformAssembly), $additionalImage, $fixture)
}
@{Passed=$true;ImageMenu='Shown and dismissed';BackgroundMenu='Shown and dismissed';AdditionalImage=$ImagePath;Fixture=$fixture} |
    ConvertTo-Json | Tee-Object -FilePath (Join-Path $fixture 'result.json')
