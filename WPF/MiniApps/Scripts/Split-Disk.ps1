# MiniApps Windows setting: split the system disk into C:, D: (and E:), following the
# rules of the CLI's $DiskScript in Setup.ps1 (formerly config/disk.ps1):
#
#   - C: is relabelled "OS" (label only, no formatting).
#   - BitLocker on C: is decrypted first; it pins files at the end of the volume and blocks
#     shrinking. The wait is capped at 60 minutes.
#   - The disk holding C: is classed by size and split with .1 GB padding, so This PC shows
#     round numbers after NTFS overhead:
#         200-300 GB   -> D: "LOCAL I" 50.1 GB
#         400-600 GB   -> D: "LOCAL I" 200.1 GB
#         800-1100 GB  -> D: "LOCAL I" 400.1 GB and E: "LOCAL II" 200.1 GB
#   - Safety interlocks: nothing is done on a disk over 1100 GB, on a size outside those
#     classes, or when D: or E: already exists; the run stops if C: would drop to 30 GB or less.
#   - Hibernation is turned off first: hiberfil.sys cannot be moved and blocks shrinking.
#
# Outcome: a skip required by those rules ends normally; a stop caused by a problem, or any
# failure, ends with an error so the Windows Setting row shows it failed.
param(
    # Print the plan for this machine without changing anything.
    [switch]$DryRun
)

# MiniApps runs settings under 'Stop'. Critical cmdlets opt in with -ErrorAction Stop; native
# tools such as manage-bde must not be able to end the run by writing to stderr.
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$LabelC = 'OS'
$LabelD = 'LOCAL I'
$LabelE = 'LOCAL II'
$MinimumC = 30GB

$osPartition = Get-Partition -DriveLetter C -ErrorAction Stop
$osDisk = Get-Disk -Number $osPartition.DiskNumber -ErrorAction Stop
$totalGB = [Math]::Round($osDisk.Size / 1GB, 1)

if ($totalGB -gt 1100) { Write-Output "Bỏ qua: ổ $totalGB GB, vượt giới hạn an toàn 1100 GB."; return }
if ($totalGB -ge 200 -and $totalGB -le 300) { $plan = @{ SizeD = 50.1GB; SizeE = 0; HasE = $false }; $planName = 'C: phần còn lại, D: 50.1 GB' }
elseif ($totalGB -ge 400 -and $totalGB -le 600) { $plan = @{ SizeD = 200.1GB; SizeE = 0; HasE = $false }; $planName = 'C: phần còn lại, D: 200.1 GB' }
elseif ($totalGB -ge 800 -and $totalGB -le 1100) { $plan = @{ SizeD = 400.1GB; SizeE = 200.1GB; HasE = $true }; $planName = 'C: phần còn lại, D: 400.1 GB, E: 200.1 GB' }
else { Write-Output "Bỏ qua: ổ $totalGB GB không thuộc nhóm 256 GB / 512 GB / 1 TB."; return }

# Never re-partition a machine that already has D: or E:.
if ((Get-Partition -DriveLetter D -ErrorAction SilentlyContinue) -or (Get-Partition -DriveLetter E -ErrorAction SilentlyContinue)) {
    Write-Output 'Bỏ qua: máy đã có ổ D: hoặc E:, không chia lại.'
    return
}

$targetC = $osPartition.Size - ($plan.SizeD + $plan.SizeE)
if ($targetC -le $MinimumC) { throw "Dừng: C: sẽ còn $([Math]::Round($targetC / 1GB, 1)) GB, dưới mức tối thiểu 30 GB. Không thay đổi gì." }
Write-Output "Ổ hệ thống $totalGB GB. Kế hoạch: $planName."
if ($DryRun) { Write-Output "DRY RUN: C: sẽ còn $([Math]::Round($targetC / 1GB, 1)) GB; không thay đổi gì."; return }

# STEP 1: relabel C: (label only; a failure here does not stop the split).
try { Set-Volume -DriveLetter C -NewFileSystemLabel $LabelC -ErrorAction Stop; Write-Output "Đổi tên C: thành '$LabelC'." }
catch { Write-Output "Không đổi được tên C: ($($_.Exception.Message))." }

