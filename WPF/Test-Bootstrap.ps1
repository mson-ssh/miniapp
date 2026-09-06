# A local disposable fixture; no downloads, installers, elevation or Windows changes.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'bootstrap.ps1')
$rejected = $false
try { Test-MiniAppsWindowsBuild -Build 17762 -DisplayVersion '1803' | Out-Null }
catch { $rejected = $_.Exception.Message -match 'build 17762' -and $_.Exception.Message -match '17763' }
if (-not $rejected) { throw 'Bootstrap must reject Windows build 17762 with the detected and required builds.' }
if (-not (Test-MiniAppsWindowsBuild -Build 17763 -DisplayVersion '1809')) { throw 'Bootstrap must accept Windows build 17763.' }
Write-Host 'PASS bootstrap Windows boundary: reject 17762, accept 17763.'

$targetCases = @(
    @{ Release = 0; Expected = 'net10' },
    @{ Release = 528039; Expected = 'net10' },
    @{ Release = 528040; Expected = 'net48' },
    @{ Release = 533325; Expected = 'net48' }
)
foreach ($case in $targetCases) {
    $actual = Select-MiniAppsTarget -FrameworkRelease $case.Release
    if ($actual -ne $case.Expected) { throw "Framework release $($case.Release) selected $actual instead of $($case.Expected)." }
}
Write-Host 'PASS bootstrap target selection: missing/lower => net10; 4.8/higher => net48.'

$manifestJson = '{"schemaVersion":2,"version":"0.2.0","architecture":"win-x64","assets":[]}'
$fromBytes = ConvertFrom-MiniAppsManifestContent -Content ([Text.Encoding]::UTF8.GetBytes($manifestJson))
$fromString = ConvertFrom-MiniAppsManifestContent -Content $manifestJson
if ($fromBytes.schemaVersion -ne 2 -or $fromString.schemaVersion -ne 2) { throw 'Manifest content decoder failed.' }
$invalidRejected = $false
try { ConvertFrom-MiniAppsManifestContent -Content '{invalid' | Out-Null } catch { $invalidRejected = $_.Exception.Message -match 'Invalid release manifest JSON' }
if (-not $invalidRejected) { throw 'Manifest content decoder must reject invalid JSON.' }
Write-Host 'PASS bootstrap manifest content decoding for PowerShell 5.1 byte/string responses.'

$manifest = [pscustomobject]@{
    schemaVersion = 2
    version = '0.2.0'
    architecture = 'win-x64'
    assets = @(
        [pscustomobject]@{ target = 'net48'; architecture = 'win-x64'; file = 'MiniApps-net48-win-x64.zip'; url = 'https://github.com/mson-ssh/miniapp/releases/download/v0.2.0/MiniApps-net48-win-x64.zip'; sha256 = ('a' * 64); size = 10; selfContained = $false },
        [pscustomobject]@{ target = 'net10'; architecture = 'win-x64'; file = 'MiniApps-net10-win-x64.zip'; url = 'https://github.com/mson-ssh/miniapp/releases/download/v0.2.0/MiniApps-net10-win-x64.zip'; sha256 = ('b' * 64); size = 20; selfContained = $true }
    )
}
if ((Select-MiniAppsAsset -Manifest $manifest -Target net48 -Architecture win-x64).file -ne 'MiniApps-net48-win-x64.zip') { throw 'Manifest did not select net48.' }
if ((Select-MiniAppsAsset -Manifest $manifest -Target net10 -Architecture win-x64).file -ne 'MiniApps-net10-win-x64.zip') { throw 'Manifest did not select net10.' }
$badHash = $manifest | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$badHash.assets[0].sha256 = 'invalid'
$rejected = $false
try { Select-MiniAppsAsset -Manifest $badHash -Target net48 -Architecture win-x64 | Out-Null } catch { $rejected = $_.Exception.Message -match 'SHA-256' }
if (-not $rejected) { throw 'Manifest must reject a malformed SHA-256.' }
$missing = $manifest | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$missing.assets = @($missing.assets | Where-Object { $_.target -ne 'net10' })
$rejected = $false
try { Select-MiniAppsAsset -Manifest $missing -Target net10 -Architecture win-x64 | Out-Null } catch { $rejected = $_.Exception.Message -match 'exactly one' }
if (-not $rejected) { throw 'Manifest must reject a missing selected asset.' }
Write-Host 'PASS bootstrap manifest selector and malformed/missing asset checks.'
$fixture = Join-Path $env:TEMP ('MiniApps-bootstrap-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$originalTemp = $env:TEMP; $originalTmp = $env:TMP; $oldResult = $env:MINIAPPS_FIXTURE_RESULT
try {
    $payload = Join-Path $fixture 'payload'
    New-Item -ItemType Directory -Path $payload | Out-Null
    $env:MINIAPPS_FIXTURE_RESULT = Join-Path $fixture 'result.txt'
    Add-Type -TypeDefinition 'public class BootstrapFixture { public static void Main(string[] args) { if (args.Length == 0 || args[0] != "child") { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName, "child") { UseShellExecute = false, CreateNoWindow = true }); return; } System.Threading.Thread.Sleep(500); System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable("MINIAPPS_FIXTURE_RESULT"), "child completed"); } }' -OutputAssembly (Join-Path $payload 'MiniApps.exe') -OutputType ConsoleApplication
    $package = Join-Path $fixture 'fixture.zip'
    Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $package
    $env:TEMP = Join-Path $fixture 'temp'; $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP | Out-Null
    Start-MiniApps -PackagePath $package -ExpectedSha256 (Get-FileHash $package -Algorithm SHA256).Hash -Preview
    if (-not (Test-Path -LiteralPath $env:MINIAPPS_FIXTURE_RESULT)) { throw 'Fixture/child did not finish before cleanup.' }
    $sessions = @(Get-ChildItem -LiteralPath (Join-Path $env:TEMP 'MiniApps') -Directory)
    if ($sessions.Count -ne 0) { throw 'Bootstrap did not clean its session.' }
    Write-Host 'PASS bootstrap: verify, extract, launch fixture, wait, clean session.'
    $files = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') + @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'MiniApps/Scripts') -Filter '*.ps1')
    foreach ($file in $files) {
        $tokens = $null; $errors = $null
        [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count) { throw "Parse failure: $($file.Name): $errors" }
    }
    Write-Host 'PASS PowerShell syntax.'
} finally {
    $env:TEMP = $originalTemp; $env:TMP = $originalTmp; $env:MINIAPPS_FIXTURE_RESULT = $oldResult
    $full = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($full) -ne [IO.Path]::GetFullPath($originalTemp).TrimEnd('\') -or [IO.Path]::GetFileName($full) -notmatch '^MiniApps-bootstrap-test-[a-f0-9]{32}$') { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $full -Recurse -Force
}
