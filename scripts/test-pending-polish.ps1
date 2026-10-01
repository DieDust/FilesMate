#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TestExe,
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
if (!$Output) { $Output = Join-Path $repo 'artifacts/pending-polish-results' }
New-Item -ItemType Directory -Force -Path $profile,$Output | Out-Null
@{ Tabs=@('filesmate:home'); SelectedTabIndex=0 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'window-session.json') -Encoding utf8
'{"Language":"zh-CN"}' | Set-Content -LiteralPath (Join-Path $profile 'language.json') -Encoding utf8
'{"Completed":true}' | Set-Content -LiteralPath (Join-Path $profile 'feature-setup.json') -Encoding utf8
@{ Roots=@($testRoot); Exclusions=@(); DatabaseDirectory=(Join-Path $profile 'index'); AutoRefresh=$false; MaxDepth=2 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'search-index.json') -Encoding utf8
$report = Join-Path $testRoot 'pending-polish-smoke.json'
$started = [DateTime]::UtcNow
$previousSmoke = $env:FILESMATE_PENDING_POLISH_SMOKE
$env:FILESMATE_PENDING_POLISH_SMOKE = '1'
try { $fixture = Start-Process -FilePath $exe -ArgumentList '--native-shell' -WindowStyle Hidden -PassThru }
finally { $env:FILESMATE_PENDING_POLISH_SMOKE = $previousSmoke }
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    do {
        Start-Sleep -Milliseconds 250
        $file = Get-Item -LiteralPath $report -ErrorAction SilentlyContinue
        if ($fixture.HasExited) { throw 'Pending polish fixture exited before completion' }
    } until (($file -and $file.LastWriteTimeUtc -gt $started) -or [DateTime]::UtcNow -gt $deadline)
    if (!$file -or $file.LastWriteTimeUtc -le $started) { throw 'Pending polish fixture timed out' }
    $data = Get-Content -LiteralPath $report -Raw
    Copy-Item -LiteralPath $report -Destination $Output
    Get-ChildItem -LiteralPath $testRoot -File -Filter '*.png' | Where-Object LastWriteTimeUtc -gt $started | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Output
    }
    if (!(ConvertFrom-Json $data).Passed) { throw $data }
    $data
} finally {
    if (!$fixture.HasExited) {
        [void]$fixture.CloseMainWindow()
        if (!$fixture.WaitForExit(8000)) { throw 'Pending polish fixture did not close' }
    }
}
