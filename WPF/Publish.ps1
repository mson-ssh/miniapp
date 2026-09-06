param(
    [ValidateSet('win-x64')][string]$Runtime = 'win-x64',
    [ValidatePattern('^[a-zA-Z0-9._-]+$')][string]$Version = '0.2.0',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -Path $artifacts -ItemType Directory -Force | Out-Null
$project = Join-Path $PSScriptRoot 'MiniApps/MiniApps.csproj'
$assets = @()

function New-MiniAppsPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$Framework,
        [Parameter(Mandatory = $true)][bool]$SelfContained
    )
    $stage = Join-Path $artifacts ("publish-$Target-" + [Guid]::NewGuid().ToString('N'))
    New-Item -Path $stage -ItemType Directory -Force | Out-Null
    $arguments = @('publish', $project, '-c', 'Release', '-f', $Framework, '-p:PlatformTarget=x64', "-p:Version=$Version", '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $stage)
    if ($SelfContained) {
        $arguments += @('-r', $Runtime, '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false')
    } else {
        $arguments += @('--no-self-contained')
    }
    & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Target." }

    $asset = "MiniApps-$Target-$Runtime.zip"
    $zip = Join-Path $artifacts $asset
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $zip).Length
    Set-Content -LiteralPath ($zip + '.sha256') -Value "$hash  $asset" -Encoding ASCII
    $script:assets += [ordered]@{
        target = $Target
        architecture = $Runtime
        file = $asset
        url = "https://github.com/mson-ssh/miniapp/releases/download/v$Version/$asset"
        sha256 = $hash
        size = $size
        selfContained = $SelfContained
    }
    Write-Host "Built $zip ($size bytes)"
    Write-Host "SHA-256: $hash"
}

New-MiniAppsPackage -Target 'net48' -Framework 'net48' -SelfContained $false
New-MiniAppsPackage -Target 'net10' -Framework 'net10.0-windows' -SelfContained $true

$manifest = [ordered]@{
    schemaVersion = 2
    version = $Version
    architecture = $Runtime
    assets = $assets
}
$manifestPath = Join-Path $artifacts "manifest-$Runtime.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Built $manifestPath"
Write-Host "Upload both ZIP files, both .sha256 files and manifest-$Runtime.json to release v$Version. No upload was performed."
