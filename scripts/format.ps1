$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$formatScript = Join-Path $scriptDir 'format.py'

$python = Get-Command python -ErrorAction SilentlyContinue
if ($null -ne $python) {
  & $python.Source $formatScript @args
} else {
  py -3 $formatScript @args
}
exit $LASTEXITCODE
