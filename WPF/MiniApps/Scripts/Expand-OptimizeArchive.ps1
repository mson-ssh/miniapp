# Safe, flattened extraction for the pinned Optimize archive. PowerShell 5.1 compatible.
function Expand-MiniAppsOptimizeArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$ExpectedRoot
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    if ([string]::IsNullOrWhiteSpace($ExpectedRoot) -or
        $ExpectedRoot.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        $ExpectedRoot.Contains('/') -or $ExpectedRoot.Contains('\')) {
        throw 'The expected ZIP root is invalid.'
    }

    $archiveFull = [IO.Path]::GetFullPath($ArchivePath)
    $destinationFull = [IO.Path]::GetFullPath($DestinationPath).TrimEnd('\')
    if ([IO.Directory]::Exists($destinationFull) -or [IO.File]::Exists($destinationFull)) {
        throw "Optimize extraction destination already exists: $destinationFull"
    }

    $destinationPrefix = $destinationFull + '\'
    $invalidNameChars = [IO.Path]::GetInvalidFileNameChars()
    $seenPaths = New-Object 'Collections.Generic.Dictionary[string,bool]' ([StringComparer]::OrdinalIgnoreCase)
    $items = New-Object Collections.Generic.List[object]
    [long]$totalLength = 0
    $archiveStream = $null
    $archive = $null
    try {
        $archiveStream = New-Object IO.FileStream($archiveFull, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $archive = New-Object IO.Compression.ZipArchive($archiveStream, [IO.Compression.ZipArchiveMode]::Read, $false)

        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            $isDirectory = $name.EndsWith('/', [StringComparison]::Ordinal)
            if ($name -eq ($ExpectedRoot + '/')) { continue }
            if (-not $name.StartsWith($ExpectedRoot + '/', [StringComparison]::Ordinal)) {
                throw "ZIP entry is outside the expected root: $($entry.FullName)"
            }

            $relative = $name.Substring($ExpectedRoot.Length + 1)
            if ($isDirectory) { $relative = $relative.TrimEnd('/') }
            if ([string]::IsNullOrWhiteSpace($relative)) { throw "ZIP entry has an empty path: $($entry.FullName)" }
            $segments = $relative.Split('/')
            foreach ($segment in $segments) {
                if ([string]::IsNullOrEmpty($segment) -or $segment -eq '.' -or $segment -eq '..' -or
                    $segment.IndexOfAny($invalidNameChars) -ge 0 -or $segment.EndsWith(' ') -or $segment.EndsWith('.')) {
                    throw "ZIP entry has an unsafe path: $($entry.FullName)"
                }
                $baseName = $segment.Split('.')[0]
                if ($baseName -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') {
                    throw "ZIP entry uses a reserved Windows name: $($entry.FullName)"
                }
            }

            $relativeWindows = $relative.Replace('/', '\')
            if ($seenPaths.ContainsKey($relativeWindows)) {
                throw "ZIP contains duplicate destination paths: $($entry.FullName)"
            }
            $seenPaths.Add($relativeWindows, $isDirectory)

            $target = [IO.Path]::GetFullPath([IO.Path]::Combine($destinationFull, $relativeWindows))
            if (-not $target.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "ZIP entry escapes the extraction destination: $($entry.FullName)"
            }
            if ($target.Length -gt $(if ($isDirectory) { 247 } else { 259 })) {
                throw "ZIP entry exceeds the Windows path limit: $($entry.FullName)"
            }
            $parent = if ($isDirectory) { $target } else { [IO.Path]::GetDirectoryName($target) }
            if ($parent.Length -gt 247) { throw "ZIP entry directory exceeds the Windows path limit: $($entry.FullName)" }
            if (-not $isDirectory) {
                if ($entry.Length -gt 536870912) { throw "ZIP entry is too large: $($entry.FullName)" }
                $totalLength += $entry.Length
                if ($totalLength -gt 1073741824) { throw 'Optimize ZIP expands beyond the allowed size.' }
            }
            $items.Add([PSCustomObject]@{ Entry = $entry; Relative = $relativeWindows; IsDirectory = $isDirectory })
        }

        if ($items.Count -eq 0) { throw 'Optimize ZIP does not contain files under the expected root.' }

        # Reject file/directory aliases, including conflicts created only by implicit parents.
        foreach ($item in $items) {
            $parts = $item.Relative.Split('\')
            for ($index = 1; $index -lt $parts.Length; $index++) {
                $ancestor = [string]::Join('\', $parts, 0, $index)
                if ($seenPaths.ContainsKey($ancestor) -and -not $seenPaths[$ancestor]) {
                    throw "ZIP path places content below a file: $($item.Entry.FullName)"
                }
            }
            if (-not $item.IsDirectory) {
                $filePrefix = $item.Relative + '\'
                foreach ($other in $seenPaths.Keys) {
                    if ($other.StartsWith($filePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                        throw "ZIP path uses a file as a directory: $($item.Entry.FullName)"
                    }
                }
            }
        }

        $parentPath = [IO.Path]::GetDirectoryName($destinationFull)
        $staging = [IO.Path]::Combine($parentPath, 'x' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        if ([IO.Directory]::Exists($staging) -or [IO.File]::Exists($staging)) { throw 'Could not allocate an Optimize extraction staging directory.' }
        foreach ($item in $items) {
            $stageTarget = [IO.Path]::GetFullPath([IO.Path]::Combine($staging, $item.Relative))
            if ($stageTarget.Length -gt $(if ($item.IsDirectory) { 247 } else { 259 })) {
                throw "ZIP entry exceeds the staging path limit: $($item.Entry.FullName)"
            }
            $stageParent = if ($item.IsDirectory) { $stageTarget } else { [IO.Path]::GetDirectoryName($stageTarget) }
            if ($stageParent.Length -gt 247) {
                throw "ZIP entry staging directory exceeds the Windows path limit: $($item.Entry.FullName)"
            }
        }

        [IO.Directory]::CreateDirectory($parentPath) | Out-Null
        [IO.Directory]::CreateDirectory($staging) | Out-Null
        $extractionError = $null
        try {
            foreach ($item in $items) {
                $stageTarget = [IO.Path]::Combine($staging, $item.Relative)
                if ($item.IsDirectory) {
                    [IO.Directory]::CreateDirectory($stageTarget) | Out-Null
                    continue
                }
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($stageTarget)) | Out-Null
                $entryStream = $null
                $output = $null
                try {
                    $entryStream = $item.Entry.Open()
                    $output = New-Object IO.FileStream($stageTarget, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    $entryStream.CopyTo($output)
                }
                finally {
                    if ($output) { $output.Dispose() }
                    if ($entryStream) { $entryStream.Dispose() }
                }
                if (([IO.FileInfo]$stageTarget).Length -ne $item.Entry.Length) {
                    throw "ZIP entry length changed while extracting: $($item.Entry.FullName)"
                }
            }
            [IO.Directory]::Move($staging, $destinationFull)
        }
        catch { $extractionError = $_ }
        finally {
            if ([IO.Directory]::Exists($staging)) {
                try { [IO.Directory]::Delete($staging, $true) }
                catch {
                    if (-not $extractionError) { throw }
                    Write-Warning ('Could not clean Optimize extraction staging directory: ' + $_.Exception.Message)
                }
            }
        }
        if ($extractionError) { throw $extractionError }
    }
    finally {
        if ($archive) { $archive.Dispose() }
        if ($archiveStream) { $archiveStream.Dispose() }
    }
}
