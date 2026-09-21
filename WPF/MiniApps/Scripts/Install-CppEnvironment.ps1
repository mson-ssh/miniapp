# MiniApps EXTEND: C/C++ environment for VS Code, following
#   https://code.visualstudio.com/docs/cpp/config-mingw
# VS Code + MSYS2 + the MinGW-w64 UCRT64 toolchain + the C/C++ extension, with the
# toolchain on the machine PATH. The extension ships no compiler, so all four matter.
#
# Every step checks the machine first and skips what is already there, and every
# step is judged by what ends up on disk rather than by an installer's exit code.
#
# Output convention, shared with MiniApps: lines that start at column 0 are progress
# for the technician and appear on the EXTEND card; indented lines are raw tool output
# and only go to the log.
param(
    # Report what is installed and what would be done, without changing anything.
    [switch]$DryRun
)

# 'Continue', not 'Stop': under Windows PowerShell 5.1 every stderr line of winget, pacman
# or code turns into a terminating error under 'Stop'. Failures are checked explicitly.
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$MsysRoot = 'C:\msys64'
$MsysBash = Join-Path $MsysRoot 'usr\bin\bash.exe'
$ToolchainBin = Join-Path $MsysRoot 'ucrt64\bin'
$Compilers = @('gcc.exe', 'g++.exe', 'gdb.exe')
$Extension = 'ms-vscode.cpptools'

function Get-Winget {
    $command = Get-Command winget.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'
    if (Test-Path -LiteralPath $alias) { return $alias }
    return $null
}

function Get-CodeCli {
    # Looked up by path: the VS Code installer adds itself to PATH, but this process
    # started before that and will not see it.
    foreach ($candidate in @(
        (Join-Path $env:ProgramFiles 'Microsoft VS Code\bin\code.cmd'),
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft VS Code\bin\code.cmd'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\bin\code.cmd'))) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }
    return $null
}

function Test-Toolchain {
    foreach ($exe in $Compilers) { if (-not (Test-Path -LiteralPath (Join-Path $ToolchainBin $exe))) { return $false } }
    return $true
}

function Write-Tool([string]$Prefix) {
    # Tool output, indented; spinner and progress-bar lines carry no letters and are dropped.
    process { $line = "$_".TrimEnd(); if ($line -match '\w') { "$Prefix$line" } }
}

function Install-WingetPackage([string]$Id, [string]$Label, [string[]]$Extra = @()) {
    $winget = Get-Winget
    # --source winget: without it winget also queries the certificate-pinned msstore source,
    # which fails behind SSL inspection and takes the whole command down with it.
    $arguments = @('install', '--id', $Id, '--exact', '--source', 'winget', '--silent',
        '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity') + $Extra
    Write-Output "${Label}: đang tải..."
    & $winget @arguments 2>&1 | ForEach-Object {
        $line = "$_".TrimEnd()
        # The milestones become short progress lines; everything else is detail for the log.
        if ($line -match 'Starting package install') { "${Label}: đang cài..." }
        # Progress bars (one line per percent once redirected) carry nothing worth keeping.
        elseif ($line -match '\w' -and $line -notmatch '[█▒]') { "    $line" }
    }
    Write-Output "    winget kết thúc với mã $LASTEXITCODE."
}

function Invoke-Pacman([string]$Arguments, [string]$Label) {
    # Up to three tries: mirrors time out, and the first system update can replace pacman
    # itself and stop half-way, which the next pass completes.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Write-Output "${Label}$(if ($attempt -gt 1) { " (thử lại lần $attempt)" })..."
        & $MsysBash -lc "pacman $Arguments" 2>&1 | ForEach-Object {
            $line = "$_".TrimEnd()
            # "(12/150) installing mingw-w64-ucrt-x86_64-gcc" is the one line worth showing.
            if ($line -match '^\((\d+)/(\d+)\) (?:installing|upgrading|reinstalling) (\S+)') {
                "${Label}: gói $($Matches[1])/$($Matches[2]) ($($Matches[3] -replace '^mingw-w64-ucrt-x86_64-', ''))"
            }
            elseif ($line -match '^:: Retrieving packages') { "${Label}: đang tải gói..." }
            elseif ($line -match '\w') { "      $line" }
        }
        if ($LASTEXITCODE -eq 0) { return $true }
        Start-Sleep -Seconds (5 * $attempt)
    }
    return $false
}

