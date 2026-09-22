#Requires -Version 7
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'src/FilesMate.App/Localization'
$base = Get-Content -LiteralPath (Join-Path $source 'StringTable.cs') -Raw
$japanese = Get-Content -LiteralPath (Join-Path $source 'StringTable.Japanese.cs') -Raw
$additional = (Get-ChildItem -LiteralPath $source -Filter 'StringTable.*.cs' -File |
    Where-Object Name -ne 'StringTable.Japanese.cs' | Sort-Object Name |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
$literal = '"(?:[^"\\]|\\.)*"'
$pairPattern = '\[(?<key>' + $literal + ')\]\s*=\s*(?<value>' + $literal + ')'
$tuplePattern = '\[(?<key>' + $literal + ')\]\s*=\s*\((?<en>' + $literal + '),\s*(?<zh>' + $literal + '),\s*(?<ja>' + $literal + ')\)'
$split = $base.IndexOf('IReadOnlyDictionary<string, string> Chinese')
if ($split -lt 0) { throw 'Cannot find the Chinese resource table.' }
$sources = @{ 'en-US' = $base.Substring(0, $split); 'zh-Hans' = $base.Substring($split); 'ja-JP' = $japanese }
$tupleLanguages = @{ 'en-US' = 'en'; 'zh-Hans' = 'zh'; 'ja-JP' = 'ja' }
foreach ($language in @('en-US', 'zh-Hans', 'ja-JP')) {
    $values = [System.Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($sources[$language], $pairPattern)) {
        $values.Add(($match.Groups['key'].Value | ConvertFrom-Json), ($match.Groups['value'].Value | ConvertFrom-Json))
    }
    foreach ($match in [regex]::Matches($additional, $tuplePattern)) {
        $values.Add(($match.Groups['key'].Value | ConvertFrom-Json), ($match.Groups[$tupleLanguages[$language]].Value | ConvertFrom-Json))
    }
    $directory = Join-Path $root "src/FilesMate.App/Strings/$language"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $writer = [System.Xml.XmlWriter]::Create((Join-Path $directory 'Resources.resw'), $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteComment(' Generated from Localization/StringTable*.cs. Run scripts/sync-localization-resources.ps1; do not edit here. ')
        $writer.WriteStartElement('root')
        foreach ($entry in $values.GetEnumerator()) {
            $writer.WriteStartElement('data')
            $writer.WriteAttributeString('name', $entry.Key)
            $writer.WriteAttributeString('xml', 'space', 'http://www.w3.org/XML/1998/namespace', 'preserve')
            $writer.WriteElementString('value', $entry.Value)
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    }
    finally { $writer.Dispose() }
    Write-Host "$language : $($values.Count) resources"
}
