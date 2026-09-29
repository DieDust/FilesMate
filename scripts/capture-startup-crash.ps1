#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [string]$OutputDirectory,
    [string]$ProfileDirectory,
    [string]$ProcDumpPath,
    [switch]$NoNativeShell,
    [switch]$AcceptProcDumpEula
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (![Environment]::Is64BitProcess) { throw 'Run this tool in 64-bit Windows PowerShell.' }
if (!$ExecutablePath) {
    $candidates = @()
    foreach ($key in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7238544-6934-4DD5-A828-887E8E2A40AA}_is1',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7238544-6934-4DD5-A828-887E8E2A40AA}_is1'
    )) {
        if (Test-Path -LiteralPath $key) {
            $location = (Get-Item -LiteralPath $key).GetValue('InstallLocation', '')
            if ($location) { $candidates += Join-Path $location 'FilesMate.App.exe' }
        }
    }
    $candidates += Join-Path $env:LOCALAPPDATA 'Programs\FilesMate\FilesMate.App.exe'
    $ExecutablePath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (!$ExecutablePath -or !(Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw 'FilesMate was not found. Specify -ExecutablePath with the installed FilesMate.App.exe path.'
}
$ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath)
$processName = [IO.Path]::GetFileNameWithoutExtension($ExecutablePath)
if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'The application is already running. Close its windows normally before starting this capture.'
}

