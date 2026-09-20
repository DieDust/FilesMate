#Requires -Version 7
[CmdletBinding()]
param(
    [string]$AppPath,

    [Parameter(Mandatory = $true)]
    [string]$DatasetRoot,

    [string]$Scenario = 'AllReleaseGates',

    [int]$Repetitions = 3,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ($Repetitions -lt 1) {
    throw 'Repetitions must be >= 1.'
}

if (-not [System.IO.Path]::IsPathRooted($DatasetRoot)) {
    throw "DatasetRoot must be an absolute path. Received: $DatasetRoot"
}

$resolvedDatasets = [System.IO.Path]::GetFullPath($DatasetRoot)
$commit = 'unknown'
try {
    $commit = (git -C $repoRoot rev-parse --short HEAD).Trim()
}
catch {
    Write-Warning 'git rev-parse failed; using commit=unknown'
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\perf\$commit\$timestamp"
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

Write-Host "FilesMate run-perf"
Write-Host "  scenario     : $Scenario"
Write-Host "  repetitions  : $Repetitions"
Write-Host "  app          : $(if ($AppPath) { $AppPath } else { '(not supplied)' })"
Write-Host "  datasets     : $resolvedDatasets"
Write-Host "  output       : $resolvedOutput"
Write-Host "  commit       : $commit"
Write-Host "  machine      : $env:COMPUTERNAME"
Write-Host "  os           : $([System.Environment]::OSVersion.VersionString)"
Write-Host "  dotnet       : $(dotnet --version)"
Write-Host "  time         : $((Get-Date).ToString('o'))"

$datasetNames = @('small-1k', 'medium-10k', 'large-100k', 'images-10k', 'unicode-5k', 'deep-tree')
$present = @()
foreach ($name in $datasetNames) {
    $path = Join-Path $resolvedDatasets $name
    $marker = Join-Path $path '.filesmate-dataset-marker'
    $present += [pscustomobject]@{
        name       = $name
        path       = $path
        present    = (Test-Path $marker)
        markerPath = $marker
    }
}

$summary = [ordered]@{
    schema        = 'filesmate-perf-summary/v1'
    scenario      = $Scenario
    repetitions   = $Repetitions
    commit        = $commit
    machine       = $env:COMPUTERNAME
    os            = [System.Environment]::OSVersion.VersionString
    dotnet        = (dotnet --version)
    appPath       = $AppPath
    datasetRoot   = $resolvedDatasets
    generatedUtc  = [DateTimeOffset]::UtcNow.ToString('o')
    status        = 'skeleton'
    notes         = 'End-to-end scenario instrumentation lands in Task 20. This run records environment and dataset presence only.'
    datasets      = $present
    results       = @()
}

$jsonPath = Join-Path $resolvedOutput 'summary.json'
$mdPath = Join-Path $resolvedOutput 'summary.md'
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $jsonPath -Encoding utf8

$md = @()
$md += "# FilesMate perf $Scenario"
$md += ""
$md += "- commit: $commit"
$md += "- machine: $env:COMPUTERNAME"
$md += "- status: skeleton (Task 20 fills measurements)"
$md += ""
$md += "| Dataset | Present |"
$md += "| --- | --- |"
foreach ($row in $present) {
    $md += "| $($row.name) | $($row.present) |"
}
$md -join [Environment]::NewLine | Set-Content -Path $mdPath -Encoding utf8

Write-Host "Wrote $jsonPath"
Write-Host "Wrote $mdPath"
