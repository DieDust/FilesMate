#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release', 'Profile')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot "src\FilesMate.FlowPlugin\bin\$Configuration\net10.0"
if (-not (Test-Path (Join-Path $source 'FilesMate.FlowPlugin.exe'))) {
    throw "Build the Flow plugin first: pwsh ./scripts/build.ps1 -Configuration $Configuration"
}

$manifest = Get-Content (Join-Path $source 'plugin.json') -Raw | ConvertFrom-Json
$pluginsRoot = Join-Path $env:APPDATA 'FlowLauncher\Plugins'
$existing = @(Get-ChildItem -LiteralPath $pluginsRoot -Directory -ErrorAction SilentlyContinue | Where-Object {
    $manifestPath = Join-Path $_.FullName 'plugin.json'
    if (Test-Path -LiteralPath $manifestPath) {
        try { (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).ID -eq $manifest.ID } catch { $false }
    }
})
if ($existing.Count -gt 1) { throw 'Multiple FilesMate plugin installations found. Resolve duplicate installations before upgrading.' }
$destination = if ($existing.Count -eq 1) { $existing[0].FullName } else { Join-Path $pluginsRoot ('FilesMate-' + $manifest.Version) }
$app = Join-Path $repoRoot "src\FilesMate.App\bin\$Configuration\net10.0-windows10.0.26100.0\win-x64\FilesMate.App.exe"
if (-not (Test-Path -LiteralPath $app)) { throw "Build FilesMate before installing its plugin: $app" }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
@{ executable = $app } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'filesmate.json') -Encoding utf8
Write-Host "Installed Flow plugin to $destination"
Write-Host "The executable plugin is updated in place; existing Flow action keywords are preserved."
Write-Host "Restart Flow Launcher once to clear cached queries, or press Ctrl+R to refresh the current query."
