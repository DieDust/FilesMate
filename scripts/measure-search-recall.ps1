#Requires -Version 7
param([Parameter(Mandatory)][string]$Profile, [int]$Samples=5)
$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
public static class SearchRecallProbe {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 public static IntPtr Find(uint pid,bool listener) {
  IntPtr after=IntPtr.Zero; string title=listener?"FilesMate.GlobalSearch.Hotkey":"FilesMate 全局搜索";
  while((after=FindWindowEx(listener?new IntPtr(-3):IntPtr.Zero,after,null,title))!=IntPtr.Zero) {
   uint owner;GetWindowThreadProcessId(after,out owner); if(owner==pid)return after;
  }return IntPtr.Zero;
 }
 public static double Show(uint pid) {
  var target=Find(pid,true); if(target==IntPtr.Zero)throw new Exception("Listener not found");
  var clock=Stopwatch.StartNew();PostMessage(target,0x312,new IntPtr(1),IntPtr.Zero);
  while(clock.ElapsedMilliseconds<5000){var window=Find(pid,false);if(window!=IntPtr.Zero&&IsWindowVisible(window))return clock.Elapsed.TotalMilliseconds;Thread.Sleep(1);}
  throw new TimeoutException("Palette did not become visible");
 }
 public static void Close(uint pid){PostMessage(Find(pid,false),0x10,IntPtr.Zero,IntPtr.Zero);}
 public static void Repeat(uint pid){PostMessage(Find(pid,true),0x312,new IntPtr(1),IntPtr.Zero);}
}
'@
$times=@()
for($sample=0;$sample -lt $Samples;$sample++){
 $state=& "$PSScriptRoot\search-host-command.ps1" -Profile $Profile | ConvertFrom-Json
 $times += [math]::Round([SearchRecallProbe]::Show($state.Pid),2)
 [SearchRecallProbe]::Close($state.Pid)
 Start-Sleep -Milliseconds 700
}
$state=& "$PSScriptRoot\search-host-command.ps1" -Profile $Profile | ConvertFrom-Json
$null=[SearchRecallProbe]::Show($state.Pid)
[SearchRecallProbe]::Repeat($state.Pid)
Start-Sleep -Milliseconds 150
$visible=[SearchRecallProbe]::IsWindowVisible([SearchRecallProbe]::Find($state.Pid,$false))
if($visible){[SearchRecallProbe]::Close($state.Pid)}
[pscustomobject]@{VisibleMilliseconds=$times;RepeatedHotkeyCloses=(-not $visible)}|ConvertTo-Json
