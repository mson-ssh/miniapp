<#
.SYNOPSIS
    Share-LAN.ps1 v2 - Chia se du lieu qua LAN (SMB) - Windows 10 Enterprise IoT LTSC

.DESCRIPTION
    Mac dinh:
      - May khach DOC + GHI + XOA (dung -ReadOnly de khoa lai)
      - KHONG can mat khau, moi may cung LAN deu vao duoc
      - May chu van toan quyen tren du lieu goc (truy cap local)

.EXAMPLE
    .\Share-LAN.ps1
    .\Share-LAN.ps1 -Mode Server -Path "D:\Data" -ShareName "Data"
    .\Share-LAN.ps1 -Mode Server -Path "D:\Data" -ShareName "Data" -ReadOnly
    .\Share-LAN.ps1 -Mode Client -Server 192.168.1.10
    .\Share-LAN.ps1 -Mode Client -Server 192.168.1.10 -ShareName Data -DriveLetter Z
#>

[CmdletBinding()]
param(
    [ValidateSet('Server','Client','Manage','Diag')]
    [string] $Mode,

    # Server
    [string] $Path,
    [switch] $ReadOnly,        # Mac dinh READ/WRITE. Bat -ReadOnly de khoa ghi/xoa.

    # Client
    [string] $Server,
    [string] $DriveLetter,
    [switch] $NoMap,

    # Chung
    [string] $ShareName
)

$ErrorActionPreference = 'Stop'
$script:Fail = 0

#region ===== HELPER =======================================================
# Menu dieu huong bang phim mui ten. Tra ve chi so (0-based), -1 neu Esc.
# Tu dong fallback sang nhap so neu host khong ho tro ReadKey (ISE, redirect).
# Goi 'net use' an toan.
# LUU Y: KHONG dung "& cmd /c ... 2>&1" trong PowerShell. Khi $ErrorActionPreference
# = 'Stop', moi dong stderr cua chuong trinh ngoai bi bien thanh loi TERMINATING,
# nen 'net use /delete' khi khong co ket noi (thong bao binh thuong) se lam chet script.
# Cach dung dung: dat 2>&1 BEN TRONG chuoi lenh cmd -> stderr khong bao gio ve PowerShell.
function Invoke-NetUse {
    param(
        [Parameter(Mandatory)][string] $Arguments,
        [switch] $Quiet
    )
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $out  = @()
    $code = -1
    try {
        $out  = @(& cmd.exe /c "net use $Arguments 2>&1")
        $code = $LASTEXITCODE
    } catch {
        $out = @($_.Exception.Message)
    } finally {
        $ErrorActionPreference = $prev
    }
    if (-not $Quiet) {
        $out | Where-Object { $_ -match '\S' } |
            ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
    }
    [pscustomobject]@{ ExitCode = $code; Output = ($out -join "`n") }
}

