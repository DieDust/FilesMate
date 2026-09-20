#Requires -Version 7
param([string]$TestExe, [string]$FilePath, [string]$Output, [switch]$Baseline)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing.Common
Add-Type @'
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
public static class TextPreviewProbe {
 [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint msg,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
 public static Task Monitor(IntPtr hwnd,string output) => Task.Run(()=>{
   var stop=Stopwatch.StartNew(); var misses=0; long maximum=0;
   while(stop.ElapsedMilliseconds<12000) {
     var watch=Stopwatch.StartNew(); var ok=SendMessageTimeout(hwnd,0,IntPtr.Zero,IntPtr.Zero,2,300,out _);
     maximum=Math.Max(maximum,watch.ElapsedMilliseconds); if(ok==IntPtr.Zero)misses++;
     File.WriteAllText(output,$"{{\"MissedResponses\":{misses},\"MaxResponseMs\":{maximum}}}");
     Thread.Sleep(50);
   }
 });
}
'@
[IO.Directory]::CreateDirectory($Output) | Out-Null
$app = Start-Process -FilePath $TestExe -ArgumentList ('--select "'+$FilePath+'"') -PassThru
$app.Id | Set-Content (Join-Path $Output 'pid.txt')
$scope = [Windows.Automation.TreeScope]::Descendants
function Root { [Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle) }
function Find($id) { (Root).FindFirst($scope,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id)) }
function Invoke($id) { (Find $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
try {
    for($i=0;$i -lt 100;$i++) { $app.Refresh(); if($app.MainWindowHandle -ne 0){break}; Start-Sleep -Milliseconds 100 }
    Start-Sleep -Seconds 2
    $monitor = [TextPreviewProbe]::Monitor($app.MainWindowHandle,(Join-Path $Output 'latency.json'))
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Invoke 'PreviewButton'
    Start-Sleep -Seconds 2
    $text = Find 'TextContent'
    $value = $text.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($value.Length -lt 1000) { throw 'Expected a large text preview' }
    $bounds = $text.Current.BoundingRectangle
    $window = (Root).Current.BoundingRectangle
    if (!$Baseline -and ($bounds.Height -gt $window.Height -or $bounds.Height -lt 100)) { throw "Text viewport is unbounded: $bounds" }
    $scrolled = $false
    if (!$Baseline) {
        if($value.Length -gt 16384) { throw 'Native editor received an unbounded text page' }
        $firstPage = $value
        $firstLabel = (Find 'TextPageLabel').Current.Name
        for($i=0;$i -lt 3;$i++) { Invoke 'TextNext' }
        $laterPage = (Find 'TextContent').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
        if((Find 'TextPageLabel').Current.Name -eq $firstLabel) { throw 'Next page did not advance' }
        for($i=0;$i -lt 3;$i++) { Invoke 'TextPrevious' }
        $restored = (Find 'TextContent').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
        if($restored -ne $firstPage) { throw 'Previous page did not restore content' }
        $pattern = $text.GetCurrentPattern([Windows.Automation.TextPattern]::Pattern)
        $tail = $pattern.DocumentRange.Clone()
        $tail.MoveEndpointByRange([Windows.Automation.Text.TextPatternRangeEndpoint]::Start,$tail,[Windows.Automation.Text.TextPatternRangeEndpoint]::End)
        $tail.MoveEndpointByUnit([Windows.Automation.Text.TextPatternRangeEndpoint]::Start,[Windows.Automation.Text.TextUnit]::Character,-20) | Out-Null
        $tail.ScrollIntoView($false)
        Start-Sleep -Milliseconds 150
        $scrolled = $true # WinUI does not expose ScrollPattern or range geometry for this TextBox.
        $pattern.DocumentRange.ScrollIntoView($true)
    }
    for($i=0;$i -lt 3;$i++) { Invoke 'InfoTab'; Invoke 'ContentTab'; Start-Sleep -Milliseconds 150 }
    $watch.Stop()
    $monitor.GetAwaiter().GetResult()
    $latency = Get-Content (Join-Path $Output 'latency.json') -Raw | ConvertFrom-Json
    if (!$Baseline -and $latency.MissedResponses -gt 0) { throw "UI message timeout: $($latency|ConvertTo-Json -Compress)" }
    [pscustomobject]@{Passed=$true;Characters=$value.Length;Viewport=$bounds.ToString();ScrollRequested=$scrolled;CyclesMs=$watch.ElapsedMilliseconds;Latency=$latency} | ConvertTo-Json | Set-Content (Join-Path $Output 'result.json')
}
catch { $_ | Out-String | Set-Content (Join-Path $Output 'failure.txt'); throw }
finally { if(!$app.HasExited){$app.CloseMainWindow()|Out-Null} }
