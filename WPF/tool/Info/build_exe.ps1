<#
.SYNOPSIS
    Kich ban dong goi info.ps1 thanh info.exe bang Invoke-PS2EXE
#>

Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "  TIEN HANH DONG GOI UNG DUNG INFO.EXE         " -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }

$inputFile  = Join-Path $scriptDir "info.ps1"
$outputFile = Join-Path $scriptDir "info.exe"
$iconFile   = Join-Path $scriptDir "app.ico"

if (-not (Test-Path $inputFile)) {
    Write-Error "Khong tim thay file nguon: $inputFile"
    Pause
    exit 1
}

# Kiem tra module ps2exe
if (-not (Get-Command Invoke-PS2EXE -ErrorAction SilentlyContinue)) {
    Write-Host "[*] Dang tai module ps2exe..." -ForegroundColor Yellow
    Import-Module ps2exe -ErrorAction Stop
}

Write-Host "[*] Nguon: $inputFile" -ForegroundColor Gray
Write-Host "[*] Dich : $outputFile" -ForegroundColor Gray
if (Test-Path $iconFile) {
    Write-Host "[*] Icon : $iconFile" -ForegroundColor Gray
}
Write-Host "[*] Dang bien dich thanh file EXE..." -ForegroundColor Yellow

try {
    $ps2exeParams = @{
        inputFile   = $inputFile
        outputFile  = $outputFile
        noConsole   = $true
        STA         = $true
        DPIAware    = $true
        title       = "System Information"
        description = "System Specs Tool - Info"
        product     = "info.exe"
        version     = "1.0.0.0"
    }
    if (Test-Path $iconFile) {
        $ps2exeParams["iconFile"] = $iconFile
    }

    Invoke-PS2EXE @ps2exeParams

    if (Test-Path $outputFile) {
        $fileSize = [math]::Round((Get-Item $outputFile).Length / 1KB, 1)
        Write-Host ""
        Write-Host "===============================================" -ForegroundColor Green
        Write-Host "  DONG GOI THANH CONG: info.exe ($fileSize KB)" -ForegroundColor Green
        Write-Host "  Duong dan: $outputFile" -ForegroundColor Green
        Write-Host "===============================================" -ForegroundColor Green
    } else {
        Write-Host "[-] Dong goi that bai, khong tim thay file output." -ForegroundColor Red
    }
}
catch {
    Write-Error "Loi trong qua trinh dong goi: $_"
}
