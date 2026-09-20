#Requires -Version 7
param([Parameter(Mandatory)][string]$TestExe, [int]$Width=1800)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class TitleDragProbe {
    public delegate bool EnumProc(IntPtr h,IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h,EnumProc cb,IntPtr p);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h,StringBuilder s,int n);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr h,IntPtr r);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int ht,uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h,int command);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr r,int x,int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr r);
    public static bool IsCaption(IntPtr window,int screenX,int screenY) {
        IntPtr sink=IntPtr.Zero;
        EnumProc cb=(h,p)=>{var name=new StringBuilder(128);GetClassNameW(h,name,128);
            if(name.ToString()=="InputNonClientPointerSource"){sink=h;return false;}return true;};
        EnumChildWindows(window,cb,IntPtr.Zero);
        if(sink==IntPtr.Zero)throw new Exception("Title-bar input sink not found.");
        GetWindowRect(sink,out var bounds);
        var region=CreateRectRgn(0,0,0,0);
        try {if(GetWindowRgn(sink,region)==0)throw new Exception("No title-bar input region.");
            return PtInRegion(region,screenX-bounds.Left,screenY-bounds.Top);}
        finally {DeleteObject(region);}
    }
}
'@
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repo ('artifacts\titlebar-drag-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$exe = [IO.Path]::GetFullPath($TestExe)
if (-not $exe.StartsWith((Join-Path $repo 'artifacts'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Pass an isolated FilesMateUITest build under artifacts.'
}
$profile = Join-Path (Split-Path -Parent $exe) 'test-profile'
New-Item -ItemType Directory -Path $fixture,$profile -Force | Out-Null
$tabs = @(foreach($i in 1..12){$path=Join-Path $fixture ("窗口拖动测试 文件夹 $i");New-Item -ItemType Directory -Path $path | Out-Null;$path})
@{Tabs=$tabs;SelectedTabIndex=0}|ConvertTo-Json|Set-Content (Join-Path $profile 'window-session.json')
@{restoreLastSession=$true;showFolderSizes=$false}|ConvertTo-Json|Set-Content (Join-Path $profile 'explorer.json')
@{x=60;y=60;width=$Width;height=1000;maximized=$false}|ConvertTo-Json|Set-Content (Join-Path $profile 'window.json')
$process = Start-Process -FilePath $exe -WindowStyle Normal -PassThru
$dpi = [TitleDragProbe]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$checks = [Collections.Generic.List[object]]::new()
$script:windowHandle = [IntPtr]::Zero
function Find([string]$id) {
    $process.Refresh()
    if ($script:windowHandle -eq 0) { $script:windowHandle = $process.MainWindowHandle }
    if ($script:windowHandle -eq 0) { return $null }
    $root=[Windows.Automation.AutomationElement]::FromHandle($script:windowHandle)
    $root.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
function Check([string]$scenario) {
    $ready=[DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 200
        $add=(Find 'NewTabButton').Current.BoundingRectangle
        $min=(Find 'Minimize').Current.BoundingRectangle
        if($add.Width -gt 0 -and $min.Width -gt 0 -and $min.Left-$add.Right -ge 40){break}
    } while([DateTime]::UtcNow -lt $ready)
    $y=[int]($add.Top+$add.Height/2)
    $x=[int](($add.Right+$min.Left)/2)
    if($min.Left-$add.Right -lt 40){throw "No usable caption gap: $scenario; add=$add; minimize=$min"}
    if(-not [TitleDragProbe]::IsCaption($script:windowHandle,$x,$y)){throw "Blank title bar cannot drag: $scenario ($x,$y)"}
    if([TitleDragProbe]::IsCaption($script:windowHandle,[int]($add.Left+$add.Width/2),$y)){throw "New-tab button intercepted by caption: $scenario"}
    $root=[Windows.Automation.AutomationElement]::FromHandle($script:windowHandle)
    $items=$root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::TabItem))
    if($items.Count -ne 12){throw "Expected 12 overflow tabs, got $($items.Count)."}
    $visible=@($items | Where-Object {-not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Width -gt 100})[0].Current.BoundingRectangle
    if([TitleDragProbe]::IsCaption($script:windowHandle,[int]($visible.Left+$visible.Width/2),$y)){throw "Tab intercepted by caption: $scenario"}
    $checks.Add(@{Scenario=$scenario;CaptionPoint=@($x,$y);TabCount=$items.Count;Caption=$true;ControlsInteractive=$true})
}
try {
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    while($null -eq (Find 'NewTabButton')){if([DateTime]::UtcNow -gt $deadline){throw 'Test window did not load.'};Start-Sleep -Milliseconds 100}
    Start-Sleep -Seconds 2
    foreach($width in @($Width)) {
        [void][TitleDragProbe]::SetWindowPos($script:windowHandle,0,60,60,$width,1000,0x0014)
        Check "overflow-width-$width"
        $root=[Windows.Automation.AutomationElement]::FromHandle($script:windowHandle)
        $items=$root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::TabItem))
        $items[$items.Count-1].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Check "scroll-to-last-$width"
        $items[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Check "scroll-to-first-$width"
    }
    @{Passed=$true;Checks=$checks.ToArray()}|ConvertTo-Json -Depth 6|Tee-Object -FilePath (Join-Path $fixture 'result.json')
} finally {
    [void][TitleDragProbe]::SetThreadDpiAwarenessContext($dpi)
    if(-not $process.HasExited){[void]$process.CloseMainWindow();if(-not $process.WaitForExit(10000)){Write-Warning 'Test window did not close.'}}
}
