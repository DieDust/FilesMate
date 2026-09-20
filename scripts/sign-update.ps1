#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$DisplayVersion,
    [string]$Notes = '',
    [string]$LocalizedNotesPath,
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\update-feed\latest.json'),
    [string]$KeyPath = (Join-Path $env:LOCALAPPDATA 'FilesMatePublisher\update-signing.key')
)
$ErrorActionPreference = 'Stop'
if ([version]$Version -ge [version]'1.1.70.0' -and -not $LocalizedNotesPath) {
    throw 'Releases from 1.1.70 require -LocalizedNotesPath with zh-CN, en-US and ja-JP notes.'
}
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
$file = Get-Item -LiteralPath $Installer
if ($file.Name -notmatch '^FilesMate-Setup-[A-Za-z0-9.-]+-win-x64\.exe$' -or $file.Length -gt 512MB) { throw 'Invalid installer filename or size.' }
$actualVersion = '{0}.{1}.{2}.{3}' -f $file.VersionInfo.FileMajorPart, $file.VersionInfo.FileMinorPart, $file.VersionInfo.FileBuildPart, $file.VersionInfo.FilePrivatePart
if ($actualVersion -ne $Version) { throw 'Installer version differs from manifest version.' }
$payload = [ordered]@{ schema=1; product='FilesMate'; version=$Version; displayVersion=$DisplayVersion; fileName=$file.Name;
    size=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash; notes=$Notes; architecture='win-x64'; minimumWindowsBuild=22621 }
if ($LocalizedNotesPath) {
    $translations = Get-Content -LiteralPath $LocalizedNotesPath -Raw | ConvertFrom-Json -AsHashtable
    if ($translations -isnot [System.Collections.IDictionary] -or $translations.Count -gt 16) { throw 'Invalid localized notes map.' }
    foreach ($language in $translations.Keys) {
        if ($language -notmatch '^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$' -or $translations[$language] -isnot [string] -or
            [string]::IsNullOrWhiteSpace($translations[$language]) -or $translations[$language].Length -gt 8000) { throw "Invalid release notes: $language" }
    }
    foreach ($language in @('zh-CN', 'en-US', 'ja-JP')) {
        if (-not $translations.Contains($language)) { throw "Missing release notes: $language" }
    }
    $payload.localizedNotes = $translations
    if (-not $payload.notes) { $payload.notes = $translations['en-US'] }
}
if ($payload.notes.Length -gt 8000) { throw 'Release notes exceed 8000 characters.' }
$bytes = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Compress))
$rsa = [Security.Cryptography.RSA]::Create()
$private = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($KeyPath), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
try {
    $read = 0
    $rsa.ImportPkcs8PrivateKey($private, [ref]$read)
    $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
    $envelope = @{ payload=[Convert]::ToBase64String($bytes); signature=[Convert]::ToBase64String($signature) } | ConvertTo-Json
    if ([Text.Encoding]::UTF8.GetByteCount($envelope) -gt 64KB) { throw 'Signed manifest exceeds 64 KB.' }
    $outputPath = [IO.Path]::GetFullPath($Output)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
    [IO.File]::WriteAllText($outputPath, $envelope, [Text.UTF8Encoding]::new($false))
    Write-Output "Signed feed: $outputPath"
} finally { [Array]::Clear($private); $rsa.Dispose() }
