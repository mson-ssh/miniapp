# MiniApps net48: bundled, pinned Win11Debloat default profile, excluding restore points.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'Copy-OptimizeEngine.ps1')
. (Join-Path $PSScriptRoot 'Configure-OptimizeEngine.ps1')
. (Join-Path $PSScriptRoot 'Optimize-ParallelEngines.ps1')

# Create a durable, session-specific diagnostic location before preparing the engine.
$logRoot = Join-Path $env:LOCALAPPDATA 'MiniApps\OptimizeLogs'
$logs = Join-Path $logRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $logs -Force | Out-Null
Write-Output ('MINIAPPS_LOG:' + $logs)
Write-Output ('MINIAPPS_RUNNER_PID:' + $PID)
$transcriptStarted = $false
try {
    Start-Transcript -LiteralPath (Join-Path $logs 'session.log') -Force | Out-Null
    $transcriptStarted = $true
} catch { }

$root = Join-Path $PWD 'src'
$primaryError = $null
try {
    Write-Output 'MINIAPPS_STAGE:Preparing'
    $bundledEngine = Join-Path (Split-Path -Parent $PSScriptRoot) 'Engine\Debloat'
    Copy-MiniAppsOptimizeEngine -SourcePath $bundledEngine -DestinationPath $root
    $entry = Join-Path $root 'Win11Debloat.ps1'
    $backupPath = Set-MiniAppsOptimizeEngineConfiguration -EngineRoot $root -LogDirectory $logs

    # Run app removal and the remaining Default features as two concurrent engine lanes.
    # Each lane has an isolated transcript; both report one combined 17-task protocol.
    $escapedEntry = $entry.Replace("'", "''")
    $escapedBackupPath = $backupPath.Replace("'", "''")
    $escapedBridgePath = (Join-Path $PSScriptRoot 'Optimize-TaskBridge.ps1').Replace("'", "''")
    $escapedAppxWorkerPath = (Join-Path $PSScriptRoot 'Invoke-MiniAppsAppxWorker.ps1').Replace("'", "''")
    $profile = Get-Content -LiteralPath (Join-Path $root 'Config\DefaultSettings.json') -Raw | ConvertFrom-Json
    $featureNames = @($profile.Settings | Where-Object { $_.Name -ne 'CreateRestorePoint' -and $_.Value -eq $true } | ForEach-Object { [string]$_.Name })
    if ($featureNames.Count -ne 16 -or @($featureNames | Where-Object { $_ -notmatch '^[A-Za-z][A-Za-z0-9]*$' }).Count -gt 0) {
        throw 'Bundled Default profile does not expose the reviewed 16-feature lane.'
    }
    $commonArguments = "-Silent -SkipExplorerRestart -MiniApps -MiniAppsBackupPath '$escapedBackupPath' -MiniAppsBridgePath '$escapedBridgePath' -MiniAppsAppxWorkerPath '$escapedAppxWorkerPath' -MiniAppsAppTimeoutSeconds 180 -MiniAppsRemoveAppsTimeoutSeconds 1200"
    $featureLogs = Join-Path $logs 'Features'
    $removeAppsLogs = Join-Path $logs 'RemoveApps'
    New-Item -ItemType Directory -Path $featureLogs, $removeAppsLogs -Force | Out-Null
    $escapedFeatureLogs = $featureLogs.Replace("'", "''")
    $escapedRemoveAppsLogs = $removeAppsLogs.Replace("'", "''")
    # RunDefaults deliberately goes through upstream Import-Settings so Windows build and
    # Modern Standby compatibility rules remain authoritative for the feature lane.
    $featureCommand = "[Console]::WriteLine('MINIAPPS_ENGINE_PID:' + `$PID); & '$escapedEntry' -RunDefaults $commonArguments -MiniAppsLane Features -LogPath '$escapedFeatureLogs'; exit `$LASTEXITCODE"
    $removeAppsCommand = "[Console]::WriteLine('MINIAPPS_ENGINE_PID:' + `$PID); & '$escapedEntry' -RemoveApps -Apps Default -SkipRegistryBackup $commonArguments -MiniAppsLane RemoveApps -LogPath '$escapedRemoveAppsLogs'; exit `$LASTEXITCODE"
    $lanes = @(
        @{ Name = 'Features'; EncodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($featureCommand)); StdoutPath = (Join-Path $featureLogs 'stdout.txt'); StderrPath = (Join-Path $featureLogs 'stderr.txt') },
        @{ Name = 'RemoveApps'; EncodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($removeAppsCommand)); StdoutPath = (Join-Path $removeAppsLogs 'stdout.txt'); StderrPath = (Join-Path $removeAppsLogs 'stderr.txt') }
    )
    Write-Output 'MINIAPPS_STAGE:Applying'
    $exitCodes = Invoke-MiniAppsParallelEngines -Lanes $lanes
    $stderr = Join-Path $logs 'upstream-stderr.txt'
    @(
        foreach ($lane in $lanes) {
            "[$($lane.Name)]"
            if (Test-Path -LiteralPath $lane.StderrPath) { Get-Content -LiteralPath $lane.StderrPath }
        }
    ) | Set-Content -LiteralPath $stderr -Encoding UTF8
    $failedLanes = @($exitCodes.Keys | Where-Object { $exitCodes[$_] -ne 0 } | ForEach-Object { "$_=$($exitCodes[$_])" })
    $errors = if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Raw } else { '' }
    # Windows PowerShell may serialize host/information records to stderr as CLIXML.
    # Task failures are reported through MINIAPPS_TASK_JSON; stderr alone is diagnostic.
    if ($failedLanes.Count -gt 0) { throw "MiniApps Debloat lane failed ($($failedLanes -join ', ')): $errors" }
    $backupFiles = @(Get-ChildItem -LiteralPath $backupPath -Filter 'Win11Debloat-RegistryBackup-*.json' -File -ErrorAction Stop)
    if ($backupFiles.Count -eq 0) { throw 'Win11Debloat completed without creating the required Registry backup.' }
}
catch {
    try { ($_ | Out-String).Trim() | Set-Content -LiteralPath (Join-Path $logs 'failure.txt') -Encoding UTF8 } catch { }
    $primaryError = $_
}
finally {
    if ($transcriptStarted) { try { Stop-Transcript | Out-Null } catch { } }
}
if ($primaryError) { throw $primaryError }
Write-Output 'MINIAPPS_STAGE:Completed'
Write-Output 'Default profile completed. Sign out or restart Windows to apply all changes.'
