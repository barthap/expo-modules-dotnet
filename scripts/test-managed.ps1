[CmdletBinding()]
param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = $env:CONFIGURATION,
  [ValidateSet('Debug', 'Release')]
  [string]$NativeConfiguration = $env:NATIVE_CONFIGURATION,
  [string]$HermesPrebuiltRoot = $env:HERMES_PREBUILT_ROOT,
  [ValidateSet('x64', 'x86', 'arm64', 'arm64ec')]
  [string]$Arch = 'x64',
  [string[]]$Project = @(),
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

$testProjects = [System.Collections.Generic.List[string]]::new()
$seenTestProjects = [System.Collections.Generic.HashSet[string]]::new(
  [System.StringComparer]::OrdinalIgnoreCase
)

function Add-TestProject {
  param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [Parameter(Mandatory = $true)]
    [string]$DisplayPath
  )

  $projectPath = [IO.Path]::GetFullPath($ProjectPath)
  $repoRootWithSeparator = $repoRoot + [IO.Path]::DirectorySeparatorChar
  if (!$projectPath.StartsWith($repoRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Invalid test project path: $DisplayPath (must resolve inside the repository)."
  }
  if (!(Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Invalid test project path: $DisplayPath (must be an existing regular file)."
  }

  $ancestorPath = $projectPath
  while ($true) {
    $ancestorItem = Get-Item -LiteralPath $ancestorPath -Force
    if ($ancestorItem.Attributes -band [IO.FileAttributes]::ReparsePoint) {
      throw "Invalid test project path: $DisplayPath (must not traverse a reparse point)."
    }
    if ($ancestorPath.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
      break
    }

    $parent = [IO.Directory]::GetParent($ancestorPath)
    if ($null -eq $parent) {
      throw "Invalid test project path: $DisplayPath (must resolve inside the repository)."
    }
    $ancestorPath = $parent.FullName
  }

  $projectItem = Get-Item -LiteralPath $projectPath -Force
  if ($projectItem -isnot [System.IO.FileInfo]) {
    throw "Invalid test project path: $DisplayPath (must be an existing regular file)."
  }
  if (!$projectItem.Name.EndsWith('.Tests.csproj', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Invalid test project path: $DisplayPath (must name a *.Tests.csproj file)."
  }
  if (!$seenTestProjects.Add($projectPath)) {
    throw "Invalid test project path: $DisplayPath (duplicate selection)."
  }

  $testProjects.Add($projectPath)
}

if ($Project.Count -gt 0) {
  foreach ($selectedProject in $Project) {
    if ([IO.Path]::IsPathRooted($selectedProject)) {
      throw "Invalid test project path: $selectedProject (paths must be repo-relative)."
    }

    Add-TestProject (Join-Path $repoRoot $selectedProject) $selectedProject
  }
} else {
  Add-TestProject (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.ModulesCore.Generator.Tests\Expo.ModulesCore.Generator.Tests.csproj') 'Expo.ModulesCore.Generator.Tests'
  Add-TestProject (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.JSI.Tests\Expo.JSI.Tests.csproj') 'Expo.JSI.Tests'
  Add-TestProject (Join-Path $repoRoot 'packages\expo-modules-dotnet\managed\packages\Expo.ModulesCore.Tests\Expo.ModulesCore.Tests.csproj') 'Expo.ModulesCore.Tests'

  foreach ($packageDirectory in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'packages') -Directory -Force | Sort-Object FullName) {
    $dotnetDirectory = Join-Path $packageDirectory.FullName 'dotnet'
    if (Test-Path -LiteralPath $dotnetDirectory -PathType Container) {
      foreach ($projectDirectory in Get-ChildItem -LiteralPath $dotnetDirectory -Directory -Filter '*.Tests' -Force | Sort-Object FullName) {
        $projectPath = Join-Path $projectDirectory.FullName "$($projectDirectory.Name).csproj"
        if (Test-Path -LiteralPath $projectPath -PathType Leaf) {
          Add-TestProject $projectPath $projectPath
        }
      }
    }
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

foreach ($testProject in $testProjects) {
  Write-Host
  Write-Host "==> Running $([IO.Path]::GetFileNameWithoutExtension($testProject))"
  Invoke-Process -FilePath 'dotnet' -ArgumentList (@(
    'test',
    $testProject,
    '-c',
    $Configuration
  ) + $DotNetTestArgs)
}
