$ErrorActionPreference = 'Stop'
$collector = Join-Path $PSScriptRoot 'MiniApps\Scripts\Read-Information.ps1'
$fixtureState = @{ Calls = [System.Collections.Generic.List[string]]::new(); FailQueries = $false }
function Get-CimInstance {
    param($ClassName, $Property, $Filter, $OperationTimeoutSec)
    $fixtureState.Calls.Add($ClassName)
    if ($OperationTimeoutSec -ne 8) { throw 'Missing query timeout' }
    if ($fixtureState.FailQueries) { throw 'Fixture provider unavailable' }
    switch ($ClassName) {
        'Win32_OperatingSystem' {
            if ($Property -ne 'Caption') { throw 'Unexpected OS fields' }
            [pscustomobject]@{ Caption = 'Microsoft Windows 11 Pro' }
        }
        'Win32_BIOS' {
            if ($Property -ne 'SerialNumber') { throw 'Unexpected BIOS fields' }
            [pscustomobject]@{ SerialNumber = 'FIXTURE-SERIAL' }
        }
        'Win32_ComputerSystem' {
            if ($Property -ne 'Manufacturer') { throw 'Unexpected machine fields' }
            [pscustomobject]@{ Manufacturer = 'Dell Inc.' }
        }
        'SoftwareLicensingProduct' {
            if ($Property -ne 'LicenseStatus' -or $Filter -notmatch '55c92734-d682-4d71-983e-d6ec3f16059f' -or $Filter -notmatch 'PartialProductKey is not null') { throw 'Unscoped licence query' }
            [pscustomobject]@{ LicenseStatus = 0 }
            [pscustomobject]@{ LicenseStatus = 1 }
        }
        default { throw "Unexpected inventory query: $ClassName" }
    }
}
$data = & $collector -AsJson | ConvertFrom-Json
if ($data.OS -ne 'Microsoft Windows 11 Pro' -or $data.Serial -ne 'FIXTURE-SERIAL' -or $data.Manufacturer -ne 'Dell Inc.' -or -not $data.IsActivated) { throw 'Incorrect minimal report' }
if (($fixtureState.Calls -join ',') -ne 'Win32_OperatingSystem,Win32_BIOS,Win32_ComputerSystem,SoftwareLicensingProduct') { throw 'Unexpected queries' }
if (($data.PSObject.Properties.Name | Sort-Object) -join ',' -ne 'DateTime,Hostname,IsActivated,Manufacturer,OS,Serial') { throw 'Unexpected report fields' }
'PASS minimal collector: only four scoped queries, any active Windows licence is accepted'
$fixtureState.FailQueries = $true
$data = & $collector -AsJson | ConvertFrom-Json
if ($data.IsActivated -or $data.OS -or $data.Serial -or $data.Manufacturer -or -not $data.Hostname) { throw 'Missing data must stay unknown' }
'PASS unavailable providers: HOST preserved, missing data and activation remain unknown'
