param(
    [Parameter(Mandatory)][string]$ProjectRoot,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ProjectRoot)
$output = [IO.Path]::GetFullPath($OutputPath)
$engine = Join-Path $root 'ThirdParty\Win11Debloat'
$scripts = Join-Path $root 'MiniApps\Scripts'
$helperNames = @(
    'Optimize-Defaults.ps1',
    'Optimize-TaskBridge.ps1',
    'Optimize-ParallelEngines.ps1',
    'Optimize-WorkerMonitor.ps1',
    'Invoke-MiniAppsAppxWorker.ps1',
    'Invoke-MiniAppsWingetWorker.ps1',
    'Copy-OptimizeEngine.ps1',
    'Configure-OptimizeEngine.ps1'
)

foreach ($required in @(
    (Join-Path $engine 'Win11Debloat.ps1'),
    (Join-Path $engine 'LICENSE'),
    (Join-Path $engine 'UPSTREAM.md'),
    (Join-Path $engine 'Config\Apps.json'),
    (Join-Path $engine 'Config\DefaultSettings.json'),
    (Join-Path $engine 'Config\Features.json')
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Optimize payload input is missing: $required" }
}
foreach ($name in $helperNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $scripts $name) -PathType Leaf)) { throw "Optimize helper is missing: $name" }
}

$parent = Split-Path -Parent $output
[IO.Directory]::CreateDirectory($parent) | Out-Null
$temporary = $output + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = $null
$archive = $null
try {
    $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    $entries = New-Object Collections.Generic.List[object]
    $enginePrefix = $engine.TrimEnd('\') + '\'
    foreach ($file in Get-ChildItem -LiteralPath $engine -Recurse -File -Force | Sort-Object FullName) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Optimize payload contains a reparse point: $($file.FullName)" }
        $relative = $file.FullName.Substring($enginePrefix.Length).Replace('\', '/')
        $entries.Add([pscustomobject]@{ Source = $file.FullName; Entry = 'Engine/Debloat/' + $relative })
    }
    foreach ($name in $helperNames) {
        $entries.Add([pscustomobject]@{ Source = (Join-Path $scripts $name); Entry = 'Scripts/' + $name })
    }
    foreach ($item in $entries | Sort-Object Entry) {
        $entry = $archive.CreateEntry($item.Entry, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $input = [IO.File]::OpenRead($item.Source)
        $destination = $entry.Open()
        try { $input.CopyTo($destination) }
        finally { $destination.Dispose(); $input.Dispose() }
    }
}
finally {
    if ($archive) { $archive.Dispose() }
    if ($stream) { $stream.Dispose() }
}
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
[IO.File]::Move($temporary, $output)
Write-Host "Built Optimize payload: $output"
