#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TestExe,
    [switch]$NoNativeShell,
    [string]$Output
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = [IO.Path]::GetFullPath($TestExe)
if (!$exe.StartsWith((Join-Path $repo 'artifacts') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an isolated FilesMateUITest build under artifacts.'
}
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Test executable not found: $exe" }
$testRoot = Split-Path $exe -Parent
$profile = Join-Path $testRoot 'test-profile'
if (!$Output) { $Output = Join-Path $repo ('artifacts/startup-fix/' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force -Path $profile,$Output | Out-Null
@{ Tabs=@($testRoot); SelectedTabIndex=0 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'window-session.json') -Encoding utf8
'{"Language":"zh-CN"}' | Set-Content -LiteralPath (Join-Path $profile 'language.json') -Encoding utf8
'{"Completed":true,"FavoritesBarEnabled":true}' | Set-Content -LiteralPath (Join-Path $profile 'feature-setup.json') -Encoding utf8
@{ Roots=@($testRoot); Exclusions=@(); DatabaseDirectory=(Join-Path $profile 'index'); AutoRefresh=$false; MaxDepth=1 } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'search-index.json') -Encoding utf8
$report = Join-Path $testRoot 'database-startup-smoke.json'
$started = [DateTime]::UtcNow
$previousSmoke = $env:FILESMATE_DATABASE_STARTUP_SMOKE
$env:FILESMATE_DATABASE_STARTUP_SMOKE = '1'
$mode = if ($NoNativeShell) { '--no-native-shell' } else { '--native-shell' }
try { $fixture = Start-Process -FilePath $exe -ArgumentList $mode -WindowStyle Hidden -PassThru }
finally { $env:FILESMATE_DATABASE_STARTUP_SMOKE = $previousSmoke }
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 150
        $file = Get-Item -LiteralPath $report -ErrorAction SilentlyContinue
        if ($fixture.HasExited) { throw ('Startup fixture exited before completion: ' + $fixture.ExitCode) }
    } until (($file -and $file.LastWriteTimeUtc -gt $started) -or [DateTime]::UtcNow -gt $deadline)
    if (!$file -or $file.LastWriteTimeUtc -le $started) { throw 'Startup fixture timed out' }
    $data = Get-Content -LiteralPath $report -Raw
    Copy-Item -LiteralPath $report -Destination $Output
    if (!(ConvertFrom-Json $data).Passed) { throw $data }
    $data
} finally {
    if (!$fixture.HasExited) {
        [void]$fixture.CloseMainWindow()
        if (!$fixture.WaitForExit(8000)) { throw 'Startup fixture did not close' }
    }
}
