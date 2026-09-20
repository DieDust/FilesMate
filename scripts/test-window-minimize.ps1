#Requires -Version 7
[CmdletBinding()]
param(
    [string]$ExePath,
    [ValidateRange(1, 20)]
    [int]$Cycles = 3,
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $PSScriptRoot '..\src\FilesMate.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\FilesMate.App.exe'
}
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
if (Get-Process -Name FilesMate.App -ErrorAction SilentlyContinue) {
    throw 'Close existing FilesMate windows before running this test.'
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FilesMateWindowTest
{
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
'@

$process = Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path $ExePath) -PassThru
$hwnd = [IntPtr]::Zero

function Assert-WindowState {
    param([string]$Stage, [bool]$Minimized, [bool]$Maximized = $false)
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $process.Refresh()
        if ($process.HasExited) {
            throw "$Stage : primary process exited with code $($process.ExitCode)."
        }
        if ([FilesMateWindowTest]::IsIconic($hwnd) -eq $Minimized -and
            ($Minimized -or [FilesMateWindowTest]::IsZoomed($hwnd) -eq $Maximized)) {
            break
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    # The previous bug could crash or reverse the state after the first transition.
    Start-Sleep -Milliseconds 800
    $process.Refresh()
    if ($process.HasExited -or -not [FilesMateWindowTest]::IsWindow($hwnd)) {
        throw "$Stage : primary window was destroyed."
    }
    if ([FilesMateWindowTest]::IsIconic($hwnd) -ne $Minimized) {
        throw "$Stage : expected minimized=$Minimized."
    }
    if (-not $Minimized -and [FilesMateWindowTest]::IsZoomed($hwnd) -ne $Maximized) {
        throw "$Stage : expected maximized=$Maximized."
    }
    Write-Output "PASS $Stage (PID=$($process.Id), HWND=$hwnd)"
}

function Invoke-RedirectedLaunch {
    $secondary = Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path $ExePath) -PassThru
    if (-not $secondary.WaitForExit(5000)) {
        throw "Secondary process $($secondary.Id) did not exit after redirect."
    }
    if ($secondary.ExitCode -ne 0) {
        throw "Secondary process exited with code $($secondary.ExitCode)."
    }
    $secondary.Dispose()
}

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $process.Refresh()
        if ($process.HasExited) {
            throw "Startup failed: exit code $($process.ExitCode)."
        }
        $hwnd = $process.MainWindowHandle
        if ($hwnd -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($hwnd -eq [IntPtr]::Zero) { throw 'Startup timed out.' }
    Start-Sleep -Seconds 2
    [void][FilesMateWindowTest]::ShowWindowAsync($hwnd, 9)
    Assert-WindowState 'initial window' $false

    for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
        [void][FilesMateWindowTest]::PostMessageW($hwnd, 0x0112, [IntPtr]0xF020, [IntPtr]::Zero)
        Assert-WindowState "system minimize $cycle" $true
        [void][FilesMateWindowTest]::PostMessageW($hwnd, 0x0112, [IntPtr]0xF120, [IntPtr]::Zero)
        Assert-WindowState "system restore $cycle" $false
        Invoke-RedirectedLaunch
        Assert-WindowState "launcher minimize $cycle" $true
        Invoke-RedirectedLaunch
        Assert-WindowState "launcher restore $cycle" $false
    }

    [void][FilesMateWindowTest]::ShowWindowAsync($hwnd, 3)
    Assert-WindowState 'maximize' $false $true
    Invoke-RedirectedLaunch
    Assert-WindowState 'minimize maximized window' $true
    Invoke-RedirectedLaunch
    Assert-WindowState 'restore maximized window' $false $true
    if (@(Get-Process -Name FilesMate.App).Count -ne 1) {
        throw 'The test created an additional primary process.'
    }

    if (-not $KeepRunning) {
        [void][FilesMateWindowTest]::PostMessageW($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
        if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) {
            throw 'Normal window close failed.'
        }
        Write-Output 'PASS normal close'
    }
}
finally {
    if (-not $KeepRunning -and -not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(3000)) {
            Stop-Process -Id $process.Id -Force
        }
    }
    $process.Dispose()
}
