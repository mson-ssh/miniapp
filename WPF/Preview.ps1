# Open the latest locally published build without downloads/installers/system changes.
$ErrorActionPreference = 'Stop'
$build = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'artifacts') -Directory -Filter 'publish-*' |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'MiniApps.exe') } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $build) { throw 'Run WPF/Publish.ps1 first.' }
Start-Process -FilePath (Join-Path $build.FullName 'MiniApps.exe') -ArgumentList '--preview'
