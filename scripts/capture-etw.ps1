#Requires -Version 7
[CmdletBinding(DefaultParameterSetName = 'Script')]
param(
    [Parameter(Mandatory = $true)]
    [string]$EtlPath,

    [string]$Profile = 'GeneralProfile',

    [Parameter(ParameterSetName = 'Script')]
    [scriptblock]$ScriptBlock,

    [Parameter(ParameterSetName = 'Action')]
    [ValidateSet('Start', 'Stop')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$wpr = Get-Command wpr -ErrorAction SilentlyContinue
if (-not $wpr) {
    throw 'wpr.exe was not found on PATH. Install the Windows Performance Toolkit.'
}

Write-Host "FilesMate capture-etw"
Write-Host "  wpr      : $($wpr.Source)"
Write-Host "  profile  : $Profile"
Write-Host "  etl      : $EtlPath"
Write-Host "  action   : $(if ($Action) { $Action } else { 'ScriptBlock' })"
Write-Host "  machine  : $env:COMPUTERNAME"
Write-Host "  os       : $([System.Environment]::OSVersion.VersionString)"
Write-Host "  time     : $((Get-Date).ToString('o'))"

$etlDirectory = Split-Path -Parent $EtlPath
if ($etlDirectory -and -not (Test-Path $etlDirectory)) {
    New-Item -ItemType Directory -Force -Path $etlDirectory | Out-Null
}

function Start-FilesMateWpr {
    Write-Host "wpr -start $Profile"
    & wpr -start $Profile
    if ($LASTEXITCODE -ne 0) {
        throw "wpr start failed with exit $LASTEXITCODE"
    }
}

function Stop-FilesMateWpr {
    Write-Host "wpr -stop $EtlPath"
    & wpr -stop $EtlPath
    if ($LASTEXITCODE -ne 0) {
        throw "wpr stop failed with exit $LASTEXITCODE"
    }
}

if ($Action -eq 'Start') {
    Start-FilesMateWpr
    return
}

if ($Action -eq 'Stop') {
    Stop-FilesMateWpr
    return
}

$started = $false
try {
    Start-FilesMateWpr
    $started = $true
    if ($null -ne $ScriptBlock) {
        & $ScriptBlock
    }
}
finally {
    if ($started) {
        Stop-FilesMateWpr
    }
}
