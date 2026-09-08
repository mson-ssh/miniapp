param(
    [ValidateSet('net48')][string]$Target = 'net48',
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
$build = Join-Path $artifacts ("developer-net48-live-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$project = Join-Path $PSScriptRoot 'MiniApps/MiniApps.csproj'
$configRoot = Join-Path $PSScriptRoot 'ReleaseConfig'

& $Dotnet publish $project -c Release -f $Target --no-self-contained `
    -p:MiniAppsEdition=Developer -p:DebugType=None -p:DebugSymbols=false -o $build
if ($LASTEXITCODE -ne 0) { throw 'Developer build failed.' }

$appsConfig = Join-Path $configRoot 'apps.json'
$windowsConfig = Join-Path $configRoot 'windows.json'
if (-not (Test-Path -LiteralPath $appsConfig) -or -not (Test-Path -LiteralPath $windowsConfig)) {
    $initialize = Start-Process -FilePath (Join-Path $build 'MiniApps.exe') `
        -ArgumentList @('--initialize-release-config', '--config-root', ('"' + $configRoot + '"')) `
        -Wait -PassThru -WindowStyle Hidden
    if ($initialize.ExitCode -ne 0) { throw 'Could not initialize ReleaseConfig.' }
}

$process = Start-Process -FilePath (Join-Path $build 'MiniApps.exe') `
    -ArgumentList @('--config-root', ('"' + $configRoot + '"')) -PassThru
Write-Output "MiniApps Developer started (PID $($process.Id))."
Write-Output "Build: $build"
Write-Output "Configuration: $configRoot"
