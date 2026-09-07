# Extraction-only fixtures. No upstream code is executed.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
. (Join-Path $PSScriptRoot 'MiniApps\Scripts\Expand-OptimizeArchive.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ma-zip-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$expectedRoot = 'Win11Debloat-6012b02ea282f23ea943946206762fd430025c6f'
function New-FixtureZip([string]$Path, [string[]]$Names) {
    $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $Names) {
            $entry = $archive.CreateEntry($name)
            if (-not $name.EndsWith('/')) {
                $writer = New-Object IO.StreamWriter($entry.Open())
                try { $writer.Write('fixture text - not executable') } finally { $writer.Dispose() }
            }
        }
    } finally { $archive.Dispose() }
}
try {
    $longName = 'Regfiles/Hide_duplicate_removable_drives_from_navigation_pane_of_File_Explorer.reg'
    # Recreate a nested irm session. The legacy root exceeds MAX_PATH; flattened src fits.
    $session = Join-Path $testRoot ('MiniApps\' + ('a' * 32) + '\temp\MiniApps')
    $old = Join-Path $session ('optimize-' + ('b' * 32) + '\' + $expectedRoot + '\' + $longName)
    $destination = Join-Path $session 'o12345678\src'
    if ($old.Length -lt 260) { throw 'Fixture must exercise the old long-path failure.' }
    $zip = Join-Path $testRoot 'valid.zip'
    New-FixtureZip $zip @("$expectedRoot/", "$expectedRoot/.github/", "$expectedRoot/$longName", "$expectedRoot/Config/DefaultSettings.json")
    Expand-MiniAppsOptimizeArchive -ArchivePath $zip -DestinationPath $destination -ExpectedRoot $expectedRoot
    $actual = Join-Path $destination $longName
    if (-not [IO.File]::Exists($actual)) { throw 'Long-name file was not extracted under short src.' }
    if ([IO.File]::ReadAllText($actual) -ne 'fixture text - not executable') { throw 'Extracted content mismatch.' }
    Write-Output "PASS nested irm extraction: old path $($old.Length) chars, flattened path $($actual.Length) chars."

    $cases = @(
        @{ Name='traversal'; Entries=@("$expectedRoot/../escape.txt") },
        @{ Name='nested-traversal'; Entries=@("$expectedRoot/dir/../../escape.txt") },
        @{ Name='foreign-root'; Entries=@('OtherRoot/file.txt') },
        @{ Name='duplicate'; Entries=@("$expectedRoot/same.txt", "$expectedRoot/SAME.txt") },
        @{ Name='absolute'; Entries=@('/outside.txt') },
        @{ Name='ads'; Entries=@("$expectedRoot/file.txt:stream") },
        @{ Name='too-long'; Entries=@("$expectedRoot/" + ('z' * 240) + '.txt') }
    )
    foreach ($case in $cases) {
        $badZip = Join-Path $testRoot ($case.Name + '.zip')
        $badDest = Join-Path $testRoot ($case.Name + '-out')
        New-FixtureZip $badZip (@("$expectedRoot/first.txt") + $case.Entries)
        $failed = $false
        try { Expand-MiniAppsOptimizeArchive -ArchivePath $badZip -DestinationPath $badDest -ExpectedRoot $expectedRoot } catch { $failed = $true }
        if (-not $failed) { throw "Unsafe fixture accepted: $($case.Name)" }
        if (Test-Path -LiteralPath (Join-Path $badDest 'first.txt')) { throw "Validation wrote files before rejecting $($case.Name)." }
        Write-Output "PASS reject $($case.Name) before extraction."
    }
    $corrupt = Join-Path $testRoot 'corrupt.zip'
    [IO.File]::WriteAllText($corrupt, 'not a zip')
    $failed = $false
    try { Expand-MiniAppsOptimizeArchive -ArchivePath $corrupt -DestinationPath (Join-Path $testRoot 'corrupt-out') -ExpectedRoot $expectedRoot } catch { $failed = $true }
    if (-not $failed) { throw 'Corrupt ZIP accepted.' }
    Write-Output 'PASS corrupt ZIP rejected. No Windows settings or installers ran.'
}
finally {
    # Validate the exact generated fixture directory before recursive cleanup.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ma-zip-[a-f0-9]{8}$') { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
