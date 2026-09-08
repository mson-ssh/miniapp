param([Parameter(Mandatory)][ValidateSet('Desktop','Timezone','Dns','FastStartup','Power','PasswordExpiry','Winget','Debloat')][string]$Option)
$ErrorActionPreference = 'Stop'
try {
    switch ($Option) {
        'Winget' { & (Join-Path $PSScriptRoot 'Update-Winget.ps1'); if (-not $?) { throw 'WinGet update failed.' } }
        'Desktop' {
            $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel'
            New-Item -Path $key -Force | Out-Null
            foreach ($id in @('{20D04FE0-3AEA-1069-A2D8-08002B30309D}', '{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}', '{59031a47-3f72-44a7-89c5-5595fe6b30ee}')) {
                New-ItemProperty -Path $key -Name $id -PropertyType DWord -Value 0 -Force | Out-Null
            }
        }
        'Timezone' { Set-TimeZone -Id 'SE Asia Standard Time' }
        'Dns' {
            $adapters = @(Get-NetAdapter -Physical | Where-Object Status -eq 'Up')
            if ($adapters.Count -eq 0) { throw 'No active physical network adapter.' }
            foreach ($adapter in $adapters) { Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses @('1.1.1.1','8.8.8.8') }
        }
        'FastStartup' { New-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -Value 0 -PropertyType DWord -Force | Out-Null }
        'Power' {
            foreach ($setting in @('monitor-timeout-ac','monitor-timeout-dc','standby-timeout-ac','standby-timeout-dc')) {
                powercfg /change $setting 0
                if ($LASTEXITCODE -ne 0) { throw "powercfg failed: $setting ($LASTEXITCODE)" }
            }
        }
        'PasswordExpiry' { net accounts /maxpwage:unlimited; if ($LASTEXITCODE -ne 0) { throw "net accounts failed ($LASTEXITCODE)" } }
    }
    Write-Output "$Option : OK"
    exit 0
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
