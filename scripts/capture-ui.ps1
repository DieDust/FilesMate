#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath,

    [AllowNull()]
    [AllowEmptyString()]
    [string]$WindowTitle,

    [AllowNull()]
    [AllowEmptyString()]
    [string]$Width,

    [AllowNull()]
    [AllowEmptyString()]
    [string]$Height,

    [AllowNull()]
    [AllowEmptyString()]
    [string]$Theme
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing.Common

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

public static class NativeWindowCapture
{
    public static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new IntPtr(-4);
    public const uint PrintWindowClientOnly = 0x00000001;
    public const uint PrintWindowRenderFullContent = 0x00000002;
    public const uint SetWindowPosNoMove = 0x0002;
    public const uint SetWindowPosNoZOrder = 0x0004;
    public const uint SetWindowPosNoActivate = 0x0010;

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsHungAppWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    public static IntPtr[] EnumerateTopLevelWindowsByTitle(string exactTitle)
    {
        var matches = new List<IntPtr>();
        EnumWindowsCallback callback = (window, _) =>
        {
            if (!IsWindowVisible(window))
            {
                return true;
            }

            var length = GetWindowTextLength(window);
            if (length == 0)
            {
                return true;
            }

            var title = new StringBuilder(length + 1);
            _ = GetWindowText(window, title, title.Capacity);
            if (string.Equals(title.ToString(), exactTitle, StringComparison.Ordinal))
            {
                matches.Add(window);
            }

            return true;
        };

        if (!EnumWindows(callback, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumWindows failed.");
        }

        return matches.ToArray();
    }
}
'@

function Get-NativeErrorMessage {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    return "Win32 error ${errorCode}: $([ComponentModel.Win32Exception]::new($errorCode).Message)"
}

function Get-CaptureMutexName {
    param(
        [Parameter(Mandatory)]
        [string]$ResolvedOutput
    )

    $normalizedPath = [IO.Path]::GetFullPath($ResolvedOutput).ToUpperInvariant()
    $pathBytes = [Text.Encoding]::UTF8.GetBytes($normalizedPath)
    $pathHash = [Security.Cryptography.SHA256]::HashData($pathBytes)
    return "Local\FilesMate.VisualCapture.$([Convert]::ToHexString($pathHash))"
}

function Enter-CaptureOutputLock {
    param(
        [Parameter(Mandatory)]
        [string]$ResolvedOutput,

        [int]$TimeoutMilliseconds = 10000
    )

    if ($TimeoutMilliseconds -lt 1 -or $TimeoutMilliseconds -gt 60000) {
        throw "Mutex timeout must be between 1 and 60000 milliseconds. Received: $TimeoutMilliseconds."
    }

    $mutexName = Get-CaptureMutexName -ResolvedOutput $ResolvedOutput
    $mutex = [Threading.Mutex]::new($false, $mutexName)
    $acquired = $false
    try {
        try {
            $acquired = $mutex.WaitOne($TimeoutMilliseconds)
        }
        catch [Threading.AbandonedMutexException] {
            $acquired = $true
            Write-Warning "Recovered abandoned capture mutex '$mutexName'."
        }

        if (-not $acquired) {
            throw "Timed out after $TimeoutMilliseconds ms waiting for the capture lock for '$ResolvedOutput'."
        }

        return $mutex
    }
    catch {
        if (-not $acquired) {
            $mutex.Dispose()
        }

        throw
    }
}

function Exit-CaptureOutputLock {
    param(
        [Parameter(Mandatory)]
        [Threading.Mutex]$Mutex
    )

    try {
        $Mutex.ReleaseMutex()
    }
    finally {
        $Mutex.Dispose()
    }
}

function Enter-PerMonitorV2DpiContext {
    try {
        $previousContext = [NativeWindowCapture]::SetThreadDpiAwarenessContext(
            [NativeWindowCapture]::DpiAwarenessContextPerMonitorAwareV2)
    }
    catch {
        if ($_.Exception -is [EntryPointNotFoundException] -or
            $_.Exception.InnerException -is [EntryPointNotFoundException]) {
            throw 'SetThreadDpiAwarenessContext is unavailable; per-monitor-v2 physical-pixel capture cannot be guaranteed.'
        }

        throw
    }

    if ($previousContext -eq [IntPtr]::Zero) {
        throw "SetThreadDpiAwarenessContext(PER_MONITOR_AWARE_V2) failed. $(Get-NativeErrorMessage)"
    }

    return $previousContext
}

function Exit-PerMonitorV2DpiContext {
    param(
        [Parameter(Mandatory)]
        [IntPtr]$PreviousContext
    )

    $replacedContext = [NativeWindowCapture]::SetThreadDpiAwarenessContext($PreviousContext)
    if ($replacedContext -eq [IntPtr]::Zero) {
        throw "Restoring the previous thread DPI awareness context failed. $(Get-NativeErrorMessage)"
    }
}

function Test-FilesMateReleaseProcessPath {
    param(
        [AllowNull()]
        [string]$ProcessPath,

        [Parameter(Mandatory)]
        [string]$ReleaseRoot
    )

    if ([string]::IsNullOrWhiteSpace($ProcessPath) -or
        -not [IO.Path]::GetFileName($ProcessPath).Equals('FilesMate.App.exe', [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    try {
        $fullProcessPath = [IO.Path]::GetFullPath($ProcessPath)
        $fullReleaseRoot = [IO.Path]::GetFullPath($ReleaseRoot)
        $relativePath = [IO.Path]::GetRelativePath($fullReleaseRoot, $fullProcessPath)
    }
    catch {
        return $false
    }

    $parentPrefix = "..$([IO.Path]::DirectorySeparatorChar)"
    $alternateParentPrefix = "..$([IO.Path]::AltDirectorySeparatorChar)"
    return -not [IO.Path]::IsPathRooted($relativePath) -and
        $relativePath -ne '..' -and
        -not $relativePath.StartsWith($parentPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        -not $relativePath.StartsWith($alternateParentPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-UniqueTargetWindow {
    param(
        [Parameter(Mandatory)]
        [string]$WindowTitle,

        [Parameter(Mandatory)]
        [string]$ReleaseRoot
    )

    $titleMatches = [NativeWindowCapture]::EnumerateTopLevelWindowsByTitle($WindowTitle)
    $qualifiedMatches = [Collections.Generic.List[object]]::new()
    foreach ($candidateWindow in $titleMatches) {
        $candidateProcessId = [uint32]0
        if ([NativeWindowCapture]::GetWindowThreadProcessId($candidateWindow, [ref]$candidateProcessId) -eq 0) {
            continue
        }

        try {
            $candidateProcess = Get-Process -Id $candidateProcessId -ErrorAction Stop
            if (-not $candidateProcess.ProcessName.Equals('FilesMate.App', [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-FilesMateReleaseProcessPath -ProcessPath $candidateProcess.Path -ReleaseRoot $ReleaseRoot)) {
                continue
            }

            [void]$qualifiedMatches.Add([pscustomobject]@{
                    Window = $candidateWindow
                    Process = $candidateProcess
                })
        }
        catch {
            # An inaccessible or short-lived process cannot qualify as the capture target.
            continue
        }
    }

    if ($qualifiedMatches.Count -eq 0) {
        throw "No visible FilesMate Release window has the exact title '$WindowTitle'. Rejected $($titleMatches.Count) unqualified title match(es)."
    }

    if ($qualifiedMatches.Count -gt 1) {
        throw "Multiple visible FilesMate Release windows have the exact title '$WindowTitle'. Close all but one before capture."
    }

    return $qualifiedMatches[0]
}

function Remove-CaptureFilesBestEffort {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Paths
    )

    $failures = [Collections.Generic.List[string]]::new()
    $attempted = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not $attempted.Add($path)) {
            continue
        }

        try {
            if ([IO.File]::Exists($path)) {
                Remove-Item -LiteralPath $path -Force -ErrorAction Stop
            }

            if ([IO.File]::Exists($path)) {
                throw 'The file still exists after deletion returned.'
            }
        }
        catch {
            [void]$failures.Add("Failed to delete '$path': $($_.Exception.Message)")
        }
    }

    return $failures.ToArray()
}

function Get-StaleCaptureTransactionPaths {
    param(
        [Parameter(Mandatory)]
        [string]$OutputPath
    )

    $directory = [IO.Path]::GetDirectoryName($OutputPath)
    if (-not [IO.Directory]::Exists($directory)) {
        return
    }

    $prefix = ".$([IO.Path]::GetFileName($OutputPath))."
    foreach ($candidate in [IO.Directory]::EnumerateFiles($directory)) {
        $candidateName = [IO.Path]::GetFileName($candidate)
        if (-not $candidateName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $transactionSuffix = $candidateName.Substring($prefix.Length)
        if ($transactionSuffix -match '^[0-9a-fA-F]{32}\.tmp\.(?:png|capture\.json)$') {
            $candidate
        }
    }
}

function Get-WindowRectChecked {
    param(
        [Parameter(Mandatory)]
        [IntPtr]$Window
    )

    $rect = [NativeRect]::new()
    if (-not [NativeWindowCapture]::GetWindowRect($Window, [ref]$rect)) {
        throw "GetWindowRect failed. $(Get-NativeErrorMessage)"
    }

    return $rect
}

function Get-ClientRectChecked {
    param(
        [Parameter(Mandatory)]
        [IntPtr]$Window
    )

    $rect = [NativeRect]::new()
    if (-not [NativeWindowCapture]::GetClientRect($Window, [ref]$rect)) {
        throw "GetClientRect failed. $(Get-NativeErrorMessage)"
    }

    return $rect
}

function Set-ClientSize {
    param(
        [Parameter(Mandatory)]
        [IntPtr]$Window,

        [Parameter(Mandatory)]
        [int]$ClientWidth,

        [Parameter(Mandatory)]
        [int]$ClientHeight
    )

    # A second pass accounts for applications that recalculate their non-client frame
    # after the first resize (for example, when a custom title bar is active).
    foreach ($attempt in 1..2) {
        $windowRect = Get-WindowRectChecked -Window $Window
        $clientRect = Get-ClientRectChecked -Window $Window
        $windowWidth = $windowRect.Right - $windowRect.Left
        $windowHeight = $windowRect.Bottom - $windowRect.Top
        $currentClientWidth = $clientRect.Right - $clientRect.Left
        $currentClientHeight = $clientRect.Bottom - $clientRect.Top
        $outerWidth = $windowWidth + $ClientWidth - $currentClientWidth
        $outerHeight = $windowHeight + $ClientHeight - $currentClientHeight
        $flags = [NativeWindowCapture]::SetWindowPosNoMove -bor
            [NativeWindowCapture]::SetWindowPosNoZOrder -bor
            [NativeWindowCapture]::SetWindowPosNoActivate

        if (-not [NativeWindowCapture]::SetWindowPos(
                $Window,
                [IntPtr]::Zero,
                0,
                0,
                $outerWidth,
                $outerHeight,
                $flags)) {
            throw "SetWindowPos failed. $(Get-NativeErrorMessage)"
        }

        Start-Sleep -Milliseconds 150
    }

    $resizedClient = Get-ClientRectChecked -Window $Window
    $actualWidth = $resizedClient.Right - $resizedClient.Left
    $actualHeight = $resizedClient.Bottom - $resizedClient.Top
    if ($actualWidth -ne $ClientWidth -or $actualHeight -ne $ClientHeight) {
        throw "The window client area is ${actualWidth}x${actualHeight}; requested ${ClientWidth}x${ClientHeight}. Ensure the window is not constrained by the current display."
    }

    return [pscustomobject]@{
        Width = $actualWidth
        Height = $actualHeight
    }
}

function Invoke-WindowCapture {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath, (Get-Location).Path)
    if (-not [IO.Path]::GetExtension($resolvedOutput).Equals('.png', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputPath must name a .png file.'
    }

    $outputDirectory = [IO.Path]::GetDirectoryName($resolvedOutput)
    if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
        throw 'OutputPath must include or resolve to a parent directory.'
    }

    $metadataPath = "$resolvedOutput.capture.json"
    $artifactName = [IO.Path]::GetFileName($resolvedOutput)
    $transactionId = [Guid]::NewGuid().ToString('N')
    $temporaryOutput = Join-Path $outputDirectory ".$artifactName.$transactionId.tmp.png"
    $temporaryMetadata = Join-Path $outputDirectory ".$artifactName.$transactionId.tmp.capture.json"
    $knownStalePaths = @()
    $captureMutex = $null
    $lockAcquired = $false
    $previousDpiContext = [IntPtr]::Zero
    $dpiContextChanged = $false
    $published = $false
    $operationFailure = $null
    $finalizationFailures = [Collections.Generic.List[string]]::new()

    try {
        $captureMutex = Enter-CaptureOutputLock -ResolvedOutput $resolvedOutput -TimeoutMilliseconds 10000
        $lockAcquired = $true

        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
        $knownStalePaths = @(Get-StaleCaptureTransactionPaths -OutputPath $resolvedOutput)
        $initialCleanupFailures = @(Remove-CaptureFilesBestEffort -Paths @(
                $resolvedOutput
                $metadataPath
                $knownStalePaths
            ))
        foreach ($failure in $initialCleanupFailures) {
            [void]$finalizationFailures.Add($failure)
        }

        if ($initialCleanupFailures.Count -gt 0) {
            throw 'Initial capture cleanup failed.'
        }

        if ([string]::IsNullOrWhiteSpace($WindowTitle)) {
            throw 'WindowTitle must not be empty.'
        }

        $normalizedTheme = $null
        foreach ($allowedTheme in @('Light', 'Dark', 'HighContrast')) {
            if ([string]::Equals($Theme, $allowedTheme, [StringComparison]::OrdinalIgnoreCase)) {
                $normalizedTheme = $allowedTheme
                break
            }
        }

        if ($null -eq $normalizedTheme) {
            throw "Theme must be Light, Dark, or HighContrast. Received: '$Theme'."
        }

        $captureWidth = 0
        if (-not [int]::TryParse(
                $Width,
                [Globalization.NumberStyles]::Integer,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$captureWidth) -or
            $captureWidth -lt 320 -or $captureWidth -gt 7680) {
            throw "Width must be between 320 and 7680 pixels. Received: '$Width'."
        }

        $captureHeight = 0
        if (-not [int]::TryParse(
                $Height,
                [Globalization.NumberStyles]::Integer,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$captureHeight) -or
            $captureHeight -lt 240 -or $captureHeight -gt 4320) {
            throw "Height must be between 240 and 4320 pixels. Received: '$Height'."
        }

        $repoRoot = Split-Path -Parent $PSScriptRoot
        $releaseRoot = Join-Path $repoRoot 'src\FilesMate.App\bin\Release'
        $previousDpiContext = Enter-PerMonitorV2DpiContext
        $dpiContextChanged = $true

        $target = Resolve-UniqueTargetWindow -WindowTitle $WindowTitle -ReleaseRoot $releaseRoot
        $window = $target.Window
        $targetProcess = $target.Process

        if (-not [NativeWindowCapture]::IsWindowVisible($window)) {
            throw "The target window '$WindowTitle' became invisible before capture."
        }

        if ([NativeWindowCapture]::IsIconic($window)) {
            throw "The target window '$WindowTitle' is minimized. Restore it before capture."
        }

        $dpi = [NativeWindowCapture]::GetDpiForWindow($window)
        if ($dpi -eq 0) {
            throw "GetDpiForWindow failed for '$WindowTitle'. $(Get-NativeErrorMessage)"
        }

        $dpiScalePercent = [Math]::Round(($dpi / 96.0) * 100)
        if ($dpi -ne 96) {
            Write-Warning "'$WindowTitle' is at $dpi DPI ($dpiScalePercent%). The PNG is valid capture evidence but does not qualify for the fixed 100%-DPI visual baseline."
        }

        $physicalClientSize = Set-ClientSize -Window $window -ClientWidth $captureWidth -ClientHeight $captureHeight

        if ([NativeWindowCapture]::IsHungAppWindow($window)) {
            throw "The target window '$WindowTitle' is not responding; PrintWindow was not attempted."
        }

        $bitmap = $null
        $graphics = $null
        $deviceContext = [IntPtr]::Zero
        try {
            $bitmap = [Drawing.Bitmap]::new(
                $captureWidth,
                $captureHeight,
                [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $deviceContext = $graphics.GetHdc()
            $printFlags = [NativeWindowCapture]::PrintWindowClientOnly -bor
                [NativeWindowCapture]::PrintWindowRenderFullContent

            if (-not ([NativeWindowCapture]::PrintWindow($window, $deviceContext, $printFlags))) {
                throw "PrintWindow returned false for '$WindowTitle'. No fallback screenshot was saved. $(Get-NativeErrorMessage)"
            }

            $graphics.ReleaseHdc($deviceContext)
            $deviceContext = [IntPtr]::Zero
            $bitmap.Save($temporaryOutput, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            if ($deviceContext -ne [IntPtr]::Zero -and $null -ne $graphics) {
                $graphics.ReleaseHdc($deviceContext)
            }

            if ($null -ne $graphics) {
                $graphics.Dispose()
            }

            if ($null -ne $bitmap) {
                $bitmap.Dispose()
            }
        }

        $metadataJson = [ordered]@{
            capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            outputPath = $resolvedOutput
            windowTitle = $WindowTitle
            width = $captureWidth
            height = $captureHeight
            physicalClientWidth = $physicalClientSize.Width
            physicalClientHeight = $physicalClientSize.Height
            clientMetricsUnit = 'physicalPixels'
            dpi = $dpi
            dpiScalePercent = $dpiScalePercent
            dpiAwareness = 'DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2'
            theme = $normalizedTheme
            captureMethod = 'PrintWindow(PW_CLIENTONLY|PW_RENDERFULLCONTENT)'
            processPath = $targetProcess.Path
        } | ConvertTo-Json

        [IO.File]::WriteAllText(
            $temporaryMetadata,
            $metadataJson,
            [Text.UTF8Encoding]::new($false))

        if (-not [IO.File]::Exists($temporaryOutput) -or
            -not [IO.File]::Exists($temporaryMetadata) -or
            ([IO.FileInfo]::new($temporaryOutput)).Length -eq 0 -or
            ([IO.FileInfo]::new($temporaryMetadata)).Length -eq 0) {
            throw 'The capture transaction did not produce a complete PNG/metadata pair.'
        }

        # Commit metadata first and the PNG last. The final PNG is the transaction's
        # visible commit marker; catch cleanup removes either file if the second move fails.
        [IO.File]::Move($temporaryMetadata, $metadataPath, $false)
        [IO.File]::Move($temporaryOutput, $resolvedOutput, $false)
        $published = $true

        Write-Host "Captured '$WindowTitle' client area to '$resolvedOutput' (${captureWidth}x${captureHeight}, $normalizedTheme, $dpi DPI / $dpiScalePercent%)."
        Write-Host "Theme is recorded as scene metadata only; this script does not change the app or system theme."
    }
    catch {
        $operationFailure = $_
    }
    finally {
        if ($lockAcquired) {
            if ($dpiContextChanged) {
                try {
                    Exit-PerMonitorV2DpiContext -PreviousContext $previousDpiContext
                }
                catch {
                    [void]$finalizationFailures.Add("Failed to restore thread DPI awareness: $($_.Exception.Message)")
                }
            }

            $cleanupTargets = @($temporaryOutput, $temporaryMetadata)
            if (-not $published) {
                $cleanupTargets += @($resolvedOutput, $metadataPath)
                $cleanupTargets += $knownStalePaths
            }

            foreach ($failure in @(Remove-CaptureFilesBestEffort -Paths $cleanupTargets)) {
                [void]$finalizationFailures.Add($failure)
            }

            try {
                Exit-CaptureOutputLock -Mutex $captureMutex
            }
            catch {
                [void]$finalizationFailures.Add("Failed to release capture mutex: $($_.Exception.Message)")
            }
        }
        elseif ($null -ne $captureMutex) {
            try {
                $captureMutex.Dispose()
            }
            catch {
                [void]$finalizationFailures.Add("Failed to dispose capture mutex: $($_.Exception.Message)")
            }
        }
    }

    if ($finalizationFailures.Count -gt 0) {
        $failureSummary = $finalizationFailures -join ' | '
        if ($null -ne $operationFailure) {
            throw "$($operationFailure.Exception.Message) Cleanup/finalization failures: $failureSummary"
        }

        throw "Cleanup/finalization failures: $failureSummary"
    }

    if ($null -ne $operationFailure) {
        throw $operationFailure
    }
}

if ($MyInvocation.InvocationName -eq '.') {
    return
}

try {
    Invoke-WindowCapture
}
catch {
    Write-Error $_
    exit 1
}

exit 0
