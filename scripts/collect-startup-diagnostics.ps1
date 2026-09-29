#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$InstallDirectory,
    [string]$ProfileDirectory = (Join-Path $env:LOCALAPPDATA 'FilesMate'),
    [string]$OutputDirectory,
    [switch]$SkipEvents
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$OutputDirectory) {
    $OutputDirectory = [Environment]::GetFolderPath('Desktop')
    if (!$OutputDirectory -or !(Test-Path -LiteralPath $OutputDirectory)) { $OutputDirectory = $PSScriptRoot }
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$reportDirectory = Join-Path $OutputDirectory ('FilesMate-Diagnostics-' + $stamp)
New-Item -ItemType Directory -Path $reportDirectory | Out-Null
$errors = New-Object 'Collections.Generic.List[string]'

function Save-Report([string]$Name, [string]$Text) {
    if ($env:USERPROFILE) {
        $Text = $Text.Replace($env:USERPROFILE.Replace('\', '\\'), '%USERPROFILE%')
        $Text = $Text.Replace($env:USERPROFILE, '%USERPROFILE%')
    }
    [IO.File]::WriteAllText((Join-Path $reportDirectory $Name), $Text, (New-Object Text.UTF8Encoding($true)))
}

$candidates = New-Object 'Collections.Generic.List[string]'
if ($InstallDirectory) { $candidates.Add([IO.Path]::GetFullPath($InstallDirectory)) }
foreach ($key in @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7238544-6934-4DD5-A828-887E8E2A40AA}_is1',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7238544-6934-4DD5-A828-887E8E2A40AA}_is1'
)) {
    if (!(Test-Path -LiteralPath $key)) { continue }
    try {
        $location = (Get-Item -LiteralPath $key).GetValue('InstallLocation', '')
        if ($location) { $candidates.Add($location) }
    } catch { $errors.Add('Read installation registration: ' + $_.Exception.Message) }
}
$candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\FilesMate'))
$required = @('FilesMate.App.exe', 'FilesMate.App.dll', 'FilesMate.App.runtimeconfig.json',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.WindowsAppRuntime.dll',
    'Microsoft.ui.xaml.dll', 'Microsoft.Internal.FrameworkUdk.dll', 'Microsoft.UI.Windowing.Core.dll',
    'Microsoft.UI.Xaml.Internal.dll', 'e_sqlite3.dll', 'FilesMate.App.pri',
    'SearchHost\FilesMate.SearchHost.exe', 'SearchHost\coreclr.dll')
$installations = @()
foreach ($candidate in @($candidates | Select-Object -Unique)) {
    if (!(Test-Path -LiteralPath (Join-Path $candidate 'FilesMate.App.exe') -PathType Leaf)) { continue }
    $files = foreach ($name in $required) {
        try {
            $file = Get-Item -LiteralPath (Join-Path $candidate $name) -ErrorAction SilentlyContinue
            if (!$file) { [pscustomobject]@{ Name = $name; Exists = $false }; continue }
            [pscustomobject]@{ Name = $name; Exists = $true; Bytes = $file.Length;
                Version = $file.VersionInfo.ProductVersion; SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
        } catch {
            $errors.Add('Read installed file ' + $name + ': ' + $_.Exception.Message)
            [pscustomobject]@{ Name = $name; Error = $_.Exception.Message }
        }
    }
    $installations += [pscustomobject]@{ Directory = $candidate;
        NativeShellDefault = (Test-Path -LiteralPath (Join-Path $candidate 'shell-compatibility.enabled'));
        Files = @($files) }
}
Save-Report 'installation.json' (ConvertTo-Json -InputObject @($installations) -Depth 6)

$system = [ordered]@{ CollectedAt = (Get-Date -Format o); OSVersion = [Environment]::OSVersion.VersionString;
    ProcessArchitecture = $env:PROCESSOR_ARCHITECTURE; NativeArchitecture = $env:PROCESSOR_ARCHITEW6432;
    PowerShell = $PSVersionTable.PSVersion.ToString() }
try {
    $os = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    foreach ($name in @('ProductName','DisplayVersion','CurrentBuildNumber','UBR','InstallationType')) {
        if ($os.PSObject.Properties[$name]) { $system[$name] = $os.$name }
    }
} catch { $errors.Add('Read Windows version: ' + $_.Exception.Message) }
try {
    $system['Graphics'] = @(Get-CimInstance Win32_VideoController -OperationTimeoutSec 10 |
        Select-Object Name,DriverVersion,DriverDate)
} catch { $errors.Add('Read graphics driver: ' + $_.Exception.Message) }
Save-Report 'system.json' ($system | ConvertTo-Json -Depth 5)

$logStatus = @()
foreach ($name in @('crash.log', 'launch.log')) {
    $source = Join-Path $ProfileDirectory $name
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        try {
            Save-Report $name ((Get-Content -LiteralPath $source -Encoding UTF8 -Tail 1200) -join [Environment]::NewLine)
            $logStatus += $name + ': collected recent lines'
        } catch { $errors.Add('Read ' + $name + ': ' + $_.Exception.Message) }
    } else { $logStatus += $name + ': not present (the process may have failed before application logging)' }
}

$events = @()
if (!$SkipEvents) {
    try {
        # Application Error / WER provide a separate executable-name field.
        # .NET Runtime puts the name and stack into one field, so filter those
        # messages locally before saving. Never export unrelated crash records.
        $query = "*[System[(EventID=1000 or EventID=1001 or EventID=1026) and TimeCreated[timediff(@SystemTime) <= 604800000]]] and *[EventData[Data='FilesMate.App.exe' or Data='FilesMate.SearchHost.exe']]"
        $queryErrors = @()
        $found = @(Get-WinEvent -LogName Application -FilterXPath $query -MaxEvents 60 -ErrorAction SilentlyContinue -ErrorVariable +queryErrors)
        $runtimeQuery = "*[System[Provider[@Name='.NET Runtime'] and EventID=1026 and TimeCreated[timediff(@SystemTime) <= 604800000]]]"
        $runtime = @(Get-WinEvent -LogName Application -FilterXPath $runtimeQuery -MaxEvents 100 -ErrorAction SilentlyContinue -ErrorVariable +queryErrors |
            Where-Object { $_.Message -match '(?i)\bFilesMate\.(App|SearchHost)\.exe\b' })
        $events = @(($found + $runtime) | Sort-Object RecordId -Unique | Select-Object TimeCreated,Id,ProviderName,Message)
        foreach ($issue in $queryErrors) {
            if ($issue.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { $errors.Add('Read Windows application events: ' + $issue.Exception.Message) }
        }
    } catch { $errors.Add('Read Windows application events: ' + $_.Exception.Message) }
}
Save-Report 'windows-events.json' (ConvertTo-Json -InputObject @($events) -Depth 5)

$summary = @(
    'FilesMate startup diagnostics',
    ('Collected: ' + (Get-Date -Format o)),
    ('Installations found: ' + $installations.Count),
    ('Matching Windows events: ' + $events.Count),
    ('Event query skipped: ' + $SkipEvents.IsPresent),
    '',
    'Application logs:',
    ($logStatus -join [Environment]::NewLine),
    '',
    'This tool only reads system/app diagnostics and creates this local report.',
    'It does not launch/stop FilesMate, change settings, install runtimes, or upload files.',
    'Logs may contain file paths. The current user-profile prefix is replaced.',
    '',
    'Collection errors:',
    ($errors -join [Environment]::NewLine)
) -join [Environment]::NewLine
Save-Report 'README.txt' $summary
$zipPath = $reportDirectory + '.zip'
Compress-Archive -LiteralPath $reportDirectory -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host ''
Write-Host 'Diagnostic report created:' -ForegroundColor Green
Write-Host $zipPath
[pscustomobject]@{ Report = $zipPath; Installations = $installations.Count; Events = $events.Count; CollectionErrors = $errors.Count }
