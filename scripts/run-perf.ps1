#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('ResourceChecks')][string]$Scenario = 'ResourceChecks',
    [ValidateRange(1, 20)][int]$Repetitions = 3,
    [string]$OutputDirectory,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$commit = (& git -C $repoRoot rev-parse --short HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the measured source revision.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ("artifacts/perf/{0}/{1}" -f $commit, (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$project = Join-Path $repoRoot 'tests/FilesMate.PerformanceTests/FilesMate.PerformanceTests.csproj'
if (!$NoBuild) {
    & dotnet build $project -c Release '-p:Platform=x64' *> (Join-Path $output 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "Resource-check build failed. See $output/build.log" }
}
$samples = [Collections.Generic.List[object]]::new()
$runs = [Collections.Generic.List[object]]::new()
for ($i = 1; $i -le $Repetitions; $i++) {
    & dotnet test $project -c Release --no-build '-p:Platform=x64' --logger "trx;LogFileName=resource-$i.trx" --results-directory $output *> (Join-Path $output "run-$i.log")
    $testExit = $LASTEXITCODE
    $trxPath = Join-Path $output "resource-$i.trx"
    if (!(Test-Path -LiteralPath $trxPath)) { throw "No measurement result for run $i; see its log." }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counter = $trx.TestRun.ResultSummary.Counters
    $runs.Add([pscustomobject]@{Run=$i;ExitCode=$testExit;Total=[int]$counter.total;Passed=[int]$counter.passed;Failed=[int]$counter.failed;Skipped=[int]$counter.notExecuted})
    foreach ($node in $trx.SelectNodes("//*[local-name()='StdOut']")) {
        foreach ($match in [regex]::Matches($node.InnerText, '(?m)^PERF_SAMPLE (.+)$')) {
            $sample = $match.Groups[1].Value.Trim() | ConvertFrom-Json
            $sample | Add-Member -NotePropertyName Run -NotePropertyValue $i
            $samples.Add($sample)
        }
    }
}
function Percentile([double[]]$Values, [double]$Quantile) {
    $sorted = @($Values | Sort-Object)
    return [math]::Round($sorted[[math]::Max(0, [math]::Ceiling($sorted.Count * $Quantile) - 1)], 4)
}
$statistics = @($samples | Group-Object Scenario | ForEach-Object {
    [pscustomobject]@{Scenario=$_.Name;Samples=$_.Count;MedianMs=(Percentile $_.Group.Milliseconds .5);P95Ms=(Percentile $_.Group.Milliseconds .95)}
})
$passed = @($runs | Where-Object { $_.ExitCode -ne 0 -or $_.Total -ne 2 -or $_.Passed -ne 2 -or $_.Failed -ne 0 -or $_.Skipped -ne 0 }).Count -eq 0 -and $statistics.Count -eq 2
$summary = [ordered]@{
    Schema='filesmate-resource-checks/v1'; Scenario=$Scenario; Commit=$commit
    GeneratedUtc=[DateTimeOffset]::UtcNow.ToString('O'); Machine=$env:COMPUTERNAME
    OS=[Environment]::OSVersion.VersionString; LogicalProcessors=[Environment]::ProcessorCount
    Configuration='Release x64'; Repetitions=$Repetitions; Passed=$passed
    Scope='100,000 metadata events over 8 names; repeated early disposal of real Windows enumeration over 40 generated files. Each run warms up before measured rounds.'
    Limits='Bounded-work and release checks. Timing is observational, with no latency pass threshold. Does not measure app launch, UI frames, preview child processes, or certify all release budgets.'
    Statistics=$statistics; Runs=$runs; Samples=$samples
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
$lines = @('# FilesMate resource checks', '', "- Commit: $commit", "- Passed: $passed", "- Scope: $($summary.Scope)", "- Limits: $($summary.Limits)", '', '| Scenario | Samples | Median ms | P95 ms |', '| --- | ---: | ---: | ---: |')
foreach ($row in $statistics) { $lines += "| $($row.Scenario) | $($row.Samples) | $($row.MedianMs) | $($row.P95Ms) |" }
$lines -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $output 'summary.md') -Encoding utf8
$statistics | Format-Table
Write-Output "Results: $output"
if (!$passed) { throw 'One or more resource checks failed or measurements are missing.' }
