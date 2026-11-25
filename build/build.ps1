<#
.SYNOPSIS
    Builds CityHelper for release: a standalone (portable) zip and, when Inno Setup is available, an installer.

.DESCRIPTION
    1. Restores and builds the solution in Release (fails on any error).
    2. Publishes a self-contained single-file win-x64 exe (no .NET install needed on the target machine).
    3. Standalone: dist\CityHelper-<version>-portable-win-x64.zip. It contains portable.txt, so settings are kept
       beside the exe (CityHelper.ini). Unzip anywhere and run CityHelper.exe.
    4. Installed: dist\CityHelper-<version>-setup-win-x64.exe, built from build\CityHelper.iss with Inno Setup 6 or 7
       (https://jrsoftware.org/isinfo.php). Installed copies keep settings in %APPDATA%\CityHelper.
       If ISCC.exe isn't found the installer step is skipped with a message.

.PARAMETER Version
    The version label of this build (for example 20261123 or v20261123.2). Written to Directory.Build.props
    (InformationalVersion), the single place the version is kept. Omit to build the version already there.

.PARAMETER BumpMinor
    Also raises the internal assembly version's minor number by one (2026.15.0 becomes 2026.16.0).

.PARAMETER Runtime
    Runtime identifier to publish for. Default win-x64.

.PARAMETER SkipTests
    Do not run the unit tests (CityHelper.Tests). By default a failing test stops the build.

.PARAMETER SkipInstaller
    Only produce the standalone zip.

.EXAMPLE
    .\build\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$BumpMinor,
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

$repo     = Split-Path -Parent $PSScriptRoot
$desktop  = Join-Path $repo 'CityHelper.Desktop\CityHelper.Desktop.csproj'
$solution = Join-Path $repo 'CityHelper.sln'
$props    = Join-Path $repo 'Directory.Build.props'
$dist     = Join-Path $repo 'dist'
$publish  = Join-Path $dist 'publish'
$portable = Join-Path $dist 'CityHelper'

foreach ($p in @($desktop, $solution, $props)) {
    if (-not (Test-Path $p)) { throw "Missing $p" }
}

# Version: set / bump it in Directory.Build.props first, so that file stays the one place it lives
if ($Version -or $BumpMinor) {
    $text = Get-Content $props -Raw
    if ($Version) {
        $label = $Version.Trim().TrimStart('v', 'V')
        $text = [regex]::Replace($text, '<InformationalVersion>.*?</InformationalVersion>', "<InformationalVersion>$label</InformationalVersion>")
    }
    if ($BumpMinor) {
        $text = [regex]::Replace($text, '<Version>(\d+)\.(\d+)\.(\d+)</Version>', { param($m) "<Version>$($m.Groups[1].Value).$([int]$m.Groups[2].Value + 1).0</Version>" })
    }
    Set-Content -Path $props -Value $text -NoNewline
}

$group = ([xml](Get-Content $props)).Project.PropertyGroup
$version = $group.InformationalVersion, $group.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = '1.0.0' }
Write-Host "CityHelper $version ($Runtime)" -ForegroundColor Cyan

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed ($LASTEXITCODE)" }
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $publish, $portable | Out-Null

Write-Host '== Build (Release) ==' -ForegroundColor Cyan
Invoke-Dotnet build $solution -c Release --nologo

if (-not $SkipTests) {
    Write-Host '== Tests ==' -ForegroundColor Cyan
    Invoke-Dotnet test (Join-Path $repo 'CityHelper.Tests\CityHelper.Tests.csproj') -c Release --no-build --nologo
}

Write-Host '== Publish (self-contained, single file) ==' -ForegroundColor Cyan
Invoke-Dotnet publish $desktop -c Release -r $Runtime --self-contained true --nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $publish

$exe = Join-Path $publish 'CityHelper.Desktop.exe'
if (-not (Test-Path $exe)) { throw "Publish did not produce $exe" }
Copy-Item $exe (Join-Path $publish 'CityHelper.exe')
Remove-Item $exe

Write-Host '== Standalone zip ==' -ForegroundColor Cyan
Copy-Item (Join-Path $publish 'CityHelper.exe') $portable
Set-Content -Path (Join-Path $portable 'portable.txt') -Encoding ASCII -Value @(
    'CityHelper standalone mode.',
    'While this file is next to CityHelper.exe, settings are kept in CityHelper.ini in this folder.',
    'Delete this file to keep settings in %APPDATA%\CityHelper instead.')
$zip = Join-Path $dist "CityHelper-$version-portable-$Runtime.zip"
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip
Write-Host "  $zip"

if ($SkipInstaller) { return }

Write-Host '== Installer ==' -ForegroundColor Cyan
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    # Newest first: Inno Setup 7, then 6, in Program Files, Program Files (x86) and the per-user location
    $iscc = foreach ($v in '7', '6') {
        "$env:ProgramFiles\Inno Setup $v\ISCC.exe"
        "${env:ProgramFiles(x86)}\Inno Setup $v\ISCC.exe"
        "$env:LOCALAPPDATA\Programs\Inno Setup $v\ISCC.exe"
    }
    $iscc = $iscc | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    Write-Warning 'Inno Setup (ISCC.exe) was not found, so the installer was not built. Install it from https://jrsoftware.org/isdl.php (or: winget install JRSoftware.InnoSetup) and run this script again.'
    return
}

& $iscc "/DAppVersion=$version" "/DSourceExe=$(Join-Path $publish 'CityHelper.exe')" "/DOutputDir=$dist" (Join-Path $PSScriptRoot 'CityHelper.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
Write-Host "  $(Get-ChildItem $dist -Filter '*-setup-*.exe' | Select-Object -First 1 -ExpandProperty FullName)"
