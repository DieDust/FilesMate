#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateRange(5, 300)]
    [int]$Seconds = 15,
    [string]$OutputPath,
    [ValidateRange(0, 2147483647)]
    [int]$ProcessId = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$processes = @(if ($ProcessId -gt 0) { Get-Process -Id $ProcessId -ErrorAction Stop } else { Get-Process -Name FilesMate.App -ErrorAction SilentlyContinue })
if ($processes.Count -ne 1) {
    throw 'Open exactly one FilesMate process before measuring idle usage.'
}
$process = $processes[0]
if ($process.ProcessName -notin @('FilesMate.App', 'FilesMate.SearchHost')) { throw 'Select a FilesMate app or search host process.' }
$executable = $process.Path
$window = $process.MainWindowTitle
$startedAt = [DateTimeOffset]::Now
$samples = [Collections.Generic.List[object]]::new()
$clock = [Diagnostics.Stopwatch]::StartNew()
$previousMs = $clock.Elapsed.TotalMilliseconds
$previousCpu = $process.TotalProcessorTime.TotalMilliseconds
for ($i = 0; $i -lt $Seconds; $i++) {
    Start-Sleep -Seconds 1
    $process.Refresh()
    if ($process.HasExited) { throw 'FilesMate exited during measurement.' }
    $nowMs = $clock.Elapsed.TotalMilliseconds
    $cpu = $process.TotalProcessorTime.TotalMilliseconds
    $samples.Add([pscustomobject]@{
        ElapsedMs = [math]::Round($nowMs, 1)
        CpuMachinePercent = ($cpu - $previousCpu) / ($nowMs - $previousMs) / [Environment]::ProcessorCount * 100
        WorkingSetMB = $process.WorkingSet64 / 1MB
        PrivateMB = $process.PrivateMemorySize64 / 1MB
        Handles = $process.HandleCount
    })
    $previousMs = $nowMs
    $previousCpu = $cpu
}

function Percentile([double[]]$Values, [double]$Quantile) {
    $sorted = @($Values | Sort-Object)
    return [math]::Round($sorted[[math]::Max(0, [math]::Ceiling($sorted.Count * $Quantile) - 1)], 3)
}

$result = [ordered]@{
    StartedAt = $startedAt.ToString('O')
    ProcessId = $process.Id
    ProcessName = $process.ProcessName
    Executable = $executable
    ExecutableModifiedUtc = (Get-Item -LiteralPath $executable).LastWriteTimeUtc.ToString('O')
    OS = [Environment]::OSVersion.VersionString
    LogicalProcessors = [Environment]::ProcessorCount
    WindowTitle = $window
    DurationSeconds = $Seconds
    CpuMedianMachinePercent = Percentile $samples.CpuMachinePercent 0.5
    CpuP95MachinePercent = Percentile $samples.CpuMachinePercent 0.95
    WorkingSetMedianMB = Percentile $samples.WorkingSetMB 0.5
    PrivateMedianMB = Percentile $samples.PrivateMB 0.5
    Notes = 'Observational sample of the current window; not a cold-launch, frame-time, or full release-gate benchmark.'
    Samples = $samples
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot ("..\artifacts\perf\idle-{0}.json" -f $startedAt.ToString('yyyyMMdd-HHmmss'))
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path $OutputPath) -Force | Out-Null
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
[pscustomobject]$result | Select-Object CpuMedianMachinePercent, CpuP95MachinePercent, WorkingSetMedianMB, PrivateMedianMB
Write-Output "Results: $OutputPath"
$process.Dispose()
