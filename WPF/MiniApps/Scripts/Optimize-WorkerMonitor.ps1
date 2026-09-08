function Wait-MiniAppsOptimizeWorker {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [Parameter(Mandatory)][ValidateRange(1, 1800)][int]$TimeoutSeconds,
        [scriptblock]$OnOverdue
    )

    if ($Process.WaitForExit($TimeoutSeconds * 1000)) { return $true }
    if ($OnOverdue) { & $OnOverdue }
    return $false
}