# STEP 2: BitLocker off, with a bounded wait.
if (Get-Command Get-BitLockerVolume -ErrorAction SilentlyContinue) {
    $bde = Get-BitLockerVolume -MountPoint C: -ErrorAction SilentlyContinue
    if ($bde -and $bde.VolumeStatus -ne 'FullyDecrypted') {
        Write-Output 'BitLocker đang bật trên C:, đang giải mã (có thể lâu)...'
        Disable-BitLocker -MountPoint C: -ErrorAction SilentlyContinue | Out-Null
        & manage-bde -off C: 2>&1 | Out-Null
        $deadline = (Get-Date).AddMinutes(60)
        do {
            $status = (Get-BitLockerVolume -MountPoint C: -ErrorAction SilentlyContinue).VolumeStatus
            if (-not $status -or $status -eq 'FullyDecrypted') { break }
            Start-Sleep -Seconds 5
        } while ((Get-Date) -lt $deadline)
        if ($status -and $status -ne 'FullyDecrypted') { throw 'Dừng: BitLocker vẫn đang giải mã sau 60 phút; chưa chia ổ.' }
        Write-Output 'Đã tắt BitLocker trên C:.'
    }
}

# STEP 3: shrink C:. Hibernation goes first; Windows is then asked how far C: can shrink,
# so an impossible target stops here before anything on the disk changes.
& powercfg /h off 2>&1 | Out-Null
$supported = Get-PartitionSupportedSize -DriveLetter C -ErrorAction Stop
if ($targetC -lt $supported.SizeMin) {
    throw "Dừng: Windows chỉ thu nhỏ được C: tới $([Math]::Round($supported.SizeMin / 1GB, 1)) GB (cần $([Math]::Round($targetC / 1GB, 1)) GB). Chưa chia ổ; đã tắt Hibernate để thử."
}
Resize-Partition -DriveLetter C -Size $targetC -ErrorAction Stop
Write-Output "Đã thu nhỏ C: còn $([Math]::Round($targetC / 1GB, 1)) GB."

function New-DataVolume([UInt64]$Size, [string]$Letter, [string]$Label) {
    # A fixed size when another partition follows; otherwise all of the freed space.
    $partition = if ($Size -gt 0) {
        New-Partition -DiskNumber $osDisk.Number -Size $Size -AssignDriveLetter -ErrorAction Stop
    } else {
        New-Partition -DiskNumber $osDisk.Number -UseMaximumSize -AssignDriveLetter -ErrorAction Stop
    }
    Start-Sleep -Seconds 3
    Update-HostStorageCache
    $lastError = 'chưa thử lần nào'
    for ($i = 0; $i -lt 10; $i++) {
        try {
            # Format-Volume has no -Quick switch (quick is the default): passing one fails at
            # binding and leaves the volume RAW. It is bound by partition because a new RAW
            # partition is not always visible to Get-Volume yet.
            $current = Get-Partition -DiskNumber $osDisk.Number -PartitionNumber $partition.PartitionNumber -ErrorAction Stop
            Format-Volume -Partition $current -FileSystem NTFS -NewFileSystemLabel $Label -Force -Confirm:$false -ErrorAction Stop | Out-Null
            Update-HostStorageCache
            $volume = Get-Partition -DiskNumber $osDisk.Number -PartitionNumber $partition.PartitionNumber | Get-Volume -ErrorAction Stop
            if ($volume.FileSystem -match 'NTFS') { break }
            $lastError = "ổ báo FileSystem='$($volume.FileSystem)'"
        }
        catch { $lastError = $_.Exception.Message; Start-Sleep -Seconds 3 }
        if ($i -eq 9) { throw "Không format được ${Letter}: - $lastError" }
    }
    if ($partition.DriveLetter -ne $Letter) {
        Set-Partition -DiskNumber $osDisk.Number -PartitionNumber $partition.PartitionNumber -NewDriveLetter $Letter -ErrorAction SilentlyContinue
    }
    Write-Output "Đã tạo ${Letter}: '$Label'."
}

# STEP 4/5: D:, then E: for the 1 TB class (D: takes its exact size so E: gets the rest).
New-DataVolume -Size $(if ($plan.HasE) { [UInt64]$plan.SizeD } else { 0 }) -Letter 'D' -Label $LabelD
if ($plan.HasE) { New-DataVolume -Size 0 -Letter 'E' -Label $LabelE }
Write-Output "Chia ổ xong: $planName."
