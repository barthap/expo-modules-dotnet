$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir '..\..\..')
$ManagedProject = Join-Path $RepoRoot 'packages\example-module\dotnet\ExampleModule\ExampleModule.csproj'
$GeneratorProject = Join-Path $RepoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.ModulesCore.Generator\Expo.ModulesCore.Generator.csproj'
$ManagedDir = Join-Path $RepoRoot 'apps\desktop-app\windows\Managed'
$Loader = if ($env:EXPO_DOTNET_LOADER) { $env:EXPO_DOTNET_LOADER } elseif ($env:EXPO_JSI_DOTNET_LOADER) { $env:EXPO_JSI_DOTNET_LOADER } else { 'hostfxr' }

function Get-ManagedConfiguration {
  if ($env:CONFIGURATION) {
    return $env:CONFIGURATION
  }
  if ($Loader -eq 'nativeaot') {
    return 'Release'
  }
  return 'Debug'
}

function Reset-ManagedDir {
  New-Item -ItemType Directory -Force -Path $ManagedDir | Out-Null
  Get-ChildItem -Force -Path $ManagedDir |
    Where-Object { $_.Name -ne '.gitignore' -and $_.Name -ne '.gitkeep' } |
    Remove-Item -Recurse -Force
}

function Invoke-DotnetBuild {
  param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

  $envNames = @(
    'ACTION',
    'ARCHS',
    'CURRENT_ARCH',
    'PLATFORM_NAME',
    'PRODUCT_NAME',
    'PROJECT_NAME',
    'TARGET_NAME',
    'TARGETNAME'
  )
  $previous = @{}
  foreach ($name in $envNames) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $null, 'Process')
  }

  try {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
      throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
  } finally {
    foreach ($name in $envNames) {
      [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
  }
}

function Get-DotnetRoot {
  $info = & dotnet --info
  $baseLine = $info | Where-Object { $_ -match '^\s*Base Path:' } | Select-Object -First 1
  if ($baseLine) {
    $basePath = ($baseLine -split ':', 2)[1].Trim()
    return (Resolve-Path (Join-Path $basePath '..\..')).Path
  }

  $dotnetCommand = Get-Command dotnet -ErrorAction Stop
  return (Split-Path -Parent $dotnetCommand.Source)
}

function Get-LatestNetHostNativeDir {
  $packRoot = Join-Path (Get-DotnetRoot) 'packs\Microsoft.NETCore.App.Host.win-x64'
  if (-not (Test-Path $packRoot)) {
    throw "Missing .NET host pack: $packRoot"
  }

  $packVersion = Get-ChildItem -Directory $packRoot |
    Where-Object { $_.Name -match '^[0-9]+(\.[0-9]+){2,}$' } |
    Sort-Object { [version]$_.Name } |
    Select-Object -Last 1
  if (-not $packVersion) {
    throw "No stable .NET host pack versions found under $packRoot"
  }

  return Join-Path $packVersion.FullName 'runtimes\win-x64\native'
}

function Copy-HostFxrArtifacts {
  param([string]$Configuration)

  $outputDir = Join-Path $RepoRoot "packages\example-module\dotnet\ExampleModule\bin\$Configuration\net10.0"
  Invoke-DotnetBuild build $ManagedProject -c $Configuration

  Reset-ManagedDir
  Get-ChildItem -Path $outputDir -File |
    Where-Object { $_.Name -like '*.dll' -or $_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json' } |
    Copy-Item -Destination $ManagedDir

  $nativeDir = Get-LatestNetHostNativeDir
  foreach ($artifact in @('nethost.dll', 'nethost.lib')) {
    Copy-Item (Join-Path $nativeDir $artifact) -Destination $ManagedDir
  }
}

function Copy-NativeAotArtifacts {
  param([string]$Configuration)

  $rid = 'win-x64'
  $publishDir = Join-Path $RepoRoot "packages\example-module\dotnet\ExampleModule\bin\$Configuration\net10.0\$rid\publish"

  Invoke-DotnetBuild build $GeneratorProject -c Debug
  Invoke-DotnetBuild publish $ManagedProject -c $Configuration -r $rid /p:PublishAot=true /p:NativeLib=Shared

  Reset-ManagedDir
  Copy-Item (Join-Path $publishDir 'ExampleModule.dll') -Destination $ManagedDir
  if (Test-Path (Join-Path $publishDir 'ExampleModule.lib')) {
    Copy-Item (Join-Path $publishDir 'ExampleModule.lib') -Destination $ManagedDir
  }
  if (Test-Path (Join-Path $publishDir 'ExampleModule.pdb')) {
    Copy-Item (Join-Path $publishDir 'ExampleModule.pdb') -Destination $ManagedDir
  }
}

if ($args.Count -gt 0 -and ($args[0] -eq '--help' -or $args[0] -eq '-h')) {
  Write-Output 'Usage: apps/desktop-app/scripts/build-managed.ps1'
  Write-Output ''
  Write-Output 'Environment:'
  Write-Output '  CONFIGURATION          .NET configuration. Default: Debug for HostFXR, Release for NativeAOT'
  Write-Output '  EXPO_DOTNET_LOADER     Managed loader: hostfxr or nativeaot. Default: hostfxr'
  Write-Output '  EXPO_JSI_DOTNET_LOADER Compatibility alias for EXPO_DOTNET_LOADER'
  exit 0
}

if ($Loader -ne 'hostfxr' -and $Loader -ne 'nativeaot') {
  throw "EXPO_DOTNET_LOADER must be hostfxr or nativeaot, got: $Loader"
}

$Configuration = Get-ManagedConfiguration
if ($Loader -eq 'hostfxr') {
  Copy-HostFxrArtifacts $Configuration
} else {
  Copy-NativeAotArtifacts $Configuration
}

Write-Output "Staged $Loader managed artifacts in apps/desktop-app/windows/Managed"
