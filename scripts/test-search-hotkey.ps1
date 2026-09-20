#Requires -Version 7
param([Parameter(Mandatory)][string]$Profile)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
public static class SearchKeyboardProbe {
 [StructLayout(LayoutKind.Sequential)] struct Keyboard {public ushort Key,Scan;public uint Flags,Time;public IntPtr Extra;}
 [StructLayout(LayoutKind.Explicit,Size=40)] struct Input {[FieldOffset(0)]public uint Type;[FieldOffset(8)]public Keyboard Keyboard;}
 [DllImport("user32.dll")] static extern uint SendInput(uint count,Input[] data,int size);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string name);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 public static IntPtr Find(uint pid){var h=IntPtr.Zero;while((h=FindWindowEx(IntPtr.Zero,h,null,"FilesMate 全局搜索"))!=IntPtr.Zero){uint p;GetWindowThreadProcessId(h,out p);if(p==pid)return h;}return IntPtr.Zero;}
 static Input Key(ushort key,bool up=false)=>new Input{Type=1,Keyboard=new Keyboard{Key=key,Flags=up?2u:0u}};
 public static double Press(uint pid,bool opening=true){
  var keys=new[]{Key(17),Key(18),Key(16),Key(121),Key(121,true),Key(16,true),Key(18,true),Key(17,true)};
  var timer=Stopwatch.StartNew();if(SendInput((uint)keys.Length,keys,40)!=keys.Length)throw new Exception("Keyboard input was not accepted");
  while(timer.ElapsedMilliseconds<2000){var h=Find(pid);if(h!=IntPtr.Zero&&(opening ? IsWindowVisible(h)&&GetForegroundWindow()==h : !IsWindowVisible(h)))return timer.Elapsed.TotalMilliseconds;Thread.Sleep(1);}
  uint owner;GetWindowThreadProcessId(GetForegroundWindow(),out owner);
  throw new Exception("Search did not acquire foreground: foreground PID="+owner+", expected PID="+pid+", palette="+Find(pid));
 }
 public static void Dismiss(uint pid){if(GetForegroundWindow()!=Find(pid))throw new Exception("Foreground changed; no Escape sent");var keys=new[]{Key(27),Key(27,true)};SendInput(2,keys,40);}
}
'@
$settings=Get-Content (Join-Path $Profile 'global-search.json') -Raw | ConvertFrom-Json
if($settings.Hotkey -ne 'Ctrl+Alt+Shift+F10'){throw 'This probe only uses the isolated Ctrl+Alt+Shift+F10 fixture.'}
$state=& "$PSScriptRoot\search-host-command.ps1" -Profile $Profile | ConvertFrom-Json
if($state.Visible){throw 'Start with the fixture hidden.'}
$latency=[SearchKeyboardProbe]::Press($state.Pid)
$window=[System.Windows.Automation.AutomationElement]::FromHandle([SearchKeyboardProbe]::Find($state.Pid))
$query=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'GlobalSearchQuery'))
if(-not $query.Current.HasKeyboardFocus){throw 'Search opened without focusing its input.'}
$value=$query.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$value.SetValue('hotkey-preserved')
$null=[SearchKeyboardProbe]::Press($state.Pid,$false)
Start-Sleep -Milliseconds 100
$hidden=& "$PSScriptRoot\search-host-command.ps1" -Profile $Profile | ConvertFrom-Json
if($hidden.Visible){throw 'Second shortcut did not close search.'}
$null=[SearchKeyboardProbe]::Press($state.Pid)
if(-not $query.Current.HasKeyboardFocus){throw 'Third shortcut did not focus input.'}
[SearchKeyboardProbe]::Dismiss($state.Pid)
Start-Sleep -Milliseconds 150
$closed=& "$PSScriptRoot\search-host-command.ps1" -Profile $Profile | ConvertFrom-Json
if($closed.Visible){throw 'Escape did not dismiss search.'}
[pscustomobject]@{Passed=$true;KeyboardToForegroundMilliseconds=[math]::Round($latency,2);InputFocused=$true;SecondPressCloses=$true;ThirdPressOpens=$true;EscapeDismisses=$true}|ConvertTo-Json
