param(
    [ValidateSet('win-x64')][string]$Runtime = 'win-x64',
    [ValidatePattern('^[a-zA-Z0-9._-]+$')][string]$Version = '0.3.4',
    [ValidateSet('net48','both')][string]$Target = 'net48',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -Path $artifacts -ItemType Directory -Force | Out-Null
$project = Join-Path $PSScriptRoot 'MiniApps/MiniApps.csproj'
$releaseConfig = Join-Path $PSScriptRoot 'ReleaseConfig'
if (-not (Test-Path -LiteralPath (Join-Path $releaseConfig 'apps.json')) -or -not (Test-Path -LiteralPath (Join-Path $releaseConfig 'windows.json'))) {
    throw 'ReleaseConfig is incomplete. Run Preview.ps1 -Developer once and save the reviewed configuration.'
}
$assets = @()

function New-MiniAppsPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$Framework,
        [Parameter(Mandatory = $true)][bool]$SelfContained
    )
    $stage = Join-Path $artifacts ("publish-$Target-" + [Guid]::NewGuid().ToString('N'))
    New-Item -Path $stage -ItemType Directory -Force | Out-Null
    $arguments = @('publish', $project, '-c', 'Release', '-f', $Framework, '-p:PlatformTarget=x64', '-p:MiniAppsEdition=Public', "-p:Version=$Version", '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $stage)
    if ($SelfContained) {
        $arguments += @('-r', $Runtime, '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false')
    } else {
        $arguments += @('--no-self-contained')
    }
    & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Target." }

    $packagedConfig = Join-Path $stage 'ReleaseConfig'
    Copy-Item -LiteralPath $releaseConfig -Destination $packagedConfig -Recurse
    $validation = Start-Process -FilePath (Join-Path $stage 'MiniApps.exe') -ArgumentList @('--validate-config', '--config-root', ('"' + $packagedConfig + '"')) -Wait -PassThru -WindowStyle Hidden
    if ($validation.ExitCode -ne 0) { throw "ReleaseConfig validation failed for $Target." }

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
if ($Target -eq 'both') {
    New-MiniAppsPackage -Target 'net10' -Framework 'net10.0-windows' -SelfContained $true
} else {
    # Preserve the published v0.3.0 fallback byte-for-byte; do not rebuild net10.
    $fallbackFile = "MiniApps-net10-$Runtime.zip"
    $fallbackZip = Join-Path $artifacts $fallbackFile
    $fallbackHash = '828341fa007fb89f1d246758eaa9dae93e835b283464c2d5068be785b3e42b17'
    $fallbackSize = 63072275
    $reuseVerified = (Test-Path -LiteralPath $fallbackZip) -and
        ((Get-Item -LiteralPath $fallbackZip).Length -eq $fallbackSize) -and
        ((Get-FileHash -LiteralPath $fallbackZip -Algorithm SHA256).Hash.ToLowerInvariant() -eq $fallbackHash)
    if (-not $reuseVerified) {
        Invoke-WebRequest "https://github.com/mson-ssh/miniapp/releases/download/v0.3.0/$fallbackFile" -OutFile $fallbackZip -UseBasicParsing -TimeoutSec 600
    }
    if ((Get-Item -LiteralPath $fallbackZip).Length -ne $fallbackSize -or (Get-FileHash -LiteralPath $fallbackZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $fallbackHash) { throw 'Published net10 fallback verification failed.' }
    Set-Content -LiteralPath ($fallbackZip + '.sha256') -Value "$fallbackHash  $fallbackFile" -Encoding ASCII
    $assets += [ordered]@{ target = 'net10'; architecture = $Runtime; file = $fallbackFile; url = "https://github.com/mson-ssh/miniapp/releases/download/v$Version/$fallbackFile"; sha256 = $fallbackHash; size = $fallbackSize; selfContained = $true }
    Write-Host 'Reused net10 v0.3.0 unchanged; no net10 build was performed.'
}

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