if (!$OutputDirectory) {
    $OutputDirectory = [Environment]::GetFolderPath('Desktop')
    if (!$OutputDirectory -or !(Test-Path -LiteralPath $OutputDirectory)) { $OutputDirectory = $PSScriptRoot }
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$reportDirectory = Join-Path $OutputDirectory ('FilesMate-CrashCapture-' + $stamp)

if (!$ProcDumpPath) {
    $toolDirectory = Join-Path $PSScriptRoot 'tools'
    New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
    $ProcDumpPath = Join-Path $toolDirectory 'procdump64.exe'
    if (!(Test-Path -LiteralPath $ProcDumpPath -PathType Leaf)) {
        Write-Host 'Downloading Microsoft ProcDump from the official Sysinternals site...'
        $archive = Join-Path $toolDirectory ('Procdump-' + $stamp + '.zip')
        $previousProtocol = [Net.ServicePointManager]::SecurityProtocol
        try {
            [Net.ServicePointManager]::SecurityProtocol = $previousProtocol -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri 'https://download.sysinternals.com/files/Procdump.zip' -OutFile $archive -UseBasicParsing -TimeoutSec 60
        } finally { [Net.ServicePointManager]::SecurityProtocol = $previousProtocol }
        Expand-Archive -LiteralPath $archive -DestinationPath $toolDirectory -Force
    }
}
$ProcDumpPath = [IO.Path]::GetFullPath($ProcDumpPath)
$signature = Get-AuthenticodeSignature -LiteralPath $ProcDumpPath
if ($signature.Status -ne 'Valid' -or !$signature.SignerCertificate -or
    $signature.SignerCertificate.Subject -notmatch '(^|,\s*)CN=Microsoft Corporation(,|$)') {
    throw 'ProcDump does not have a valid Microsoft signature. Capture was not started.'
}

# With redirected output and no console, ProcDump prints its EULA and exits
# instead of opening a license dialog. Obtain consent here before launching it.
$licenseAccepted = $AcceptProcDumpEula.IsPresent
if (!$licenseAccepted) {
    $licenseKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Sysinternals\ProcDump')
    try { $licenseAccepted = $licenseKey -and $licenseKey.GetValue('EulaAccepted', 0) -eq 1 }
    finally { if ($licenseKey) { $licenseKey.Dispose() } }
}
if (!$licenseAccepted) {
    $licensePath = Join-Path ([IO.Path]::GetDirectoryName($ProcDumpPath)) 'Eula.txt'
    if (!(Test-Path -LiteralPath $licensePath -PathType Leaf)) {
        throw 'Microsoft Eula.txt is missing. Extract the complete official ProcDump ZIP into the tools folder and try again.'
    }
    Write-Host ''
    Write-Host (Get-Content -LiteralPath $licensePath -Raw -Encoding UTF8)
    Write-Host ''
    Write-Host 'Microsoft ProcDump requires acceptance of the license above.' -ForegroundColor Yellow
    Write-Host 'A crash dump may contain private application memory. Nothing will be uploaded.'
    Write-Host 'Type Y and press Enter to accept the Microsoft ProcDump license; N to cancel.'
    $answer = [string](Read-Host 'Accept license')
    if ($answer.Trim() -notin @('Y', 'YES')) {
        Write-Host 'Cancelled. FilesMate was not started and no crash capture was attempted.'
        exit 2
    }
    $licenseAccepted = $true
}

New-Item -ItemType Directory -Path $reportDirectory | Out-Null
$launchArguments = @()
if ($NoNativeShell) { $launchArguments += '--no-native-shell' }
$capture = [ordered]@{
    CaptureToolVersion = 2
    StartedAt = (Get-Date -Format o)
    Executable = $ExecutablePath
    Version = (Get-Item -LiteralPath $ExecutablePath).VersionInfo.ProductVersion
    SHA256 = (Get-FileHash -LiteralPath $ExecutablePath -Algorithm SHA256).Hash
    Arguments = @($launchArguments)
    ProcDumpVersion = (Get-Item -LiteralPath $ProcDumpPath).VersionInfo.FileVersion
    ProcDumpSHA256 = (Get-FileHash -LiteralPath $ProcDumpPath -Algorithm SHA256).Hash
}
Write-Host ''
Write-Host 'FilesMate will start once under Microsoft ProcDump.'
Write-Host 'If FilesMate opens normally, close its window normally to finish the report.'
Write-Host 'This captures application memory on a crash. Share the result privately with support.'
Write-Host 'It does not install a debugger, change FilesMate settings, or upload the report.'
Write-Host ''

# Native debugging captures WinUI fail-fast / stowed exceptions; managed-only
# exception monitoring can miss these. Pass -accepteula only after the user's
# explicit consent above (or a previously accepted license). No -i or -k.
$captureArguments = @('-ma', '-e', '-g', '-n', '1', '-accepteula')
$captureArguments += @('-x', $reportDirectory, $ExecutablePath)
$captureArguments += $launchArguments
# ProcDump emits UTF-16 when redirected. Copy raw streams first: a PowerShell
# 5.1 native-command pipeline otherwise corrupts both text and non-ASCII paths.
$monitor = New-Object Diagnostics.Process
$monitor.StartInfo = New-Object Diagnostics.ProcessStartInfo
$monitor.StartInfo.FileName = $ProcDumpPath
# These are switches and absolute file paths (no embedded quotes or trailing
# directory separator). Arguments go directly to CreateProcess, never a shell.
$monitor.StartInfo.Arguments = ($captureArguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
$monitor.StartInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($ExecutablePath)
$monitor.StartInfo.UseShellExecute = $false
$monitor.StartInfo.CreateNoWindow = $true
$monitor.StartInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$monitor.StartInfo.RedirectStandardOutput = $true
$monitor.StartInfo.RedirectStandardError = $true
$stdout = $null
$stderr = $null
try {
    if (!$monitor.Start()) { throw 'ProcDump could not be started.' }
    $stdout = [IO.File]::Create((Join-Path $reportDirectory 'capture.log'))
    $stderr = [IO.File]::Create((Join-Path $reportDirectory 'capture-errors.log'))
    $stdoutCopy = $monitor.StandardOutput.BaseStream.CopyToAsync($stdout)
    $stderrCopy = $monitor.StandardError.BaseStream.CopyToAsync($stderr)
    $monitor.WaitForExit()
    [void]$stdoutCopy.GetAwaiter().GetResult()
    [void]$stderrCopy.GetAwaiter().GetResult()
    $capture['ProcDumpExitCode'] = $monitor.ExitCode
} finally {
    if ($stdout) { $stdout.Dispose() }
    if ($stderr) { $stderr.Dispose() }
    $monitor.Dispose()
}
foreach ($logName in @('capture.log', 'capture-errors.log')) {
    $logPath = Join-Path $reportDirectory $logName
    $bytes = [IO.File]::ReadAllBytes($logPath)
    $encoding = [Text.Encoding]::UTF8
    if ($bytes.Length -ge 2 -and ($bytes[1] -eq 0 -or ($bytes[0] -eq 255 -and $bytes[1] -eq 254))) {
        $encoding = [Text.Encoding]::Unicode
    }
    $logText = $encoding.GetString($bytes).TrimStart([char]0xFEFF)
    if ($env:USERPROFILE) { $logText = $logText.Replace($env:USERPROFILE, '%USERPROFILE%') }
    [IO.File]::WriteAllText($logPath, $logText, (New-Object Text.UTF8Encoding($true)))
    if ($logText) { Write-Host $logText }
}
$dumps = @(Get-ChildItem -LiteralPath $reportDirectory -Filter '*.dmp' -File)
$capture['DumpCount'] = $dumps.Count
$captureText = [IO.File]::ReadAllText((Join-Path $reportDirectory 'capture.log'))
$capture['MonitoringStarted'] = [regex]::IsMatch($captureText, '(?m)^Process:\s+\S')
$capture['Outcome'] = if ($dumps.Count -gt 0) { 'CrashCaptured' }
    elseif (!$capture.MonitoringStarted) { 'MonitorDidNotStart' }
    elseif ($capture.ProcDumpExitCode -ne 0) { 'CaptureFailed' }
    else { 'NoCrashCaptured' }
$capture['FinishedAt'] = Get-Date -Format o
$captureJson = $capture | ConvertTo-Json -Depth 4
if ($env:USERPROFILE) {
    $captureJson = $captureJson.Replace($env:USERPROFILE.Replace('\', '\\'), '%USERPROFILE%')
}
[IO.File]::WriteAllText((Join-Path $reportDirectory 'capture.json'), $captureJson, (New-Object Text.UTF8Encoding($true)))

$collector = Join-Path $PSScriptRoot 'collect-startup-diagnostics.ps1'
if (Test-Path -LiteralPath $collector -PathType Leaf) {
    $collectArguments = @{ InstallDirectory = [IO.Path]::GetDirectoryName($ExecutablePath); OutputDirectory = $reportDirectory }
    if ($ProfileDirectory) { $collectArguments['ProfileDirectory'] = $ProfileDirectory }
    try { & $collector @collectArguments | Out-Host }
    catch { $_ | Out-String | Set-Content -LiteralPath (Join-Path $reportDirectory 'collection-error.txt') -Encoding UTF8 }
}
$summary = @(
    'FilesMate native startup crash capture',
    ('Outcome: ' + $capture.Outcome),
    ('Monitoring started: ' + $capture.MonitoringStarted),
    ('Crash dumps: ' + $dumps.Count),
    ('Native shell disabled for this launch: ' + $NoNativeShell.IsPresent),
    '',
    'A dump contains the application memory and may include file names, paths and other private data.',
    'Send the ZIP privately to the developer; do not post it publicly.',
    'No report was uploaded. No app files or settings were changed by this capture tool.',
    'If no dump was captured, send capture.log and describe whether the application opened normally.'
) -join [Environment]::NewLine
[IO.File]::WriteAllText((Join-Path $reportDirectory 'README.txt'), $summary, (New-Object Text.UTF8Encoding($true)))
$zipPath = $reportDirectory + '.zip'
Compress-Archive -LiteralPath $reportDirectory -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host ''
if ($dumps.Count -gt 0) { Write-Host 'Crash captured. Report ready:' -ForegroundColor Green }
elseif ($capture.Outcome -in @('MonitorDidNotStart', 'CaptureFailed')) {
    Write-Host 'Capture FAILED. No crash dump was obtained. Send capture.log to support.' -ForegroundColor Red
}
else { Write-Host 'No crash dump was captured. Diagnostic report ready:' -ForegroundColor Yellow }
Write-Host $zipPath
[pscustomobject]@{ Report = $zipPath; Outcome = $capture.Outcome; Dumps = $dumps.Count; ProcDumpExitCode = $capture.ProcDumpExitCode }
if ($capture.Outcome -in @('MonitorDidNotStart', 'CaptureFailed')) { exit 3 }