function Show-Menu {
    param(
        [Parameter(Mandatory)][string[]] $Items,
        [string] $Title,
        [string] $Footer = "  [Up/Down] di chuyen    [Enter] chon    [Esc] thoat"
    )

    $useKeys = $true
    try {
        if ($Host.Name -notmatch 'ConsoleHost') { $useKeys = $false }
        elseif ([Console]::IsInputRedirected)   { $useKeys = $false }
        else { $null = [Console]::WindowWidth }
    } catch { $useKeys = $false }

    # --- Fallback: nhap so ---
    if (-not $useKeys) {
        if ($Title) { Write-Host "`n$Title" -ForegroundColor White }
        for ($i = 0; $i -lt $Items.Count; $i++) { "{0,2}. {1}" -f ($i+1), $Items[$i] | Write-Host }
        while ($true) {
            $r = (Read-Host "Chon so (1-$($Items.Count), 0 = thoat)").Trim()
            $n = 0
            if ([int]::TryParse(($r -replace '[^\d]',''), [ref]$n)) {
                if ($n -eq 0) { return -1 }
                if ($n -ge 1 -and $n -le $Items.Count) { return ($n - 1) }
            }
            Write-Host "  Nhap tu 1 den $($Items.Count)." -ForegroundColor Red
        }
    }

    # --- Che do phim mui ten ---
    if ($Title) { Write-Host "`n$Title" -ForegroundColor White }

    # Neu khong du cho ve o cuoi man hinh -> clear de tranh cuon lam lech con tro
    $needed = $Items.Count + 3
    if (([Console]::CursorTop + $needed) -ge [Console]::WindowHeight) {
        Clear-Host
        if ($Title) { Write-Host "$Title" -ForegroundColor White }
    }

    $top = [Console]::CursorTop
    $w   = [Math]::Max(24, [Console]::WindowWidth - 1)
    $idx = 0

    try { [Console]::CursorVisible = $false } catch {}
    try {
        while ($true) {
            [Console]::SetCursorPosition(0, $top)
            for ($i = 0; $i -lt $Items.Count; $i++) {
                $line = if ($i -eq $idx) { "  > $($Items[$i])" } else { "    $($Items[$i])" }
                if ($line.Length -gt $w) { $line = $line.Substring(0, $w) }
                $line = $line.PadRight($w)
                if ($i -eq $idx) { Write-Host $line -ForegroundColor Black -BackgroundColor Cyan }
                else             { Write-Host $line -ForegroundColor Gray }
            }
            Write-Host ''.PadRight($w)
            Write-Host $Footer.PadRight($w) -ForegroundColor DarkGray

            $k = [Console]::ReadKey($true)
            switch ($k.Key) {
                'UpArrow'   { $idx = ($idx - 1 + $Items.Count) % $Items.Count }
                'DownArrow' { $idx = ($idx + 1) % $Items.Count }
                'Home'      { $idx = 0 }
                'End'       { $idx = $Items.Count - 1 }
                'Enter'     { [Console]::SetCursorPosition(0, $top + $Items.Count + 2); return $idx }
                'Escape'    { [Console]::SetCursorPosition(0, $top + $Items.Count + 2); return -1 }
                default {
                    # Van cho phep bam so de nhay nhanh
                    if ($k.KeyChar -match '[1-9]') {
                        $n = [int]::Parse($k.KeyChar)
                        if ($n -le $Items.Count) { $idx = $n - 1 }
                    }
                }
            }
        }
    } finally {
        try { [Console]::CursorVisible = $true } catch {}
    }
}

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Write-Step { param($m) Write-Host "`n[*] $m" -ForegroundColor Cyan }
function Write-Ok   { param($m) Write-Host "    [OK]   $m" -ForegroundColor Green }
function Write-Warn2{ param($m) Write-Host "    [!]    $m" -ForegroundColor Yellow }
function Write-Err2 { param($m) Write-Host "    [X]    $m" -ForegroundColor Red; $script:Fail++ }

function Get-SidName {
    param([string]$Sid)
    try {
        (New-Object System.Security.Principal.SecurityIdentifier($Sid)).Translate(
            [System.Security.Principal.NTAccount]).Value
    } catch { $null }
}

function Set-NetworkPrivate {
    Write-Step "Network profile -> Private"
    $p = Get-NetConnectionProfile -ErrorAction SilentlyContinue
    if (-not $p) { Write-Warn2 "Khong doc duoc network profile"; return }
    foreach ($n in $p) {
        if ($n.NetworkCategory -eq 'Public') {
            try {
                Set-NetConnectionProfile -InterfaceIndex $n.InterfaceIndex -NetworkCategory Private -ErrorAction Stop
                Write-Ok "$($n.Name) -> Private"
            } catch { Write-Warn2 "$($n.Name): $($_.Exception.Message)" }
        } else { Write-Ok "$($n.Name) = $($n.NetworkCategory)" }
    }
}

function Enable-Svc {
    param([string[]]$Names)
    foreach ($s in ($Names | Select-Object -Unique)) {
        $svc = Get-Service -Name $s -ErrorAction SilentlyContinue
        if (-not $svc) { Write-Warn2 "$s : khong co tren he thong"; continue }
        try {
            Set-Service -Name $s -StartupType Automatic -ErrorAction SilentlyContinue
            if ((Get-Service $s).Status -ne 'Running') { Start-Service -Name $s -ErrorAction Stop }
            Write-Ok $s
        } catch { Write-Warn2 "$s : $($_.Exception.Message)" }
    }
}

