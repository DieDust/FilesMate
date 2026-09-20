#Requires -Version 7
param(
    [Parameter(Mandatory)][string]$TestExe,
    [switch]$Baseline,
    [switch]$WinUIHost
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $TestExe).Path
if ($exe -notlike '*\artifacts\*') { throw 'Use an isolated UI test build under artifacts.' }
$dir = Split-Path $exe
$profile = Join-Path $dir 'test-profile'
New-Item -ItemType Directory -Path $profile -Force | Out-Null
@{restoreLastSession=$false;showFolderSizes=$false} | ConvertTo-Json | Set-Content (Join-Path $profile 'explorer.json')
$placement = Join-Path $profile 'window.json'
@{x=60;y=60;width=1100;height=700;maximized=$false} | ConvertTo-Json | Set-Content $placement
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PlacementProbe {
    public delegate bool EnumProc(IntPtr h,IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb,IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int ht,uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h,int command);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeoutW(IntPtr h,uint m,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    public static IntPtr Find(int pid) {
        IntPtr found=IntPtr.Zero;
        EnumWindows((h,p)=> { uint id; GetWindowThreadProcessId(h,out id); if(id==pid && IsWindowVisible(h)){found=h;return false;}return true; },IntPtr.Zero);
        return found;
    }
    public static void Send(IntPtr h,uint message) {
        if(SendMessageTimeoutW(h,message,IntPtr.Zero,IntPtr.Zero,2,2000,out _)==IntPtr.Zero) throw new Exception("Window stopped responding");
    }
}
'@
$dpi = [PlacementProbe]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$hostArg = if ($WinUIHost) {'--no-native-shell'} else {'--native-shell'}
$process = Start-Process -FilePath $exe -ArgumentList $hostArg -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $hwnd = [PlacementProbe]::Find($process.Id)
        if ($hwnd -ne 0) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($hwnd -eq 0) { throw 'Window did not appear' }
    Start-Sleep -Seconds 3
    $stamp = [IO.File]::GetLastWriteTimeUtc($placement)
    $during = 0
    if (-not $WinUIHost) { [PlacementProbe]::Send($hwnd,0x231) }
    for ($i=0; $i -lt 60; $i++) {
        [void][PlacementProbe]::SetWindowPos($hwnd,0,(100+$i),120,1100,700,0x14)
        Start-Sleep -Milliseconds 20
        $next = [IO.File]::GetLastWriteTimeUtc($placement)
        if ($next -ne $stamp) { $during++; $stamp=$next }
        [PlacementProbe]::Send($hwnd,0)
    }
    if (-not $WinUIHost) {
        Start-Sleep -Milliseconds 700
        $next = [IO.File]::GetLastWriteTimeUtc($placement)
        if ($next -ne $stamp) { $during++; $stamp=$next }
    }
    if (-not $WinUIHost) { [PlacementProbe]::Send($hwnd,0x232) }
    Start-Sleep -Milliseconds 900
    $saved = Get-Content -LiteralPath $placement -Raw | ConvertFrom-Json
    if ($saved.x -ne 159 -or $saved.y -ne 120) { throw 'Final drag position was not saved' }
    if (-not $Baseline -and $during -ne 0) { throw "Wrote window.json $during times during movement" }
    Write-Host "Observed writes across 60 move samples: $during"
    $settled = [IO.File]::GetLastWriteTimeUtc($placement)
    Start-Sleep -Milliseconds 600
    if ([IO.File]::GetLastWriteTimeUtc($placement) -ne $settled) { throw 'Placement keeps saving when idle' }
    # Bounds must be captured before maximizing, even within the debounce interval.
    [void][PlacementProbe]::SetWindowPos($hwnd,0,210,140,1200,760,0x14)
    Start-Sleep -Milliseconds 100
    [void][PlacementProbe]::ShowWindowAsync($hwnd,3)
    Start-Sleep -Milliseconds 900
    $maximized = Get-Content -LiteralPath $placement -Raw | ConvertFrom-Json
    if (-not $maximized.maximized -or $maximized.width -ne 1200 -or $maximized.height -ne 760) {
        throw 'Maximizing lost the normal window dimensions'
    }
    [void][PlacementProbe]::ShowWindowAsync($hwnd,9)
    Start-Sleep -Milliseconds 600
    [void][PlacementProbe]::SetWindowPos($hwnd,0,220,150,1200,760,0x14)
    [PlacementProbe]::Send($hwnd,0x10)
    if (-not $process.WaitForExit(10000)) { throw 'Test app did not close normally' }
    $closed = Get-Content -LiteralPath $placement -Raw | ConvertFrom-Json
    if ($closed.x -ne 220 -or $closed.y -ne 150 -or $closed.maximized) { throw 'Closing lost the final bounds' }
    @{Baseline=[bool]$Baseline;WinUIHost=[bool]$WinUIHost;MoveSamples=60;ObservedWritesDuringMove=$during;FinalBoundsSaved=$true;MaximizeRestore=$true;CloseFlush=$true} |
        ConvertTo-Json | Tee-Object -FilePath (Join-Path $dir ('placement-result-' + $hostArg.TrimStart('-') + '.json'))
} finally {
    if (-not $process.HasExited) { [void]$process.CloseMainWindow() }
    [void][PlacementProbe]::SetThreadDpiAwarenessContext($dpi)
}
