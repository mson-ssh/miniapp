# Checks MiniApps/Scripts/Split-Disk.ps1 against the CLI's partitioning rules.
# Every disk, partition, BitLocker and power command is replaced by a fake below, so nothing
# on this machine is read or changed. Functions win over cmdlets and executables, and the
# script sees the ones defined here.
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'MiniApps\Scripts\Split-Disk.ps1'
$GB = 1GB

function Get-Partition { [CmdletBinding()] param([string]$DriveLetter, [int]$DiskNumber, [int]$PartitionNumber)
    if ($DriveLetter -eq 'C') { return [pscustomobject]@{ DiskNumber = 0; Size = $state.C; DriveLetter = 'C' } }
    if ($DriveLetter -eq 'D') { if ($state.HasD) { return [pscustomobject]@{ DriveLetter = 'D' } } else { return } }
    if ($DriveLetter -eq 'E') { if ($state.HasE) { return [pscustomobject]@{ DriveLetter = 'E' } } else { return } }
    [pscustomobject]@{ PartitionNumber = $PartitionNumber }
}
function Get-Disk { [CmdletBinding()] param([int]$Number) [pscustomobject]@{ Number = 0; Size = $state.Disk } }
function Get-Volume { [CmdletBinding()] param([Parameter(ValueFromPipeline)]$InputObject) process { [pscustomobject]@{ FileSystem = 'NTFS' } } }
function Get-PartitionSupportedSize { [CmdletBinding()] param([string]$DriveLetter) [pscustomobject]@{ SizeMin = $state.MinC; SizeMax = $state.C } }
function Get-BitLockerVolume { [CmdletBinding()] param($MountPoint) [pscustomobject]@{ VolumeStatus = $state.BitLocker } }
function Disable-BitLocker { [CmdletBinding()] param($MountPoint) $state.Calls.Add('bitlocker off'); $state.BitLocker = 'FullyDecrypted' }
function manage-bde { $state.Calls.Add("manage-bde $args") }
function powercfg { $state.Calls.Add("powercfg $args") }
function Start-Sleep { [CmdletBinding()] param([int]$Seconds) }
function Update-HostStorageCache { [CmdletBinding()] param() }
function Set-Volume { [CmdletBinding()] param([string]$DriveLetter, [string]$NewFileSystemLabel) $state.Calls.Add("label ${DriveLetter}=$NewFileSystemLabel") }
function Resize-Partition { [CmdletBinding()] param([string]$DriveLetter, [double]$Size) $state.Calls.Add(('resize {0}={1:N1}' -f $DriveLetter, ($Size / 1GB))) }
function New-Partition { [CmdletBinding()] param([int]$DiskNumber, [double]$Size, [switch]$UseMaximumSize, [switch]$AssignDriveLetter)
    $state.Next++
    $state.Calls.Add($(if ($UseMaximumSize) { "new $($state.Next) max" } else { 'new {0} {1:N1}' -f $state.Next, ($Size / 1GB) }))
    [pscustomobject]@{ PartitionNumber = $state.Next; DriveLetter = 'Z' }
}
function Format-Volume { [CmdletBinding()] param($Partition, [string]$FileSystem, [string]$NewFileSystemLabel, [switch]$Force, [bool]$Confirm)
    if ($state.FormatFails) { throw 'fixture: disk not ready' }
    $state.Calls.Add("format $($Partition.PartitionNumber) $FileSystem '$NewFileSystemLabel'")
}
function Set-Partition { [CmdletBinding()] param([int]$DiskNumber, [int]$PartitionNumber, [string]$NewDriveLetter) $state.Calls.Add("letter $PartitionNumber=$NewDriveLetter") }

