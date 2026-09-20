#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$Payload
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repoRoot ('artifacts\installer-test-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$installDir = Join-Path $testRoot 'installed'
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7238544-6934-4DD5-A828-887E8E2A40AA}_is1'
if (Test-Path -LiteralPath $registration) {
    throw 'FilesMate is already installed for this user; do not overwrite that installation for a test.'
}
New-Item -ItemType Directory -Path $testRoot | Out-Null

function Get-AssociationSnapshot {
    $keys = @('Software\FilesMate\DefaultFolderHandler',
        'Software\Microsoft\Windows\CurrentVersion\App Paths\explorer.exe')
    foreach ($class in @('Directory','Drive','Folder',
        'CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}',
        'CLSID\{52205fd8-5dfb-447b-9315-f8f65da153c3}')) {
        $keys += "Software\Classes\$class\shell"
    }
    $values = [ordered]@{}
    foreach ($key in $keys) {
        $path = "HKCU:\$key"
        if (-not (Test-Path -LiteralPath $path)) { continue }
        foreach ($item in @((Get-Item -LiteralPath $path)) + @(Get-ChildItem -LiteralPath $path -Recurse)) {
            foreach ($name in ($item.GetValueNames() | Sort-Object)) {
                $values[$item.Name + '|' + $name] = $item.GetValue($name)
            }
        }
    }
    $values | ConvertTo-Json -Depth 8 -Compress
}

$runPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$startupName = 'FilesMate.GlobalSearch'
$startupKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
$startupBefore = if($startupKey){$startupKey.GetValue($startupName)}else{$null}
if($startupKey){$startupKey.Dispose()}
$before = Get-AssociationSnapshot
$uninstall = Join-Path $installDir 'unins000.exe'
try {
    $process = Start-Process -FilePath ([IO.Path]::GetFullPath($Installer)) -WindowStyle Hidden -PassThru -Wait `
        -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOICONS',
            '/NOCLOSEAPPLICATIONS',"/DIR=`"$installDir`"", "/LOG=`"$testRoot\install.log`"")
    if ($process.ExitCode -ne 0) { throw "Install failed ($($process.ExitCode))." }
    $startupKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
    $startupAfter = if($startupKey){$startupKey.GetValue($startupName)}else{$null}
    if($startupKey){$startupKey.Dispose()}
    $globalPath = Join-Path $env:LOCALAPPDATA 'FilesMate\global-search.json'
    $startupExpected = $true
    if(Test-Path -LiteralPath $globalPath){
        $globalSettings = Get-Content -LiteralPath $globalPath -Raw | ConvertFrom-Json
        if($globalSettings.PSObject.Properties['Enabled'] -and -not $globalSettings.Enabled){$startupExpected=$false}
        if($globalSettings.PSObject.Properties['StartAtLogin'] -and -not $globalSettings.StartAtLogin){$startupExpected=$false}
    }
    $setupPath = Join-Path $env:LOCALAPPDATA 'FilesMate\feature-setup.json'
    $setupComplete = (Test-Path -LiteralPath $setupPath) -and (Get-Content -LiteralPath $setupPath -Raw | ConvertFrom-Json).Completed
    if(-not $setupComplete){
        if($startupAfter -cne $startupBefore){throw 'Installer changed startup before the first-run choice.'}
    }elseif($startupExpected){
        $expectedCommand = '"' + (Join-Path $installDir 'SearchHost\FilesMate.SearchHost.exe') + '" --background --startup'
        if($startupAfter -cne $expectedCommand){throw 'Installer did not register the background-only startup command.'}
    }elseif($null -ne $startupAfter){throw 'Installer ignored the saved startup opt-out.'}
    $count = 0
    foreach ($file in Get-ChildItem -LiteralPath $Payload -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath([IO.Path]::GetFullPath($Payload), $file.FullName)
        $installed = Join-Path $installDir $relative
        if (-not (Test-Path -LiteralPath $installed) -or
            (Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
            throw "Installed payload mismatch: $relative"
        }
        $count++
    }

    # Execute the shipped managed entry point without starting the user's UI/session.
    $process = Start-Process -FilePath (Join-Path $installDir 'FilesMate.App.exe') `
        -ArgumentList '--unregister-folder-handler' -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Installed runtime smoke check failed ($($process.ExitCode))." }
    if ((Get-AssociationSnapshot) -cne $before) { throw 'The unrelated folder association changed.' }
}
finally {
    try {
        if (Test-Path -LiteralPath $uninstall) {
            $process = Start-Process -FilePath $uninstall -WindowStyle Hidden -PassThru -Wait `
                -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$testRoot\uninstall.log`"")
            if ($process.ExitCode -ne 0) { throw "Uninstall failed ($($process.ExitCode))." }
        }
        $startupKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
        $remaining = if($startupKey){$startupKey.GetValue($startupName)}else{$null}
        if($startupKey){$startupKey.Dispose()}
        if($remaining -and $remaining.Contains($installDir)){throw 'Uninstaller left its startup command behind.'}
    }finally {
        $startupKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runPath)
        try { if($null -eq $startupBefore){$startupKey.DeleteValue($startupName,$false)}else{$startupKey.SetValue($startupName,$startupBefore,[Microsoft.Win32.RegistryValueKind]::String)} }
        finally {$startupKey.Dispose()}
    }
}
if (Test-Path -LiteralPath $registration) { throw 'Uninstall registration was left behind.' }
if (Test-Path -LiteralPath (Join-Path $installDir 'FilesMate.App.exe')) { throw 'Application binary was left behind.' }
if ((Get-AssociationSnapshot) -cne $before) { throw 'Uninstall changed an unrelated folder association.' }
[pscustomobject]@{ Passed = $true; FilesVerified = $count; RuntimeEntryPoint = 'Passed';
    StartupRegistrationAndCleanup = $true; AssociationsPreserved = $true; InstallAndUninstall = 'Passed';
    CleanMachineUiTest = 'Not performed'; Installer = [IO.Path]::GetFullPath($Installer) } |
    ConvertTo-Json | Tee-Object -FilePath (Join-Path $testRoot 'result.json')
