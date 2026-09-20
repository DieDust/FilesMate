#Requires -Version 7
param([switch]$KeepRunning)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (Get-Process FilesMate.App -ErrorAction SilentlyContinue) { throw 'Close FilesMate before the isolated search UI test.' }
$repo = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $repo ('artifacts/search-regression-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$nested = Join-Path $fixture 'one/two/three/four/five'
$noise = Join-Path $fixture 'AetherFolder'
[IO.Directory]::CreateDirectory($nested) | Out-Null
[IO.Directory]::CreateDirectory($noise) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'prefix-Aether-中文100%.txt'), '')
[IO.File]::WriteAllText((Join-Path $nested 'deep-aether.txt'), '')
[IO.File]::WriteAllText((Join-Path $noise 'unrelated-readme.txt'), '')
$session = Join-Path $env:LOCALAPPDATA 'FilesMate/window-session.json'
if (Test-Path -LiteralPath $session) { Copy-Item -LiteralPath $session -Destination (Join-Path $fixture 'session-before.json') }
$exe = Join-Path $repo 'src/FilesMate.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/FilesMate.App.exe'
$process = Start-Process -FilePath $exe -ArgumentList ('/open "' + $fixture + '"') -PassThru
function Find([string]$id) {
    $process.Refresh()
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { return $null }
    $root = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $root.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
}
function WaitFor([scriptblock]$condition, [string]$description) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $description"
}
function Query([string]$text) {
    $box = Find 'SearchBox'
    $box.SetFocus()
    ([Windows.Automation.ValuePattern]$box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue($text)
}
function Names {
    $list = Find 'SearchHits'
    if ($null -eq $list) { return @() }
    @($list.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'NameText')) |
        ForEach-Object { $_.Current.Name })
}
$checks = [Collections.Generic.List[string]]::new()
try {
    WaitFor { $null -ne (Find 'SearchButton') } 'search button'
    ([Windows.Automation.InvokePattern](Find 'SearchButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Query 'aether'
    WaitFor { $status = Find 'SearchStatus'; $status -and $status.Current.Name -like '*3*' } 'three scoped results'
    $names = Names
    if ($names -contains 'unrelated-readme.txt' -or $names -notcontains 'deep-aether.txt') { throw "Incorrect scoped results: $names" }
    $checks.Add('Current folder search finds deep new files and excludes parent-name-only matches.')
    Query 'no-such-file-fm-regression'
    WaitFor { $status = Find 'SearchStatus'; $status -and $status.Current.Name -like '*0*' } 'visible empty state'
    $checks.Add('No-result status stays visible.')
    Query 'aether'
    WaitFor { (Names) -contains 'deep-aether.txt' } 'results restored'
    $toggle = Find 'SearchEverywhere'
    ([Windows.Automation.TogglePattern]$toggle.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)).Toggle()
    WaitFor { @((Names) | Where-Object { $_ -like 'AetherSwap*' }).Count -gt 0 } 'global indexed results'
    $checks.Add('Scope toggle searches the real global index without losing the results panel.')
    ([Windows.Automation.TogglePattern](Find 'SearchEverywhere').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Query 'deep-aether'
    WaitFor { (Names) -contains 'deep-aether.txt' } 'deep target ready to open'
    $item = (Find 'SearchHits').FindFirst([Windows.Automation.TreeScope]::Children,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
    ([Windows.Automation.InvokePattern]$item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
    WaitFor { $value = [Windows.Automation.ValuePattern](Find 'PathBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern); $value.Current.Value -eq $nested.Replace('/','\') } 'target parent folder'
    WaitFor { (Find 'SelectionBlock').Current.Name -match '1' } 'target selection'
    $checks.Add('Clicking a search result opens its parent and selects the target file.')
    [pscustomobject]@{Success=$true;Fixture=$fixture;Checks=$checks.ToArray()} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $fixture 'result.json')
} catch {
    $status = Find 'SearchStatus'
    Write-Host "Search status: $($status.Current.Name); visible results: $(Names)"
    throw
} finally {
    if (-not $KeepRunning) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(10000)) { throw 'Search test window did not close normally.' }
        $backup = Join-Path $fixture 'session-before.json'
        if (Test-Path -LiteralPath $backup) { Copy-Item -LiteralPath $backup -Destination $session -Force }
    }
}
