# Copies the bundled Win11Debloat engine into one short, private working tree.
# The engine in Engine\Debloat is read-only product content; all run-time edits happen here.
function Copy-MiniAppsOptimizeEngine {
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $source = [System.IO.Path]::GetFullPath($SourcePath)
    $destination = [System.IO.Path]::GetFullPath($DestinationPath)
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Bundled Win11Debloat engine is missing: $source"
    }
    foreach ($required in @('Win11Debloat.ps1', 'Config\DefaultSettings.json', 'Config\Apps.json', 'Regfiles', 'Assets', 'Schemas', 'Scripts')) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $required))) {
            throw "Bundled Win11Debloat engine is incomplete: missing $required"
        }
    }
    if (Test-Path -LiteralPath $destination) {
        throw "Optimize working directory already exists: $destination"
    }

    $parent = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw "Optimize working directory parent is missing: $parent"
    }
    $staging = Join-Path $parent ('e-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
    $primaryError = $null
    try {
        [System.IO.Directory]::CreateDirectory($staging) | Out-Null
        $sourcePrefix = $source.TrimEnd('\') + '\'
        foreach ($item in Get-ChildItem -LiteralPath $source -Force -Recurse) {
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Bundled Win11Debloat engine contains a reparse point: $($item.FullName)"
            }
            if (-not $item.FullName.StartsWith($sourcePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Bundled Win11Debloat path escaped its root: $($item.FullName)"
            }
            $relative = $item.FullName.Substring($sourcePrefix.Length)
            $invalidSegments = @($relative.Split([char]'\') | Where-Object { $_ -eq '.' -or $_ -eq '..' })
            if ([string]::IsNullOrWhiteSpace($relative) -or $invalidSegments.Count -gt 0) {
                throw "Bundled Win11Debloat engine contains an invalid path: $relative"
            }
            $target = [System.IO.Path]::GetFullPath((Join-Path $staging $relative))
            if (-not $target.StartsWith($staging.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Bundled Win11Debloat path escaped its destination: $relative"
            }
            if ($target.Length -gt 259) {
                throw "Bundled Win11Debloat path is too long for Windows PowerShell: $target"
            }
            if ($item.PSIsContainer) {
                [System.IO.Directory]::CreateDirectory($target) | Out-Null
            } else {
                [System.IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
                [System.IO.File]::Copy($item.FullName, $target, $false)
            }
        }
        [System.IO.Directory]::Move($staging, $destination)
    }
    catch {
        $primaryError = $_
        throw
    }
    finally {
        if (Test-Path -LiteralPath $staging) {
            try { [System.IO.Directory]::Delete($staging, $true) }
            catch { if (-not $primaryError) { throw } }
        }
    }
}
