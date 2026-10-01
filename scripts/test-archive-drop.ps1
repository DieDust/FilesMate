#Requires -Version 7
[CmdletBinding()]
param([Parameter(Mandatory)][string]$TestExe, [string]$Output)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = [IO.Path]::GetFullPath($TestExe)
if (!$exe.StartsWith((Join-Path $repo 'artifacts') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an isolated FilesMateUITest build under artifacts.'
}
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Test executable not found: $exe" }
$testRoot = Split-Path $exe -Parent
$profile = Join-Path $testRoot 'test-profile'
if (!$Output) { $Output = Join-Path $repo 'artifacts/archive-drop/results' }
New-Item -ItemType Directory -Force -Path $profile,$Output | Out-Null
@{ Tabs=@('filesmate:home'); SelectedTabIndex=0 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'window-session.json') -Encoding utf8
'{"Language":"zh-CN"}' | Set-Content -LiteralPath (Join-Path $profile 'language.json') -Encoding utf8
'{"Completed":true}' | Set-Content -LiteralPath (Join-Path $profile 'feature-setup.json') -Encoding utf8
@{ Roots=@(); Exclusions=@(); DatabaseDirectory=(Join-Path $profile 'index'); AutoRefresh=$false; MaxDepth=1 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'search-index.json') -Encoding utf8
$report = Join-Path $testRoot 'archive-drop-smoke.json'
$started = [DateTime]::UtcNow
$priorSmoke = $env:FILESMATE_ARCHIVE_DROP_SMOKE
$env:FILESMATE_ARCHIVE_DROP_SMOKE = '1'
try { $fixture = Start-Process -FilePath $exe -ArgumentList '--native-shell' -WindowStyle Hidden -PassThru }
finally { $env:FILESMATE_ARCHIVE_DROP_SMOKE = $priorSmoke }
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 200
        if ($fixture.HasExited) { throw 'Archive drop fixture exited before reporting' }
        $file = Get-Item -LiteralPath $report -ErrorAction SilentlyContinue
    } until (($file -and $file.LastWriteTimeUtc -gt $started) -or [DateTime]::UtcNow -gt $deadline)
    if (!$file -or $file.LastWriteTimeUtc -le $started) { throw 'Archive drop fixture timed out' }
    Copy-Item -LiteralPath $report -Destination $Output
    $data = Get-Content -LiteralPath $report -Raw -Encoding utf8 | ConvertFrom-Json
    if (!$data.Passed) { throw ($data | ConvertTo-Json -Depth 8) }
    [pscustomobject]@{ Passed=$data.Passed; Checks=$data.Checks.Count; Fixture=$data.Fixture } | ConvertTo-Json -Compress
} finally {
    if (!$fixture.HasExited) {
        [void]$fixture.CloseMainWindow()
        if (!$fixture.WaitForExit(8000)) { throw 'Archive drop fixture did not close' }
    }
}
