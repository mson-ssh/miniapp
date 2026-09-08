param(
    [Parameter(Mandatory)][string]$AppId,
    [Parameter(Mandatory)][string]$ResultPath,
    [Parameter(Mandatory)][string]$LogPath,
    [string]$WingetPath = 'winget.exe'
)

$ErrorActionPreference = 'Stop'
$workerMutexName = 'Global\MiniApps.DebloatAppxWorker'
$gate = New-Object Threading.Mutex($false, $workerMutexName)
$acquired = $false
$startedAt = [DateTimeOffset]::UtcNow
$errors = New-Object Collections.Generic.List[string]
$output = New-Object Collections.Generic.List[string]
$success = $false
$exitCode = $null

try {
    # Use the same package-operation lock as Appx workers. If the parent reaches its
    # deadline while this worker is queued or running, the worker remains independently
    # observable through the mutex and finishes without holding the MiniApps UI open.
    try { $acquired = $gate.WaitOne() }
    catch [Threading.AbandonedMutexException] { $acquired = $true }

    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $WingetPath
    $start.Arguments = 'uninstall --accept-source-agreements --disable-interactivity --id "' + $AppId.Replace('"', '\"') + '"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process) { throw "Unable to start WinGet for $AppId." }
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [Threading.Tasks.Task]::WaitAll(@($stdoutTask, $stderrTask))
        $exitCode = $process.ExitCode
        foreach ($line in @($stdoutTask.Result, $stderrTask.Result)) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { $output.Add($line.TrimEnd()) }
        }
        # WinGet result codes are logged for diagnostics. The caller verifies whether
        # the package is still installed, matching the upstream behavior.
        $success = $true
    }
    finally { $process.Dispose() }
}
catch {
    $errors.Add($_.Exception.ToString())
}
finally {
    $endedAt = [DateTimeOffset]::UtcNow
    $result = [ordered]@{
        SchemaVersion = 1
        WorkerPid = $PID
        AppId = $AppId
        StartedAtUtc = $startedAt
        EndedAtUtc = $endedAt
        Success = $success
        ExitCode = $exitCode
        Output = @($output)
        Errors = @($errors)
    }
    try {
        [IO.Directory]::CreateDirectory((Split-Path -Parent $ResultPath)) | Out-Null
        $temporary = $ResultPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporary -Encoding UTF8
        Move-Item -LiteralPath $temporary -Destination $ResultPath -Force
        if ($output.Count -gt 0 -or $errors.Count -gt 0) {
            [IO.Directory]::CreateDirectory((Split-Path -Parent $LogPath)) | Out-Null
            @($output) + @($errors) | Set-Content -LiteralPath $LogPath -Encoding UTF8
        }
    }
    finally {
        if ($acquired) { $gate.ReleaseMutex() }
        $gate.Dispose()
    }
}

if ($success) { exit 0 }
exit 1
