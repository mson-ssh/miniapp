# MiniApps bootstrap: irm https://raw.githubusercontent.com/mson-ssh/miniapp/main/WPF/bootstrap.ps1 | iex
# Publish the ZIP and SHA-256 assets with Publish.ps1 before distributing this command.
function Test-MiniAppsWindowsBuild {
    param(
        [Parameter(Mandatory = $true)][int]$Build,
        [string]$DisplayVersion = ''
    )
    if ($Build -lt 17763) {
        $versionText = if ([string]::IsNullOrWhiteSpace($DisplayVersion)) { '' } else { " $DisplayVersion" }
        throw "Detected Windows$versionText (build $Build). MiniApps requires Windows 10 1809 (build 17763) or later."
    }
    return $true
}

function Select-MiniAppsTarget {
    param([Parameter(Mandatory = $true)][int]$FrameworkRelease)
    if ($FrameworkRelease -ge 528040) { return 'net48' }
    return 'net10'
}

function ConvertFrom-MiniAppsManifestContent {
    param([Parameter(Mandatory = $true)]$Content)
    $json = if ($Content -is [byte[]]) {
        [Text.Encoding]::UTF8.GetString($Content)
    } else {
        [string]$Content
    }
    if ([string]::IsNullOrWhiteSpace($json)) { throw 'Release manifest is empty.' }
    try { return ($json.TrimStart([char]0xFEFF) | ConvertFrom-Json) }
    catch { throw "Invalid release manifest JSON: $($_.Exception.Message)" }
}

function Select-MiniAppsAsset {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][ValidateSet('net48','net10')][string]$Target,
        [Parameter(Mandatory = $true)][ValidateSet('win-x64')][string]$Architecture
    )
    if ([int]$Manifest.schemaVersion -ne 2) { throw 'Unsupported or missing release manifest schema.' }
    if ([string]$Manifest.version -notmatch '^[a-zA-Z0-9._-]+$') { throw 'Invalid release manifest version.' }
    if ([string]$Manifest.architecture -ne $Architecture) { throw 'Release manifest architecture mismatch.' }
    $matches = @($Manifest.assets | Where-Object { [string]$_.target -eq $Target -and [string]$_.architecture -eq $Architecture })
    if ($matches.Count -ne 1) { throw "Release manifest must contain exactly one $Target/$Architecture asset." }
    $asset = $matches[0]
    $expectedFile = "MiniApps-$Target-$Architecture.zip"
    $expectedUrl = "https://github.com/mson-ssh/miniapp/releases/download/v$($Manifest.version)/$expectedFile"
    if ([string]$asset.file -ne $expectedFile) { throw 'Release manifest filename mismatch.' }
    if ([string]$asset.url -ne $expectedUrl) { throw 'Release manifest URL is not an approved immutable GitHub release URL.' }
    if ([string]$asset.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid release manifest SHA-256.' }
    if ([long]$asset.size -le 0) { throw 'Invalid release manifest asset size.' }
    if ($Target -eq 'net48' -and [bool]$asset.selfContained) { throw 'The net48 manifest asset must be framework-dependent.' }
    if ($Target -eq 'net10' -and -not [bool]$asset.selfContained) { throw 'The net10 manifest asset must be self-contained.' }
    return $asset
}

