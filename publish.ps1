[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Message
)

& (Join-Path $PSScriptRoot 'Publish-Jolti.ps1') @PSBoundParameters
