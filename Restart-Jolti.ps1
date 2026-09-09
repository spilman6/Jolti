$ErrorActionPreference = 'Stop'

# Force stopping discards any recording or transcription currently in progress.
$processes = @(Get-Process -Name Jolti -ErrorAction SilentlyContinue)
foreach ($process in $processes) {
    if (-not $process.HasExited) {
        Stop-Process -InputObject $process -Force
        if (-not $process.WaitForExit(10000)) {
            throw 'Jolti did not exit within 10 seconds. Restart canceled.'
        }
    }
}

& (Join-Path $PSScriptRoot 'Start-Jolti.ps1')
