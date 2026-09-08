# Verifies that the two Optimize engine lanes overlap. Child commands only write timestamps.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MiniApps\Scripts\Optimize-ParallelEngines.ps1')

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('MiniApps-ParallelEngineFixture-' + [Guid]::NewGuid().ToString('N'))
$capture = New-Object IO.StringWriter
$originalOut = [Console]::Out
try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    $lanes = foreach ($name in @('Features', 'RemoveApps')) {
        $startPath = Join-Path $fixture ($name + '.start')
        $endPath = Join-Path $fixture ($name + '.end')
        $command = "[IO.File]::WriteAllText('$($startPath.Replace("'", "''"))',[DateTime]::UtcNow.Ticks); [Console]::WriteLine('LANE:$name'); Start-Sleep -Seconds 1; [IO.File]::WriteAllText('$($endPath.Replace("'", "''"))',[DateTime]::UtcNow.Ticks); exit 0"
        @{
            Name = $name
            EncodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
            StdoutPath = Join-Path $fixture ($name + '.stdout')
            StderrPath = Join-Path $fixture ($name + '.stderr')
        }
    }
    [Console]::SetOut($capture)
    $codes = Invoke-MiniAppsParallelEngines -Lanes $lanes -PollMilliseconds 30
    [Console]::SetOut($originalOut)
    if ($codes.Features -ne 0 -or $codes.RemoveApps -ne 0) {
        $details = @($lanes | ForEach-Object { "[$($_.Name)] " + (Get-Content -LiteralPath $_.StderrPath -Raw) }) -join ' '
        $shape = @($codes | ForEach-Object { $_.GetType().FullName + ':' + ($_ | Out-String).Trim() }) -join ' | '
        throw "A harmless fixture lane failed: Features=$($codes.Features), RemoveApps=$($codes.RemoveApps). Shape=$shape. $details"
    }
    $featureStart = [long](Get-Content -LiteralPath (Join-Path $fixture 'Features.start') -Raw)
    $featureEnd = [long](Get-Content -LiteralPath (Join-Path $fixture 'Features.end') -Raw)
    $removeStart = [long](Get-Content -LiteralPath (Join-Path $fixture 'RemoveApps.start') -Raw)
    $removeEnd = [long](Get-Content -LiteralPath (Join-Path $fixture 'RemoveApps.end') -Raw)
    if (-not ($featureStart -lt $removeEnd -and $removeStart -lt $featureEnd)) { throw 'Optimize lanes did not overlap.' }
    $output = $capture.ToString()
    if (-not ($output.Contains('LANE:Features') -and $output.Contains('LANE:RemoveApps'))) { throw 'Parallel lane output was not relayed.' }

    # A protocol record can arrive in several writes. It must be relayed once, only after
    # ReadLineAsync has received the complete record. The real process exit code must survive.
    $capture.GetStringBuilder().Clear() | Out-Null
    [Console]::SetOut($capture)
    $protocol = 'MINIAPPS_TASK_JSON:{"event":"DONE","id":"RemoveApps","message":"fixture"}'
    $partialCommand = "[Console]::Write('MINIAPPS_TASK_JSON:'); [Console]::Out.Flush(); Start-Sleep -Milliseconds 250; [Console]::WriteLine('{`"event`":`"DONE`",`"id`":`"RemoveApps`",`"message`":`"fixture`"}'); [Console]::Write('FINAL-FRAGMENT'); exit 7"
    $partialLane = @{
        Name = 'Failure'
        EncodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($partialCommand))
        StdoutPath = Join-Path $fixture 'Failure.stdout'
        StderrPath = Join-Path $fixture 'Failure.stderr'
    }
    $failureCode = Invoke-MiniAppsParallelEngines -Lanes @($partialLane) -PollMilliseconds 20
    [Console]::SetOut($originalOut)
    if ($failureCode.Failure -ne 7) { throw "Lane exit code was lost: expected 7, actual $($failureCode.Failure)." }
    $relayed = $capture.ToString()
    if (($relayed.Split(@($protocol), [StringSplitOptions]::None).Count - 1) -ne 1) { throw 'A partial protocol record was lost, duplicated, or emitted before completion.' }
    if (-not $relayed.Contains('FINAL-FRAGMENT')) { throw 'A final stdout fragment without a newline was not relayed.' }
    if ($relayed -match "MINIAPPS_TASK_JSON:\s*(\r?\n|$)") { throw 'A bare protocol prefix was emitted.' }

    # If a later lane cannot start, wait for every lane that already started before throwing.
    $startFailureMarker = Join-Path $fixture 'start-failure-first-lane-ended'
    $firstCommand = "Start-Sleep -Milliseconds 250; [IO.File]::WriteAllText('$($startFailureMarker.Replace("'", "''"))','ended'); exit 0"
    $startFailureLanes = @(
        @{ Name = 'First'; EncodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($firstCommand)); StdoutPath = (Join-Path $fixture 'First.stdout'); StderrPath = (Join-Path $fixture 'First.stderr') },
        @{ Name = 'CannotStart'; FilePath = (Join-Path $fixture 'missing-powershell.exe'); EncodedCommand = ''; StdoutPath = (Join-Path $fixture 'CannotStart.stdout'); StderrPath = (Join-Path $fixture 'CannotStart.stderr') }
    )
    $startFailed = $false
    try { $null = Invoke-MiniAppsParallelEngines -Lanes $startFailureLanes -PollMilliseconds 20 }
    catch { $startFailed = $true }
    if (-not $startFailed) { throw 'An invalid second lane executable did not fail.' }
    if (-not (Test-Path -LiteralPath $startFailureMarker)) { throw 'The first lane was orphaned when the second lane failed to start.' }
}
finally {
    [Console]::SetOut($originalOut)
    $capture.Dispose()
    if (Test-Path -LiteralPath $fixture) {
        Get-ChildItem -LiteralPath $fixture -File | Remove-Item -Force
        Remove-Item -LiteralPath $fixture
    }
}

Write-Output 'PASS Optimize parallel lanes overlap, preserve exit codes and complete protocol lines, and await earlier lanes after a start failure. No system changes.'
