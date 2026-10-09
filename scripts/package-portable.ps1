#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.]+)?$')][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = [IO.Path]::GetFullPath($Payload)
foreach ($name in @('FilesMate.App.exe','FilesMate.App.dll','coreclr.dll','Microsoft.UI.Xaml.dll',
        'SearchHost\FilesMate.SearchHost.exe','SearchHost\coreclr.dll','SearchHost\PresentationFramework.dll',
        'LICENSE','NOTICE','THIRD-PARTY-NOTICES.md','shell-compatibility.enabled')) {
    if (!(Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) { throw "Missing portable dependency: $name" }
}
foreach ($name in @('data','test-profile','.filesmate-portable')) {
    if (Test-Path -LiteralPath (Join-Path $source $name)) { throw "The installer payload contains private/portable data: $name" }
}
$stage = Join-Path (Split-Path -Parent $source) 'portable\FilesMate'
if (Test-Path -LiteralPath $stage) { throw 'Portable staging must be a new directory.' }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $stage -Recurse -Force
[IO.File]::WriteAllText((Join-Path $stage '.filesmate-portable'), 'FilesMate portable v1', [Text.UTF8Encoding]::new($false))
$instructions = @'
FilesMate portable
=================
Windows 11 x64, build 22621 or newer.
Extract the entire ZIP to a writable folder and run FilesMate.App.exe.
No MSIX installation, administrator initialization or file-association registration is required.
.NET, WinUI and the matching SearchHost runtime are included.
PDF/HTML document previews need the Microsoft Edge WebView2 Runtime.

Settings, the search index, cache and logs are kept in the adjacent data folder.
Keep .filesmate-portable beside FilesMate.App.exe. Do not rename or remove it.
Copy the whole folder, including data, to carry settings to another directory.
Saved references to external files/folders remain absolute paths.
An unwritable or unrelated existing data folder is an error; the installed profile is never used as fallback.
Junctions/symbolic links in the portable data path are refused; use a regular writable folder.

Portable and installed copies use different settings and process identities.
A keyboard shortcut already owned by another application is reported as a conflict.
Portable mode does not register login startup or a default-folder association.
Its matching SearchHost is stopped when the last FilesMate window closes.

Updating: download the new portable ZIP from https://github.com/DieDust/FilesMate/releases.
Close all FilesMate windows before replacing program files. Keep the existing data folder.
Extract the new ZIP into a fresh folder, copy the old data into it, then run the new copy.
Do not use the installer to update a portable copy.
'@
[IO.File]::WriteAllText((Join-Path $stage 'PORTABLE-README.txt'), $instructions, [Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$zip = Join-Path $OutputDirectory "FilesMate-Portable-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { throw 'Refusing to overwrite an existing portable ZIP.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$zip.sha256", "$hash  $([IO.Path]::GetFileName($zip))" + [Environment]::NewLine, [Text.Encoding]::ASCII)
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ Path=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/'); Bytes=$_.Length;
        SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
[IO.File]::WriteAllText("$zip.files.json", ($files | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Zip=$zip; Bytes=(Get-Item -LiteralPath $zip).Length; SHA256=$hash; Payload=$stage; Files=$files.Count } |
    ConvertTo-Json | Tee-Object -FilePath (Join-Path (Split-Path -Parent $source) 'portable-package.json')
