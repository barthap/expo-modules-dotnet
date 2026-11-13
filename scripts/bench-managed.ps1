[CmdletBinding(PositionalBinding = $false)]
param(
  [switch]$Help,
  [ValidateSet('x64', 'x86', 'arm64', 'arm64ec')]
  [string]$Arch = 'x64',
  [string]$HermesPrebuiltRoot = $env:HERMES_PREBUILT_ROOT,
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$BenchmarkArgs = @()
)

$ErrorActionPreference = 'Stop'

if ($Help) {
  @'
Usage: scripts\bench-managed.ps1 [-Arch <x64|x86|arm64|arm64ec>] [-- <benchmark args...>]

Builds everything in Release, then runs the call-path benchmark against the
Hermes-backed native JSI testhost. Stdout carries only the header, the table, and
the warning lines. Build output and progress go to the host.

Benchmark arguments (after --):
  --filter <regex>   Run only the cases whose "<name> (<direction>)" matches
                     (cases without a direction match on "<name>")
  --help             Print the benchmark usage

Environment:
  HERMES_PREBUILT_ROOT    Hermes destroot. Default: <repo>\build\hermes\source\destroot

The native testhost is built incrementally in build\jsi-testhost-bench-windows.
The directory is not deleted between runs and is separate from the one used by
scripts\test-managed.ps1.

Exit codes (the benchmark's own code is passed through):
  0    Success
  1    Benchmark: at least one sample set is noisy (the table is still printed).
       Also this script: the Hermes prebuilt was not found (a hint is printed).
  2    Benchmark: --filter matches no case
  3    Benchmark: a case failed verification or threw
  4    Benchmark: tiered compilation is not off, or the native testhost is not
       optimized (the table is still printed)
  64   Benchmark: unknown argument or invalid regex.
       Also this script: an unknown argument before the benchmark arguments.
  70   This script: a build step failed
'@ | Write-Host
  exit 0
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$HermesPrebuiltRoot) {
  $HermesPrebuiltRoot = Join-Path $repoRoot 'build\hermes\source\destroot'
}

# Runs a build step with its output on the host, so stdout carries only the
# benchmark table. A failure exits with 70, so it cannot be mistaken for a
# benchmark result.
function Invoke-BuildStep {
  param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,
    [Parameter(Mandatory = $true)]
    [string[]]$ArgumentList
  )

  & $FilePath @ArgumentList | Out-Host
  if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("$FilePath exited with code $LASTEXITCODE")
    exit 70
  }
}

# Benchmark arguments follow a leading --. PowerShell consumes the first -- itself, so it is dropped
# here only when it still arrives. Like the sh script, reject a leading argument that is not a
# benchmark option, before any build step runs. Unlike the sh script, this cannot tell
# "-- --filter x" from "--filter x", so both are accepted.
$forwardedArgs = @($BenchmarkArgs)
if ($forwardedArgs.Count -gt 0 -and $forwardedArgs[0] -eq '--') {
  $forwardedArgs = @($forwardedArgs | Select-Object -Skip 1)
}
if ($forwardedArgs.Count -gt 0 -and !([string]$forwardedArgs[0]).StartsWith('--')) {
  [Console]::Error.WriteLine("Unknown argument: $($forwardedArgs[0]) (benchmark arguments go after --, see -Help).")
  exit 64
}

if (!(Test-Path -LiteralPath (Join-Path $HermesPrebuiltRoot 'include\hermes\hermes.h'))) {
  [Console]::Error.WriteLine("Hermes prebuilt was not found at $HermesPrebuiltRoot. Run scripts\build-hermes-windows.ps1 first.")
  exit 1
}
if (
  !(Test-Path -LiteralPath (Join-Path $HermesPrebuiltRoot "bin\win32\$Arch\hermesvm.dll")) -and
  !(Test-Path -LiteralPath (Join-Path $HermesPrebuiltRoot "bin\win32\$Arch\hermes.dll"))
) {
  [Console]::Error.WriteLine("Hermes runtime DLL was not found for $Arch at $HermesPrebuiltRoot. Run scripts\build-hermes-windows.ps1 -Arch $Arch first.")
  exit 1
}

$benchmarkProject = Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.ModulesCore.Benchmarks\Expo.ModulesCore.Benchmarks.csproj'
# Not the directory scripts\test-managed.ps1 deletes on every run.
# @ref docs/specs/hermes-testhost.md "Canonical Benchmark Runners"
$buildDir = Join-Path $repoRoot 'build\jsi-testhost-bench-windows'
$testhostDll = Join-Path $buildDir 'Release\expo_jsi_testhost.dll'

Write-Host '==> Building Expo.JSI'
Invoke-BuildStep -FilePath 'dotnet' -ArgumentList @(
  'build',
  (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.JSI\Expo.JSI.csproj'),
  '-c',
  'Release'
)

Write-Host
Write-Host '==> Building Expo.ModulesCore'
Invoke-BuildStep -FilePath 'dotnet' -ArgumentList @(
  'build',
  (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.ModulesCore\Expo.ModulesCore.csproj'),
  '-c',
  'Release'
)

Write-Host
Write-Host '==> Building Expo.ModulesCore.Generator'
Invoke-BuildStep -FilePath 'dotnet' -ArgumentList @(
  'build',
  (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.ModulesCore.Generator\Expo.ModulesCore.Generator.csproj'),
  '-c',
  'Release'
)

Write-Host
Write-Host '==> Building Expo.ModulesCore.Benchmarks'
Invoke-BuildStep -FilePath 'dotnet' -ArgumentList @(
  'build',
  $benchmarkProject,
  '-c',
  'Release'
)

Write-Host
Write-Host '==> Configuring native testhost'
Invoke-BuildStep -FilePath 'cmake' -ArgumentList @(
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
# Release on purpose: a Debug testhost gives numbers that mislead.
# @ref docs/specs/hermes-testhost.md "Canonical Benchmark Runners"
Write-Host '==> Building native testhost'
Invoke-BuildStep -FilePath 'cmake' -ArgumentList @(
  '--build',
  $buildDir,
  '--config',
  'Release',
  '--target',
  'expo_jsi_testhost',
  '--parallel'
)

if (!(Test-Path -LiteralPath $testhostDll)) {
  [Console]::Error.WriteLine("Native testhost DLL was not produced at $testhostDll")
  exit 70
}

$env:EXPO_JSI_TESTHOST_LIBRARY = $testhostDll

Write-Host
Write-Host '==> Running Expo.ModulesCore.Benchmarks'
& dotnet run --no-build -c Release --project $benchmarkProject -- @forwardedArgs
exit $LASTEXITCODE
