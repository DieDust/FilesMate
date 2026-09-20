#Requires -Version 7
param(
    [string]$Profile = "$env:LOCALAPPDATA\FilesMate",
    [string]$Command = 'status',
    [string]$Hotkey,
    [Nullable[bool]]$Enabled,
    [string]$ManagerPath,
    [ValidateSet('Search','Files')][string]$TrayLeftAction = 'Search',
    [Nullable[bool]]$StartAtLogin
)
$ErrorActionPreference = 'Stop'
$identity = [IO.Path]::GetFullPath($Profile).ToUpperInvariant()
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity)))
$name = 'FilesMate.Search.' + $hash.Substring(0,24)
$request = @{Command=$Command}
if($Hotkey){$request.Settings=@{Enabled=($Enabled -ne $false);Hotkey=$Hotkey;TrayLeftAction=$TrayLeftAction}}
if($Hotkey){
    $startupEnabled=$true
    if($null -ne $StartAtLogin){$startupEnabled=[bool]$StartAtLogin}
    elseif(Test-Path -LiteralPath (Join-Path $Profile 'global-search.json')){
        $savedSettings=Get-Content -LiteralPath (Join-Path $Profile 'global-search.json') -Raw | ConvertFrom-Json
        if($savedSettings.PSObject.Properties['StartAtLogin']){$startupEnabled=[bool]$savedSettings.StartAtLogin}
    }
    $request.Settings.StartAtLogin=$startupEnabled
    $request.Settings.PreviewEnabled=$true
    if(Test-Path -LiteralPath (Join-Path $Profile 'global-search.json')){
        $savedPreview=Get-Content -LiteralPath (Join-Path $Profile 'global-search.json') -Raw | ConvertFrom-Json
        if($savedPreview.PSObject.Properties['PreviewEnabled']){$request.Settings.PreviewEnabled=[bool]$savedPreview.PreviewEnabled}
    }
}
if($ManagerPath){$request.ManagerPath=$ManagerPath}
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $name, [IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(3000)
    $writer = [IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),1024,$true)
    $reader = [IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8,$false,1024,$true)
    try {
        $writer.AutoFlush=$true
        $writer.WriteLine(($request|ConvertTo-Json -Compress))
        $reader.ReadLine()
    } finally { $writer.Dispose();$reader.Dispose() }
} finally {$pipe.Dispose()}
