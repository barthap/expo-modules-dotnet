[CmdletBinding()]
param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = $env:CONFIGURATION,
  [ValidateSet('Debug', 'Release')]
  [string]$NativeConfiguration = $env:NATIVE_CONFIGURATION,
  [string]$HermesPrebuiltRoot = $env:HERMES_PREBUILT_ROOT,
  [ValidateSet('x64', 'x86', 'arm64', 'arm64ec')]
  [string]$Arch = 'x64',
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$DotNetTestArgs = @()
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$Configuration) {
  $Configuration = 'Debug'
}
if (!$NativeConfiguration) {
  $NativeConfiguration = 'Release'
}
if (!$HermesPrebuiltRoot) {
  $HermesPrebuiltRoot = Join-Path $repoRoot 'build\hermes\source\destroot'
}

function Invoke-Process {
  param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,
    [Parameter(Mandatory = $true)]
    [string[]]$ArgumentList
  )

  & $FilePath @ArgumentList
  if ($LASTEXITCODE -ne 0) {
    throw "$FilePath exited with code $LASTEXITCODE"
  }
}

if (!(Test-Path -LiteralPath (Join-Path $HermesPrebuiltRoot 'include\hermes\hermes.h'))) {
  throw "Hermes prebuilt was not found at $HermesPrebuiltRoot. Run scripts\build-hermes-windows.ps1 first."
}
if (
  !(Test-Path -LiteralPath (Join-Path $HermesPrebuiltRoot "bin\win32\$Arch\hermesvm.dll")) -and
  !(Test-Path -LiteralPath (Join-Path $HermesPrebuiltRoot "bin\win32\$Arch\hermes.dll"))
) {
  throw "Hermes runtime DLL was not found for $Arch at $HermesPrebuiltRoot. Run scripts\build-hermes-windows.ps1 -Arch $Arch first."
}

$buildDir = Join-Path $repoRoot 'build\jsi-testhost-windows'
$testhostDll = Join-Path $buildDir "$NativeConfiguration\expo_jsi_testhost.dll"

Write-Host '==> Building Expo.JSI'
Invoke-Process -FilePath 'dotnet' -ArgumentList @(
  'build',
  (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.JSI\Expo.JSI.csproj'),
  '-c',
  $Configuration
)

Write-Host
Write-Host '==> Building Expo.ModulesCore'
Invoke-Process -FilePath 'dotnet' -ArgumentList @(
  'build',
  (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.ModulesCore\Expo.ModulesCore.csproj'),
  '-c',
  $Configuration
)

Write-Host
Write-Host '==> Building Expo.ModulesCore.Generator'
Invoke-Process -FilePath 'dotnet' -ArgumentList @(
  'build',
  (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.ModulesCore.Generator\Expo.ModulesCore.Generator.csproj'),
  '-c',
  $Configuration
)

Write-Host
Write-Host '==> Running Expo.ModulesCore.Generator.Tests'
Invoke-Process -FilePath 'dotnet' -ArgumentList (@(
  'test',
  (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.ModulesCore.Generator.Tests\Expo.ModulesCore.Generator.Tests.csproj'),
  '-c',
  $Configuration
) + $DotNetTestArgs)

Write-Host
Write-Host '==> Configuring native testhost'
if (Test-Path -LiteralPath $buildDir) {
  Remove-Item -LiteralPath $buildDir -Recurse -Force
}
Invoke-Process -FilePath 'cmake' -ArgumentList @(
  '-S',
  (Join-Path $repoRoot 'packages\expo-modules-dotnet\native\testhost'),
  '-B',
  $buildDir,
  '-G',
  'Visual Studio 18 2026',
  '-A',
  $Arch,
  "-DHERMES_PREBUILT_ROOT=$HermesPrebuiltRoot"
)

Write-Host
Write-Host '==> Building native testhost'
Invoke-Process -FilePath 'cmake' -ArgumentList @(
  '--build',
  $buildDir,
  '--config',
  $NativeConfiguration,
  '--target',
  'expo_jsi_testhost',
  '--parallel'
)

if (!(Test-Path -LiteralPath $testhostDll)) {
  throw "Native testhost DLL was not produced at $testhostDll"
}

$env:EXPO_JSI_TESTHOST_LIBRARY = $testhostDll

Write-Host
Write-Host '==> Running Expo.JSI.Tests'
Invoke-Process -FilePath 'dotnet' -ArgumentList (@(
  'test',
  (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.JSI.Tests\Expo.JSI.Tests.csproj'),
  '-c',
  $Configuration
) + $DotNetTestArgs)

Write-Host
Write-Host '==> Running Expo.ModulesCore.Tests'
Invoke-Process -FilePath 'dotnet' -ArgumentList (@(
  'test',
  (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.ModulesCore.Tests\Expo.ModulesCore.Tests.csproj'),
  '-c',
  $Configuration
) + $DotNetTestArgs)
