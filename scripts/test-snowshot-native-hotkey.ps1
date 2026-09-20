#Requires -Version 7
param([Parameter(Mandatory)][int]$ManagerPid, [Parameter(Mandatory)][int]$SnowPid,
    [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;
public static class SnowNativeInput {
 [StructLayout(LayoutKind.Sequential)] struct Keyboard{public ushort Key,Scan;public uint Flags,Time;public IntPtr Extra;}
 [StructLayout(LayoutKind.Explicit,Size=40)] struct Input{[FieldOffset(0)]public uint Type;[FieldOffset(8)]public Keyboard Keyboard;}
 [DllImport("user32.dll")] static extern uint SendInput(uint count,Input[] data,int size);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from,uint to,bool attach);
 [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
 [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
 [DllImport("user32.dll")] static extern bool EnumWindows(Proc callback,IntPtr parameter);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
 delegate bool Proc(IntPtr window,IntPtr parameter);
 public static bool AltVAvailable(){if(!RegisterHotKey(IntPtr.Zero,0x7143,1,86))return false;UnregisterHotKey(IntPtr.Zero,0x7143);return true;}
 public static long[] Windows(uint id){var list=new List<long>();EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==id&&IsWindowVisible(h))list.Add(h.ToInt64());return true;},IntPtr.Zero);return list.ToArray();}
 public static void Focus(IntPtr target){
  uint owner;uint thread=GetWindowThreadProcessId(GetForegroundWindow(),out owner);uint current=GetCurrentThreadId();
  bool attached=thread!=0&&thread!=current&&AttachThreadInput(current,thread,true);
  try{ShowWindow(target,IsIconic(target)?9:5);SetForegroundWindow(target);}finally{if(attached)AttachThreadInput(current,thread,false);}
 }
 public static void Press(IntPtr expected){
  if(GetForegroundWindow()!=expected){uint owner;var actual=GetForegroundWindow();GetWindowThreadProcessId(actual,out owner);throw new Exception("Foreground changed; no input sent. expected="+expected+" actual="+actual+" pid="+owner);}
  ushort[] keys={18,86,86,18};var data=new Input[4];
  for(int i=0;i<4;i++)data[i]=new Input{Type=1,Keyboard=new Keyboard{Key=keys[i],Flags=i<2?0u:2u}};
  if(SendInput(4,data,40)!=4)throw new Exception("Keyboard input failed");
 }
}
'@
$manager=Get-Process -Id $ManagerPid
$snow=Get-Process -Id $SnowPid
if($manager.ProcessName -ne 'FilesMate.App' -or $snow.ProcessName -ne 'snow_shot'){throw 'Unexpected target processes'}
if([SnowNativeInput]::AltVAvailable()){throw 'Alt+V is not registered; no keyboard input sent'}
$before=@([SnowNativeInput]::Windows($SnowPid))
[SnowNativeInput]::Focus($manager.MainWindowHandle)
Start-Sleep -Milliseconds 150
[SnowNativeInput]::Press($manager.MainWindowHandle)
$deadline=[DateTime]::UtcNow.AddSeconds(5)
do {
    Start-Sleep -Milliseconds 100
    $after=@([SnowNativeInput]::Windows($SnowPid))
    $created=@($after|Where-Object {$_ -notin $before})
} while($created.Count -eq 0 -and [DateTime]::UtcNow -lt $deadline)
@{Passed=$created.Count -gt 0;ManagerPid=$ManagerPid;SnowPid=$SnowPid;Before=$before;After=$after;NewWindows=$created}|
    ConvertTo-Json -Depth 4|Tee-Object -FilePath $Output
if($created.Count -eq 0){throw 'No visible SnowShot pin window appeared'}
