param([Parameter(Mandatory = $true)][string]$Source)
$ErrorActionPreference = 'Stop'
$expected = 'a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002'
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $expected) {
    throw 'SHA-256 verification failed. Supply the approved, complete ggml-base.en.bin model.'
}
$destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Jolti\models'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$directory = [IO.DirectoryInfo]::new($destination)
while ($null -ne $directory) {
    if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Model directory cannot use symbolic links or junctions.' }
    $directory = $directory.Parent
}
$target = Join-Path $destination 'ggml-base.en.bin'
if ($sourcePath -eq $target) { Write-Output "Verified installed model: $target"; return }
$partial = Join-Path $destination ('install-' + [Guid]::NewGuid().ToString('N') + '.partial')
try {
    Copy-Item -LiteralPath $sourcePath -Destination $partial
    if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $expected) { throw 'Installed copy failed SHA-256 verification.' }
    Move-Item -LiteralPath $partial -Destination $target -Force
} finally {
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
}
Write-Output "Verified model installed: $target"
