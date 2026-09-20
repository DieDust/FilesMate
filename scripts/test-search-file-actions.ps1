#Requires -Version 7
param([Parameter(Mandatory)][string]$Manager)
$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetFullPath((Split-Path $Manager)) -ne [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\search-menu-ui'))) {
    throw 'This fixture check requires the isolated FilesMateUITest build in artifacts/search-menu-ui.'
}
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$testRoot = Join-Path (Split-Path $Manager) 'action-fixtures'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$first = Join-Path $testRoot '搜索 操作.txt'
$secondDir = Join-Path $testRoot 'other'
New-Item -ItemType Directory -Path $secondDir -Force | Out-Null
$second = Join-Path $secondDir 'second.txt'
Set-Content -LiteralPath $first -Value 'search action fixture'
Set-Content -LiteralPath $second -Value 'second fixture'
$requestDir = Join-Path $env:LOCALAPPDATA 'FilesMate\search-actions'
New-Item -ItemType Directory -Path $requestDir -Force | Out-Null
function Start-Action([string]$Command, [string[]]$Paths) {
    $id = [Guid]::NewGuid().ToString('N')
    @{Command=$Command;Paths=$Paths} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $requestDir "$id.json")
    Start-Process -FilePath $Manager -ArgumentList @('--search-action',$id) -WindowStyle Hidden -PassThru
}
function Close-Test($Process) {
    $Process.Refresh()
    if (-not $Process.HasExited) {
        $null = $Process.CloseMainWindow()
        if (-not $Process.WaitForExit(5000)) { throw 'Test window did not close normally.' }
    }
}
function Wait-For([scriptblock]$Condition) {
    for ($attempt=0; $attempt -lt 80; $attempt++) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'Timed out waiting for search file action.'
}
$process = Start-Action 'CreateShortcut' @($first)
try { Wait-For { @(Get-ChildItem -LiteralPath $testRoot -Filter '*.lnk').Count -gt 0 } }
finally { Close-Test $process }
$process = Start-Action 'AddToShelf' @($first,$second)
try {
    $shelf = Join-Path (Split-Path $Manager) 'test-profile\file-shelf.json'
    Wait-For {
        if (Test-Path -LiteralPath $shelf) {
            $paths = Get-Content -LiteralPath $shelf -Raw | ConvertFrom-Json
            return $first -in $paths -and $second -in $paths
        }
        return $false
    }
} finally { Close-Test $process }
$process = Start-Action 'Rename' @($first)
try {
    $script:renameBox = $null
    Wait-For {
        $process.Refresh()
        if ($process.MainWindowHandle -eq 0) { return $false }
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $edits = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit))
        foreach ($edit in $edits) {
            $value = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            if ($value.Current.Value -eq [IO.Path]::GetFileName($first)) { $script:renameBox=$edit; return $true }
        }
        return $false
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $cancel = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'取消')))
    if (-not $cancel) { throw 'Rename cancel button missing' }
    $cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    if (-not (Test-Path -LiteralPath $first)) { throw 'Cancelled rename changed the original file' }
} finally { Close-Test $process }
[pscustomobject]@{Passed=$true;ColdStartAction=$true;ShortcutCreated=$true;CrossFolderMultiSelectShelf=$true;RenameTargetsSearchFile=$true;CancelKeepsOriginal=$true} | ConvertTo-Json