function Add-MachinePath([string]$Folder) {
    $current = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $parts = @($current -split ';' | Where-Object { $_ })
    if ($parts | Where-Object { $_.TrimEnd('\') -ieq $Folder.TrimEnd('\') }) { return $false }
    # Machine scope so every account on the computer can build, and SetEnvironmentVariable
    # announces the change so programs started afterwards see it.
    [Environment]::SetEnvironmentVariable('Path', (($parts + $Folder) -join ';'), 'Machine')
    return $true
}

$results = [ordered]@{}

Write-Output 'Đang kiểm tra máy...'
$hasCode = [bool](Get-CodeCli)
$hasMsys = Test-Path -LiteralPath $MsysBash
$hasToolchain = Test-Toolchain
$onPath = [bool](@([Environment]::GetEnvironmentVariable('Path', 'Machine') -split ';') | Where-Object { $_.TrimEnd('\') -ieq $ToolchainBin })
Write-Output "    VS Code: $(if ($hasCode) { 'đã có' } else { 'chưa có' })"
Write-Output "    MSYS2: $(if ($hasMsys) { 'đã có' } else { 'chưa có' })"
Write-Output "    Bộ biên dịch gcc/g++/gdb: $(if ($hasToolchain) { 'đã có' } else { 'chưa có' })"
Write-Output "    $ToolchainBin trong PATH máy: $(if ($onPath) { 'có' } else { 'chưa' })"
if ($DryRun) {
    Write-Output 'DRY RUN: không thay đổi gì.'
    exit 0
}

# --- 0. winget, which the first two steps need ---
if (-not ($hasCode -and $hasMsys) -and -not (Get-Winget)) {
    Write-Output 'Chưa có winget, đang cài winget trước...'
    & (Join-Path $PSScriptRoot 'Update-Winget.ps1') 2>&1 | Write-Tool '    '
    if (-not (Get-Winget)) {
        Write-Output 'Không cài được winget nên không thể cài VS Code và MSYS2.'
        exit 1
    }
}

# --- 1. The editor ---
if (-not $hasCode) { Install-WingetPackage 'Microsoft.VisualStudioCode' 'VS Code' @('--scope', 'machine') }
else { Write-Output 'VS Code: đã có, bỏ qua.' }
$results['VS Code'] = [bool](Get-CodeCli)

# --- 2. The toolchain host ---
if (-not $hasMsys) { Install-WingetPackage 'MSYS2.MSYS2' 'MSYS2' }
else { Write-Output 'MSYS2: đã có, bỏ qua.' }
$results['MSYS2'] = Test-Path -LiteralPath $MsysBash

# --- 3. The compiler itself ---
if (-not $results['MSYS2']) {
    Write-Output 'Bộ biên dịch: bỏ qua vì chưa có MSYS2.'
}
elseif (Test-Toolchain) {
    Write-Output 'Bộ biên dịch: đã có gcc, g++, gdb, bỏ qua.'
}
else {
    # Two system updates: the first may only update pacman and the MSYS2 core.
    $updated = (Invoke-Pacman '-Syu --noconfirm' 'Cập nhật MSYS2') -and (Invoke-Pacman '-Syu --noconfirm' 'Cập nhật MSYS2 (lượt 2)')
    if (-not $updated) { Write-Output 'Cập nhật MSYS2 chưa xong; vẫn thử cài bộ biên dịch.' }
    # Its return value is not needed here: the toolchain is judged by its files below.
    $null = Invoke-Pacman '-S --needed --noconfirm base-devel mingw-w64-ucrt-x86_64-toolchain' 'Bộ biên dịch'
}
$results['Bộ biên dịch'] = Test-Toolchain

# --- 4. PATH and the extension ---
if (Test-Path -LiteralPath $ToolchainBin) {
    if (Add-MachinePath $ToolchainBin) { Write-Output "PATH: đã thêm $ToolchainBin." }
    else { Write-Output 'PATH: đã có bộ biên dịch, bỏ qua.' }
    $results['PATH'] = $true
}
else {
    Write-Output 'PATH: chưa sửa vì chưa có bộ biên dịch.'
    $results['PATH'] = $false
}
$code = Get-CodeCli
if ($code) {
    Write-Output 'Extension C/C++: đang cài...'
    & $code --install-extension $Extension --force 2>&1 | Write-Tool '    '
    $results['Extension C/C++'] = [bool](& $code --list-extensions 2>$null | Where-Object { $_ -ieq $Extension })
}
else {
    Write-Output 'Extension C/C++: chưa cài vì chưa có VS Code.'
    $results['Extension C/C++'] = $false
}

# --- What actually ended up on the machine ---
foreach ($name in $results.Keys) {
    Write-Output ("    [{0}] {1}" -f $(if ($results[$name]) { 'OK' } else { 'LỖI' }), $name)
}
foreach ($exe in $Compilers) {
    $full = Join-Path $ToolchainBin $exe
    if (Test-Path -LiteralPath $full) { Write-Output ("    {0}" -f ((& $full --version 2>$null | Select-Object -First 1))) }
}
# The last progress line is what the card keeps showing, so it is the summary.
$missing = @($results.Keys | Where-Object { -not $results[$_] })
if ($missing.Count -gt 0) {
    Write-Output "Chưa xong: $($missing -join ', '). Xem nhật ký để biết lý do."
    exit 1
}
Write-Output 'Xong: VS Code, bộ biên dịch gcc/g++/gdb và extension C/C++ đã sẵn sàng. Mở terminal hoặc VS Code mới để dùng.'
exit 0
