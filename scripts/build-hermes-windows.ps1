[CmdletBinding()]
param(
  [ValidateSet('x64', 'x86', 'arm64', 'arm64ec')]
  [string]$Arch = 'x64',
  [ValidateSet('Release', 'Debug')]
  [string]$Configuration = 'Release',
  [switch]$Clean,
  [switch]$EnableIntl,
  [switch]$AllowNuGetFallback,
  [string]$HermesRepoUrl = 'https://github.com/facebook/hermes.git',
  [string]$HermesRef,
  [string]$HermesWorkDir,
  [string]$NuGetPackageRoot
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$HermesRef) {
  $HermesRef = (Get-Content -LiteralPath (Join-Path $repoRoot 'scripts\hermes-ref.txt') -Raw).Trim()
}
if (!$HermesWorkDir) {
  $HermesWorkDir = Join-Path $repoRoot 'build\hermes'
}
if (!$NuGetPackageRoot) {
  $NuGetPackageRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.javascript.hermes'
}

$sourceDir = Join-Path $HermesWorkDir 'source'
$destRoot = Join-Path $sourceDir 'destroot'
$officialFlavor = if ($EnableIntl) { 'windows-official-shared-intl' } else { 'windows-official-shared-no-intl' }
$officialBuildDir = Join-Path $HermesWorkDir "$officialFlavor\$Arch"

function Invoke-Process {
  param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,
    [Parameter(Mandatory = $true)]
    [string[]]$ArgumentList,
    [string]$WorkingDirectory = $repoRoot
  )

  Push-Location $WorkingDirectory
  try {
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
      throw "$FilePath exited with code $LASTEXITCODE"
    }
  } finally {
    Pop-Location
  }
}

function Copy-FileRequired {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Source,
    [Parameter(Mandatory = $true)]
    [string]$Destination
  )

  if (!(Test-Path -LiteralPath $Source)) {
    throw "Required file not found: $Source"
  }

  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
  Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Copy-DirectoryContents {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Source,
    [Parameter(Mandatory = $true)]
    [string]$Destination
  )

  if (!(Test-Path -LiteralPath $Source)) {
    throw "Required directory not found: $Source"
  }

  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}

function Test-NormalizedHermesPrebuilt {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Root,
    [Parameter(Mandatory = $true)]
    [string]$TargetArch
  )

  $required = @(
    (Join-Path $Root 'include\hermes\hermes.h'),
    (Join-Path $Root 'include\jsi\jsi.h')
  )

  foreach ($path in $required) {
    if (!(Test-Path -LiteralPath $path)) {
      return $false
    }
  }

  $officialLib = Join-Path $Root "lib\win32\$TargetArch\hermesvm.lib"
  $officialDll = Join-Path $Root "bin\win32\$TargetArch\hermesvm.dll"
  $officialJsiLib = Join-Path $Root "lib\win32\$TargetArch\jsi.lib"
  $rnwLib = Join-Path $Root "lib\win32\$TargetArch\hermes.lib"
  $rnwDll = Join-Path $Root "bin\win32\$TargetArch\hermes.dll"

  return (
    (
      (Test-Path -LiteralPath $officialLib) -and
      (Test-Path -LiteralPath $officialDll) -and
      (Test-Path -LiteralPath $officialJsiLib)
    ) -or
    ((Test-Path -LiteralPath $rnwLib) -and (Test-Path -LiteralPath $rnwDll))
  )
}

function Initialize-HermesSource {
  New-Item -ItemType Directory -Force -Path $HermesWorkDir | Out-Null
  if (!(Test-Path -LiteralPath (Join-Path $sourceDir '.git'))) {
    Invoke-Process -FilePath 'git' -ArgumentList @('clone', $HermesRepoUrl, $sourceDir)
  }

  Invoke-Process -FilePath 'git' -ArgumentList @('-C', $sourceDir, 'fetch', '--tags', 'origin', $HermesRef)
  Invoke-Process -FilePath 'git' -ArgumentList @('-C', $sourceDir, 'checkout', '--detach', 'FETCH_HEAD')
}

function Find-FirstFile {
  param(
    [Parameter(Mandatory = $true)]
    [string[]]$Roots,
    [Parameter(Mandatory = $true)]
    [string]$FileName
  )

  foreach ($root in $Roots) {
    if (!(Test-Path -LiteralPath $root)) {
      continue
    }

    $match = Get-ChildItem -LiteralPath $root -Recurse -File -Filter $FileName -ErrorAction SilentlyContinue |
      Select-Object -First 1
    if ($match) {
      return $match.FullName
    }
  }

  return $null
}

function Copy-OfficialHeaders {
  Copy-DirectoryContents `
    -Source (Join-Path $sourceDir 'API\hermes') `
    -Destination (Join-Path $destRoot 'include\hermes')
  Copy-DirectoryContents `
    -Source (Join-Path $sourceDir 'public\hermes') `
    -Destination (Join-Path $destRoot 'include\hermes')

  Copy-DirectoryContents `
    -Source (Join-Path $sourceDir 'API\jsi\jsi') `
    -Destination (Join-Path $destRoot 'include\jsi')
}

