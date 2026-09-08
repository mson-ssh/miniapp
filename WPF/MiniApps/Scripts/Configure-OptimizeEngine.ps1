function Set-MiniAppsOptimizeEngineConfiguration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$EngineRoot,
        [Parameter(Mandatory)][string]$LogDirectory
    )

    $root = [System.IO.Path]::GetFullPath($EngineRoot)
    $logs = [System.IO.Path]::GetFullPath($LogDirectory)
    $entry = Join-Path $root 'Win11Debloat.ps1'
    $defaultsPath = Join-Path $root 'Config\DefaultSettings.json'
    if (-not (Test-Path -LiteralPath $entry -PathType Leaf)) { throw 'Bundled Win11Debloat entry point is missing.' }
    if (-not (Test-Path -LiteralPath $defaultsPath -PathType Leaf)) { throw 'Bundled Win11Debloat default profile is missing.' }

    # Prove that the durable location is writable before upstream can apply anything.
    $backupPath = Join-Path $logs 'Backups'
    New-Item -ItemType Directory -Path $backupPath -Force -ErrorAction Stop | Out-Null
    $probe = Join-Path $backupPath ('.write-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $stream = [System.IO.File]::Open($probe, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        $stream.Dispose()
        Remove-Item -LiteralPath $probe -Force -ErrorAction Stop
    }
    catch {
        try { if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue } } catch { }
        throw "Durable Registry backup directory is not writable: $($_.Exception.Message)"
    }

    $defaults = Get-Content -LiteralPath $defaultsPath -Raw | ConvertFrom-Json
    if ($defaults.Version -ne '1.0') { throw 'Unexpected default profile schema.' }
    $defaults.Settings = @($defaults.Settings | Where-Object { $_.Name -ne 'CreateRestorePoint' })
    $defaults | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $defaultsPath -Encoding UTF8

    return $backupPath
}
