param(
    [ValidateSet('net48','net10')][string]$Target = 'net48',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
# Open one explicitly selected public build without downloads/installers/system changes.
$build = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'artifacts') -Directory -Filter "publish-$Target-*" |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'MiniApps.exe') } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $build) { throw "Run WPF/Publish.ps1 first; no $Target publish was found." }
Start-Process -FilePath (Join-Path $build.FullName 'MiniApps.exe') -ArgumentList '--preview'
