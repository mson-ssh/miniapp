# Exercises the external worker monitor. The fixture process only sleeps.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MiniApps\Scripts\Optimize-WorkerMonitor.ps1')

$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $PSHOME 'powershell.exe'
$start.Arguments = '-NoProfile -NonInteractive -Command "Start-Sleep -Seconds 20"'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$worker = [Diagnostics.Process]::Start($start)
$watch = [Diagnostics.Stopwatch]::StartNew()
$overdueCalled = $false
try {
    $completed = Wait-MiniAppsOptimizeWorker -Process $worker -TimeoutSeconds 1 -OnOverdue { $script:overdueCalled = $true }
    if ($completed) { throw 'The external worker unexpectedly completed before its deadline.' }
    if (-not $overdueCalled) { throw 'The monitor did not report the overdue worker.' }
    if ($worker.HasExited) { throw 'The monitor terminated the worker instead of detaching safely.' }
    if ($watch.Elapsed.TotalSeconds -gt 5) { throw "The worker monitor blocked too long: $($watch.Elapsed)." }
}
finally {
    $watch.Stop()
    if (-not $worker.HasExited) { $worker.Kill(); $worker.WaitForExit() }
    $worker.Dispose()
}

Write-Output 'PASS Optimize monitor reports and detaches from an overdue external worker without stopping it. No system changes.'
