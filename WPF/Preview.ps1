param(
    [ValidateSet('net48','net10')][string]$Target = 'net48',
    [switch]$Developer,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
if ($Developer) {
    if ($Target -ne 'net48') { throw 'Developer edition is currently supported only for net48.' }
    $build = Join-Path $artifacts 'developer-net48'
    & $Dotnet publish (Join-Path $PSScriptRoot 'MiniApps/MiniApps.csproj') -c Release -f net48 --no-self-contained -p:MiniAppsEdition=Developer -p:DebugType=None -p:DebugSymbols=false -o $build
    if ($LASTEXITCODE -ne 0) { throw 'Developer build failed.' }
    $configRoot = Join-Path $PSScriptRoot 'ReleaseConfig'
    $initialize = Start-Process -FilePath (Join-Path $build 'MiniApps.exe') -ArgumentList @('--initialize-release-config', '--config-root', ('"' + $configRoot + '"')) -Wait -PassThru
    if ($initialize.ExitCode -ne 0) { throw 'Could not initialize ReleaseConfig.' }
    Start-Process -FilePath (Join-Path $build 'MiniApps.exe') -ArgumentList @('--developer-preview', '--config-root', ('"' + $configRoot + '"'))
    return
}

# Open one explicitly selected public build without downloads/installers/system changes.
$build = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'artifacts') -Directory -Filter "publish-$Target-*" |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'MiniApps.exe') } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $build) { throw "Run WPF/Publish.ps1 first; no $Target publish was found." }
Start-Process -FilePath (Join-Path $build.FullName 'MiniApps.exe') -ArgumentList '--preview'
