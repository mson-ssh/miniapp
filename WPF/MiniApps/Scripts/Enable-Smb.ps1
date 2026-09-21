# MiniApps Windows setting: make SMB file and printer sharing work on Windows 10/11.
#
# Windows 11 24H2 tightened SMB defaults. This undoes the parts that stop ordinary
# office and home networks from working, and leaves the rest of SMB's protection on:
#   - reaching NAS, printers and other PCs: guest (no password) shares and devices that
#     cannot sign SMB traffic, plus the SMB1 client for very old devices;
#   - sharing from this PC: network discovery and file/printer sharing on private and
#     domain networks only, never on public ones.
# Every step runs even if an earlier one fails; failures are listed at the end.

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]
$restart = $false

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Output "[OK] $Name" }
    catch { $failures.Add("$Name - $($_.Exception.Message)"); Write-Output "[LỖI] $Name - $($_.Exception.Message)" }
}

# --- Reaching other devices (SMB client) ---
Invoke-Step 'Cho phép vào thư mục share không mật khẩu (guest)' {
    Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -Force
    # 24H2 also enforces this through policy; the policy value is what Windows reads first.
    $policy = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation'
    New-Item -Path $policy -Force | Out-Null
    New-ItemProperty -Path $policy -Name AllowInsecureGuestAuth -PropertyType DWord -Value 1 -Force | Out-Null
}
Invoke-Step 'Không bắt buộc ký SMB khi kết nối tới thiết bị khác' {
    # Signing is still used whenever the other side supports it; only the requirement goes.
    Set-SmbClientConfiguration -RequireSecuritySignature $false -Force
}
Invoke-Step 'Bật SMB1 client cho thiết bị rất cũ (SMB1 server vẫn tắt)' {
    $client = Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Client
    if ($client.State -ne 'Enabled') {
        $result = Enable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Client -All -NoRestart
        if ($result.RestartNeeded) { $script:restart = $true }
    }
    # "Automatic removal" uninstalls SMB1 again after 15 days without use.
    $removal = Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Deprecation -ErrorAction SilentlyContinue
    if ($removal -and $removal.State -eq 'Enabled') {
        $result = Disable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Deprecation -NoRestart
        if ($result.RestartNeeded) { $script:restart = $true }
    }
    # This PC must never serve SMB1 to the network.
    Set-SmbServerConfiguration -EnableSMB1Protocol $false -Force
}

# --- Sharing from this PC (SMB server) ---
Invoke-Step 'Không bắt buộc ký SMB với máy kết nối vào máy này' {
    Set-SmbServerConfiguration -RequireSecuritySignature $false -Force
}
Invoke-Step 'Đặt mạng đang dùng là Private' {
    # Discovery and sharing stay closed on Public networks, so the current one must be Private.
    # Domain networks are left alone: Windows manages them and they already allow sharing.
    foreach ($connection in @(Get-NetConnectionProfile | Where-Object NetworkCategory -eq 'Public')) {
        Set-NetConnectionProfile -InterfaceIndex $connection.InterfaceIndex -NetworkCategory Private
    }
}
Invoke-Step 'Bật Network discovery và File and Printer Sharing trên tường lửa (Private/Domain)' {
    # Groups by resource id, not display name: the names are translated on non-English Windows.
    foreach ($group in @('@FirewallAPI.dll,-32752', '@FirewallAPI.dll,-28502')) {
        $enabled = 0
        foreach ($rule in @(Get-NetFirewallRule -Group $group)) {
            $profiles = "$($rule.Profile)"
            if ($profiles -eq 'Public') { continue }
            # Some built-in rules cover several profiles at once ("Private, Public", "Any").
            # Public is taken out of those before enabling, so sharing never opens on public networks.
            $keep = if ($profiles -eq 'Any') { @('Domain', 'Private') } else { @($profiles -split ',\s*' | Where-Object { $_ -ne 'Public' }) }
            if ($keep.Count -ne @($profiles -split ',\s*').Count -or $profiles -eq 'Any') {
                Set-NetFirewallRule -InputObject $rule -Profile $keep
            }
            Enable-NetFirewallRule -InputObject $rule
            $enabled++
        }
        if ($enabled -eq 0) { throw "Không tìm thấy rule Private/Domain trong nhóm $group." }
    }
}
Invoke-Step 'Bật các dịch vụ chia sẻ và hiển thị máy trong mục Network' {
    foreach ($name in @('LanmanServer', 'LanmanWorkstation', 'FDResPub', 'fdPHost', 'SSDPSRV', 'upnphost')) {
        $service = Get-Service -Name $name -ErrorAction SilentlyContinue
        if (-not $service) { continue }
        Set-Service -Name $name -StartupType Automatic
        if ($service.Status -ne 'Running') { Start-Service -Name $name }
    }
}

if ($restart) { Write-Output 'Cần khởi động lại máy để SMB1 client có hiệu lực.' }
if ($failures.Count -gt 0) { throw "SMB: $($failures.Count) bước chưa xong: $($failures -join '; ')" }
