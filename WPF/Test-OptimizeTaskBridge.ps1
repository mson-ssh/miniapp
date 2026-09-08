# Tests only the reporting bridge with fake features. Never executes Win11Debloat.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('MiniApps-bridge-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$originalOut = [Console]::Out
$capture = New-Object IO.StringWriter
try {
    $script:DefaultSettingsFilePath = Join-Path $fixture 'DefaultSettings.json'
    @{ Settings = @(@{ Name = 'Good'; Value = $true }, @{ Name = 'Bad'; Value = $true }, @{ Name = 'Unsupported'; Value = $true }, @{ Name = 'Throws'; Value = $true }) } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $script:DefaultSettingsFilePath -Encoding UTF8
    $script:Params = @{ Good = $true; Bad = $true; RemoveApps = $true; Throws = $true }
    $script:AppRemovalFailures = 0
    $script:AppRemovalVerificationUnavailable = $false
    $script:FixtureOverdue = $false
    $script:MiniAppsWorkerOverdue = $false
    function Invoke-FeatureApply {
        param([string]$FeatureId)
        if ($FeatureId -eq 'Throws') { throw 'fixture failure' }
        if ($FeatureId -eq 'RemoveApps' -and $script:FixtureOverdue) {
            $script:MiniAppsWorkerOverdue = $true
            Write-MiniAppsTask 'OVERDUE' 'RemoveApps' 'fixture worker remains active'
            throw 'fixture overdue'
        }
        return ($FeatureId -ne 'Bad')
    }
    [Console]::SetOut($capture)
    . (Join-Path $PSScriptRoot 'MiniApps\Scripts\Optimize-TaskBridge.ps1')
    $good = Invoke-FeatureApply Good
    $bad = Invoke-FeatureApply Bad
    $script:AppRemovalVerificationUnavailable = $true
    $unknown = Invoke-FeatureApply RemoveApps
    $script:AppRemovalVerificationUnavailable = $false
    $script:FixtureOverdue = $true
    $overdueThrew = $false
    try { Invoke-FeatureApply RemoveApps | Out-Null } catch { $overdueThrew = $true }
    $script:MiniAppsWorkerOverdue = $false
    $script:FixtureOverdue = $false
    $threw = $false
    try { Invoke-FeatureApply Throws | Out-Null } catch { $threw = $true }
    if ($good -isnot [bool] -or -not $good -or $bad -isnot [bool] -or $bad -or -not $unknown -or -not $overdueThrew -or -not $threw) { throw 'Bridge changed return/exception semantics.' }
    $events = @($capture.ToString() -split '\r?\n' | Where-Object { $_ } | ForEach-Object {
        if (-not $_.StartsWith('MINIAPPS_TASK_JSON:')) { throw 'Unexpected bridge output' }
        $_.Substring('MINIAPPS_TASK_JSON:'.Length) | ConvertFrom-Json
    })
    foreach ($case in @(@('Good','DONE'), @('Bad','ERROR'), @('RemoveApps','ERROR'), @('Throws','ERROR'), @('Unsupported','SKIP'))) {
        if (@($events | Where-Object { $_.id -eq $case[0] -and $_.event -eq $case[1] }).Count -ne 1) { throw "Wrong terminal signal: $($case[0])" }
    }
    if (@($events | Where-Object { $_.id -eq 'RemoveApps' -and $_.event -eq 'OVERDUE' }).Count -ne 1) { throw 'Missing overdue signal.' }
    if (@($events | Where-Object { $_.id -eq 'RemoveApps' -and $_.event -eq 'ERROR' }).Count -ne 1) { throw 'Overdue signal was overwritten by another error.' }
    if (@($events | Where-Object { $_.event -eq 'START' }).Count -ne 5) { throw 'Missing start signals' }
}
finally {
    [Console]::SetOut($originalOut)
    $capture.Dispose()
    # Only files created by this fixture; no recursive removal of a computed directory.
    $file = Join-Path $fixture 'DefaultSettings.json'
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture }
}
Write-Output 'PASS Optimize bridge: return values, errors, overdue preservation, unknown verification, skip and start signals. No system changes.'
