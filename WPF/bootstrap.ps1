# MiniApps bootstrap: irm https://raw.githubusercontent.com/mson-ssh/miniapp/main/WPF/bootstrap.ps1 | iex
# Publish the ZIP and SHA-256 assets with Publish.ps1 before distributing this command.
function Start-MiniApps {
    param(
        [string]$ReleaseBase = 'https://github.com/mson-ssh/miniapp/releases/latest/download',
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
        $escapedBase = $ReleaseBase.Replace("'", "''")
        $escapedPath = $PackagePath.Replace("'", "''")
        $escapedHash = $ExpectedSha256.Replace("'", "''")
        $command = "function Start-MiniApps { $body }; Start-MiniApps -ReleaseBase '$escapedBase' -PackagePath '$escapedPath' -ExpectedSha256 '$escapedHash'"
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"
        return
    }
    $os = [Environment]::OSVersion.Version
    if ($os.Build -lt 19044 -and -not $Preview) { throw 'MiniApps requires Windows 10 21H2 (build 19044) or later. Windows 11 is supported.' }
    # .NET Framework 4.8 is an OS component on the supported Windows builds.
    # Inspect the explicit registry view so a 32-bit bootstrap also detects it.
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
    if ($release -lt 528040) { throw 'MiniApps requires the Windows .NET Framework 4.8 component. No runtime was installed. Check or repair this Windows component.' }
    $arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    $rid = switch ($arch) { 'AMD64' { 'win-x64' }; 'ARM64' { throw 'This net48 release has not been validated for ARM64. Use a validated ARM64 release.' }; 'x86' { 'win-x86' }; default { throw "Unsupported architecture: $arch" } }
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
            if ($ReleaseBase -notmatch '^https://') { throw 'Release URL must use HTTPS.' }
            $asset = "MiniApps-$rid.zip"
            Write-Host 'Downloading MiniApps...' -ForegroundColor Cyan
            # Cross-check the checksum and manifest, then download from the immutable versioned URL.
            $hashResponse = Invoke-WebRequest -Uri "$ReleaseBase/$asset.sha256" -UseBasicParsing -TimeoutSec 90
            $ExpectedSha256 = ([string]$hashResponse.Content).Trim().Split(' ')[0]
            if ($ExpectedSha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid release checksum.' }
            # Release maintainers publish immutable versioned URLs in the companion manifest.
            $manifest = Invoke-RestMethod -Uri "$ReleaseBase/manifest-$rid.json" -TimeoutSec 90
            if ($manifest.sha256 -ne $ExpectedSha256 -or $manifest.url -notmatch '^https://github.com/mson-ssh/miniapp/releases/download/') { throw 'Release manifest mismatch; please retry.' }
            Invoke-WebRequest -Uri $manifest.url -OutFile $zip -UseBasicParsing -TimeoutSec 600
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