function Enable-SharingFirewall {
    Write-Step "Firewall: File and Printer Sharing + Network Discovery"
    # Dung Group ID -> khong phu thuoc ngon ngu Windows
    $groups = [ordered]@{
        '@FirewallAPI.dll,-28502' = 'File and Printer Sharing'
        '@FirewallAPI.dll,-32752' = 'Network Discovery'
    }
    foreach ($g in $groups.Keys) {
        try {
            $r = @(Get-NetFirewallRule -Group $g -ErrorAction Stop)
            if ($r.Count -eq 0) { throw "empty" }
            $r | Set-NetFirewallRule -Enabled True -ErrorAction SilentlyContinue
            Write-Ok "$($groups[$g]) ($($r.Count) rules)"
        } catch {
            try {
                Set-NetFirewallRule -DisplayGroup $groups[$g] -Enabled True `
                    -Profile Private,Domain -ErrorAction Stop
                Write-Ok "$($groups[$g]) (fallback theo ten)"
            } catch { Write-Warn2 "$($groups[$g]): khong tim thay rule group" }
        }
    }
}

# secedit export ra UTF-16LE. Ghi lai bang Set-Content mac dinh (ASCII) se lam
# hong file va /configure that bai am tham -> bat buoc -Encoding Unicode.
function Grant-GuestNetworkLogon {
    Write-Step "Cho phep Guest dang nhap qua mang (User Rights Assignment)"
    $inf = Join-Path $env:TEMP "slan_$(Get-Random).inf"
    $db  = Join-Path $env:TEMP "slan_$(Get-Random).sdb"
    $log = Join-Path $env:TEMP "slan_secedit.log"
    try {
        $null = & secedit /export /cfg $inf /areas USER_RIGHTS
        if (-not (Test-Path $inf)) { Write-Warn2 "secedit /export that bai"; return }

        $out = foreach ($line in (Get-Content $inf)) {
            if     ($line -match '^\s*SeDenyNetworkLogonRight') { 'SeDenyNetworkLogonRight =' }
            elseif ($line -match '^\s*SeNetworkLogonRight') {
                $l = $line.TrimEnd()
                foreach ($sid in @('*S-1-1-0','*S-1-5-32-546')) {   # Everyone, Guests
                    if ($l -notmatch [regex]::Escape($sid)) { $l = "$l,$sid" }
                }
                $l
            }
            else { $line }
        }
        $out | Set-Content -Path $inf -Encoding Unicode

        $null = & secedit /configure /db $db /cfg $inf /areas USER_RIGHTS /log $log /quiet
        if ($LASTEXITCODE -eq 0) { Write-Ok "Guest/Everyone duoc phep truy cap qua mang" }
        else { Write-Warn2 "secedit tra ve ma $LASTEXITCODE - xem log: $log" }
    } catch {
        Write-Warn2 "Khong ap dung duoc User Rights: $($_.Exception.Message)"
    } finally {
        foreach ($f in @($inf,$db)) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
#endregion

#region ===== AUTO ELEVATE =================================================
if (-not (Test-Admin)) {
    Write-Host "Dang xin quyen Administrator..." -ForegroundColor Yellow
    $a = @('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$PSCommandPath`"")
    foreach ($k in $PSBoundParameters.Keys) {
        $v = $PSBoundParameters[$k]
        if ($v -is [switch]) { if ($v.IsPresent) { $a += "-$k" } }
        else { $a += @("-$k","`"$v`"") }
    }
    try { Start-Process powershell.exe -Verb RunAs -ArgumentList $a }
    catch { Write-Host "Nguoi dung tu choi UAC." -ForegroundColor Red }
    exit
}
#endregion

#region ===== MENU =========================================================
if (-not $Mode) {
    Clear-Host
    Write-Host ""
    Write-Host "  =========================================================" -ForegroundColor DarkCyan
    Write-Host "   SHARE-LAN v2   |   Chia se du lieu qua LAN (SMB)"        -ForegroundColor White
    Write-Host "   Mac dinh: may khach DOC + GHI, khong can mat khau"         -ForegroundColor DarkGray
    Write-Host "  =========================================================" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "   May nay : $env:COMPUTERNAME" -ForegroundColor DarkGray
    Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        ForEach-Object { Write-Host "   IP      : $($_.IPAddress)  ($($_.InterfaceAlias))" -ForegroundColor DarkGray }
    Write-Host ""
    $opts = @(
        'MAY CHU    - Chia se o dia / thu muc (doc + ghi)',
        'MAY KHACH  - Ket noi toi du lieu share',
        'QUAN LY    - Xem / go bo share tren may nay',
        'CHAN DOAN  - Kiem tra cau hinh hien tai',
        'Thoat'
    )
    $pick = Show-Menu -Items $opts
    switch ($pick) {
        0 { $Mode = 'Server' }
        1 { $Mode = 'Client' }
        2 { $Mode = 'Manage' }
        3 { $Mode = 'Diag'   }
        default { exit 0 }     # 4 = Thoat, -1 = Esc
    }
}
#endregion


#region ===== SERVER =======================================================
function Invoke-ServerMode {

    $Everyone = Get-SidName 'S-1-1-0'
    $Guests   = Get-SidName 'S-1-5-32-546'
    if (-not $Everyone) { Write-Err2 "Khong phan giai duoc SID Everyone"; return }

    # --- Chon o dia / thu muc ---
    if (-not $script:Path) {
        $drives = @(Get-Volume |
                  Where-Object { $_.DriveLetter -and $_.DriveType -in 'Fixed','Removable' } |
                  Sort-Object DriveLetter)

        $items = foreach ($d in $drives) {
            "{0}:\  {1,-16} {2,7} GB (trong {3,6} GB)  [{4}]" -f `
                $d.DriveLetter, $d.FileSystemLabel,
                [math]::Round($d.Size/1GB,1), [math]::Round($d.SizeRemaining/1GB,1), $d.FileSystem
        }
        $items = @($items) + @('Nhap duong dan thu muc thu cong (vd: D:\KhachHang)')

        $pick = Show-Menu -Items $items -Title "Chon o dia / thu muc can chia se:"
        if ($pick -lt 0) { Write-Warn2 "Da huy."; return }

        if ($pick -eq ($items.Count - 1)) {
            while (-not $script:Path) {
                $p = (Read-Host "Duong dan day du").Trim().Trim('"').Trim()
                if ($p) { $script:Path = $p } else { Write-Host "  Chua nhap gi." -ForegroundColor Red }
            }
        } else {
            $script:Path = "$($drives[$pick].DriveLetter):\"
            $sub = Read-Host "Share ca o dia? Enter = ca o, hoac go ten thu muc con"
            if ($sub) { $script:Path = Join-Path $script:Path $sub.Trim() }
        }
    }

    if (-not (Test-Path -LiteralPath $script:Path)) {
        if ((Read-Host "'$($script:Path)' chua ton tai. Tao moi? (Y/N)") -match '^[Yy]') {
            New-Item -ItemType Directory -Path $script:Path -Force | Out-Null
        } else { Write-Err2 "Huy."; return }
    }
    $script:Path = (Resolve-Path -LiteralPath $script:Path).Path

    if (-not $script:ShareName) {
        $def = if ($script:Path -match '^[A-Za-z]:\\?$') { "$($script:Path[0])_Drive" }
               else { Split-Path $script:Path -Leaf }
        $def = ($def -replace '[^\w\-]','_')
        $script:ShareName = Read-Host "Ten share (Enter = $def)"
        if (-not $script:ShareName) { $script:ShareName = $def }
    }
    $script:ShareName = $script:ShareName -replace '[^\w\-]','_'
    if ($script:ShareName.Length -gt 60) { $script:ShareName = $script:ShareName.Substring(0,60) }

    $script:Write = -not $script:ReadOnly
    $rw = if ($script:Write) { 'READ/WRITE' } else { 'READ-ONLY' }
    Write-Host "`n=== $($script:Path)  ->  \\$env:COMPUTERNAME\$($script:ShareName)  [$rw] ===" -ForegroundColor White
    if ($script:Write) {
        Write-Warn2 "Che do GHI: bat ky may nao trong LAN deu co the SUA va XOA du lieu nay."
        Write-Warn2 "Khong co Recycle Bin qua mang - file bi xoa la mat luon."
        Write-Host  "      Ctrl+C trong 4 giay de huy." -ForegroundColor DarkGray
        Start-Sleep -Seconds 4
    }

    # --- Ha tang mang ---
    Set-NetworkPrivate
    Write-Step "Service phia server"
    Enable-Svc @('LanmanServer','FDResPub','fdPHost','SSDPSRV','upnphost')
    Enable-SharingFirewall

    if (-not (Get-NetFirewallRule -Name 'ShareLAN-SMB-445-In' -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -Name 'ShareLAN-SMB-445-In' -DisplayName 'Share-LAN SMB 445 In' `
            -Direction Inbound -Protocol TCP -LocalPort 445 -Action Allow `
            -Profile Private,Domain | Out-Null
    }
    Write-Ok "TCP 445 inbound"

    # --- Truy cap khong mat khau ---
    Write-Step "Tat 'Password protected sharing'"
    $lsa = 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa'
    Set-ItemProperty $lsa -Name 'LimitBlankPasswordUse'     -Value 0 -Type DWord
    Set-ItemProperty $lsa -Name 'everyoneincludesanonymous' -Value 1 -Type DWord
    Set-ItemProperty $lsa -Name 'restrictanonymous'         -Value 0 -Type DWord
    Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters' `
        -Name 'RestrictNullSessAccess' -Value 0 -Type DWord
    Write-Ok "Registry LSA / LanmanServer"

    $guest = Get-LocalUser -ErrorAction SilentlyContinue | Where-Object { $_.SID.Value -like '*-501' }
    if ($guest) {
        if (-not $guest.Enabled) { Enable-LocalUser -Name $guest.Name }
        Write-Ok "Tai khoan '$($guest.Name)' da bat"
    } else { Write-Warn2 "Khong tim thay tai khoan Guest" }

    Grant-GuestNetworkLogon

    # --- Tao share ---
    Write-Step "Tao SMB share"
    if (Get-SmbShare -Name $script:ShareName -ErrorAction SilentlyContinue) {
        Remove-SmbShare -Name $script:ShareName -Force
        Write-Warn2 "Share trung ten -> da xoa va tao lai"
    }

    $acct = @($Everyone)
    if ($Guests) { $acct += $Guests }

    if ($script:Write) {
        New-SmbShare -Name $script:ShareName -Path $script:Path -ChangeAccess $acct `
            -FolderEnumerationMode Unrestricted | Out-Null
    } else {
        New-SmbShare -Name $script:ShareName -Path $script:Path -ReadAccess $acct `
            -FolderEnumerationMode Unrestricted | Out-Null
    }
    Write-Ok "Share '$($script:ShareName)' [$rw] cho: $($acct -join ', ')"

    # --- NTFS ---
    # Quyen hieu luc qua mang = GIAO cua Share permission va NTFS permission.
    # Che do ghi doi HAI tang deu cho ghi: Share=Change VA NTFS=Modify.
    # Guest khong thuoc nhom Users nen phai cap qua Everyone (Everyone bao gom Guest).
    $need = if ($script:Write) { 'Modify' } else { 'ReadAndExecute' }
    Write-Step "NTFS: cap quyen $need cho Everyone"
    try {
        $acl = Get-Acl -LiteralPath $script:Path
        $wantMask = if ($script:Write) {
            [System.Security.AccessControl.FileSystemRights]::Modify
        } else {
            [System.Security.AccessControl.FileSystemRights]::ReadAndExecute
        }
        $has = $acl.Access | Where-Object {
                    $_.IdentityReference.Value -eq $Everyone -and
                    $_.AccessControlType -eq 'Allow' -and
                    (($_.FileSystemRights -band $wantMask) -eq $wantMask)
               }
        if ($has) {
            Write-Ok "Everyone da co quyen $need - giu nguyen ACL"
        } else {
            $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                        $Everyone, $need, 'ContainerInherit,ObjectInherit','None','Allow')
            $acl.AddAccessRule($rule)          # AddAccessRule KHONG xoa rule khac
            Set-Acl -LiteralPath $script:Path -AclObject $acl
            Write-Ok "Everyone = $need (quyen cu giu nguyen)"
        }
    } catch { Write-Err2 "Khong sua duoc NTFS ACL: $($_.Exception.Message)" }

    # --- Tu kiem tra ---
    Write-Step "Tu kiem tra"
    $sh = Get-SmbShare -Name $script:ShareName -ErrorAction SilentlyContinue
    if ($sh) { Write-Ok "Share ton tai -> $($sh.Path)" } else { Write-Err2 "Share khong duoc tao" }

    $acc  = @(Get-SmbShareAccess -Name $script:ShareName -ErrorAction SilentlyContinue)
    $want = if ($script:Write) { 'Change' } else { 'Read' }
    $bad  = @($acc | Where-Object { $_.AccessRight -ne $want })
    if ($bad.Count -gt 0) { Write-Err2 "Entry sai quyen ($want): $($bad.AccountName -join ', ')" }
    else { Write-Ok "Tat ca entry deu la $want" }

    # Thu ghi that su vao thu muc de chac chan NTFS khong chan
    if ($script:Write) {
        $probe = Join-Path $script:Path "_wtest_$(Get-Random).tmp"
        try {
            New-Item -Path $probe -ItemType File -ErrorAction Stop | Out-Null
            Remove-Item $probe -Force -ErrorAction SilentlyContinue
            Write-Ok "Ghi thu vao thu muc thanh cong"
        } catch { Write-Err2 "Khong ghi duoc vao thu muc: $($_.Exception.Message)" }
    }

    if ((Get-Service LanmanServer).Status -eq 'Running') { Write-Ok "LanmanServer dang chay" }
    else { Write-Err2 "LanmanServer khong chay" }

    if (Get-NetTCPConnection -LocalPort 445 -State Listen -ErrorAction SilentlyContinue) {
        Write-Ok "Dang lang nghe TCP 445"
    } else { Write-Err2 "Khong lang nghe TCP 445" }

    # --- Tong ket ---
    Write-Step "HOAN TAT ($($script:Fail) loi)"
    $ips = Get-NetIPAddress -AddressFamily IPv4 |
           Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
           Select-Object -ExpandProperty IPAddress
    Write-Host ""
    Write-Host "  Thu muc : $($script:Path)"
    Write-Host "  Che do  : $rw" -ForegroundColor $(if($script:Write){'Yellow'}else{'Green'})
    Write-Host "  Truy cap: \\$env:COMPUTERNAME\$($script:ShareName)" -ForegroundColor Yellow
    foreach ($ip in $ips) { Write-Host "            \\$ip\$($script:ShareName)" -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "  May khach: chay script nay -> [2] MAY KHACH -> IP $($ips | Select-Object -First 1)" -ForegroundColor Cyan
    Write-Host ""
    Get-SmbShareAccess -Name $script:ShareName |
        Format-Table AccountName, AccessControlType, AccessRight -AutoSize
}
#endregion


#region ===== CLIENT =======================================================
function Invoke-ClientMode {

    Set-NetworkPrivate
    Write-Step "Service phia client"
    Enable-Svc @('LanmanWorkstation','FDResPub','fdPHost','SSDPSRV','upnphost')
    Enable-SharingFirewall

    # Windows 10 1709+ chan guest logon -> loi "your organization's security
    # policies block unauthenticated guest access". Bat lai ca 2 noi.
    Write-Step "Bat 'Insecure guest logons'"
    Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters' `
        -Name 'AllowInsecureGuestAuth' -Value 1 -Type DWord
    $pol = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation'
    if (-not (Test-Path $pol)) { New-Item -Path $pol -Force | Out-Null }
    Set-ItemProperty $pol -Name 'AllowInsecureGuestAuth' -Value 1 -Type DWord
    Write-Ok "AllowInsecureGuestAuth = 1"

    try {
        Restart-Service LanmanWorkstation -Force -ErrorAction Stop
        Start-Sleep -Seconds 2
        Write-Ok "Restart LanmanWorkstation"
    } catch { Write-Warn2 "Khong restart duoc - can reboot de ap dung" }

    # --- Server ---
    if (-not $script:Server) { $script:Server = Read-Host "`nIP may chu (vd: 192.168.1.10)" }
    $script:Server = $script:Server.Trim().Trim('\','/')
    if (-not $script:Server) { Write-Err2 "Chua nhap dia chi may chu"; return }

    Write-Step "Kiem tra ket noi toi $($script:Server)"
    if (Test-Connection -ComputerName $script:Server -Count 2 -Quiet -ErrorAction SilentlyContinue) {
        Write-Ok "Ping OK"
    } else { Write-Warn2 "Ping khong phan hoi (firewall ICMP co the dang chan - van co the ket noi duoc)" }

    $t = Test-NetConnection -ComputerName $script:Server -Port 445 -WarningAction SilentlyContinue
    if ($t.TcpTestSucceeded) { Write-Ok "Port 445 mo" }
    else {
        Write-Err2 "Khong ket noi duoc port 445."
        Write-Host "      -> Tren may chu kiem tra: da chay che do [1] chua, firewall, cung subnet chua." -ForegroundColor DarkGray
        return
    }

    # --- Liet ke share ---
    Write-Step "Liet ke share tren \\$($script:Server)"
    $shares = @()
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = @(& cmd.exe /c "net view `"\\$($script:Server)`" /all 2>&1")
        $shares = @($raw | Where-Object { $_ -match '\s{2,}Disk' } |
                   ForEach-Object { ($_ -split '\s{2,}')[0].Trim() } |
                   Where-Object { $_ -and $_ -notmatch '\$$' })
    } catch {
    } finally {
        $ErrorActionPreference = $prev
    }

    if ($shares.Count -gt 0 -and -not $script:ShareName) {
        $items = @($shares) + @('Go ten share thu cong')
        $pick  = Show-Menu -Items $items -Title "Share tim thay tren \\$($script:Server):"
        if ($pick -lt 0) { Write-Warn2 "Da huy."; return }
        if ($pick -eq ($items.Count - 1)) { $script:ShareName = (Read-Host "Ten share").Trim() }
        else { $script:ShareName = $shares[$pick] }
    }
    elseif ($shares.Count -eq 0) {
        Write-Warn2 "Khong liet ke duoc share (may chu tat Network Discovery)."
        if (-not $script:ShareName) { $script:ShareName = (Read-Host "Go ten share thu cong").Trim() }
    }
    if (-not $script:ShareName) { Write-Err2 "Chua chon share"; return }

    $unc = "\\$($script:Server)\$($script:ShareName)"

    # --- Ket noi ---
    Write-Step "Ket noi $unc"
    Invoke-NetUse "`"$unc`" /delete /y" -Quiet | Out-Null
    $r = Invoke-NetUse "`"$unc`" /persistent:yes"
    if ($r.ExitCode -ne 0) { Write-Warn2 "net use tra ve ma $($r.ExitCode)" }

    if (-not (Test-Path -LiteralPath $unc)) {
        Write-Warn2 "Ket noi an danh that bai."
        Write-Host "      Hay gap: may chu chua chay che do [1], hoac client chua reboot" -ForegroundColor DarkGray
        Write-Host "      sau khi bat AllowInsecureGuestAuth." -ForegroundColor DarkGray
        if ((Read-Host "Thu dang nhap bang tai khoan cua may chu? (Y/N)") -match '^[Yy]') {
            $c = Get-Credential -Message "User/password cua MAY CHU ($($script:Server))"
            $u = $c.UserName; $p = $c.GetNetworkCredential().Password
            Invoke-NetUse "`"$unc`" `"$p`" /user:`"$u`" /persistent:yes" | Out-Null
        }
    }
    if (-not (Test-Path -LiteralPath $unc)) { Write-Err2 "Khong truy cap duoc $unc"; return }
    Write-Ok "Truy cap duoc $unc"

    # --- Map o dia ---
    if (-not $script:NoMap) {
        if (-not $script:DriveLetter) {
            $used = (Get-PSDrive -PSProvider FileSystem).Name
            $free = @(90..68 | ForEach-Object { [string][char]$_ } | Where-Object { $_ -notin $used })
            $script:DriveLetter = Read-Host "Map vao o dia nao? (Enter = $($free[0]), go N de bo qua)"
            if (-not $script:DriveLetter) { $script:DriveLetter = $free[0] }
        }
        if ($script:DriveLetter -notmatch '^[Nn]$') {
            $dl = $script:DriveLetter.TrimEnd(':')
            Invoke-NetUse "${dl}: /delete /y" -Quiet | Out-Null
            Invoke-NetUse "${dl}: `"$unc`" /persistent:yes" | Out-Null
            if (Test-Path "${dl}:\") { Write-Ok "Da map ${dl}: -> $unc"; $script:DriveLetter = $dl }
            else { Write-Warn2 "Map that bai, van dung duoc bang UNC"; $script:DriveLetter = $null }
        } else { $script:DriveLetter = $null }
    }

    # --- Xac minh read-only ---
    Write-Step "Xac minh quyen tren share"
    $probe = Join-Path $unc "_rwtest_$(Get-Random).tmp"
    try {
        New-Item -Path $probe -ItemType File -ErrorAction Stop | Out-Null
        Set-Content -Path $probe -Value 'test' -ErrorAction Stop
        Remove-Item $probe -Force -ErrorAction Stop
        Write-Ok "DOC + GHI + XOA deu OK"
    } catch {
        Remove-Item $probe -Force -ErrorAction SilentlyContinue
        Write-Warn2 "Share dang CHI DOC - khong ghi/xoa duoc"
        Write-Host "      -> Tren may chu: chay lai che do [1] (mac dinh la READ/WRITE)," -ForegroundColor DarkGray
        Write-Host "         hoac kiem tra NTFS cua thu muc goc co chan Everyone khong." -ForegroundColor DarkGray
    }

    # --- Tong ket ---
    Write-Step "HOAN TAT ($($script:Fail) loi)"
    Write-Host ""
    Write-Host "  UNC   : $unc" -ForegroundColor Yellow
    if ($script:DriveLetter) { Write-Host "  O dia : $($script:DriveLetter):" -ForegroundColor Yellow }
    Write-Host ""
    Get-ChildItem -LiteralPath $unc -ErrorAction SilentlyContinue | Select-Object -First 15 |
        Format-Table Mode, LastWriteTime, Length, Name -AutoSize

    if ((Read-Host "Mo File Explorer? (Y/N)") -match '^[Yy]') { Start-Process explorer.exe $unc }
}
#endregion


#region ===== MANAGE =======================================================
function Invoke-ManageMode {
    $list = @(Get-SmbShare | Where-Object { $_.Name -notmatch '\$$' })
    if ($list.Count -eq 0) { Write-Warn2 "Khong co share nao tren may nay."; return }

    $items = foreach ($s in $list) {
        $r = (Get-SmbShareAccess -Name $s.Name | Select-Object -ExpandProperty AccessRight -Unique) -join '/'
        "{0,-20} [{1,-6}] -> {2}" -f $s.Name, $r, $s.Path
    }
    $items = @($items) + @('Quay lai')

    $pick = Show-Menu -Items $items -Title "Share dang co tren may nay (chon de GO BO):"
    if ($pick -lt 0 -or $pick -eq ($items.Count - 1)) { return }

    $name = $list[$pick].Name
    if ((Read-Host "Xac nhan go bo '$name'? (Y/N)") -match '^[Yy]') {
        Remove-SmbShare -Name $name -Force
        Write-Ok "Da go bo '$name' (du lieu tren o dia van nguyen ven)"
    }
}
#endregion


#region ===== DIAG =========================================================
function Invoke-DiagMode {
    Write-Step "Network profile"
    Get-NetConnectionProfile | Format-Table Name, InterfaceAlias, NetworkCategory -AutoSize

    Write-Step "Service"
    Get-Service LanmanServer,LanmanWorkstation,FDResPub,fdPHost,SSDPSRV,upnphost -ErrorAction SilentlyContinue |
        Format-Table Name, Status, StartType -AutoSize

    Write-Step "Registry quan trong"
    $checks = @(
        @{P='HKLM:\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters'; N='AllowInsecureGuestAuth';    W=1; S='Client'},
        @{P='HKLM:\SYSTEM\CurrentControlSet\Control\Lsa';                           N='everyoneincludesanonymous'; W=1; S='Server'},
        @{P='HKLM:\SYSTEM\CurrentControlSet\Control\Lsa';                           N='LimitBlankPasswordUse';     W=0; S='Server'}
    )
    foreach ($c in $checks) {
        $v = (Get-ItemProperty -Path $c.P -Name $c.N -ErrorAction SilentlyContinue).$($c.N)
        $txt = "[$($c.S)] $($c.N) = $(if($null -eq $v){'<chua dat>'}else{$v})  (can $($c.W))"
        if ($v -eq $c.W) { Write-Ok $txt } else { Write-Warn2 $txt }
    }

    Write-Step "Tai khoan Guest"
    $g = Get-LocalUser -ErrorAction SilentlyContinue | Where-Object { $_.SID.Value -like '*-501' }
    if ($g) { if ($g.Enabled) { Write-Ok "$($g.Name): Enabled" } else { Write-Warn2 "$($g.Name): Disabled" } }
    else { Write-Warn2 "Khong tim thay tai khoan Guest" }

    Write-Step "Firewall"
    foreach ($grp in @('@FirewallAPI.dll,-28502','@FirewallAPI.dll,-32752')) {
        $r  = @(Get-NetFirewallRule -Group $grp -ErrorAction SilentlyContinue)
        $on = @($r | Where-Object { $_.Enabled -eq 'True' }).Count
        Write-Host "    $grp : $on/$($r.Count) rules enabled"
    }
    if (Get-NetTCPConnection -LocalPort 445 -State Listen -ErrorAction SilentlyContinue) {
        Write-Ok "TCP 445 dang lang nghe"
    } else { Write-Warn2 "TCP 445 khong lang nghe (may nay khong chia se gi)" }

    Write-Step "Share hien co"
    Get-SmbShare | Format-Table Name, Path -AutoSize
    foreach ($s in @(Get-SmbShare | Where-Object { $_.Name -notmatch '\$$' })) {
        Write-Host "  [$($s.Name)]" -ForegroundColor Cyan
        Get-SmbShareAccess -Name $s.Name | Format-Table AccountName, AccessControlType, AccessRight -AutoSize
    }

    Write-Step "Ket noi dang mo"
    Invoke-NetUse "" | Out-Null
}
#endregion


#region ===== RUN =========================================================
try {
    switch ($Mode) {
        'Server' { Invoke-ServerMode }
        'Client' { Invoke-ClientMode }
        'Manage' { Invoke-ManageMode }
        'Diag'   { Invoke-DiagMode   }
    }
} catch {
    Write-Host ""
    Write-Host "LOI KHONG XU LY DUOC:" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Dong: $($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor DarkGray
}

Write-Host ""
Read-Host "Nhan Enter de thoat"
#endregion
