param(
    [ValidateSet('net48','net10')][string]$Target = 'net48'
)
# Open one explicitly selected locally published build without downloads/installers/system changes.
$ErrorActionPreference = 'Stop'
$build = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'artifacts') -Directory -Filter "publish-$Target-*" |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'MiniApps.exe') } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $build) { throw "Run WPF/Publish.ps1 first; no $Target publish was found." }
Start-Process -FilePath (Join-Path $build.FullName 'MiniApps.exe') -ArgumentList '--preview'
