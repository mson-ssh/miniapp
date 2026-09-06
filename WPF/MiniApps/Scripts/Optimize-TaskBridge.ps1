# Loaded into the pinned upstream script immediately before Invoke-AllChanges.
# Console output must not enter the success pipeline consumed as a feature Boolean.
function Write-MiniAppsTask {
    param([string]$Event, [string]$Id, [string]$Message)
    [Console]::WriteLine('MINIAPPS_TASK_JSON:' + (@{ event = $Event; id = $Id; message = $Message } | ConvertTo-Json -Compress))
}
$script:MiniAppsFeatureApply = ${function:Invoke-FeatureApply}
$script:MiniAppsExpectedIds = @('RemoveApps') + @(
    (Get-Content -LiteralPath $script:DefaultSettingsFilePath -Raw | ConvertFrom-Json).Settings |
        Where-Object { $_.Name -ne 'CreateRestorePoint' -and $_.Value -eq $true } |
        ForEach-Object { $_.Name }
)
foreach ($id in $script:Params.Keys) {
    if ($script:MiniAppsExpectedIds -contains $id) { Write-MiniAppsTask 'QUEUED' $id '' }
}
foreach ($id in $script:MiniAppsExpectedIds) {
    if (-not $script:Params.ContainsKey($id)) {
        Write-MiniAppsTask 'SKIP' $id 'Not selected by upstream compatibility/profile filtering.'
    }
}
function Invoke-FeatureApply {
    param([Parameter(Mandatory)][string]$FeatureId)
    Write-MiniAppsTask 'START' $FeatureId ''
    $failuresBefore = $script:AppRemovalFailures
    try {
        $result = & $script:MiniAppsFeatureApply -FeatureId $FeatureId
        $successful = ($result -is [bool]) -and $result -and ($script:AppRemovalFailures -eq $failuresBefore)
        if ($script:AppRemovalVerificationUnavailable -and $FeatureId -in @('RemoveApps', 'DisableBing', 'DisableCopilot', 'DisableWidgets')) {
            $successful = $false
        }
        if ($successful) { Write-MiniAppsTask 'DONE' $FeatureId '' }
        else { Write-MiniAppsTask 'ERROR' $FeatureId 'Upstream failed or could not verify this operation. See log.' }
        # Preserve upstream return semantics and failure accounting.
        return $result
    }
    catch {
        Write-MiniAppsTask 'ERROR' $FeatureId $_.Exception.Message
        throw
    }
}
