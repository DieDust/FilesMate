#Requires -Version 7
[CmdletBinding()]
param(
    # Defaults to the VersionPrefix/VersionSuffix declared in Directory.Build.props, so a
    # release is bumped in that one file and the installer follows.
    [ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.]+)?$')]
    [string]$Version,
    [switch]$IncludePortable,
    [switch]$AllowLegacyImageSharpRisk,
    [string]$RestoreConfigFile,
    [string]$StagingDirectory,
    [string]$InnoCompiler = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    [xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
    $prefix = $buildProps.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
    $suffix = $buildProps.SelectSingleNode('/Project/PropertyGroup/VersionSuffix')
    if (-not $prefix) { throw 'Directory.Build.props does not declare VersionPrefix.' }
    $Version = $prefix.InnerText.Trim()
    if ($suffix -and $suffix.InnerText.Trim()) { $Version += '-' + $suffix.InnerText.Trim() }
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.]+)?$') {
        throw "Directory.Build.props declares an unusable version: $Version"
    }
}
Write-Host "Version: $Version"
if (-not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Inno Setup 6 compiler was not found. Pass -InnoCompiler with the full ISCC.exe path.'
}

# Each invocation starts with a fresh payload, including when a development app is running.
$stageRoot = Join-Path $repoRoot ('artifacts\package-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if ($StagingDirectory) {
    $stageRoot = [IO.Path]::GetFullPath($StagingDirectory)
    if (Test-Path -LiteralPath $stageRoot) { throw 'StagingDirectory must be fresh.' }
}
$payload = Join-Path $stageRoot 'payload'
$buildOutput = Join-Path $stageRoot 'build'
$releaseDir = Join-Path $repoRoot 'artifacts\releases'
New-Item -ItemType Directory -Path $payload, $releaseDir -Force | Out-Null
$project = Join-Path $repoRoot 'src\FilesMate.App\FilesMate.App.csproj'
$fileVersion = ($Version -split '-', 2)[0] + '.0'
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $payload `
    '-p:Platform=x64' '-p:FilesMateUITest=false' "-p:OutputPath=$buildOutput\" `
    "-p:Version=$Version" '-p:AssemblyVersion=1.0.0.0' "-p:FileVersion=$fileVersion" `
    '-p:DebugType=None' '-p:DebugSymbols=false' "-p:RestoreConfigFile=$RestoreConfigFile" "-p:FilesMateAllowLegacyImageSharpRisk=$($AllowLegacyImageSharpRisk.IsPresent.ToString().ToLowerInvariant())"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

# Audit remains enabled. This opt-in accepts only the exact legacy dependency
# and advisories approved for this preview; any additional finding blocks packaging.
$knownRisk = $null
if ($AllowLegacyImageSharpRisk) {
    $acceptedAdvisories = @(
        'https://github.com/advisories/GHSA-gwg2-r3hj-4w44',
        'https://github.com/advisories/GHSA-j3p4-wp97-rph4',
        'https://github.com/advisories/GHSA-j9gm-c75j-xc9q',
        'https://github.com/advisories/GHSA-jjfr-hcj7-qf5w',
        'https://github.com/advisories/GHSA-wmxv-xphr-5c9g'
    )
    $auditReports = @()
    foreach ($auditProject in @($project, (Join-Path $repoRoot 'src\FilesMate.SearchHost\FilesMate.SearchHost.csproj'))) {
        $auditArgs = @('list', $auditProject, 'package', '--vulnerable', '--include-transitive', '--format', 'json', '--no-restore')
        if ($RestoreConfigFile) { $auditArgs += @('--configfile', $RestoreConfigFile) }
        $auditText = (& dotnet @auditArgs | Out-String)
        if ($LASTEXITCODE -ne 0) { throw 'Dependency audit failed; packaging stopped.' }
        $report = $auditText | ConvertFrom-Json
        if (-not $report.projects -or -not ($report.sources | Where-Object { $_ -eq 'https://api.nuget.org/v3/index.json' })) {
            throw 'Dependency audit is incomplete; packaging stopped.'
        }
        if ($report.PSObject.Properties['problems'] -and @($report.problems).Count -gt 0) {
            throw 'Dependency audit reported a problem; packaging stopped.'
        }
        foreach ($auditedProject in $report.projects) {
            # NuGet omits frameworks for a successful audit with no vulnerable packages.
            if (-not $auditedProject.PSObject.Properties['frameworks']) { continue }
            foreach ($framework in $auditedProject.frameworks) {
                foreach ($kind in @('topLevelPackages', 'transitivePackages')) {
                    if (-not $framework.PSObject.Properties[$kind]) { continue }
                    foreach ($package in $framework.$kind) {
                        if (-not $package.PSObject.Properties['vulnerabilities']) { continue }
                        foreach ($finding in $package.vulnerabilities) {
                            if ($package.id -ne 'SixLabors.ImageSharp' -or $package.resolvedVersion -ne '2.1.11' -or
                                $finding.advisoryurl -notin $acceptedAdvisories -or $finding.severity -notin @('Moderate', 'High')) {
                                throw "Unapproved dependency risk: $($package.id) $($package.resolvedVersion) $($finding.advisoryurl)"
                            }
                        }
                    }
                }
            }
        }
        $auditReports += $report
    }
    $knownRisk = [ordered]@{
        status = 'Known vulnerabilities retained; not fixed'
        dependency = 'NPOI 2.7.6 -> SixLabors.ImageSharp 2.1.11'
        decision = 'Release owner accepted these five findings for this preview; upgrade deferred'
        acceptedAdvisories = $acceptedAdvisories
        auditReports = $auditReports
    }
    $knownRisk | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $stageRoot 'dependency-audit.json') -Encoding utf8NoBOM
    Write-Warning 'ImageSharp 2.1.11 retains 3 High and 2 Moderate known vulnerabilities. The approved exception does not fix them.'
}


$required = @('FilesMate.App.exe', 'FilesMate.App.dll', 'coreclr.dll', 'hostfxr.dll',
    'hostpolicy.dll', 'SharpCompress.dll', 'Microsoft.WindowsAppRuntime.dll',
    'Microsoft.WindowsAppRuntime.Bootstrap.dll', 'Microsoft.ui.xaml.dll', 'e_sqlite3.dll',
    'SearchHost\FilesMate.SearchHost.exe', 'SearchHost\coreclr.dll', 'SearchHost\PresentationFramework.dll', 'SearchHost\SharpCompress.dll')
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $file))) {
        throw "Self-contained payload is missing $file"
    }
}
if (Test-Path -LiteralPath (Join-Path $payload 'test-profile')) {
    throw 'A UI test profile must never be shipped.'
}
# Ship the native Shell host validated in the approved compatibility build.
# The --no-native-shell argument remains available for troubleshooting.
Set-Content -LiteralPath (Join-Path $payload 'shell-compatibility.enabled') -Encoding ascii -Value 'FilesMate native Shell selection compatibility'
foreach ($notice in @('LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $notice) -Destination $payload
}
if (Test-Path -LiteralPath (Join-Path $repoRoot 'installer\ThirdPartyNotices')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot 'installer\ThirdPartyNotices') -Destination $payload -Recurse -Force
}

if ($knownRisk) {
    $publicRisk = [ordered]@{ status = $knownRisk.status; dependency = $knownRisk.dependency; decision = $knownRisk.decision; advisories = $knownRisk.acceptedAdvisories }
    $publicRisk | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'KNOWN-DEPENDENCY-RISKS.json') -Encoding utf8NoBOM
}

# Preserve dependency license texts and package metadata alongside the binaries.
$assets = Get-Content -LiteralPath (Join-Path $repoRoot 'src\FilesMate.App\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$packagePaths = @($assets.libraries.Values | Where-Object type -eq 'package' | ForEach-Object path)
$hostAssets = Get-Content -LiteralPath (Join-Path $repoRoot 'src\FilesMate.SearchHost\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$packagePaths += @($hostAssets.libraries.Values | Where-Object type -eq 'package' | ForEach-Object path)
$runtimeConfig = Get-Content -LiteralPath (Join-Path $payload 'FilesMate.App.runtimeconfig.json') -Raw | ConvertFrom-Json
$runtimeVersion = $runtimeConfig.runtimeOptions.includedFrameworks[0].version
$packagePaths += "microsoft.netcore.app.runtime.win-x64/$runtimeVersion"
$packagePaths += "microsoft.windowsdesktop.app.runtime.win-x64/$runtimeVersion"
foreach ($packagePath in ($packagePaths | Sort-Object -Unique)) {
    foreach ($cacheRoot in (@($assets.packageFolders.Keys) + @($hostAssets.packageFolders.Keys) | Sort-Object -Unique)) {
        $packageRoot = Join-Path $cacheRoot $packagePath
        if (-not (Test-Path -LiteralPath $packageRoot)) { continue }
        $notices = @(Get-ChildItem -LiteralPath $packageRoot -File | Where-Object {
            $_.Name -match '^(LICENSE|NOTICE|THIRD[-_]PARTY)' -or $_.Extension -eq '.nuspec'
        })
        # Some packages declare a file such as sdk_license.txt rather than LICENSE.
        $nuspec = Get-ChildItem -LiteralPath $packageRoot -Filter '*.nuspec' -File | Select-Object -First 1
        if ($nuspec) {
            [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName -Raw
            $declaredLicense = $metadata.SelectSingleNode("//*[local-name()='license' and @type='file']")
            if ($declaredLicense) {
                $licensePath = [IO.Path]::GetFullPath((Join-Path $packageRoot $declaredLicense.InnerText))
                $prefix = [IO.Path]::GetFullPath($packageRoot).TrimEnd('\') + '\'
                if (!$licensePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package license path escapes its package.' }
                if (!(Test-Path -LiteralPath $licensePath -PathType Leaf)) { throw "Missing package license: $packagePath" }
                $notices += Get-Item -LiteralPath $licensePath
            }
        }
        $notices = @($notices | Sort-Object FullName -Unique)
        if ($notices.Count -gt 0) {
            $noticeDir = Join-Path $payload ('ThirdPartyNotices\' + $packagePath.Replace('/', '\'))
            New-Item -ItemType Directory -Path $noticeDir -Force | Out-Null
            $notices | Copy-Item -Destination $noticeDir
        }
        break
    }
}

# Reference byte-identical dependencies through the same source path so Inno
# stores them once. Destination paths remain separate for reliable offline
# activation of both processes; do not delete runtime files or use shared DLLs.
$fileManifest = Join-Path $stageRoot 'payload-files.iss'
$sourcesByHash = @{}
$entries = [Collections.Generic.List[string]]::new()
$duplicateBytes = 0L
foreach ($file in (Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName)) {
    $relative = $file.FullName.Substring($payload.Length + 1)
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ($sourcesByHash.ContainsKey($hash)) {
        $source = $sourcesByHash[$hash]
        $duplicateBytes += $file.Length
    } else {
        $source = $file.FullName
        $sourcesByHash[$hash] = $source
    }
    $destination = '{app}'
    $parent = Split-Path -Parent $relative
    if ($parent) { $destination += '\' + $parent }
    $entries.Add(('Source: "{0}"; DestDir: "{1}"; DestName: "{2}"; Flags: ignoreversion' -f $source, $destination, $file.Name))
}
$entries | Set-Content -LiteralPath $fileManifest -Encoding utf8
Write-Host "Identical payload bytes stored once: $duplicateBytes"
& $InnoCompiler "/DPayloadDir=$payload" "/DPayloadFileManifest=$fileManifest" "/DReleaseDir=$releaseDir" "/DAppVersion=$Version" "/DAppFileVersion=$fileVersion" `
    (Join-Path $repoRoot 'installer\FilesMate.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }
$installer = Join-Path $releaseDir "FilesMate-Setup-$Version-win-x64.exe"
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
Set-Content -LiteralPath "$installer.sha256" -Encoding ascii -Value "$hash  $(Split-Path $installer -Leaf)"
[pscustomobject]@{ Installer = $installer; Bytes = (Get-Item -LiteralPath $installer).Length;
    SHA256 = $hash; Payload = $payload } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $stageRoot 'package.json') -Encoding utf8
Write-Host "Installer: $installer"
Write-Host "SHA256: $hash"

if ($IncludePortable) {
    & (Join-Path $PSScriptRoot 'package-portable.ps1') -Payload $payload -Version $Version -OutputDirectory $releaseDir
}
