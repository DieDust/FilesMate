#Requires -Version 7
[CmdletBinding()]
param([Parameter(Mandatory)][string]$TestExe, [string]$Output, [string[]]$Qualities = @('Standard','Ultra'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
$exe = [IO.Path]::GetFullPath($TestExe)
if (!$exe.StartsWith($artifactRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an isolated UI-test build under artifacts.' }
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Test executable not found.' }
if (!$Output) { $Output = Join-Path $artifactRoot 'thumbnail-memory' }
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$testRoot = Split-Path $exe -Parent
$profile = Join-Path $testRoot 'test-profile'
New-Item -ItemType Directory -Path $profile -Force | Out-Null
$fixtureRoot = Join-Path $artifactRoot 'thumbnail-memory-fixture'
$emptyFolder = Join-Path $fixtureRoot 'empty'
$imageFolder = Join-Path $fixtureRoot 'images'
New-Item -ItemType Directory -Path $emptyFolder,$imageFolder -Force | Out-Null
Add-Type -AssemblyName System.Drawing
for ($index=0; $index -lt 24; $index++) {
    $file = Join-Path $imageFolder ('photo-{0:D2}.png' -f $index)
    if (Test-Path -LiteralPath $file) { continue }
    $bitmap = [Drawing.Bitmap]::new(2048,1536)
    $canvas = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $canvas.Clear([Drawing.Color]::FromArgb(255,30+$index*7,70+$index*4,180-$index*3))
        $canvas.FillRectangle([Drawing.Brushes]::White,64,96,1536,640)
        $canvas.FillRectangle([Drawing.Brushes]::DarkBlue,480,400,768,900)
        $bitmap.Save($file,[Drawing.Imaging.ImageFormat]::Png)
    } finally { $canvas.Dispose(); $bitmap.Dispose() }
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ThumbnailMemoryWindow {
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint process);
}
'@
$results = @()
foreach ($quality in $Qualities) {
    if ($quality -notin @('Standard','Ultra')) { throw 'Unsupported benchmark quality.' }
    @{Tabs=@($emptyFolder);SelectedTabIndex=0} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'window-session.json') -Encoding utf8
    @{Width=2100;Height=1500;Maximized=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'window.json') -Encoding utf8
    @{ThumbnailQuality=$quality;ShowFullThumbnails=$true;ShowFolderSizes=$false;ShowGridFileSizes=$false;RestoreLastSession=$true;TabMemory='Aggressive'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'explorer.json') -Encoding utf8
    '{"Language":"zh-CN"}' | Set-Content -LiteralPath (Join-Path $profile 'language.json') -Encoding utf8
    '{"Completed":true}' | Set-Content -LiteralPath (Join-Path $profile 'feature-setup.json') -Encoding utf8
    '{}' | Set-Content -LiteralPath (Join-Path $profile 'folder-customizations.json') -Encoding utf8
    @{Global=$true;View=@{Details=$false;GridSlot=384;Sort=@{Column=0;Ascending=$true;DirectoriesFirst=$true};List=$false;ListZoomPercent=100};Initialized=$true} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $profile 'folder-customizations.view-scope.json') -Encoding utf8
    @{Roots=@($fixtureRoot);Exclusions=@();DatabaseDirectory=(Join-Path $profile 'index');AutoRefresh=$false;MaxDepth=3} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'search-index.json') -Encoding utf8
    $variables = @{
        FILESMATE_TAB_MEMORY_SMOKE='1';FILESMATE_MEMORY_NAVIGATION_PROBE='1';FILESMATE_MEMORY_PATHS=($emptyFolder+'|'+$imageFolder)
        FILESMATE_MEMORY_GRID='1';FILESMATE_MEMORY_SETTLE_MS='3000';FILESMATE_TAB_MEMORY_CYCLES='3'
    }
    $priorEnvironment = @{}
    foreach ($key in $variables.Keys) { $priorEnvironment[$key] = [Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$variables[$key]) }
    $started = [DateTime]::UtcNow
    try { $process = Start-Process -FilePath $exe -ArgumentList '--native-shell' -WindowStyle Hidden -PassThru }
    finally { foreach ($key in $variables.Keys) { [Environment]::SetEnvironmentVariable($key,$priorEnvironment[$key]) } }
    try {
        $reportPath = Join-Path $testRoot 'tab-memory-smoke.json'
        $deadline = [DateTime]::UtcNow.AddSeconds(180)
        do {
            Start-Sleep -Milliseconds 500
            $process.Refresh()
            $handle = $process.MainWindowHandle
            if ($handle -ne 0) {
                $windowProcess = 0
                [void][ThumbnailMemoryWindow]::GetWindowThreadProcessId($handle,[ref]$windowProcess)
                if ($windowProcess -eq $process.Id) { [void][ThumbnailMemoryWindow]::SetWindowPos($handle,[IntPtr]::Zero,-12000,-12000,0,0,0x15) }
            }
            $reportFile = Get-Item -LiteralPath $reportPath -ErrorAction SilentlyContinue
            if ($process.HasExited) { throw 'Memory fixture exited early.' }
        } until (($reportFile -and $reportFile.LastWriteTimeUtc -gt $started) -or [DateTime]::UtcNow -gt $deadline)
        if (!$reportFile -or $reportFile.LastWriteTimeUtc -le $started) { throw 'Memory fixture timed out.' }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        Copy-Item -LiteralPath $reportPath -Destination (Join-Path $Output ($quality+'.json'))
        $results += [pscustomobject]@{Quality=$quality;Version=(Get-Item -LiteralPath (Join-Path $testRoot 'FilesMate.App.dll')).VersionInfo.FileVersion;Report=$report}
        $results | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Output 'comparison.json') -Encoding utf8
        [pscustomobject]@{Quality=$quality;Passed=$report.Passed;Samples=$report.Samples.Count} | ConvertTo-Json -Compress
    } finally {
        if (!$process.HasExited) { [void]$process.CloseMainWindow(); if (!$process.WaitForExit(8000)) { throw 'Memory fixture did not close.' } }
    }
}
if ($results.Where({!$_.Report.Passed}).Count -gt 0) { throw 'Memory fixture checks failed; inspect the saved results.' }
