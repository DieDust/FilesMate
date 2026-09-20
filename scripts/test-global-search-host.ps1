#Requires -Version 7
[CmdletBinding()]
param([Parameter(Mandatory)][string]$HostPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SearchHostTestNative {
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd,int id);
}
'@
$hostExe = [IO.Path]::GetFullPath($HostPath)
$testDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts\global-search-lifecycle-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$profileDirectory = Join-Path $testDirectory 'profile'
New-Item -ItemType Directory -Path $profileDirectory -Force | Out-Null
Set-Content -LiteralPath "$profileDirectory\global-search.json" -Value '{"Enabled":true,"Hotkey":"Ctrl+Alt+Shift+F10"}'
function Send-HostCommand([string]$Command = 'status', [string]$Hotkey, [bool]$Enabled = $true, [string]$ManagerPath) {
    $arguments = @{Profile=$profileDirectory;Command=$Command}
    if ($Hotkey) { $arguments.Hotkey=$Hotkey; $arguments.Enabled=$Enabled }
    if ($ManagerPath) { $arguments.ManagerPath=$ManagerPath }
    & "$PSScriptRoot\search-host-command.ps1" @arguments | ConvertFrom-Json
}
function Start-TestHost([string]$Mode = '--background') {
    Start-Process -FilePath $hostExe -ArgumentList @($Mode,'--profile',('"' + $profileDirectory + '"')) -WindowStyle Hidden
    Start-Sleep -Milliseconds 700
    Send-HostCommand
}
function Find-Palette([int]$HostPid) {
    [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$HostPid),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'FilesMate 全局搜索')))
}
try {
    $pending = Start-Process -FilePath $hostExe -ArgumentList @('--background','--profile',('"' + $profileDirectory + '"')) -WindowStyle Hidden -PassThru
    if (-not $pending.WaitForExit(5000) -or $pending.ExitCode -ne 0) { throw 'Background search started before first-run consent.' }
    Set-Content -LiteralPath "$profileDirectory\feature-setup.json" -Value '{"Completed":true,"FavoritesBarEnabled":false}'
    $cold = Start-TestHost
    if (-not $cold.HotkeyRegistered -or $cold.Visible) { throw 'Cold host must register and remain hidden.' }
    $duplicate = Start-TestHost
    if ($duplicate.Pid -ne $cold.Pid) { throw 'Duplicate instance was created.' }
    $same = Send-HostCommand apply Ctrl+Alt+Shift+F10
    if (-not $same.Ok -or -not $same.HotkeyRegistered -or $same.Pid -ne $cold.Pid) { throw 'Existing host treated its own binding as a conflict.' }
    if (-not [SearchHostTestNative]::RegisterHotKey([IntPtr]::Zero,221,7,119)) { throw 'Test shortcut unavailable.' }
    try {
        $conflict = Send-HostCommand apply Ctrl+Alt+Shift+F8
        if ($conflict.Ok -or -not $conflict.HotkeyRegistered -or $conflict.ErrorCode -ne 'HotkeyConflict') { throw 'Conflicting shortcut replaced the working binding or lacked its error code.' }
        $saved = Get-Content "$profileDirectory\global-search.json" -Raw | ConvertFrom-Json
        if ($saved.Hotkey -ne 'Ctrl+Alt+Shift+F10') { throw 'Conflict changed saved settings.' }
    } finally { [SearchHostTestNative]::UnregisterHotKey([IntPtr]::Zero,221) | Out-Null }
    $changed = Send-HostCommand apply Ctrl+Alt+Shift+F9
    if (-not $changed.Ok -or -not $changed.HotkeyRegistered) { throw 'Shortcut update failed.' }
    $wrong = Send-HostCommand -Command stop-installation -ManagerPath 'C:\OtherInstallation\FilesMate.App.exe'
    if ($wrong.Ok) { throw 'A different installation could stop the host.' }
    for ($cycle = 0; $cycle -lt 2; $cycle++) {
        $shown = Send-HostCommand show
        $window = Find-Palette $shown.Pid
        if (-not $window) { throw 'Search window is missing or was interrupted by another foreground window.' }
        $query = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'GlobalSearchQuery'))
        $query.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('missing-index-test')
        $guidance = $false
        for ($attempt = 0; $attempt -lt 20 -and -not $guidance; $attempt++) {
            Start-Sleep -Milliseconds 100
            $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text))
            $guidance = @($texts | ForEach-Object {$_.Current.Name}) -match '尚未建立索引'
        }
        if (-not $guidance) { throw 'Missing-index guidance was not displayed.' }
        # WindowPattern.Close exercises the native window-close path (also used by Alt+F4).
        $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Start-Sleep -Milliseconds 1000
        $idle = Send-HostCommand
        if ($idle.Pid -ne $shown.Pid -or $idle.Visible -or -not $idle.HotkeyRegistered) { throw 'Window close did not preserve the prepared listener.' }
    }
    $process = Get-Process -Id $idle.Pid
    $cpuBefore = $process.TotalProcessorTime.TotalMilliseconds
    Start-Sleep -Seconds 3
    $process.Refresh()
    $memory = @{WorkingSetMiB=[math]::Round($process.WorkingSet64/1MB,1);PrivateMiB=[math]::Round($process.PrivateMemorySize64/1MB,1);CpuMillisecondsOver3Seconds=$process.TotalProcessorTime.TotalMilliseconds-$cpuBefore}
    $disabled = Send-HostCommand apply Ctrl+Alt+Shift+F9 $false
    Start-Sleep -Milliseconds 500
    if (Get-Process -Id $disabled.Pid -ErrorAction SilentlyContinue) { throw 'Disabled resident did not exit.' }
    $manual = Start-TestHost '--show'
    if ($manual.HotkeyRegistered -or -not $manual.Visible) { throw 'Manual disabled-mode launch registered a shortcut or did not show.' }
    (Find-Palette $manual.Pid).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 600
    if (Get-Process -Id $manual.Pid -ErrorAction SilentlyContinue) { throw 'Disabled manual search did not exit on close.' }
    Set-Content -LiteralPath "$profileDirectory\global-search.json" -Value '{"Enabled":true,"Hotkey":"Ctrl+Alt+Shift+F10"}'
    $last = Start-TestHost
    $stopper = Start-Process -FilePath $hostExe -ArgumentList @('--stop','--profile',('"' + $profileDirectory + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($stopper.ExitCode -ne 0 -or (Get-Process -Id $last.Pid -ErrorAction SilentlyContinue)) { throw 'Installation stop did not finish.' }
    $report = [ordered]@{Passed=$true;Host=$hostExe;SingleInstance=$true;ConflictRollback=$true;ShortcutChange=$true;NativeCloseAndRecall=$true;DisabledMode=$true;InstallationStop=$true;Idle=$memory}
    $report | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $testDirectory 'result.json')
} finally {
    try { Send-HostCommand stop | Out-Null } catch { }
}
