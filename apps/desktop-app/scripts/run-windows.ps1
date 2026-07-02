param(
  [string]$Configuration = "Debug",
  [string]$Platform = "x64",
  [switch]$NoPackager
)

$ErrorActionPreference = "Stop"

$appRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$exePath = Join-Path $appRoot "windows\$Platform\$Configuration\DesktopApp.exe"
$reactNative = Join-Path $appRoot "node_modules\.bin\react-native.CMD"

Push-Location $appRoot
try {
  if (-not $NoPackager) {
    $metroRunning = $false
    try {
      $response = Invoke-WebRequest -Uri "http://localhost:8081/status" -UseBasicParsing -TimeoutSec 2
      $metroRunning = $response.StatusCode -eq 200
    } catch {
      $metroRunning = $false
    }

    if (-not $metroRunning) {
      Start-Process -FilePath "cmd.exe" -ArgumentList "/c pnpm start:bundler" -WorkingDirectory $appRoot
      Start-Sleep -Seconds 3
    }
  }

  & $reactNative run-windows `
    --no-deploy `
    --no-launch `
    --no-packager `
    --no-autolink `
    --singleproc `
    --msbuildprops PlatformToolset=v145

  if (-not (Test-Path $exePath)) {
    throw "Expected Windows app executable was not produced: $exePath"
  }

  Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath)
} finally {
  Pop-Location
}
