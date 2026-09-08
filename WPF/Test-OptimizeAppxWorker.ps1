# Exercises the Appx worker with a package pattern that cannot match a real package.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('MiniApps-AppxWorkerFixture-' + [Guid]::NewGuid().ToString('N'))
$resultPath = Join-Path $fixture 'result.json'
$logPath = Join-Path $fixture 'worker.log'
$workerPath = Join-Path $PSScriptRoot 'MiniApps\Scripts\Invoke-MiniAppsAppxWorker.ps1'
$pattern = '*MiniApps.Nonexistent.Package.Fixture*'

function Quote-PowerShellLiteral([string]$Value) { "'" + $Value.Replace("'", "''") + "'" }

try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    $command = '& {0} -AppPattern {1} -TargetUser CurrentUser -ResultPath {2} -LogPath {3}' -f `
        (Quote-PowerShellLiteral $workerPath), (Quote-PowerShellLiteral $pattern), `
        (Quote-PowerShellLiteral $resultPath), (Quote-PowerShellLiteral $logPath)
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') `
        -ArgumentList @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded) `
        -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Safe no-match worker failed with exit code $($process.ExitCode)." }
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw 'Worker did not write its structured result.' }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($result.SchemaVersion -ne 1 -or -not $result.Success -or $result.AppPattern -ne $pattern -or
        $result.TargetUser -ne 'CurrentUser' -or @($result.Errors).Count -ne 0 -or $result.WorkerPid -le 0) {
        throw 'Worker result did not preserve the expected no-match contract.'
    }

    $gate = New-Object Threading.Mutex($false, 'Global\MiniApps.DebloatAppxWorker')
    $acquired = $false
    try {
        try { $acquired = $gate.WaitOne(0) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Worker did not release its process-wide Appx mutex.' }
    }
    finally {
        if ($acquired) { $gate.ReleaseMutex() }
        $gate.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath -Force }
    if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath -Force }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture }
}

Write-Output 'PASS Appx worker writes a structured no-match result and releases its mutex. No system changes.'