function Stage-OfficialHermesArtifacts {
  $hermesLib = Find-FirstFile -Roots @($officialBuildDir) -FileName 'hermesvm.lib'
  $hermesDll = Find-FirstFile -Roots @($officialBuildDir) -FileName 'hermesvm.dll'
  $jsiLib = Find-FirstFile -Roots @($officialBuildDir) -FileName 'jsi.lib'

  if (!$hermesLib -or !$hermesDll -or !$jsiLib) {
    return $false
  }

  Copy-OfficialHeaders
  Copy-FileRequired -Source $hermesLib -Destination (Join-Path $destRoot "lib\win32\$Arch\hermesvm.lib")
  Copy-FileRequired -Source $jsiLib -Destination (Join-Path $destRoot "lib\win32\$Arch\jsi.lib")
  Copy-FileRequired -Source $hermesDll -Destination (Join-Path $destRoot "bin\win32\$Arch\hermesvm.dll")

  return (Test-NormalizedHermesPrebuilt -Root $destRoot -TargetArch $Arch)
}

function Try-BuildOfficialHermes {
  Initialize-HermesSource
  if ($Clean -and (Test-Path -LiteralPath $officialBuildDir)) {
    Remove-Item -LiteralPath $officialBuildDir -Recurse -Force
  }

  New-Item -ItemType Directory -Force -Path $officialBuildDir | Out-Null
  Invoke-Process -FilePath 'cmake' -ArgumentList @(
    '-S', $sourceDir,
    '-B', $officialBuildDir,
    '-G', 'Visual Studio 18 2026',
    '-A', $Arch,
    '-DCMAKE_BUILD_TYPE=Release',
    '-DBUILD_SHARED_LIBS=ON',
    '-DHERMES_ENABLE_DEBUGGER=OFF',
    "-DHERMES_ENABLE_INTL=$(if ($EnableIntl) { 'ON' } else { 'OFF' })"
  )
  Invoke-Process -FilePath 'cmake' -ArgumentList @(
    '--build',
    $officialBuildDir,
    '--config',
    $Configuration,
    '--target',
    'hermesvm',
    '--parallel'
  )

  return (Stage-OfficialHermesArtifacts)
}

function Stage-NuGetHermesFallback {
  Initialize-HermesSource

  if (!(Test-Path -LiteralPath $NuGetPackageRoot)) {
    throw "Microsoft.JavaScript.Hermes NuGet package was not found. Restore the RNW solution first or pass -NuGetPackageRoot."
  }

  $package = Get-ChildItem -LiteralPath $NuGetPackageRoot -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1
  if (!$package) {
    throw "Microsoft.JavaScript.Hermes NuGet package has no installed versions."
  }

  $nativeRoot = Join-Path $package.FullName 'build\native'
  Copy-DirectoryContents -Source (Join-Path $nativeRoot 'include') -Destination (Join-Path $destRoot 'include')
  Copy-OfficialHeaders

  Copy-FileRequired `
    -Source (Join-Path $nativeRoot "win32\$Arch\hermes.lib") `
    -Destination (Join-Path $destRoot "lib\win32\$Arch\hermes.lib")
  Copy-FileRequired `
    -Source (Join-Path $nativeRoot "win32\$Arch\hermes.dll") `
    -Destination (Join-Path $destRoot "bin\win32\$Arch\hermes.dll")

  $icu = Join-Path $nativeRoot "win32\$Arch\hermes-icu.dll"
  if (Test-Path -LiteralPath $icu) {
    Copy-FileRequired -Source $icu -Destination (Join-Path $destRoot "bin\win32\$Arch\hermes-icu.dll")
  }

  return $package.FullName
}

if ($Clean -and (Test-Path -LiteralPath $destRoot)) {
  Remove-Item -LiteralPath $destRoot -Recurse -Force
}

$usedFallback = $false
try {
  $officialReady = Try-BuildOfficialHermes
  if ($officialReady -and (Test-NormalizedHermesPrebuilt -Root $destRoot -TargetArch $Arch)) {
    Write-Host "Hermes Windows prebuilt ready: HERMES_PREBUILT_ROOT=$destRoot"
    exit 0
  }

  throw "Official Hermes build completed but did not expose hermes.h, jsi.h, jsi.lib, hermesvm.lib, and hermesvm.dll in a normalized layout."
} catch {
  Write-Warning "Official Hermes Windows build path did not produce a normalized prebuilt: $($_.Exception.Message)"
  if ($AllowNuGetFallback) {
    throw "Microsoft.JavaScript.Hermes fallback packages expose the Hermes C API, but the current headless connector requires the C++ hermesvm API. Retry without -EnableIntl or use a compatible hermesvm prebuilt."
  }

  throw "Retry without -EnableIntl, or pass -HermesWorkDir to a clean build cache if stale CMake options are present."
}

if (!(Test-NormalizedHermesPrebuilt -Root $destRoot -TargetArch $Arch)) {
  throw "Hermes Windows prebuilt staging failed validation."
}

Write-Host "Hermes Windows prebuilt ready: HERMES_PREBUILT_ROOT=$destRoot"
if ($usedFallback) {
  Write-Host "Fallback source: Microsoft.JavaScript.Hermes"
}
