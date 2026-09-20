#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Root,

    [switch]$SkipLarge
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not [System.IO.Path]::IsPathRooted($Root)) {
    throw "Root must be an absolute path. Received: $Root"
}

$resolvedRoot = [System.IO.Path]::GetFullPath($Root)
Write-Host "FilesMate create-perf-data"
Write-Host "  root     : $resolvedRoot"
Write-Host "  machine  : $env:COMPUTERNAME"
Write-Host "  os       : $([System.Environment]::OSVersion.VersionString)"
Write-Host "  dotnet   : $(dotnet --version)"
Write-Host "  time     : $((Get-Date).ToString('o'))"

$generatorProject = Join-Path $repoRoot 'tools\FilesMate.TestDataGenerator\FilesMate.TestDataGenerator.csproj'
dotnet build $generatorProject --configuration Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$generatorDll = Join-Path $repoRoot 'tools\FilesMate.TestDataGenerator\bin\Release\net10.0\FilesMate.TestDataGenerator.dll'
if (-not (Test-Path $generatorDll)) {
    throw "Generator assembly not found: $generatorDll"
}

function Invoke-Generator {
    param(
        [string[]]$ArgumentList
    )

    Write-Host ("dotnet {0}" -f ($ArgumentList -join ' '))
    & dotnet @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Generator failed with exit $LASTEXITCODE"
    }
}

$datasets = @(
    @{ Name = 'small-1k'; Files = 1000; Directories = 40; Depth = 3; Seed = 1; Profile = 'mixed' },
    @{ Name = 'medium-10k'; Files = 10000; Directories = 80; Depth = 3; Seed = 2; Profile = 'mixed' },
    @{ Name = 'large-100k'; Files = 100000; Directories = 120; Depth = 4; Seed = 3; Profile = 'mixed' },
    @{ Name = 'images-10k'; Files = 10000; Directories = 30; Depth = 2; Seed = 4; Profile = 'images' },
    @{ Name = 'unicode-5k'; Files = 5000; Directories = 40; Depth = 3; Seed = 5; Profile = 'unicode' },
    @{ Name = 'deep-tree'; Files = 200; Directories = 40; Depth = 20; Seed = 6; Profile = 'mixed' }
)

New-Item -ItemType Directory -Force -Path $resolvedRoot | Out-Null

foreach ($dataset in $datasets) {
    if ($SkipLarge -and $dataset.Name -in @('large-100k', 'images-10k')) {
        Write-Host "Skipping $($dataset.Name)"
        continue
    }

    $target = Join-Path $resolvedRoot $dataset.Name
    Write-Host "Creating $($dataset.Name) at $target"
    Invoke-Generator -ArgumentList @(
        $generatorDll,
        'create',
        '--root', $target,
        '--files', "$($dataset.Files)",
        '--directories', "$($dataset.Directories)",
        '--depth', "$($dataset.Depth)",
        '--seed', "$($dataset.Seed)",
        '--profile', $dataset.Profile
    )
}

Write-Host "Datasets ready under $resolvedRoot"