function Start-MiniApps {
    param(
        [string]$ReleaseBase = 'https://github.com/mson-ssh/miniapp/releases/download/v0.3.4',
        [string]$PackagePath = '',
        [string]$ExpectedSha256 = '',
        [switch]$Preview
    )
    $ErrorActionPreference = 'Stop'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin -and -not $Preview) {
        # Elevate this exact function, not a second mutable copy downloaded from main.
        $body = ${function:Start-MiniApps}.ToString()
        $buildTestBody = ${function:Test-MiniAppsWindowsBuild}.ToString()
        $targetBody = ${function:Select-MiniAppsTarget}.ToString()
        $manifestBody = ${function:ConvertFrom-MiniAppsManifestContent}.ToString()
        $assetBody = ${function:Select-MiniAppsAsset}.ToString()
        $escapedBase = $ReleaseBase.Replace("'", "''")
        $escapedPath = $PackagePath.Replace("'", "''")
        $escapedHash = $ExpectedSha256.Replace("'", "''")
        $command = "function Test-MiniAppsWindowsBuild { $buildTestBody }; function Select-MiniAppsTarget { $targetBody }; function ConvertFrom-MiniAppsManifestContent { $manifestBody }; function Select-MiniAppsAsset { $assetBody }; function Start-MiniApps { $body }; Start-MiniApps -ReleaseBase '$escapedBase' -PackagePath '$escapedPath' -ExpectedSha256 '$escapedHash'"
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"
        return
    }
    # Read the installed build; Environment.OSVersion may reflect host compatibility settings.
    $osView = if ([Environment]::Is64BitOperatingSystem) { [Microsoft.Win32.RegistryView]::Registry64 } else { [Microsoft.Win32.RegistryView]::Registry32 }
    $osRoot = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $osView)
    $osKey = $null
    try {
        $osKey = $osRoot.OpenSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion')
        if (-not $osKey) { throw 'Cannot read the installed Windows build.' }
        $buildText = [string]$osKey.GetValue('CurrentBuildNumber', $osKey.GetValue('CurrentBuild', ''))
        $displayVersion = [string]$osKey.GetValue('DisplayVersion', '')
        $windowsBuild = 0
        if (-not [int]::TryParse($buildText, [ref]$windowsBuild)) { throw "Cannot determine Windows build from '$buildText'." }
    } finally {
        if ($osKey) { $osKey.Dispose() }
        $osRoot.Dispose()
    }
    if (-not $Preview) { Test-MiniAppsWindowsBuild -Build $windowsBuild -DisplayVersion $displayVersion | Out-Null }
    # Prefer the tiny framework-dependent package when .NET Framework 4.8 exists.
    # Otherwise choose the self-contained .NET 10 package; no runtime is installed.
    $view = if ([Environment]::Is64BitOperatingSystem) { [Microsoft.Win32.RegistryView]::Registry64 } else { [Microsoft.Win32.RegistryView]::Registry32 }
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
    $frameworkKey = $null
    try {
        $frameworkKey = $baseKey.OpenSubKey('SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full')
        $release = if ($frameworkKey) { [int]$frameworkKey.GetValue('Release', 0) } else { 0 }
    } finally {
        if ($frameworkKey) { $frameworkKey.Dispose() }
        $baseKey.Dispose()
    }
    $target = Select-MiniAppsTarget -FrameworkRelease $release
    $arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    $rid = switch ($arch) { 'AMD64' { 'win-x64' }; 'ARM64' { throw 'This release has not been validated for ARM64.' }; 'x86' { throw 'This release is x64 only; x86 is not supported.' }; default { throw "Unsupported architecture: $arch" } }
    $root = Join-Path ([IO.Path]::GetTempPath()) 'MiniApps'
    New-Item -Path $root -ItemType Directory -Force | Out-Null
    if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Session root cannot be a junction or symbolic link.' }
    $root = [IO.Path]::GetFullPath($root)

    function Remove-Session([string]$Path) {
        $full = [IO.Path]::GetFullPath($Path)
        if ([IO.Path]::GetDirectoryName($full) -ne $root -or [IO.Path]::GetFileName($full) -notmatch '^[a-f0-9]{32}$') { throw "Unsafe cleanup target: $full" }
        if (-not (Test-Path -LiteralPath (Join-Path $full '.miniapps-session'))) { throw 'Missing ownership marker.' }
        if ((Get-Item -LiteralPath $full).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing reparse point cleanup.' }
        $links = @(Get-ChildItem -LiteralPath $full -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
        if ($links.Count) { throw 'Session contains reparse points; manual cleanup required.' }
        Remove-Item -LiteralPath $full -Recurse -Force
    }

    # Clean only owned sessions whose bootstrap lock is released and whose app/children are gone.
    foreach ($old in @(Get-ChildItem -LiteralPath $root -Directory)) {
        if ($old.Name -notmatch '^[a-f0-9]{32}$' -or -not (Test-Path -LiteralPath (Join-Path $old.FullName '.miniapps-session'))) { continue }
        $oldLock = $null
        try {
            $oldLock = [IO.File]::Open((Join-Path $old.FullName 'session.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
            $running = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($old.FullName + '\', [StringComparison]::OrdinalIgnoreCase) })
            # Worker paths may be outside the session (powershell/msiexec); a parent marker stays until clean exit.
            if ($running.Count -gt 0 -or (Test-Path -LiteralPath (Join-Path $old.FullName 'installing'))) { continue }
            $oldLock.Dispose(); $oldLock = $null
            Remove-Session $old.FullName
        } catch { Write-Warning "Kept old session $($old.Name): $($_.Exception.Message)" }
        finally { if ($oldLock) { $oldLock.Dispose() } }
    }

    $session = Join-Path $root ([Guid]::NewGuid().ToString('N'))
    New-Item -Path $session -ItemType Directory | Out-Null
    Set-Content -LiteralPath (Join-Path $session '.miniapps-session') -Value 'MiniApps session v1'
    $lease = [IO.File]::Open((Join-Path $session 'session.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $oldTemp = $env:TEMP; $oldTmp = $env:TMP; $oldSession = $env:MINIAPPS_SESSION
    try {
        $zip = Join-Path $session 'package.zip'
        if ($PackagePath) {
            if ($ExpectedSha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'A local package requires its expected SHA-256.' }
            Copy-Item -LiteralPath $PackagePath -Destination $zip
        } else {
            if ($ReleaseBase -notmatch '^https://github\.com/mson-ssh/miniapp/releases/(latest/download|download/v[a-zA-Z0-9._-]+)$') { throw 'Release URL is not an approved MiniApps GitHub release URL.' }
            # GitHub release assets use application/octet-stream. Windows PowerShell 5.1
            # therefore returns raw bytes/string instead of deserializing JSON.
            $manifestResponse = Invoke-WebRequest -Uri "$ReleaseBase/manifest-$rid.json" -UseBasicParsing -TimeoutSec 90
            $manifest = ConvertFrom-MiniAppsManifestContent -Content $manifestResponse.Content
            $asset = Select-MiniAppsAsset -Manifest $manifest -Target $target -Architecture $rid
            $ExpectedSha256 = [string]$asset.sha256
            Write-Host "Downloading MiniApps $target..." -ForegroundColor Cyan
            Invoke-WebRequest -Uri ([string]$asset.url) -OutFile $zip -UseBasicParsing -TimeoutSec 600
            if ((Get-Item -LiteralPath $zip).Length -ne [long]$asset.size) { throw 'Package size does not match the release manifest.' }
        }
        if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $ExpectedSha256) { throw 'Package SHA-256 mismatch. Nothing was executed.' }
        $appDir = Join-Path $session 'app'
        Expand-Archive -LiteralPath $zip -DestinationPath $appDir
        $exe = Join-Path $appDir 'MiniApps.exe'
        if (-not (Test-Path -LiteralPath $exe)) { throw 'Package does not contain MiniApps.exe.' }
        $env:TEMP = Join-Path $session 'temp'; $env:TMP = $env:TEMP; $env:MINIAPPS_SESSION = $session
        New-Item -Path $env:TEMP -ItemType Directory | Out-Null
        $start = @{ FilePath = $exe; WorkingDirectory = $appDir; Wait = $true; PassThru = $true }
        if ($Preview) { $start.ArgumentList = '--preview' }
        # Visible WPF is intentional. -Wait also waits for descendant processes on Windows.
        $process = Start-Process @start
        if ($process.ExitCode -ne 0) { Write-Warning "MiniApps exited with code $($process.ExitCode)." }
        # The process tree has ended; a crash may have left this conservative stale-session marker.
        $marker = Join-Path $session 'installing'
        if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force }
    } catch {
        Write-Warning "MiniApps: $($_.Exception.Message)"
        Add-Type -AssemblyName PresentationFramework
        [Windows.MessageBox]::Show($_.Exception.Message, 'MiniApps - startup failed') | Out-Null
    } finally {
        $env:TEMP = $oldTemp; $env:TMP = $oldTmp; $env:MINIAPPS_SESSION = $oldSession
        $lease.Dispose()
        try { Remove-Session $session }
        catch { Write-Warning "Cleanup deferred: $($_.Exception.Message). Session: $session" }
    }
}

if ($MyInvocation.InvocationName -ne '.') { Start-MiniApps }
