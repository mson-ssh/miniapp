# MiniApps EXTEND: run Win11Debloat (https://github.com/Raphire/Win11Debloat) in its default mode.
#
# Pinned to one release and checked by SHA-256, so every machine runs the same reviewed code.
# To move to a newer release, change $Version and $Sha256 together.
#
# The release is kept under $InstallRoot after the run: Win11Debloat writes its registry
# backups (Backups\) and its log (Logs\) inside its own folder, and deleting that folder
# would delete the only way back.
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'MiniApps\Win11Debloat'),
    # Download, verify and unpack, then print the command instead of running it.
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Version = '2026.08.24'
$Sha256 = '00D1487B2E9B9691653774CC781ED3844E4B2FD5B0D91C32F6F78AA8C7892BD4'
$Url = "https://github.com/Raphire/Win11Debloat/archive/refs/tags/$Version.zip"

$target = Join-Path $InstallRoot $Version
$entry = Join-Path $target 'Win11Debloat.ps1'

if (-not (Test-Path -LiteralPath $entry)) {
    if (Test-Path -LiteralPath $target) {
        # Never replace a folder we did not finish writing: it may already hold registry backups.
        throw "Thư mục $target có sẵn nhưng thiếu Win11Debloat.ps1. Hãy kiểm tra và xóa thủ công rồi chạy lại."
    }
    New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
    # Short working names keep every unpacked path well under Windows' 260-character limit.
    $zip = "$target.zip"
    $partial = "$target.partial"
    try {
        for ($attempt = 1; ; $attempt++) {
            try {
                Write-Output "Đang tải Win11Debloat $Version (lần $attempt)..."
                Invoke-WebRequest -Uri $Url -OutFile $zip -UseBasicParsing -TimeoutSec 300
                break
            }
            catch {
                if ($attempt -ge 3) { throw "Không tải được Win11Debloat: $($_.Exception.Message)" }
                Start-Sleep -Seconds (5 * $attempt)
            }
        }
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        if ($actual -ne $Sha256) { throw "SHA-256 của bản tải về không khớp (nhận $actual). Không chạy gì." }
        Write-Output "Đã xác minh SHA-256, đang giải nén..."
        # A .partial folder is only ever our own unfinished unpack; nothing has run from it.
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Recurse -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $root = [IO.Path]::GetFullPath($partial) + '\'
        $prefix = "Win11Debloat-$Version/"
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($item in $archive.Entries) {
                # GitHub wraps the tree in one Win11Debloat-<version>/ folder; drop it while unpacking.
                if (-not $item.FullName.StartsWith($prefix)) { throw 'Gói Win11Debloat không đúng cấu trúc mong đợi.' }
                $relative = $item.FullName.Substring($prefix.Length)
                if (-not $relative) { continue }
                $destination = [IO.Path]::GetFullPath((Join-Path $partial $relative))
                if (-not $destination.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Đường dẫn không hợp lệ trong gói: $($item.FullName)" }
                if ($item.FullName.EndsWith('/')) { New-Item -ItemType Directory -Force -Path $destination | Out-Null; continue }
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($item, $destination, $true)
            }
        }
        finally { $archive.Dispose() }
        if (-not (Test-Path -LiteralPath (Join-Path $partial 'Win11Debloat.ps1'))) { throw 'Gói Win11Debloat thiếu Win11Debloat.ps1.' }
        Rename-Item -LiteralPath $partial -NewName (Split-Path -Leaf $target)
    }
    finally {
        Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $partial -Recurse -Force -ErrorAction SilentlyContinue
    }
}
else {
    Write-Output "Dùng Win11Debloat $Version đã có tại $target."
}

$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
# Win11Debloat needs Windows PowerShell 5.1, hence powershell.exe by full path. It is run as a
# child process because it ends with Exit, which would otherwise end this script as well.
$arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $entry, '-RunDefaults', '-Silent')
if ($DryRun) {
    Write-Output "DRY RUN: $powershell $($arguments -join ' ')"
    exit 0
}

Write-Output 'Đang chạy Win11Debloat ở chế độ mặc định. Có thể mất 5-15 phút; Explorer sẽ khởi động lại.'
# Windows PowerShell 5.1 turns every stderr line of a native command into a terminating error
# under 'Stop'; Win11Debloat reports failed app removals that way and must be allowed to finish.
$ErrorActionPreference = 'Continue'
& $powershell @arguments 2>&1 | ForEach-Object { "$_" }
$code = $LASTEXITCODE
Write-Output "Win11Debloat kết thúc với mã $code. Nhật ký chi tiết: $(Join-Path $target 'Logs\Win11Debloat.log')"
exit $code
