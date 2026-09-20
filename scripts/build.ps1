#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release', 'Profile')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$solution = Join-Path $repoRoot 'FilesMate.slnx'
if (-not (Test-Path $solution)) {
    throw "Solution not found: $solution"
}

Write-Host "FilesMate build"
Write-Host "  configuration : $Configuration"
Write-Host "  solution      : $solution"
Write-Host "  machine       : $env:COMPUTERNAME"
Write-Host "  os            : $([System.Environment]::OSVersion.VersionString)"
Write-Host "  dotnet        : $(dotnet --version)"
Write-Host "  time          : $((Get-Date).ToString('o'))"

dotnet restore $solution
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet build $solution --configuration $Configuration --no-restore -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# An unpackaged WinUI app must carry the self-contained Windows App SDK native
# runtime beside the apphost. A stale output directory can otherwise look like
# a successful build while launching into a white window and failing during
# Windows App SDK auto-initialization (REGDB_E_CLASSNOTREG).
$appOutput = Join-Path $repoRoot "src\FilesMate.App\bin\$Configuration\net10.0-windows10.0.26100.0\win-x64"
$requiredRuntimeFiles = @(
    'Microsoft.WindowsAppRuntime.dll',
    'Microsoft.WindowsAppRuntime.Bootstrap.dll',
    'Microsoft.ui.xaml.dll'
)
$missingRuntimeFiles = @(
    $requiredRuntimeFiles | Where-Object { -not (Test-Path (Join-Path $appOutput $_)) }
)
if ($missingRuntimeFiles.Count -gt 0) {
    throw "FilesMate.App output is missing required Windows App SDK runtime file(s): $($missingRuntimeFiles -join ', '). Rebuild the $Configuration configuration before launching."
}

exit 0
