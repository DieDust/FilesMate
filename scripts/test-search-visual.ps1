#Requires -Version 7
param([Parameter(Mandatory)][string]$TestExe, [int]$Width=1750, [ValidateRange(1,80)][int]$ResultCount=1)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing.Common
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class SearchVisual {
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int ht,uint flags);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
}
'@
$repo=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $repo ('artifacts/search-visual-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
for($i=0;$i -lt $ResultCount;$i++) {
 [IO.File]::WriteAllText((Join-Path $fixture ('search-demo-{0:D2}-中文文件.txt' -f $i)),'visual search fixture')
}
$process=Start-Process -FilePath ([IO.Path]::GetFullPath($TestExe)) -ArgumentList ('/open "'+$fixture+'"') -PassThru
$oldDpi=[SearchVisual]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$script:hwnd=[IntPtr]::Zero
function Find([string]$id) {
 $process.Refresh()
 if($script:hwnd -eq [IntPtr]::Zero){$script:hwnd=$process.MainWindowHandle}
 if($script:hwnd -eq [IntPtr]::Zero){return $null}
 $root=[Windows.Automation.AutomationElement]::FromHandle($script:hwnd)
 $root.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
function WaitFor([scriptblock]$condition) {
 $deadline=[DateTime]::UtcNow.AddSeconds(15)
 do {if(&$condition){return};Start-Sleep -Milliseconds 100} while([DateTime]::UtcNow -lt $deadline)
 throw 'Search visual state did not become ready.'
}
$checks=[Collections.Generic.List[object]]::new()
try {
 WaitFor { $null -ne (Find 'SearchButton') }
 Start-Sleep -Seconds 2
 foreach($width in @($Width)) {
  [void][SearchVisual]::SetWindowPos($script:hwnd,[IntPtr]::Zero,50,50,$width,950,0x0044)
  Start-Sleep -Milliseconds 500
  foreach($query in @('', 'search-demo')) {
   (Find 'SearchButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
   Start-Sleep -Milliseconds 100
   (Find 'SearchBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($query)
   WaitFor { $status=Find 'SearchStatus'; $status -and -not $status.Current.IsOffscreen -and ($query -eq '' -or $status.Current.Name -match "\b$ResultCount\b") }
   Start-Sleep -Milliseconds 300
   $window=[Windows.Automation.AutomationElement]::FromHandle($script:hwnd)
   $bounds=$window.Current.BoundingRectangle
   foreach($id in @('SearchEverywhere','SearchStatus')) {
    $element=Find $id
    $rect=$element.Current.BoundingRectangle
    if($rect.Width -le 0 -or $rect.Left -lt $bounds.Left -or $rect.Right -gt $bounds.Right -or $rect.Bottom -gt $bounds.Bottom){throw "$id extends outside the window: $rect versus $bounds"}
    $checks.Add([pscustomobject]@{WindowWidth=$width;Query=$query;Element=$id;Bounds=$rect.ToString()})
   }
   if($query -and $ResultCount -gt 4) {
    $list=Find 'SearchHits'
    $scroll=$list.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    if(-not $scroll.Current.VerticallyScrollable){throw 'Many results must scroll inside the compact panel.'}
    if($list.Current.BoundingRectangle.Height -gt 340){throw 'Results panel exceeds four-row height at 175% scaling.'}
    $scroll.SetScrollPercent(-1,100)
    Start-Sleep -Milliseconds 100
    if($scroll.Current.VerticalScrollPercent -lt 99){throw 'Could not scroll to the remaining results.'}
    $scroll.SetScrollPercent(-1,0)
    WaitFor { $scroll.Current.VerticalScrollPercent -le 1 }
   }
   $bitmap=[Drawing.Bitmap]::new([int]$bounds.Width,[int]$bounds.Height)
   $graphics=[Drawing.Graphics]::FromImage($bitmap)
   $dc=$graphics.GetHdc()
   try{[void][SearchVisual]::PrintWindow($script:hwnd,$dc,2)}finally{$graphics.ReleaseHdc($dc);$graphics.Dispose()}
   $name=if($query){'results'}else{'empty'}
   $bitmap.Save((Join-Path $fixture "$width-$name.png"))
   $bitmap.Dispose()
  }
 }
 [pscustomobject]@{Success=$true;Fixture=$fixture;Checks=$checks.ToArray()} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $fixture 'result.json')
 Write-Host "PASS: search scope and status fit the $Width pixel window; screenshots saved to $fixture"
} catch {
 Write-Host "Visual check failed: $_"
 throw
} finally {
 try {if(-not $process.HasExited){[void]$process.CloseMainWindow();if(-not $process.WaitForExit(30000)){Write-Warning 'Visual test window did not close.'}}}
 finally {[void][SearchVisual]::SetThreadDpiAwarenessContext($oldDpi)}
}
