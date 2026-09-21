# MiniApps: remove the Microsoft Office already on this machine, before a different office suite
# (WPS, OnlyOffice, Libre Office) is installed in its place.
#
# Ported from the CLI (Setup.ps1, $RemoveOfficeScript): scan first, stop Office, then run
# Office Tool Plus "toolbox /rmoffice". The CLI moved to Office Tool Plus after Microsoft's
# GetHelpCmd (OfficeScrubScenario) kept failing in real tests while /rmoffice worked.
#
# Office Tool Plus is pinned to one release and checked by SHA-256. The R2 mirror and the GitHub
# release are the same file. To move to a newer release, change the URLs and $Sha256 together.
param(
    # Scan, download, verify and unpack, then print the command instead of running it.
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Sha256 = '68A9EBF8B569FA56A55A1B3601EC9260AEB2A3D9ACFED1DEF039C4CC9E092C9F'
$Urls = @(
    'https://pub-50d6cf4af6964541b0621bbc9bc26690.r2.dev/OTP.zip',
    'https://github.com/YerongAI/Office-Tool/releases/download/v11.6.6.0/Office_Tool_with_runtime_v11.6.6.0_x64.zip'
)
$OfficeProcesses = 'lync', 'winword', 'excel', 'msaccess', 'mstore', 'infopath', 'setlang', 'msouc', 'ois', 'onenote', 'outlook', 'powerpnt', 'mspub', 'groove', 'visio', 'winproj', 'graph', 'teams'

# Click-to-Run (2016+, Microsoft 365) lists its products under its own Configuration key; MSI
# Office shows in Programs and Features. Office add-in runtimes are not Office itself.
function Get-InstalledOffice {
    $found = @()
    $c2r = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Office\ClickToRun\Configuration' -ErrorAction SilentlyContinue
    if ($c2r -and $c2r.ProductReleaseIds) { $found += "Click-to-Run: $($c2r.ProductReleaseIds)" }
    foreach ($root in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*') {
        $found += @(Get-ItemProperty -Path $root -ErrorAction SilentlyContinue |
            Where-Object { $_.Publisher -eq 'Microsoft Corporation' -and $_.DisplayName -match 'Office|365 Apps' -and $_.DisplayName -notmatch 'Runtime|Tools for Office|Visual Studio' } |
            ForEach-Object { [string]$_.DisplayName })
    }
    return @($found | Select-Object -Unique)
}

$office = @(Get-InstalledOffice)
if ($office.Count -eq 0) {
    Write-Output 'Không có Microsoft Office trên máy, không cần gỡ.'
    exit 0
}
Write-Output "Tìm thấy Microsoft Office: $($office -join '; ')"

# Short working folder: the runtime inside the package nests paths over 110 characters deep.
$work = Join-Path $env:LOCALAPPDATA ('MiniApps\OfficeRemoval\' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$zip = Join-Path $work 'otp.zip'
$unpacked = Join-Path $work 'otp'
try {
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $downloaded = $false
    foreach ($url in $Urls) {
        try {
            Write-Output "Đang tải Office Tool Plus từ $url ..."
            Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -TimeoutSec 600
            $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
            if ($actual -ne $Sha256) { throw "SHA-256 không khớp (nhận $actual)" }
            $downloaded = $true
            break
        }
        catch { Write-Output "Không dùng được bản tải từ $url - $($_.Exception.Message)" }
    }
    if (-not $downloaded) { throw 'Không tải được Office Tool Plus đã xác minh. Chưa gỡ gì.' }

    Write-Output 'Đã xác minh SHA-256, đang giải nén...'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($unpacked) + '\'
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($item in $archive.Entries) {
            $destination = [IO.Path]::GetFullPath((Join-Path $unpacked $item.FullName))
            if (-not $destination.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Đường dẫn không hợp lệ trong gói: $($item.FullName)" }
            if ($item.FullName.EndsWith('/')) { New-Item -ItemType Directory -Force -Path $destination | Out-Null; continue }
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($item, $destination, $true)
        }
    }
    finally { $archive.Dispose() }
    $console = Join-Path $unpacked 'Office Tool\Office Tool Plus.Console.exe'
    if (-not (Test-Path -LiteralPath $console)) { throw 'Gói Office Tool Plus thiếu Office Tool Plus.Console.exe.' }

    if ($DryRun) {
        Write-Output "DRY RUN: `"$console`" toolbox /rmoffice"
        exit 0
    }

    $stopped = @()
    foreach ($name in $OfficeProcesses) {
        $running = Get-Process -Name $name -ErrorAction SilentlyContinue
        if ($running) { $running | Stop-Process -Force -ErrorAction SilentlyContinue; $stopped += $name }
    }
    if ($stopped.Count -gt 0) { Write-Output "Đã đóng: $($stopped -join ', ')" }

    Write-Output 'Đang gỡ Microsoft Office (Office Tool Plus: toolbox /rmoffice). Có thể mất vài phút...'
    # Office Tool Plus may write to stderr; under 'Stop' Windows PowerShell 5.1 would abort on it.
    $ErrorActionPreference = 'Continue'
    & $console toolbox /rmoffice 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    # Office Tool Plus does not document its exit codes: 0 is success, anything else a failure.
    if ($code -ne 0) { throw "Office Tool Plus kết thúc với mã $code." }

    $left = @(Get-InstalledOffice)
    if ($left.Count -gt 0) { throw "Office vẫn còn sau khi gỡ: $($left -join '; '). Có thể cần khởi động lại rồi chạy lại." }
    Write-Output 'Đã gỡ Microsoft Office. Nên khởi động lại máy sau khi cài xong.'
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
