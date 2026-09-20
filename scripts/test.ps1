#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release', 'Profile')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$resultsRoot = Join-Path $repoRoot 'artifacts\test-results'
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

$testProjects = Get-ChildItem -Path (Join-Path $repoRoot 'tests') -Recurse -Filter '*.csproj' |
    Where-Object { $_.BaseName -notmatch 'Performance' } |
    Sort-Object FullName

if ($testProjects.Count -eq 0) {
    throw 'No non-performance test projects were found under tests/.'
}

Write-Host "Running non-performance tests ($($testProjects.Count) projects)"
$failed = 0
foreach ($project in $testProjects) {
    Write-Host "  test: $($project.BaseName)"
    $logger = "trx;LogFileName=$($project.BaseName).trx"
    dotnet test $project.FullName `
        --configuration $Configuration `
        --no-build `
        --logger $logger `
        --results-directory $resultsRoot `
        --collect:'XPlat Code Coverage'
    if ($LASTEXITCODE -ne 0) {
        $failed = $LASTEXITCODE
    }
}

if ($failed -ne 0) {
    Write-Error "One or more test projects failed (exit $failed). Results: $resultsRoot"
}

exit $failed
