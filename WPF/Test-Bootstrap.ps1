# A local disposable fixture; no downloads, installers, elevation or Windows changes.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'bootstrap.ps1')
$rejected = $false
try { Test-MiniAppsWindowsBuild -Build 17762 -DisplayVersion '1803' | Out-Null }
catch { $rejected = $_.Exception.Message -match 'build 17762' -and $_.Exception.Message -match '17763' }
if (-not $rejected) { throw 'Bootstrap must reject Windows build 17762 with the detected and required builds.' }
if (-not (Test-MiniAppsWindowsBuild -Build 17763 -DisplayVersion '1809')) { throw 'Bootstrap must accept Windows build 17763.' }
Write-Host 'PASS bootstrap Windows boundary: reject 17762, accept 17763.'
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
