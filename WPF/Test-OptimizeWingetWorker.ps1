# Runs the WinGet worker with powershell.exe as a harmless stand-in executable.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('MiniApps-WingetWorkerFixture-' + [Guid]::NewGuid().ToString('N'))
$resultPath = Join-Path $fixture 'result.json'
$logPath = Join-Path $fixture 'worker.log'
$workerPath = Join-Path $PSScriptRoot 'MiniApps\Scripts\Invoke-MiniAppsWingetWorker.ps1'
function Quote-PowerShellLiteral([string]$Value) { "'" + $Value.Replace("'", "''") + "'" }
try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    $command = '& {0} -AppId {1} -ResultPath {2} -LogPath {3} -WingetPath {4}' -f `
        (Quote-PowerShellLiteral $workerPath), (Quote-PowerShellLiteral 'MiniApps.HarmlessFixture'), `
        (Quote-PowerShellLiteral $resultPath), (Quote-PowerShellLiteral $logPath), `
        (Quote-PowerShellLiteral (Join-Path $PSHOME 'powershell.exe'))
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') `
        -ArgumentList @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded) `
        -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "WinGet worker fixture exited with $($process.ExitCode)." }
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw 'WinGet worker did not write its result.' }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if (-not $result.Success -or $result.AppId -ne 'MiniApps.HarmlessFixture' -or $null -eq $result.ExitCode) {
        throw 'WinGet worker result is incomplete.'
    }
    $gate = New-Object Threading.Mutex($false, 'Global\MiniApps.DebloatAppxWorker')
    $acquired = $false
    try {
        try { $acquired = $gate.WaitOne(0) } catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'WinGet worker did not release the package mutex.' }
    }
    finally {
        if ($acquired) { $gate.ReleaseMutex() }
        $gate.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

Write-Output 'PASS WinGet worker records the child exit and releases its package mutex. No package was changed.'
