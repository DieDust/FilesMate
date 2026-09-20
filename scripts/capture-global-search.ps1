#Requires -Version 7
param([Parameter(Mandatory)][string]$Profile, [Parameter(Mandatory)][string]$Output, [string]$Query = '', [int]$WaitMilliseconds = 1600, [ValidateSet('show','settings','ranking')][string]$Mode='show', [string]$Filter='全部')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
$reply = & "$PSScriptRoot\search-host-command.ps1" -Profile $Profile -Command $(if($Mode -eq 'ranking'){'settings'}else{$Mode}) | ConvertFrom-Json
$root = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$reply.Pid),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'FilesMate 全局搜索')))
if (-not $root) { throw 'Search palette was interrupted or did not open.' }
$empty = $root.Current.BoundingRectangle
$box = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'GlobalSearchQuery'))
if($Mode -eq 'show') { $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Query) }
if($Mode -eq 'show' -and $Query -and $Filter -ne '全部') {
    $choice=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::RadioButton),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Filter)))
    $choice.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
if($Mode -eq 'ranking') {
    $rank=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'调整顺序  ›'))
    $rank.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
Start-Sleep -Milliseconds $WaitMilliseconds
$bounds = $root.Current.BoundingRectangle
if ($bounds.IsEmpty -or $bounds.Width -le 0) { throw 'Palette lost foreground before capture.' }
# Capture the composed desktop, including DWM's real corner clipping and border.
# PrintWindow omits these and previously hid the nested-corner defect.
$bitmap = [Drawing.Bitmap]::new([int]$bounds.Width+20,[int]$bounds.Height+20)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen([int]$bounds.Left-10,[int]$bounds.Top-10,0,0,$bitmap.Size)
    $bitmap.Save([IO.Path]::GetFullPath($Output))
} finally { $graphics.Dispose(); $bitmap.Dispose() }
$texts = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text))
[pscustomobject]@{Pid=$reply.Pid;Width=$bounds.Width;Height=$bounds.Height;InputTopBefore=$empty.Top;InputTopAfter=$bounds.Top;Text=@($texts|ForEach-Object {$_.Current.Name})} | ConvertTo-Json -Depth 3
$root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
