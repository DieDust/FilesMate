#Requires -Version 7
[CmdletBinding()]
param([switch]$KeepRunning, [switch]$NativeShell)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ViewRegressionInput {
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x,int y,int w,int h,uint flags);
}
'@
if (Get-Process FilesMate.App -ErrorAction SilentlyContinue) { throw 'Close FilesMate before this isolated UI regression test.' }
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'src/FilesMate.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/FilesMate.App.exe'
$fixture = Join-Path $repo ('artifacts/view-regression-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
for ($i=0; $i -lt 180; $i++) {
    $file = Join-Path $fixture ('item{0:D3}.txt' -f $i)
    [IO.File]::WriteAllText($file, 'FilesMate UI regression fixture')
    [IO.File]::SetLastWriteTimeUtc($file, [DateTime]::new(2025,1,1).AddDays(180-$i))
}
$config = Join-Path $env:LOCALAPPDATA 'FilesMate/folder-customizations.json'
# Seed only this new fixture's presentation. Global preferences and other folders are retained.
$settings = if (Test-Path -LiteralPath $config) { Get-Content -LiteralPath $config -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
$settings[$fixture] = @{ View=@{ Details=$false; GridSlot=384; Sort=@{ Column=0; Ascending=$true; DirectoriesFirst=$true } }; CoverPath=$null }
[IO.Directory]::CreateDirectory((Split-Path $config)) | Out-Null
[IO.File]::WriteAllText($config, ($settings | ConvertTo-Json -Depth 8))
$scope = [Windows.Automation.TreeScope]::Descendants
$oldDpi = [ViewRegressionInput]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$checks = [Collections.Generic.List[string]]::new()
function Root { [Windows.Automation.AutomationElement]::FromHandle($script:windowHandle) }
function Find([string]$id) { (Root).FindFirst($scope, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id)) }
function InvokeId([string]$id) {
    $element = Find $id
    if ($null -eq $element) { throw "Missing UI element: $id" }
    ([Windows.Automation.InvokePattern]$element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
function WaitFor([scriptblock]$condition, [string]$description) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $script:process.Refresh()
        if ($script:process.HasExited) { throw "Application exited during $description (exit $($script:process.ExitCode))" }
        if (& $condition) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $description"
}
function StartApp([string]$arguments) {
    if ($NativeShell -and $arguments.StartsWith('/select,')) {
        & (Join-Path $PSScriptRoot 'invoke-shell-reveal.ps1') -Path $target
        $script:process = Get-Process FilesMate.App
    } else {
        $script:process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    }
    WaitFor { $script:process.MainWindowHandle -ne [IntPtr]::Zero } 'window creation'
    $script:windowHandle = $script:process.MainWindowHandle
    [ViewRegressionInput]::SetWindowPos($script:windowHandle,[IntPtr]::Zero,60,60,1792,1120,4) | Out-Null
    WaitFor { $null -ne (Find 'Scroller') } 'file surface'
    Start-Sleep -Milliseconds 1200
}
function CloseApp {
    $script:process.CloseMainWindow() | Out-Null
    if (-not $script:process.WaitForExit(10000)) { throw 'The test window did not close normally.' }
}
function Labels {
    $clip = (Find 'Scroller').Current.BoundingRectangle
    @((Root).FindAll($scope,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'NameText')) |
        Where-Object { $_.Current.Name -like 'item*.txt' -and -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Bottom -gt $clip.Top -and $_.Current.BoundingRectangle.Top -lt $clip.Bottom } |
        Sort-Object { $_.Current.BoundingRectangle.Top }, { $_.Current.BoundingRectangle.Left })
}
function ViewSettings {
    if (-not (Test-Path -LiteralPath $config)) { return $null }
    $all = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json -AsHashtable
    $all[$fixture].View
}
function ToTop {
    InvokeId 'RefreshButton'
    Start-Sleep -Milliseconds 700
    WaitFor { $null -ne (Find 'Scroller') } 'refreshed file surface'
}
function SaveShot([string]$name) {
    if ([ViewRegressionInput]::GetForegroundWindow() -ne $script:process.MainWindowHandle) { return }
    $rect = (Root).Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$rect.X,[int]$rect.Y,0,0,$bitmap.Size)
        $bitmap.Save((Join-Path $fixture $name),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
try {
    $target = Join-Path $fixture 'item179.txt'
    StartApp ('/select,"' + $target + '"')
    WaitFor { @((Labels) | Where-Object { $_.Current.Name -eq 'item179.txt' }).Count -gt 0 } 'external target visible'
    $selection = (Find 'SelectionBlock').Current.Name
    if ($selection -notmatch '1') { throw "Expected one selected entry; status: $selection" }
    SaveShot '01-external-reveal.png'
    $checks.Add('External file launch selected the target and scrolled it into view.')
    if ($NativeShell) {
        & (Join-Path $PSScriptRoot 'invoke-shell-reveal.ps1') -Path (Join-Path $fixture 'item010.txt') -SingleItemPidl
        WaitFor { @((Labels) | Where-Object { $_.Current.Name -eq 'item010.txt' }).Count -gt 0 } 'warm shell target visible'
        if ((Find 'SelectionBlock').Current.Name -notmatch '1') { throw 'Warm shell launch lost selection.' }
        $checks.Add('The Windows shell selected and revealed a different file in the existing window.')
    }
    ToTop
    WaitFor { (ViewSettings).GridSlot -eq 384 } 'maximum zoom saved'
    ToTop
    SaveShot '02-maximum-zoom.png'
    $scale = [ViewRegressionInput]::GetDpiForWindow($script:windowHandle) / 96.0
    if ((Labels)[0].Current.BoundingRectangle.Width / $scale -lt 250) { throw 'Maximum-size labels were not laid out at the expected logical size.' }
    $checks.Add('The maximum-size grid was restored and rendered; wheel transitions are covered by unit tests.')
    InvokeId 'DetailsViewButton'
    WaitFor { $null -ne (Find 'ModifiedHeader') } 'details header'
    InvokeId 'ModifiedHeader'
    WaitFor { (ViewSettings).Sort.Column -eq 3 } 'date sort saved'
    ToTop
    WaitFor { (Labels)[0].Current.Name -eq 'item179.txt' } 'date ascending order'
    InvokeId 'ModifiedHeader'
    CloseApp
    $view = ViewSettings
    if ($view.Sort.Column -ne 3 -or $view.Sort.Ascending) { throw 'Immediate close lost the descending date sort.' }
    StartApp ('/open "' + $fixture + '"')
    ToTop
    WaitFor { (Labels)[0].Current.Name -eq 'item000.txt' } 'restored date descending order'
    if ((ViewSettings).GridSlot -ne 384) { throw 'Restart lost the maximum zoom size.' }
    InvokeId 'UpButton'
    WaitFor { $script:process.MainWindowTitle -eq 'artifacts' } 'parent folder navigation'
    InvokeId 'BackButton'
    WaitFor { $script:process.MainWindowTitle -eq (Split-Path $fixture -Leaf) } 'back to the saved folder'
    ToTop
    WaitFor { (Labels)[0].Current.Name -eq 'item000.txt' } 'date descending order after returning'
    SaveShot '03-restored-sort.png'
    $checks.Add('Date sort direction and maximum zoom survived an immediate close and restart.')
    $checks.Add('Leaving the folder and going back restored its date sort.')
    [pscustomobject]@{ Fixture=$fixture; Checks=$checks.ToArray(); Success=$true } | ConvertTo-Json | Tee-Object -FilePath (Join-Path $fixture 'result.json')
    if (-not $KeepRunning) { CloseApp }
} catch {
    Write-Host $_.ScriptStackTrace
    throw
} finally { [ViewRegressionInput]::SetThreadDpiAwarenessContext($oldDpi) | Out-Null }
