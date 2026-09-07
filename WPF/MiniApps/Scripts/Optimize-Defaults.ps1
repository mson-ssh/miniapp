# MiniApps net48: pinned Win11Debloat default profile, excluding restore points.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$commit = '6012b02ea282f23ea943946206762fd430025c6f'
. (Join-Path $PSScriptRoot 'Expand-OptimizeArchive.ps1')

# Create a durable, session-specific diagnostic location before any network work.
$logRoot = Join-Path $env:LOCALAPPDATA 'MiniApps\OptimizeLogs'
$logs = Join-Path $logRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $logs -Force | Out-Null
Write-Output ('MINIAPPS_LOG:' + $logs)
$transcriptStarted = $false
try {
    Start-Transcript -LiteralPath (Join-Path $logs 'session.log') -Force | Out-Null
    $transcriptStarted = $true
} catch { }

$zip = Join-Path $PWD 'upstream.zip'
$root = Join-Path $PWD 'src'
$primaryError = $null
$backupError = $null
try {
    Write-Output 'MINIAPPS_STAGE:Downloading'
    Invoke-WebRequest "https://github.com/Raphire/Win11Debloat/archive/$commit.zip" -OutFile $zip -UseBasicParsing -TimeoutSec 600

    Write-Output 'MINIAPPS_STAGE:Preparing'
    Expand-MiniAppsOptimizeArchive -ArchivePath $zip -DestinationPath $root -ExpectedRoot "Win11Debloat-$commit"
    $defaultsPath = Join-Path $root 'Config\DefaultSettings.json'
    $defaults = Get-Content -LiteralPath $defaultsPath -Raw | ConvertFrom-Json
    if ($defaults.Version -ne '1.0') { throw 'Unexpected default profile schema.' }
    $defaults.Settings = @($defaults.Settings | Where-Object { $_.Name -ne 'CreateRestorePoint' })
    $defaults | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $defaultsPath -Encoding UTF8

    # Attach reporting to the reviewed upstream entry point without changing feature order.
    $entry = Join-Path $root 'Win11Debloat.ps1'
    $entryText = Get-Content -LiteralPath $entry -Raw
    $anchor = [regex]'(?m)^Invoke-AllChanges\r?$'
    if ($anchor.Matches($entryText).Count -ne 1) { throw 'Upstream task reporting anchor changed.' }
    $bridge = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Optimize-TaskBridge.ps1') -Raw
    $entryText = $anchor.Replace($entryText, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $bridge + "`r`n" + $match.Value })
    Set-Content -LiteralPath $entry -Value $entryText -Encoding UTF8

    # Keep upstream stderr and registry backups outside the disposable download directory.
    $stderr = Join-Path $logs 'upstream-stderr.txt'
    Write-Output 'MINIAPPS_STAGE:Applying'
    & (Join-Path $PSHOME 'powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'Win11Debloat.ps1') -RunDefaults -Silent -SkipExplorerRestart -LogPath $logs 2> $stderr
    $code = $LASTEXITCODE
    $errors = if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Raw } else { '' }
    if ($code -ne 0 -or -not [string]::IsNullOrWhiteSpace($errors)) { throw "Win11Debloat reported errors (exit $code): $errors" }

}
catch {
    try { ($_ | Out-String).Trim() | Set-Content -LiteralPath (Join-Path $logs 'failure.txt') -Encoding UTF8 } catch { }
    $primaryError = $_
}
finally {
    if ($root) {
        $backup = Join-Path $root 'Backups'
        if (Test-Path -LiteralPath $backup) {
            try { Move-Item -LiteralPath $backup -Destination (Join-Path $logs 'Backups') -ErrorAction Stop }
            catch {
                $backupError = $_
                try { ($_ | Out-String).Trim() | Set-Content -LiteralPath (Join-Path $logs 'backup-failure.txt') -Encoding UTF8 } catch { }
            }
        }
    }
    if ($transcriptStarted) { try { Stop-Transcript | Out-Null } catch { } }
}
if ($primaryError) { throw $primaryError }
if ($backupError) { throw $backupError }
Write-Output 'MINIAPPS_STAGE:Completed'
Write-Output 'Default profile completed. Sign out or restart Windows to apply all changes.'
