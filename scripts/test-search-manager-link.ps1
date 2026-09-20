#Requires -Version 7
param([Parameter(Mandatory)][string]$Manager)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ManagerLinkNative {
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
}
'@
if(Get-Process FilesMate.App -ErrorAction SilentlyContinue){throw 'Close the file manager before this isolated launch check.'}
$process=Start-Process -FilePath $Manager -ArgumentList '--settings-search' -WindowStyle Hidden -PassThru
try{
 for($attempt=0;$attempt -lt 60;$attempt++){
  Start-Sleep -Milliseconds 100
  $process.Refresh()
  if($process.HasExited){throw 'File manager exited during startup.'}
  if($process.MainWindowHandle -ne 0){break}
 }
 Start-Sleep -Milliseconds 1500
 $window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
 $texts=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $names=@($texts|ForEach-Object{$_.Current.Name})
 if('全局快捷键' -notin $names -or '托盘左键打开' -notin $names){throw 'Search settings were not opened directly.'}
 if('应用设置' -in $names){throw 'The manual global-search save button is still present.'}
 if('开机自启' -notin $names){throw 'Startup setting is missing.'}
 $startupControl=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'GlobalSearchStartup'))
 if(-not $startupControl){throw 'Startup control is missing from UI automation.'}
 $startupToggle=$startupControl.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
 $startupOriginal=$startupToggle.Current.ToggleState
 try{
  $startupToggle.Toggle()
  Start-Sleep -Milliseconds 450
  $afterStartup=Get-Content "$env:LOCALAPPDATA\FilesMate\global-search.json" -Raw | ConvertFrom-Json
  if([bool]$afterStartup.StartAtLogin -eq ($startupOriginal -eq [System.Windows.Automation.ToggleState]::On)){throw 'Startup toggle did not auto-save.'}
  $runKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
  try{$startupCommand=$runKey.GetValue('FilesMate.GlobalSearch')}finally{$runKey.Dispose()}
  if(([bool]$startupCommand) -ne ([bool]$afterStartup.Enabled -and [bool]$afterStartup.StartAtLogin)){throw 'Startup toggle did not update Windows registration.'}
 }finally{
  if($startupToggle.Current.ToggleState -ne $startupOriginal){$startupToggle.Toggle();Start-Sleep -Milliseconds 450}
 }

 $trayChoice=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'GlobalSearchTrayAction'))
 if(-not $trayChoice){throw 'Tray action control was not exposed to UI automation.'}
 $saved=Get-Content "$env:LOCALAPPDATA\FilesMate\global-search.json" -Raw | ConvertFrom-Json
 $original=if($saved.TrayLeftAction -eq 'Files'){'Files'}else{'Search'}
 function Set-TrayChoice([string]$Value){
  $trayChoice.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
  Start-Sleep -Milliseconds 100
  $name=if($Value -eq 'Files'){'文件管理器'}else{'搜索框'}
  $choice=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.AndCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name),
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)))
  if(-not $choice){throw "Tray choice was not found: $name"}
  $choice.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  for($attempt=0;$attempt -lt 20;$attempt++){
   Start-Sleep -Milliseconds 100
   $current=Get-Content "$env:LOCALAPPDATA\FilesMate\global-search.json" -Raw | ConvertFrom-Json
   if($current.TrayLeftAction -eq $Value){return}
  }
  throw 'Main-window settings did not auto-save.'
 }
 try { Set-TrayChoice $(if($original -eq 'Files'){'Search'}else{'Files'}); Set-TrayChoice $original }
 finally {
  $current=Get-Content "$env:LOCALAPPDATA\FilesMate\global-search.json" -Raw | ConvertFrom-Json
  if($current.TrayLeftAction -ne $original){
   $null=& "$PSScriptRoot\search-host-command.ps1" -Command apply -Hotkey $saved.Hotkey -Enabled ([bool]$saved.Enabled) -TrayLeftAction $original
  }
 }
 $activation=Start-Process -FilePath $Manager -ArgumentList '--activate' -WindowStyle Hidden -PassThru -Wait
 Start-Sleep -Milliseconds 350
 if($activation.ExitCode -ne 0 -or [ManagerLinkNative]::IsIconic($process.MainWindowHandle) -or -not [ManagerLinkNative]::IsWindowVisible($process.MainWindowHandle)){throw 'Activate command hid or minimized the manager.'}
 $repeat=Start-Process -FilePath $Manager -ArgumentList '--settings-search' -WindowStyle Hidden -PassThru -Wait
 if($repeat.ExitCode -ne 0){throw 'Redirected search settings launch failed.'}
 [pscustomobject]@{Passed=$true;StartupAutoSaveAndRegistration=$true;GlobalSettingsAutoSave=$true;DirectSearchSettings=$true;ActivateDoesNotMinimize=$true;RedirectedSettings=$true;Version=(Get-Item $Manager).VersionInfo.FileVersion}|ConvertTo-Json
}finally{
 if(-not $process.HasExited){$null=$process.CloseMainWindow();if(-not $process.WaitForExit(5000)){throw 'Test window did not close normally.'}}
}
