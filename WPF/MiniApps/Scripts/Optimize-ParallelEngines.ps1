function Invoke-MiniAppsParallelEngines {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][array]$Lanes,
        [int]$PollMilliseconds = 100
    )

    $states = New-Object Collections.Generic.List[object]
    $startError = $null
    $utf8 = New-Object Text.UTF8Encoding($false)

    function Complete-MiniAppsRead {
        param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][ValidateSet('Output', 'Error')]$Stream)

        $taskProperty = if ($Stream -eq 'Output') { 'OutputTask' } else { 'ErrorTask' }
        $eofProperty = if ($Stream -eq 'Output') { 'OutputEof' } else { 'ErrorEof' }
        $reader = if ($Stream -eq 'Output') { $State.Process.StandardOutput } else { $State.Process.StandardError }
        $path = if ($Stream -eq 'Output') { $State.StdoutPath } else { $State.StderrPath }
        $task = $State.$taskProperty
        if ($State.$eofProperty -or -not $task.IsCompleted) { return }

        try { $line = $task.GetAwaiter().GetResult() }
        catch {
            $State.$eofProperty = $true
            throw
        }
        if ($null -eq $line) {
            $State.$eofProperty = $true
            return
        }

        [IO.File]::AppendAllText($path, $line + [Environment]::NewLine, $utf8)
        if ($Stream -eq 'Output' -and -not [string]::IsNullOrWhiteSpace($line)) {
            [Console]::WriteLine($line)
        }
        $State.$taskProperty = $reader.ReadLineAsync()
    }

    try {
        foreach ($lane in $Lanes) {
            try {
                [IO.Directory]::CreateDirectory((Split-Path -Parent $lane.StdoutPath)) | Out-Null
                [IO.File]::WriteAllText($lane.StdoutPath, '', $utf8)
                [IO.File]::WriteAllText($lane.StderrPath, '', $utf8)

                $start = New-Object Diagnostics.ProcessStartInfo
                $start.FileName = if ($lane.FilePath) { [string]$lane.FilePath } else { Join-Path $PSHOME 'powershell.exe' }
                $start.Arguments = '-NoProfile -NonInteractive -OutputFormat Text -ExecutionPolicy Bypass -EncodedCommand ' + [string]$lane.EncodedCommand
                $start.WorkingDirectory = Split-Path -Parent $lane.StdoutPath
                $start.UseShellExecute = $false
                $start.CreateNoWindow = $true
                $start.RedirectStandardOutput = $true
                $start.RedirectStandardError = $true
                $process = New-Object Diagnostics.Process
                $process.StartInfo = $start
                if (-not $process.Start()) { throw "Could not start Optimize lane '$($lane.Name)'." }

                $states.Add([pscustomobject]@{
                    Name = [string]$lane.Name
                    Process = $process
                    StdoutPath = [string]$lane.StdoutPath
                    StderrPath = [string]$lane.StderrPath
                    OutputTask = $process.StandardOutput.ReadLineAsync()
                    ErrorTask = $process.StandardError.ReadLineAsync()
                    OutputEof = $false
                    ErrorEof = $false
                })
            }
            catch {
                $startError = $_
                break
            }
        }

        # Keep draining every process that did start. This prevents redirected pipes from
        # filling up and guarantees that a later lane start failure cannot orphan an earlier lane.
        do {
            foreach ($state in $states) {
                Complete-MiniAppsRead -State $state -Stream Output
                Complete-MiniAppsRead -State $state -Stream Error
            }
            $active = @($states | Where-Object {
                -not $_.Process.HasExited -or -not $_.OutputEof -or -not $_.ErrorEof
            }).Count
            if ($active -gt 0) { Start-Sleep -Milliseconds $PollMilliseconds }
        } while ($active -gt 0)

        if ($startError) { throw $startError }

        $exitCodes = @{}
        foreach ($state in $states) {
            $state.Process.WaitForExit()
            $laneName = [string]$state.Name
            $exitCodes[$laneName] = [int]$state.Process.ExitCode
        }
        return $exitCodes
    }
    finally {
        foreach ($state in $states) {
            # A handle is disposed only after the corresponding process has ended.
            if ($state.Process.HasExited) { $state.Process.Dispose() }
        }
    }
}
