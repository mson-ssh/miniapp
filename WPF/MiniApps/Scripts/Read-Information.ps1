param([switch]$AsJson)

# Read-only, minimal collector for the net48 Information page.
# Do not load the standalone hardware inventory script or native helper assemblies.
$ErrorActionPreference = 'Stop'
$osName = $null
$manufacturer = $null
$serial = $null
$activated = $false
try {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem -Property Caption -OperationTimeoutSec 8
    $osName = [string]$os.Caption
} catch {}
try {
    $bios = Get-CimInstance -ClassName Win32_BIOS -Property SerialNumber -OperationTimeoutSec 8
    $serial = [string]$bios.SerialNumber
} catch {}
try {
    $system = Get-CimInstance -ClassName Win32_ComputerSystem -Property Manufacturer -OperationTimeoutSec 8
    $manufacturer = [string]$system.Manufacturer
} catch {}
try {
    $licenses = @(Get-CimInstance -ClassName SoftwareLicensingProduct -Property LicenseStatus -Filter "ApplicationId = '55c92734-d682-4d71-983e-d6ec3f16059f' and PartialProductKey is not null" -OperationTimeoutSec 8)
    $activated = @($licenses | Where-Object { $_.LicenseStatus -eq 1 }).Count -gt 0
} catch {}

# Missing/failed activation remains unconfirmed, never a claimed licence failure.
[pscustomobject]@{
    OS = $osName
    Hostname = [Environment]::MachineName
    Serial = $serial
    Manufacturer = $manufacturer
    IsActivated = $activated
    DateTime = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
} | ConvertTo-Json -Compress
