# Versioned upstream source, downloaded inside the MiniApps session, not executed via remote iex.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$version = '2026.08.24'
$work = Join-Path $env:TEMP ('debloat-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$zip = Join-Path $work 'Win11Debloat.zip'
Write-Output "Downloading Win11Debloat $version. Default profile removes preinstalled apps and changes Windows settings."
Invoke-WebRequest "https://github.com/Raphire/Win11Debloat/archive/refs/tags/$version.zip" -OutFile $zip -UseBasicParsing -TimeoutSec 600
Expand-Archive -LiteralPath $zip -DestinationPath $work
$script = Join-Path $work "Win11Debloat-$version\Win11Debloat.ps1"
if (-not (Test-Path -LiteralPath $script)) { throw 'Win11Debloat entry point is missing.' }
# Isolate upstream exit statements and error stream, and suppress interactive prompts / Explorer restart.
$stderr = Join-Path $work 'stderr.txt'
& (Join-Path $PSHOME 'powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $script -RunDefaults -Silent -SkipExplorerRestart -LogPath $work 2> $stderr
$code = $LASTEXITCODE
$errors = if (Test-Path $stderr) { Get-Content -LiteralPath $stderr -Raw } else { '' }
if ($code -ne 0 -or -not [string]::IsNullOrWhiteSpace($errors)) { throw "Win11Debloat reported errors (exit $code): $errors" }
Write-Output 'Win11Debloat finished without reported errors. Sign out/restart to apply Explorer changes.'
