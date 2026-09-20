#Requires -Version 7
param([string]$TestExe,[string]$FilePath,[string]$Output,[switch]$Video)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
[IO.Directory]::CreateDirectory($Output)|Out-Null
$trace=Join-Path (Split-Path $TestExe) 'preview-test.log'
[IO.File]::WriteAllText($trace,'')
$app=Start-Process $TestExe -ArgumentList ('--select "'+$FilePath+'"') -PassThru
$scope=[Windows.Automation.TreeScope]::Descendants
function Find($id){
    $root=[Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
    $root.FindFirst($scope,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
function Invoke($id){(Find $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()}
function WaitTrace($pattern){
    for($i=0;$i -lt 100;$i++) {
        if([IO.File]::ReadAllText($trace) -match $pattern){return}
        Start-Sleep -Milliseconds 100
    }
    throw "Preview timed out: $pattern"
}
try {
    for($i=0;$i -lt 100;$i++){$app.Refresh();if($app.MainWindowHandle -ne 0){break};Start-Sleep -Milliseconds 100}
    Start-Sleep -Seconds 2
    Invoke 'PreviewButton'
    if($Video){
        WaitTrace 'Result Media'
        if((Find 'MediaPlayButton').Current.IsOffscreen){throw 'Missing play button'}
        if((Find 'ImageContent').Current.IsOffscreen){throw 'Missing static video poster'}
        if([IO.File]::ReadAllText($trace) -match 'player=created'){throw 'Player started before clicking play'}
        $app.Refresh();$idleCpu=$app.TotalProcessorTime.TotalMilliseconds
        Start-Sleep -Seconds 2
        $app.Refresh();$idleCpu=$app.TotalProcessorTime.TotalMilliseconds-$idleCpu
        Invoke 'MediaPlayButton'
        WaitTrace 'Media opened'
        Invoke 'PreviewButton'
        Start-Sleep -Milliseconds 300
        $events=[IO.File]::ReadAllText($trace)
        if($events.LastIndexOf('Media released') -lt $events.LastIndexOf('Media opened')){throw 'Player was not released when preview closed'}
    }else{
        WaitTrace 'Image decode'
        $events=[IO.File]::ReadAllText($trace)
        if($events -notmatch 'Image decode (\d+)x(\d+)'){throw 'No decode dimensions'}
        $width=[int]$Matches[1];$height=[int]$Matches[2]
        if($width -gt 2048 -or $height -gt 2048 -or $width -lt 100){throw 'Unbounded or missing image decode'}
        if((Find 'ImageContent').Current.IsOffscreen){throw 'Image not visible'}
        Invoke 'InfoTab'; Invoke 'ContentTab'
        Invoke 'PreviewButton'
    }
    $app.Refresh()
    [pscustomobject]@{Passed=$true;Video=$Video.IsPresent;ImageWidth=$width;ImageHeight=$height;PosterIdleCpuMs=$idleCpu;Responding=$app.Responding;WorkingSet=$app.WorkingSet64}|ConvertTo-Json|Set-Content (Join-Path $Output 'result.json')
    Copy-Item -LiteralPath $trace -Destination (Join-Path $Output 'preview-test.log')
}catch{$_|Out-String|Set-Content (Join-Path $Output 'failure.txt');throw}
finally{if(!$app.HasExited){$app.CloseMainWindow()|Out-Null;$app.WaitForExit(5000)|Out-Null}}
