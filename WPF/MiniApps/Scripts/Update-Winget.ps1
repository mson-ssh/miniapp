# Called only after explicit selection and Office/WPS confirmation. No upgrade --all.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$work = Join-Path $env:TEMP ('winget-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$release = Invoke-RestMethod 'https://api.github.com/repos/microsoft/winget-cli/releases/latest' -TimeoutSec 60
$target = [version]($release.tag_name -replace '^v','')
function Get-ClientVersion {
    $cmd = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $cmd) { return $null }
    $text = & $cmd.Source --version
    if ($LASTEXITCODE -ne 0) { return $null }
    $parsed = $null
    if ([version]::TryParse(("$text".Trim() -replace '^v',''), [ref]$parsed)) { return $parsed }
    return $null
}
$before = Get-ClientVersion
if ($before -and $before -ge $target) { Write-Output "WinGet $before is already current (stable $target)."; return }
function Get-ReleaseAsset([string]$Name) {
    $asset = @($release.assets | Where-Object name -eq $Name)
    if ($asset.Count -ne 1 -or $asset[0].browser_download_url -notlike 'https://github.com/microsoft/winget-cli/releases/download/*') { throw "Missing official release asset: $Name" }
    $path = Join-Path $work $Name
    Write-Output "Downloading $Name" | Out-Host
    Invoke-WebRequest $asset[0].browser_download_url -OutFile $path -UseBasicParsing -TimeoutSec 600
    if ($asset[0].digest -notmatch '^sha256:([a-fA-F0-9]{64})$') { throw "Missing SHA-256 for $Name" }
    if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $Matches[1]) { throw "SHA-256 mismatch: $Name" }
    return $path
}
$rawArch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
$arch = switch ($rawArch) { 'AMD64' { 'x64' }; 'ARM64' { 'arm64' }; 'x86' { 'x86' }; default { throw "Unsupported architecture: $rawArch" } }
$depsPath = Get-ReleaseAsset 'DesktopAppInstaller_Dependencies.json'
$deps = (Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json).Dependencies
if (-not $deps) { throw 'Dependency manifest is empty.' }
$missing = @($deps | Where-Object {
    $dep = $_
    -not (Get-AppxPackage -Name $dep.Name | Where-Object { ("$($_.Architecture)" -eq $arch -or "$($_.Architecture)" -eq 'Neutral') -and [version]$_.Version -ge [version]$dep.Version })
})
if ($missing.Count) {
    $zip = Get-ReleaseAsset 'DesktopAppInstaller_Dependencies.zip'
    $folder = Join-Path $work 'dependencies'
    Expand-Archive -LiteralPath $zip -DestinationPath $folder
    foreach ($dep in $missing) {
        $file = Get-ChildItem -LiteralPath (Join-Path $folder $arch) -Filter "$($dep.Name)_*.appx" | Select-Object -First 1
        if (-not $file) { throw "Missing dependency: $($dep.Name) for $arch" }
        Write-Output "Registering dependency $($dep.Name)"
        Add-AppxPackage -Path $file.FullName -ErrorAction Stop
    }
}
$bundle = Get-ReleaseAsset 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle'
Add-AppxPackage -Path $bundle -ErrorAction Stop
$after = Get-ClientVersion
if (-not $after -or $after -lt $target) { throw "WinGet verification failed. Expected $target; got $after." }
Write-Output "WinGet updated: $before -> $after"
