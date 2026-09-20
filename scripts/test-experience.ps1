#Requires -Version 7
param([string]$TestExe, [int]$TemporarilyMinimizeProcessId)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
Add-Type -AssemblyName System.Drawing.Common
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ExperienceInput {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; public Point(int x,int y){X=x;Y=y;} }
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPhysicalPoint(Point p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
    [DllImport("user32.dll")] public static extern bool SetPhysicalCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,int d,UIntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int height,uint flags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h,int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@
if (-not $TestExe -and (Get-Process FilesMate.App -ErrorAction SilentlyContinue)) { throw 'Close FilesMate before this isolated UI test.' }
$repo = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $repo ('artifacts/experience-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
foreach ($name in @('alpha','archive','beta','bravo','brush')) {
    [IO.Directory]::CreateDirectory((Join-Path $fixture $name)) | Out-Null
}
$exe = if ($TestExe) { [IO.Path]::GetFullPath($TestExe) } else { Join-Path $repo 'src/FilesMate.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/FilesMate.App.exe' }
$settingsRoot = if ($TestExe) { Join-Path (Split-Path $exe -Parent) 'test-profile' } else { Join-Path $env:LOCALAPPDATA 'FilesMate' }
if ($TestExe) {
    [IO.Directory]::CreateDirectory($settingsRoot) | Out-Null
    $preferences = Get-Content (Join-Path $env:LOCALAPPDATA 'FilesMate/explorer.json') -Raw | ConvertFrom-Json
    $preferences.OpenFoldersInNewTab = $false
    $preferences | ConvertTo-Json | Set-Content (Join-Path $settingsRoot 'explorer.json')
}
$backups = @{}
foreach ($name in @('window-session.json','window.json')) {
    $path = Join-Path $settingsRoot $name
    if (Test-Path -LiteralPath $path) {
        $backups[$name] = [IO.File]::ReadAllBytes($path)
        [IO.File]::WriteAllBytes((Join-Path $fixture ($name + '.before')),$backups[$name])
    }
}
$process = Start-Process -FilePath $exe -ArgumentList ('/open "' + $fixture + '"') -PassThru
$script:windowHandle = [IntPtr]::Zero
function Root {
    $process.Refresh()
    if ($script:windowHandle -eq [IntPtr]::Zero) { $script:windowHandle = $process.MainWindowHandle }
    if ($script:windowHandle -eq [IntPtr]::Zero) { return $null }
    [Windows.Automation.AutomationElement]::FromHandle($script:windowHandle)
}
function Find([string]$id) {
    $root = Root
    if ($root) { $root.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id)) }
}
function WaitFor([scriptblock]$condition,[string]$description) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        if ($process.HasExited) { throw 'FilesMate exited during the test.' }
        if (& $condition) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    $focus = [Windows.Automation.AutomationElement]::FocusedElement
    throw "Timed out: $description; path=$(CurrentPath); focus=$($focus.Current.AutomationId)"
}
function Keys([string]$keys) {
    Write-Host "Checking keyboard step: $keys; hwnd=$script:windowHandle; path=$(CurrentPath)"
    if ([ExperienceInput]::GetForegroundWindow() -ne $script:windowHandle) {
        [void][ExperienceInput]::SetForegroundWindow($script:windowHandle)
        Start-Sleep -Milliseconds 100
    }
    if ([ExperienceInput]::GetForegroundWindow() -ne $script:windowHandle) { throw "FilesMate lost focus before $keys; keyboard input was not sent." }
    $modifier = 0
    if ($keys.StartsWith('^')) { $modifier=0x11; $keys=$keys.Substring(1) }
    elseif ($keys.StartsWith('%')) { $modifier=0x12; $keys=$keys.Substring(1) }
    $tokens = [regex]::Matches($keys,'\{[^}]+\}|.')
    foreach ($token in $tokens) {
        $key = switch ($token.Value) {
            '{ESC}' {0x1B} '{ENTER}' {0x0D} '{BACKSPACE}' {0x08}
            '{LEFT}' {0x25} '{UP}' {0x26} '{RIGHT}' {0x27}
            default {[int][char]$token.Value.ToUpperInvariant()}
        }
        if ($modifier) { [ExperienceInput]::keybd_event($modifier,0,0,[UIntPtr]::Zero) }
        [ExperienceInput]::keybd_event($key,0,0,[UIntPtr]::Zero)
        [ExperienceInput]::keybd_event($key,0,2,[UIntPtr]::Zero)
        if ($modifier) { [ExperienceInput]::keybd_event($modifier,0,2,[UIntPtr]::Zero); $modifier=0 }
    }
}
function CurrentPath {
    $box = Find 'PathBox'
    if ($box) { $box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value }
}
$checks = [Collections.Generic.List[string]]::new()
$oldDpi = [ExperienceInput]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$obscuringWindow = [IntPtr]::Zero
try {
    if ($TemporarilyMinimizeProcessId) {
        $obscuringWindow = (Get-Process -Id $TemporarilyMinimizeProcessId).MainWindowHandle
        if ([ExperienceInput]::IsIconic($obscuringWindow)) { $obscuringWindow = [IntPtr]::Zero }
        else { [void][ExperienceInput]::ShowWindowAsync($obscuringWindow,6) }
    }
    WaitFor { $null -ne (Find 'SearchButton') } 'main window'
    [void][ExperienceInput]::SetWindowPos($script:windowHandle,[IntPtr]::new(-1),50,50,1750,1000,0x0040)
    [void][ExperienceInput]::SetForegroundWindow($process.MainWindowHandle)
    Start-Sleep -Seconds 2
    Write-Host "After window layout: path=$(CurrentPath)"
    WaitFor { (Find 'SearchBox').Current.IsKeyboardFocusable } 'persistent search field'
    $bounds = (Find 'SearchBox').Current.BoundingRectangle
    Write-Host "Search click bounds: $bounds"
    $point = [ExperienceInput+Point]::new([int]($bounds.X+20),[int]($bounds.Y+$bounds.Height/2))
    [uint32]$owner = 0
    [void][ExperienceInput]::GetWindowThreadProcessId([ExperienceInput]::WindowFromPhysicalPoint($point),[ref]$owner)
    if ($owner -ne $process.Id) { throw "Another application covers the search box (PID $owner; expected $($process.Id)); no click was sent." }
    [void][ExperienceInput]::SetPhysicalCursorPos($point.X,$point.Y)
    [ExperienceInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    [ExperienceInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    WaitFor { $box=Find 'SearchBox'; $box -and $box.Current.HasKeyboardFocus } 'search keyboard focus'
    Start-Sleep -Milliseconds 500
    Write-Host "After search focus: path=$(CurrentPath)"
    Keys '{ESC}'
    Keys '^fbravo'
    WaitFor { $box=Find 'SearchBox'; $box -and $box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'bravo' } 'immediate Ctrl+F input'
    WaitFor { $s=Find 'SearchStatus'; $s -and $s.Current.Name -match '1' } 'one matching folder'
    $checks.Add('Ctrl+F accepts immediate typing; current-folder search finds the result.')
    (Find 'ClearSearchButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    WaitFor { (Find 'SearchBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '' } 'clear search'
    $checks.Add('The visible clear button empties search and keeps the field usable.')
    Keys '{ESC}'
    Start-Sleep -Milliseconds 150
    Keys 'br{ENTER}'
    WaitFor { (CurrentPath) -eq (Join-Path $fixture 'bravo') } 'type-to-select bravo'
    $checks.Add('Typing a name prefix selects and opens the matching folder.')
    Start-Sleep -Milliseconds 500
    $focused = [Windows.Automation.AutomationElement]::FocusedElement
    if ($focused.Current.ProcessId -eq $process.Id) {
        Write-Host "Empty-folder focus: id=$($focused.Current.AutomationId); name=$($focused.Current.Name); class=$($focused.Current.ClassName); type=$($focused.Current.ControlType.ProgrammaticName)"
    }
    Keys '{BACKSPACE}'
    WaitFor { (CurrentPath) -eq $fixture } 'Backspace history navigation'
    Keys '%{RIGHT}'
    WaitFor { (CurrentPath) -eq (Join-Path $fixture 'bravo') } 'Alt+Right forward navigation'
    Keys '%{UP}'
    WaitFor { (CurrentPath) -eq $fixture } 'Alt+Up parent navigation'
    Keys '%{LEFT}'
    WaitFor { (CurrentPath) -eq (Join-Path $fixture 'bravo') } 'Alt+Left history navigation'
    $checks.Add('Backspace, Alt+Left, Alt+Right and Alt+Up preserve history and parent semantics.')
    [pscustomobject]@{Success=$true;Fixture=$fixture;Checks=$checks.ToArray()} | ConvertTo-Json |
        Tee-Object -FilePath (Join-Path $fixture 'result.json')
} catch {
    $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))
    foreach ($window in $windows) {
        Write-Host "Test window: $($window.Current.Name); class=$($window.Current.ClassName); hwnd=$($window.Current.NativeWindowHandle)"
        Write-Host "Bounds: $($window.Current.BoundingRectangle); offscreen=$($window.Current.IsOffscreen)"
        $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition) |
            Select-Object -First 15 | ForEach-Object { Write-Host "UI: $($_.Current.AutomationId); $($_.Current.ClassName); $($_.Current.Name)" }
        $bitmap = [Drawing.Bitmap]::new(1750,1000)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try { [void][ExperienceInput]::PrintWindow($script:windowHandle,$dc,2) }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        $bitmap.Save((Join-Path $fixture 'failure.png'))
        $bitmap.Dispose()
    }
    throw
} finally {
    if ($obscuringWindow -ne [IntPtr]::Zero) { [void][ExperienceInput]::ShowWindowAsync($obscuringWindow,9) }
    if (-not $process.HasExited) {
        [void][ExperienceInput]::SetWindowPos($script:windowHandle,[IntPtr]::new(-2),0,0,0,0,3)
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(30000)) { throw "Test window did not close normally; settings backup is in $fixture." }
    }
    foreach ($name in $backups.Keys) { [IO.File]::WriteAllBytes((Join-Path $settingsRoot $name),$backups[$name]) }
    [void][ExperienceInput]::SetThreadDpiAwarenessContext($oldDpi)
}
