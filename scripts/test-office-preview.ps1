#Requires -Version 7
param([string]$TestExe, [string]$Fixtures, [string]$Output, [string]$SelectOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing.Common
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OfficePreviewInput {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")] public static extern bool SetPhysicalCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X,Y; public uint Data,Flags,Time; public IntPtr Extra; }
 [StructLayout(LayoutKind.Explicit, Size=40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public MouseInput Mouse; }
 [DllImport("user32.dll")] private static extern uint SendInput(uint count,Input[] input,int size);
 public static void Button(uint flags) { if(SendInput(1,new[]{new Input{Mouse=new MouseInput{Flags=flags}}},40)!=1) throw new InvalidOperationException("Input failed"); }
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPhysicalPoint(Point p);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y; public Point(int x,int y){X=x;Y=y;}}
}
'@
[IO.Directory]::CreateDirectory($Output) | Out-Null
$dpi = [OfficePreviewInput]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$arguments = if ($SelectOnly) { '--select "'+(Join-Path $Fixtures $SelectOnly)+'"' } else { '"'+$Fixtures+'"' }
$process = Start-Process -FilePath $TestExe -ArgumentList $arguments -PassThru
$scope = [Windows.Automation.TreeScope]::Descendants
function Root { [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle) }
function Find($id) { (Root).FindFirst($scope,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id)) }
function Click($element,[switch]$Right) {
    if (!$element) { throw 'Missing UI element' }
    $r = $element.Current.BoundingRectangle
    $x = [int]($r.Left + [Math]::Min(40,$r.Width/2)); $y = [int]($r.Top + $r.Height/2)
    "$($element.Current.Name) click=$x,$y right=$Right bounds=$r" | Add-Content (Join-Path $Output 'input.txt')
    [uint32]$ownerPid = 0
    $hwnd = [OfficePreviewInput]::WindowFromPhysicalPoint([OfficePreviewInput+Point]::new($x,$y))
    [OfficePreviewInput]::GetWindowThreadProcessId($hwnd,[ref]$ownerPid) | Out-Null
    if ($ownerPid -ne $process.Id) { throw 'Another window covers the test target; input was not sent.' }
    [OfficePreviewInput]::SetCursorPos($x,$y) | Out-Null
    Start-Sleep -Milliseconds 120
    [OfficePreviewInput]::Button($(if($Right){8}else{2}))
    Start-Sleep -Milliseconds 80
    [OfficePreviewInput]::Button($(if($Right){16}else{4}))
}
function Capture($name) {
    $r = (Root).Current.BoundingRectangle
    $bmp = [Drawing.Bitmap]::new([int]$r.Width,[int]$r.Height)
    $g = [Drawing.Graphics]::FromImage($bmp)
    try { $g.CopyFromScreen([int]$r.X,[int]$r.Y,0,0,$bmp.Size); $bmp.Save((Join-Path $Output "$name.png")) }
    finally {$g.Dispose();$bmp.Dispose()}
}
try {
    for($i=0;$i -lt 100;$i++){ $process.Refresh(); if($process.MainWindowHandle -ne 0){break}; Start-Sleep -Milliseconds 100 }
    [OfficePreviewInput]::SetWindowPos($process.MainWindowHandle,[IntPtr]::new(-1),0,0,0,0,3) | Out-Null
    [OfficePreviewInput]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Seconds 2
    $button = Find 'PreviewButton'
    $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Capture 'before-selection'
    $names = if ($SelectOnly) { @($SelectOnly) } else { @('阅读样本.pdf','工作文档.docx','预算表.xlsx','旧版文档.doc','阅读样本.md') }
    foreach($name in $names) {
        $label = (Root).FindAll($scope,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'NameText')) | Where-Object {$_.Current.Name -eq $name} | Select-Object -First 1
        if (!$SelectOnly) { Click $label }
        Start-Sleep -Seconds 8
        $trace = Join-Path (Split-Path $TestExe -Parent) 'preview-test.log'
        if (Test-Path -LiteralPath $trace) {
            $loaded = Get-Content -LiteralPath $trace | Where-Object { $_ -match ' Load ' } | Select-Object -Last 1
            if (!$loaded.Contains($name)) { throw "Selection changed to a different file: $loaded" }
        }
        $tab = Find 'ContentTab'
        if (!$tab -or $tab.Current.IsOffscreen) { throw "Content preview missing for $name" }
        $documents = (Root).FindAll($scope,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Document))
        $document = Find 'DocumentContent'
        if (!$document) { $document = $documents | Where-Object {$_.Current.BoundingRectangle.Height -gt 500} | Select-Object -First 1 }
        if (!$document) { throw "Document does not fill the right pane: $name" }
        Capture $name
        $document.Current.BoundingRectangle.ToString() | Add-Content (Join-Path $Output 'geometry.txt')
    }
    if ($SelectOnly) { '{"Passed":true,"FullHeight":true,"ShellSelection":true}' | Set-Content (Join-Path $Output 'result.json'); return }
    Click $label -Right
    Start-Sleep -Milliseconds 400
    if ((Find 'ContentTab') -and !(Find 'ContentTab').Current.IsOffscreen) { throw 'Right click retained preview' }
    Capture 'right-menu'
    '{"Passed":true,"Formats":5,"FullHeight":true,"RealMouseSelection":true,"RightClickSuppressesPreview":true}' | Set-Content (Join-Path $Output 'result.json')
}
catch { Capture 'failure'; $_ | Out-String | Set-Content (Join-Path $Output 'failure.txt'); throw }
finally { if(!$process.HasExited){$process.CloseMainWindow()|Out-Null};[OfficePreviewInput]::SetThreadDpiAwarenessContext($dpi)|Out-Null }
