#Requires -Version 7
[CmdletBinding()]
param(
    [string]$ExePath,
    [ValidateRange(1, 2000)]
    [int]$TabCycles = 100,
    [ValidateRange(1, 2000)]
    [int]$NavigationCycles = 120,
    [ValidateRange(1, 10000)]
    [int]$ScrollEvents = 600,
    [ValidateRange(1, 2000)]
    [int]$ZoomEvents = 120,
    [string]$LargeFolder = 'C:\Windows\System32',
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $repositoryRoot 'src\FilesMate.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\FilesMate.App.exe'
}

$ExePath = [IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "FilesMate executable was not found: $ExePath"
}
if (Get-Process -Name FilesMate.App -ErrorAction SilentlyContinue) {
    throw 'Close existing FilesMate windows before running the isolated stress test.'
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class FilesMateStabilityInput
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y, int data, UIntPtr extra);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;
    public const uint Wheel = 0x0800;
    public const uint KeyUp = 0x0002;
    public const uint NoZOrder = 0x0004;
}
'@

$artifactRoot = Join-Path $repositoryRoot 'artifacts\crash-audit'
$dumpRoot = Join-Path $artifactRoot 'dumps'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
New-Item -ItemType Directory -Force -Path $dumpRoot | Out-Null
$startedAt = Get-Date
$existingDumps = @(
    Get-ChildItem -LiteralPath $dumpRoot -File -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
)
$samples = [Collections.Generic.List[object]]::new()
$process = $null

function Wait-MainWindow {
    param([Diagnostics.Process]$Process)

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "FilesMate exited during startup with code $($Process.ExitCode)."
        }

        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) {
            return
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw 'FilesMate did not create a main window within 15 seconds.'
}

function Assert-Healthy {
    param([string]$Stage)

    $process.Refresh()
    if ($process.HasExited) {
        throw "FilesMate exited during '$Stage' with code $($process.ExitCode)."
    }

    if (-not $process.Responding) {
        throw "FilesMate stopped responding during '$Stage'."
    }
}

function Add-Sample {
    param([string]$Stage)

    Assert-Healthy -Stage $Stage
    $samples.Add([pscustomobject]@{
        Time = (Get-Date).ToString('O')
        Stage = $Stage
        PrivateBytes = $process.PrivateMemorySize64
        WorkingSet = $process.WorkingSet64
        Handles = $process.HandleCount
        Threads = $process.Threads.Count
    })
}

