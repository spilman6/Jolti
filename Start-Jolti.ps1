$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot 'artifacts/publish/jolti-tray-win-x64/Jolti.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'Publish Jolti first. See README.md for the publish command.' }
& $app
