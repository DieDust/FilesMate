#Requires -Version 7
[CmdletBinding()]
param([switch]$KeepRunning, [int]$Left = 60, [string]$ResumeFixture, [string]$TestExe)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShelfTestInput {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; public Point(int x,int y){X=x;Y=y;} }
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPhysicalPoint(Point p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint mode);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int ht,uint f);
    [DllImport("user32.dll")] public static extern bool SetPhysicalCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,int d,UIntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
}
'@
$repo = Split-Path $PSScriptRoot -Parent
$exe = if ($TestExe) { [IO.Path]::GetFullPath($TestExe) } else { Join-Path $repo 'src/FilesMate.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/FilesMate.App.exe' }
if (-not $TestExe -and (Get-Process FilesMate.App -ErrorAction SilentlyContinue)) { throw 'Close FilesMate before the isolated drag test.' }
$shelfFile = if ($TestExe) { Join-Path (Split-Path $exe -Parent) 'test-profile/file-shelf.json' } else { Join-Path $env:LOCALAPPDATA 'FilesMate/file-shelf.json' }
if (-not $ResumeFixture -and (Test-Path -LiteralPath $shelfFile) -and @((Get-Content -LiteralPath $shelfFile -Raw | ConvertFrom-Json)).Count -gt 0) {
    throw 'The shelf must be empty. Existing user references are left untouched.'
}
$fixture = if ($ResumeFixture) { [IO.Path]::GetFullPath($ResumeFixture) } else { Join-Path $repo ('artifacts/shelf-drag-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
if (-not $fixture.StartsWith((Join-Path $repo 'artifacts/shelf-drag-').Replace('/','\'),[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test fixture must be inside the repository artifacts/shelf-drag-* directory.'
}
$copied = Join-Path $fixture 'Collected'
$moved = Join-Path $fixture 'Moved'
foreach ($path in @($fixture, $copied, $moved)) { [IO.Directory]::CreateDirectory($path) | Out-Null }
$source = Join-Path $fixture 'Shelf-test.txt'
if ($ResumeFixture) {
    if ([IO.File]::ReadAllText($source) -ne 'shelf-drag-fixture') { throw 'Unexpected fixture content.' }
    $references = @(Get-Content -LiteralPath $shelfFile -Raw | ConvertFrom-Json)
    if ($references.Count -ne 1 -or $references[0] -ne $source) { throw 'Only the fixture reference may be present.' }
} else { [IO.File]::WriteAllText($source, 'shelf-drag-fixture') }
$oldDpi = [ShelfTestInput]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$oldError = [ShelfTestInput]::SetErrorMode(3)
try { $process = Start-Process $exe -ArgumentList $fixture -PassThru }
finally { [ShelfTestInput]::SetErrorMode($oldError) | Out-Null }
$scope = [System.Windows.Automation.TreeScope]::Descendants
function Root { [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle) }
function Find([string]$id) {
    (Root).FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
function FileLabel([string]$name) {
    (Root).FindAll($scope,[System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,'NameText')) |
        Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1
}
function Center($element) {
    if ($null -eq $element) { throw 'Expected UI element was not found.' }
    $r = $element.Current.BoundingRectangle
    [Windows.Point]::new($r.X + [Math]::Min(30,$r.Width/2),$r.Y + $r.Height/2)
}
function AssertPoint([Windows.Point]$point) {
    [uint32]$owner = 0
    $window = [ShelfTestInput]::WindowFromPhysicalPoint([ShelfTestInput+Point]::new([int]$point.X,[int]$point.Y))
    [ShelfTestInput]::GetWindowThreadProcessId($window,[ref]$owner) | Out-Null
    if ($owner -ne $process.Id) {
        throw "Another application covers drag point $point (PID $owner); no input was sent."
    }
}
function MovePointer([Windows.Point]$from,[Windows.Point]$to) {
    for ($i=1;$i -le 18;$i++) {
        $x = $from.X+($to.X-$from.X)*$i/18
        $y = $from.Y+($to.Y-$from.Y)*$i/18
        $dx = [uint32](($x-[ShelfTestInput]::GetSystemMetrics(76))*65535/[ShelfTestInput]::GetSystemMetrics(78))
        $dy = [uint32](($y-[ShelfTestInput]::GetSystemMetrics(77))*65535/[ShelfTestInput]::GetSystemMetrics(79))
        [ShelfTestInput]::mouse_event(0xC001,$dx,$dy,0,0)
        Start-Sleep -Milliseconds 25
    }
}
function Drag([Windows.Point]$from,[Windows.Point]$to,[switch]$Control,[switch]$ShelfHover) {
    [ShelfTestInput]::SetWindowPos($process.MainWindowHandle,[IntPtr]::new(-1),0,0,0,0,3) | Out-Null
    AssertPoint $from
    AssertPoint $to
    [ShelfTestInput]::SetPhysicalCursorPos([int]$from.X,[int]$from.Y) | Out-Null
    [ShelfTestInput]::mouse_event(2,0,0,0,0)
    try {
        MovePointer $from ([Windows.Point]::new($from.X+18,$from.Y+12))
        Start-Sleep -Milliseconds 800
        if ($Control) { [ShelfTestInput]::keybd_event(0x11,0,0,0) }
        MovePointer ([Windows.Point]::new($from.X+18,$from.Y+12)) $to
        Start-Sleep -Milliseconds 800
        if ($ShelfHover) {
            $close = Find 'ShelfCloseButton'
            if ($null -eq $close -or $close.Current.IsOffscreen) { throw 'Hover did not expand the shelf card.' }
            $bounds = $close.Current.BoundingRectangle
            $cardPoint = [Windows.Point]::new($bounds.X - 120, $bounds.Y + 110)
            AssertPoint $cardPoint
            MovePointer $to $cardPoint
        }
    } finally {
        [ShelfTestInput]::mouse_event(4,0,0,0,0)
        [ShelfTestInput]::keybd_event(0x11,0,2,0)
    }
    Start-Sleep -Seconds 2
    $process.Refresh()
    if ($process.HasExited -or -not $process.Responding) { throw 'FilesMate became unhealthy after drag.' }
}
function ShelfRow {
    $list = Find 'PathsList'
    $list.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
}
function ShelfPoint {
    $bounds = (ShelfRow).Current.BoundingRectangle
    [Windows.Point]::new($bounds.X + [Math]::Min(150,$bounds.Width/2),$bounds.Y + $bounds.Height/2)
}
try {
    Start-Sleep -Seconds 3
    [ShelfTestInput]::SetWindowPos($process.MainWindowHandle,[IntPtr]::new(-1),$Left,60,1792,1120,0x4000) | Out-Null
    Start-Sleep -Milliseconds 400
    [ShelfTestInput]::SetWindowPos($process.MainWindowHandle,[IntPtr]::new(-1),0,0,0,0,3) | Out-Null
    [ShelfTestInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Seconds 1
    Drag (Center (FileLabel 'Shelf-test.txt')) (Center (Find 'ShelfButton')) -ShelfHover
    if (-not ((Get-Content -LiteralPath $shelfFile -Raw | ConvertFrom-Json) -contains $source)) { throw 'Drop did not add the reference.' }
    if ([IO.File]::ReadAllText($source) -ne 'shelf-drag-fixture') { throw 'Drop modified the original.' }
    Write-Host 'PASS: hover opens the card and dropping adds only a reference.'
    $row = ShelfRow
    ([System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 300
    Drag (ShelfPoint) (Center (FileLabel 'Collected')) -Control
    if ([IO.File]::ReadAllText((Join-Path $copied 'Shelf-test.txt')) -ne 'shelf-drag-fixture') { throw 'Drag-out copy failed.' }
    if (-not [IO.File]::Exists($source)) { throw 'Copy removed the original.' }
    Write-Host 'PASS: dragging out copies the actual file and retains the shelf reference.'
    Drag (ShelfPoint) (Center (FileLabel 'Moved'))
    if ([IO.File]::Exists($source) -or -not [IO.File]::Exists((Join-Path $moved 'Shelf-test.txt'))) { throw 'Drag-out move failed.' }
    if (@((Get-Content -LiteralPath $shelfFile -Raw | ConvertFrom-Json)).Count -ne 0) { throw 'Moved reference was not removed.' }
    Write-Host "PASS: default same-volume drag moves the file and removes the stale reference. Fixtures: $fixture"
} finally {
    [ShelfTestInput]::mouse_event(4,0,0,0,0)
    [ShelfTestInput]::keybd_event(0x11,0,2,0)
    $process.Refresh()
    if (-not $process.HasExited) {
        [ShelfTestInput]::SetWindowPos($process.MainWindowHandle,[IntPtr]::new(-2),0,0,0,0,0x4003) | Out-Null
        if (-not $KeepRunning) { $process.CloseMainWindow() | Out-Null }
    }
    [ShelfTestInput]::SetThreadDpiAwarenessContext($oldDpi) | Out-Null
}
