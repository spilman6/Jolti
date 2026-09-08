param([string]$Destination = (Join-Path $PSScriptRoot '../artifacts/models'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$target = Join-Path $Destination 'ggml-base.en.bin'
$expected = 'a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002'
if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $expected) {
    Write-Output "Verified model already exists: $target"
    return
}
Write-Output 'Downloading the 148 MB English base model from Hugging Face. No local audio or text is sent.'
$partial = "$target.partial"
curl.exe --fail --location --retry 3 --output $partial 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin'
if ($LASTEXITCODE -ne 0) { throw 'Model download failed. Run this script again to retry.' }
if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $expected) { throw 'Model checksum mismatch. The partial download will not be used.' }
Move-Item -LiteralPath $partial -Destination $target -Force
Write-Output "Verified model ready: $target"
Write-Output "Install separately before using Jolti: ./scripts/Install-WhisperModel.ps1 -Source '$target'"