function Get-RootElement {
    return [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
}

function Find-ById {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ButtonByName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name
    )

    $type = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $condition = [System.Windows.Automation.AndCondition]::new($type, $nameCondition)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-Element {
    param([System.Windows.Automation.AutomationElement]$Element)

    if ($null -eq $Element) {
        throw 'The requested automation element was not found.'
    }

    $pattern = [System.Windows.Automation.InvokePattern]$Element.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Send-CloseTab {
    [FilesMateStabilityInput]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    [FilesMateStabilityInput]::keybd_event(0x57, 0, 0, [UIntPtr]::Zero)
    [FilesMateStabilityInput]::keybd_event(0x57, 0, [FilesMateStabilityInput]::KeyUp, [UIntPtr]::Zero)
    [FilesMateStabilityInput]::keybd_event(0x11, 0, [FilesMateStabilityInput]::KeyUp, [UIntPtr]::Zero)
}

function Set-Address {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Path
    )

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    $pathBox = $null
    do {
        [FilesMateStabilityInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
        [FilesMateStabilityInput]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
        [FilesMateStabilityInput]::keybd_event(0x4C, 0, 0, [UIntPtr]::Zero)
        [FilesMateStabilityInput]::keybd_event(0x4C, 0, [FilesMateStabilityInput]::KeyUp, [UIntPtr]::Zero)
        [FilesMateStabilityInput]::keybd_event(0x11, 0, [FilesMateStabilityInput]::KeyUp, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 150
        $pathBox = Find-ById -Root (Get-RootElement) -AutomationId 'PathBox'
        if ($null -ne $pathBox -and $pathBox.Current.IsKeyboardFocusable) { break }
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $pathBox -or -not $pathBox.Current.IsKeyboardFocusable) {
        throw 'The address editor did not become available within five seconds.'
    }

    $pathBox.SetFocus()
    $value = [System.Windows.Automation.ValuePattern]$pathBox.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue($Path)
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
}

try {
    $startArguments = @{
        FilePath = $ExePath
        WorkingDirectory = Split-Path -Parent $ExePath
        PassThru = $true
    }
    $process = Start-Process @startArguments
    Wait-MainWindow -Process $process
    [FilesMateStabilityInput]::SetWindowPos(
        $process.MainWindowHandle,
        [IntPtr]::Zero,
        80,
        80,
        1440,
        900,
        [FilesMateStabilityInput]::NoZOrder) | Out-Null
    [FilesMateStabilityInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 500
    Add-Sample -Stage 'startup'

    $root = Get-RootElement
    $places = @($repositoryRoot, $env:WINDIR, $LargeFolder)
    for ($i = 0; $i -lt $NavigationCycles; $i++) {
        Set-Address -Root (Get-RootElement) -Path $places[$i % $places.Count]

        Start-Sleep -Milliseconds 55
        if (($i % 20) -eq 0) {
            Add-Sample -Stage "navigation-$i"
        }
    }

    Set-Address -Root $root -Path $LargeFolder
    Start-Sleep -Seconds 5
    Add-Sample -Stage 'large-folder-loaded'

    $scroller = Find-ById -Root $root -AutomationId 'Scroller'
    if ($null -eq $scroller) {
        throw 'The file-list scroller was not found.'
    }

    $bounds = $scroller.Current.BoundingRectangle
    [FilesMateStabilityInput]::SetCursorPos(
        [int]($bounds.Left + ($bounds.Width / 2)),
        [int]($bounds.Top + ($bounds.Height / 2))) | Out-Null
    for ($i = 0; $i -lt $ScrollEvents; $i++) {
        $delta = if (([int]($i / 100) % 2) -eq 0) { -120 } else { 120 }
        [FilesMateStabilityInput]::mouse_event(
            [FilesMateStabilityInput]::Wheel,
            0,
            0,
            $delta,
            [UIntPtr]::Zero)
        if (($i % 50) -eq 0) {
            Start-Sleep -Milliseconds 50
            Add-Sample -Stage "scroll-$i"
        }
    }

    [FilesMateStabilityInput]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    try {
        for ($i = 0; $i -lt $ZoomEvents; $i++) {
            $delta = if (([int]($i / 12) % 2) -eq 0) { 120 } else { -120 }
            [FilesMateStabilityInput]::mouse_event(
                [FilesMateStabilityInput]::Wheel,
                0,
                0,
                $delta,
                [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 10
            if (($i % 24) -eq 0) {
                Add-Sample -Stage "zoom-$i"
            }
        }
    }
    finally {
        [FilesMateStabilityInput]::keybd_event(
            0x11,
            0,
            [FilesMateStabilityInput]::KeyUp,
            [UIntPtr]::Zero)
    }

    $newTab = Find-ById -Root $root -AutomationId 'NewTabButton'
    for ($i = 0; $i -lt $TabCycles; $i++) {
        Invoke-Element -Element $newTab
        Start-Sleep -Milliseconds 15
        Send-CloseTab
        if (($i % 20) -eq 0) {
            Start-Sleep -Milliseconds 120
            Add-Sample -Stage "tab-$i"
        }
    }

    Start-Sleep -Seconds 5
    Add-Sample -Stage 'settled'
    $newDumps = @(
        Get-ChildItem -LiteralPath $dumpRoot -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notin $existingDumps -and $_.LastWriteTime -ge $startedAt }
    )
    if ($newDumps.Count -gt 0) {
        throw "The stress run created $($newDumps.Count) crash dump(s)."
    }

    $result = [pscustomobject]@{
        StartedAt = $startedAt.ToString('O')
        FinishedAt = (Get-Date).ToString('O')
        Executable = $ExePath
        NavigationAttempts = $NavigationCycles
        TabCycles = $TabCycles
        ScrollEvents = $ScrollEvents
        ZoomEvents = $ZoomEvents
        ProcessId = $process.Id
        Passed = $true
        Samples = $samples
    }
    $resultPath = Join-Path $artifactRoot ("stress-{0}.json" -f $startedAt.ToString('yyyyMMdd-HHmmss'))
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding utf8
    Write-Host "PASS: FilesMate remained responsive. Results: $resultPath"
}
finally {
    if ($null -ne $process -and -not $KeepRunning) {
        $process.Refresh()
        if (-not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(3000)) {
                Stop-Process -Id $process.Id -Force
            }
        }
    }
}
