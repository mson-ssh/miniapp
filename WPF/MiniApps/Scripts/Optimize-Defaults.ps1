# MiniApps net48: pinned Win11Debloat default profile, excluding restore points.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$commit = '6012b02ea282f23ea943946206762fd430025c6f'
$zip = Join-Path $PWD 'upstream.zip'
Invoke-WebRequest "https://github.com/Raphire/Win11Debloat/archive/$commit.zip" -OutFile $zip -UseBasicParsing -TimeoutSec 600
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne '24FF70B7D72C1B1470DF947C5A25583C0560FED3A7F7CF1A3FA3A7E4D9E8CD6C') { throw 'Win11Debloat checksum mismatch.' }
Expand-Archive -LiteralPath $zip -DestinationPath $PWD
$root = Join-Path $PWD "Win11Debloat-$commit"
$defaultsPath = Join-Path $root 'Config\DefaultSettings.json'
$defaults = Get-Content -LiteralPath $defaultsPath -Raw | ConvertFrom-Json
if ($defaults.Version -ne '1.0') { throw 'Unexpected default profile schema.' }
$defaults.Settings = @($defaults.Settings | Where-Object { $_.Name -ne 'CreateRestorePoint' })
$defaults | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $defaultsPath -Encoding UTF8
# Keep upstream registry backups outside the disposable download directory.
$logs = Join-Path $env:LOCALAPPDATA 'MiniApps\OptimizeLogs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$stderr = Join-Path $PWD 'stderr.txt'
try {
    & (Join-Path $PSHOME 'powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'Win11Debloat.ps1') -RunDefaults -Silent -SkipExplorerRestart -LogPath $logs 2> $stderr
    $code = $LASTEXITCODE
}
finally {
    $backup = Join-Path $root 'Backups'
    if (Test-Path -LiteralPath $backup) {
        Move-Item -LiteralPath $backup -Destination (Join-Path $logs ('Backups-' + [Guid]::NewGuid().ToString('N'))) -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $stderr) { Copy-Item -LiteralPath $stderr -Destination (Join-Path $logs 'last-stderr.txt') -Force }
}
$errors = if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Raw } else { '' }
if ($code -ne 0 -or -not [string]::IsNullOrWhiteSpace($errors)) { throw "Win11Debloat reported errors (exit $code): $errors" }
Write-Output 'Default profile completed. Sign out or restart Windows to apply all changes.'
