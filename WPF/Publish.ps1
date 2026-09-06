param(
    [ValidateSet('win-x64','win-x86')][string]$Runtime = 'win-x64',
    [ValidatePattern('^[a-zA-Z0-9._-]+$')][string]$Version = '0.2.0',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
$stage = Join-Path $artifacts ("publish-" + [Guid]::NewGuid().ToString('N'))
New-Item -Path $stage -ItemType Directory -Force | Out-Null
$platform = if ($Runtime -eq 'win-x86') { 'x86' } else { 'x64' }
& $Dotnet publish (Join-Path $PSScriptRoot 'MiniApps/MiniApps.csproj') -c Release -f net48 "-p:PlatformTarget=$platform" "-p:Version=$Version" -p:DebugType=None -p:DebugSymbols=false -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$asset = "MiniApps-$Runtime.zip"
$zip = Join-Path $artifacts $asset
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($zip + '.sha256') -Value "$hash  $asset" -Encoding ASCII
$manifest = @{ version = $Version; sha256 = $hash; framework = 'net48'; url = "https://github.com/mson-ssh/miniapp/releases/download/v$Version/$asset" }
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts "manifest-$Runtime.json") -Encoding UTF8
Write-Host "Built $zip"
Write-Host "SHA-256: $hash"
Write-Host "Upload ZIP, .sha256 and manifest-$Runtime.json to release v$Version. No upload was performed."
