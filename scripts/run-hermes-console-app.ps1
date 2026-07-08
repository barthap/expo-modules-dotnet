[CmdletBinding()]
param(
  [switch]$NoRun,
  [ValidateSet('hostfxr')]
  [string]$Loader = $(if ($env:EXPO_JSI_DOTNET_LOADER) { $env:EXPO_JSI_DOTNET_LOADER } else { 'hostfxr' }),
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = $env:CONFIGURATION,
  [ValidateSet('Debug', 'Release')]
  [string]$NativeConfiguration = $env:NATIVE_CONFIGURATION,
  [string]$HermesPrebuiltRoot = $env:HERMES_PREBUILT_ROOT,
  [ValidateSet('x64', 'x86', 'arm64', 'arm64ec')]
  [string]$Arch = 'x64',
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$AppArgs
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$appRoot = Join-Path $repoRoot 'apps\hermes-console-app'
$managedProject = Join-Path $appRoot 'managed\HermesConsoleApp\HermesConsoleApp.csproj'

if (!$Configuration) {
  $Configuration = 'Debug'
}
if (!$NativeConfiguration) {
  $NativeConfiguration = 'Release'
}
if (!$HermesPrebuiltRoot) {
  $HermesPrebuiltRoot = Join-Path $repoRoot 'build\hermes\source\destroot'
}
if ($Loader -ne 'hostfxr') {
  throw 'Windows runner supports hostfxr in this slice.'
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

Write-Host '==> Building managed dotnet HostFXR'
Invoke-Process -FilePath 'dotnet' -ArgumentList @('build', $managedProject, '-c', $Configuration)

$buildDir = Join-Path $repoRoot "build\hermes-console-app\$Loader-windows"
if (Test-Path -LiteralPath $buildDir) {
  Remove-Item -LiteralPath $buildDir -Recurse -Force
}

Write-Host
Write-Host '==> Configuring cmake'
Invoke-Process -FilePath 'cmake' -ArgumentList @(
  '-S',
  (Join-Path $appRoot 'native'),
  '-B',
  $buildDir,
  '-G',
  'Visual Studio 18 2026',
  '-A',
  $Arch,
  "-DEXPO_JSI_DOTNET_LOADER=$Loader",
  "-DEXPO_JSI_MANAGED_CONFIGURATION=$Configuration",
  "-DHERMES_PREBUILT_ROOT=$HermesPrebuiltRoot"
)

Write-Host
Write-Host '==> Building the app'
Invoke-Process -FilePath 'cmake' -ArgumentList @(
  '--build',
  $buildDir,
  '--config',
  $NativeConfiguration,
  '--target',
  'hermes_console_app',
  '--parallel'
)

$exe = Join-Path $buildDir "$NativeConfiguration\hermes_console_app.exe"
if (!(Test-Path -LiteralPath $exe)) {
  throw "Hermes console executable was not produced at $exe"
}

if (!$NoRun) {
  Write-Host
  Write-Host "==> Running Hermes console app ($Loader)"
  & $exe @AppArgs
  if ($LASTEXITCODE -ne 0) {
    throw "Hermes console app exited with code $LASTEXITCODE"
  }
}
