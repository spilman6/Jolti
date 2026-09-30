[CmdletBinding()]
param(
    [string]$Message = ('Publish Jolti - ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Git {
    param([string[]]$GitArgs)
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed (exit $LASTEXITCODE)." }
}

function Stop-JoltiForPublish {
    $processes = @(Get-Process -Name Jolti -ErrorAction SilentlyContinue)
    if ($processes.Count -gt 0) {
        Write-Warning 'Stopping Jolti for the update. Any active recording or transcription will be discarded.'
    }
    foreach ($process in $processes) {
        if (-not $process.HasExited) {
            Stop-Process -InputObject $process -Force
            if (-not $process.WaitForExit(10000)) {
                throw 'Jolti did not exit within 10 seconds. The installed build has not been replaced.'
            }
        }
    }
}

# Only move build directories directly inside this repository's publish folder.
function Assert-PublishChild {
    param([string]$Path)
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($fullPath), $publishRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected publish path: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        $item = Get-Item -LiteralPath $fullPath -Force
        # OneDrive folders also have ReparsePoint metadata; only path-redirection
        # links (junctions/symbolic links) should prevent this directory move.
        if (-not [string]::IsNullOrEmpty($item.LinkType)) {
            throw "Refusing to move a linked directory: $fullPath"
        }
    }
}

Push-Location $PSScriptRoot
try {
    if ([string]::IsNullOrWhiteSpace($Message)) { throw 'Provide a nonempty commit message with -Message.' }
    $null = Get-Command git -ErrorAction Stop
    $null = Get-Command dotnet -ErrorAction Stop
    $branch = (Invoke-Git -GitArgs @('branch', '--show-current') | Out-String).Trim()
    if (-not $branch) { throw 'Check out a branch before publishing; HEAD is detached.' }
    $null = Invoke-Git -GitArgs @('remote', 'get-url', 'origin')
    $conflicts = @(Invoke-Git -GitArgs @('diff', '--name-only', '--diff-filter=U'))
    if ($conflicts.Count -gt 0) { throw 'Resolve Git conflicts before publishing.' }

    $publishRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts/publish'))
    $target = Join-Path $publishRoot 'jolti-vad-win-x64'
    $runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $staging = Join-Path $publishRoot "staging-$runId"
    $backup = Join-Path $publishRoot "backup-$runId"
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    Assert-PublishChild $target
    Assert-PublishChild $staging
    Assert-PublishChild $backup

    Write-Host 'Building a self-contained Release version...'
    & dotnet publish src/Jolti/Jolti.csproj -c Release -r win-x64 --self-contained true -o $staging
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed. The installed build and Git history were not changed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $staging 'Jolti.exe'))) { throw 'Publish did not produce Jolti.exe.' }
    Stop-JoltiForPublish

    # Rename the old folder instead of deleting it, and restore it if installation fails.
    $hadPreviousBuild = Test-Path -LiteralPath $target
    Assert-PublishChild $target
    Assert-PublishChild $backup
    if ($hadPreviousBuild) { Move-Item -LiteralPath $target -Destination $backup }
    try {
        Assert-PublishChild $staging
        Assert-PublishChild $target
        Move-Item -LiteralPath $staging -Destination $target
    }
    catch {
        if ($hadPreviousBuild -and -not (Test-Path -LiteralPath $target)) {
            Assert-PublishChild $backup
            Assert-PublishChild $target
            Move-Item -LiteralPath $backup -Destination $target
        }
        throw
    }
    Write-Host "Updated: $(Join-Path $target 'Jolti.exe')"
    if ($hadPreviousBuild) { Write-Host "Previous build saved: $backup" }

    # Includes all nonignored source changes, including new files and deletions.
    # Published binaries remain excluded by .gitignore.
    Invoke-Git -GitArgs @('add', '--all')
    $staged = @(Invoke-Git -GitArgs @('diff', '--cached', '--name-only'))
    if ($staged.Count -gt 0) {
        Invoke-Git -GitArgs @('commit', '-m', $Message)
    }
    else {
        Write-Host 'No source changes to commit.'
    }

    Write-Host "Pushing $branch to origin..."
    Invoke-Git -GitArgs @('push', '--set-upstream', 'origin', $branch)
    Write-Host 'Published, committed, and pushed. Your existing startup shortcut is ready.'
    Write-Host 'Starting Jolti...'
    Start-Process -FilePath (Join-Path $target 'Jolti.exe') -WorkingDirectory $target
}
catch {
    Write-Warning 'Publishing stopped. Any completed local build update or commit is retained; you can fix the error and rerun the script.'
    throw
}
finally {
    Pop-Location
}