function Invoke-Case([string]$Name, [hashtable]$Setup, [switch]$DryRun) {
    $script:state = @{ Disk = 0; C = 0; MinC = 20 * $GB; HasD = $false; HasE = $false; BitLocker = 'FullyDecrypted'; FormatFails = $false; Next = 1; Calls = New-Object System.Collections.Generic.List[string] }
    foreach ($key in $Setup.Keys) { $state[$key] = $Setup[$key] }
    $output = @(); $failed = $null
    # MiniApps runs settings under 'Stop'; the case does the same.
    try { $output = @(& $script -DryRun:$DryRun 2>&1 | ForEach-Object { "$_" }) } catch { $failed = $_.Exception.Message }
    [pscustomobject]@{ Name = $Name; Output = $output -join ' | '; Failed = $failed; Calls = @($state.Calls) }
}
function Assert-Case($Result, [bool]$ShouldFail, [string]$Expect, [string[]]$Calls) {
    $text = if ($Result.Failed) { $Result.Failed } else { $Result.Output }
    if ([bool]$Result.Failed -ne $ShouldFail) { throw "$($Result.Name): expected failed=$ShouldFail, got '$text'" }
    if ($text -notmatch [regex]::Escape($Expect)) { throw "$($Result.Name): expected '$Expect' in '$text'" }
    if ($null -ne $Calls -and (@($Result.Calls) -join '; ') -ne ($Calls -join '; ')) { throw "$($Result.Name): calls were '$(@($Result.Calls) -join '; ')', expected '$($Calls -join '; ')'" }
    Write-Host "PASS $($Result.Name)"
}

# Skips required by the rules: normal end, nothing touched.
Assert-Case (Invoke-Case 'over 1100 GB is never split' @{ Disk = 1863 * $GB; C = 1860 * $GB }) $false 'vượt giới hạn an toàn 1100 GB' @()
Assert-Case (Invoke-Case 'a size outside the classes is left alone' @{ Disk = 700 * $GB; C = 690 * $GB }) $false 'không thuộc nhóm' @()
Assert-Case (Invoke-Case 'an existing D: stops the split' @{ Disk = 476.9 * $GB; C = 470 * $GB; HasD = $true }) $false 'đã có ổ D: hoặc E:' @()
Assert-Case (Invoke-Case 'an existing E: stops the split' @{ Disk = 953.9 * $GB; C = 950 * $GB; HasE = $true }) $false 'đã có ổ D: hoặc E:' @()

# Stops caused by a problem: the setting must fail, and the disk must be untouched.
Assert-Case (Invoke-Case 'C: may not drop to 30 GB' @{ Disk = 238.5 * $GB; C = 80 * $GB }) $true 'dưới mức tối thiểu 30 GB' @()
Assert-Case (Invoke-Case 'a shrink Windows cannot do stops before the disk changes' @{ Disk = 476.9 * $GB; C = 470 * $GB; MinC = 300 * $GB }) $true 'chỉ thu nhỏ được C: tới 300' @('label C=OS', 'powercfg /h off')
Assert-Case (Invoke-Case 'a format that never succeeds fails the setting' @{ Disk = 238.5 * $GB; C = 237 * $GB; FormatFails = $true }) $true 'Không format được D:' $null

# The plan alone, without changes.
Assert-Case (Invoke-Case 'dry run prints the plan and changes nothing' @{ Disk = 953.9 * $GB; C = 950 * $GB } -DryRun) $false 'D: 400.1 GB, E: 200.1 GB' @()

# The three classes, split exactly as the CLI does.
Assert-Case (Invoke-Case '256 GB class: D: 50.1 GB' @{ Disk = 238.5 * $GB; C = 237 * $GB }) $false 'Chia ổ xong' @(
    'label C=OS', 'powercfg /h off', 'resize C=186.9', 'new 2 max', "format 2 NTFS 'LOCAL I'", 'letter 2=D')
Assert-Case (Invoke-Case '512 GB class: D: 200.1 GB' @{ Disk = 476.9 * $GB; C = 475 * $GB }) $false 'Chia ổ xong' @(
    'label C=OS', 'powercfg /h off', 'resize C=274.9', 'new 2 max', "format 2 NTFS 'LOCAL I'", 'letter 2=D')
Assert-Case (Invoke-Case '1 TB class: D: 400.1 GB then E: 200.1 GB' @{ Disk = 953.9 * $GB; C = 950 * $GB }) $false 'Chia ổ xong' @(
    'label C=OS', 'powercfg /h off', 'resize C=349.8', 'new 2 400.1', "format 2 NTFS 'LOCAL I'", 'letter 2=D', 'new 3 max', "format 3 NTFS 'LOCAL II'", 'letter 3=E')
Assert-Case (Invoke-Case 'BitLocker is decrypted before the shrink' @{ Disk = 238.5 * $GB; C = 237 * $GB; BitLocker = 'FullyEncrypted' }) $false 'Đã tắt BitLocker' @(
    'label C=OS', 'bitlocker off', 'manage-bde -off C:', 'powercfg /h off', 'resize C=186.9', 'new 2 max', "format 2 NTFS 'LOCAL I'", 'letter 2=D')
Write-Host 'PASS Split-Disk follows the CLI partitioning rules.'
