param(
    [string]$ProjectRoot = $PSScriptRoot,
    [string]$PublishedPath = ''
)

$ErrorActionPreference = 'Stop'
$project = [System.IO.Path]::GetFullPath($ProjectRoot)
$vendor = Join-Path $project 'ThirdParty\Win11Debloat'
$scriptPath = Join-Path $project 'MiniApps\Scripts\Optimize-Defaults.ps1'
$copyHelper = Join-Path $project 'MiniApps\Scripts\Copy-OptimizeEngine.ps1'
$csproj = Join-Path $project 'MiniApps\MiniApps.csproj'
$expectedCommit = '6012b02ea282f23ea943946206762fd430025c6f'
$tests = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:tests++
}

Assert-True (Test-Path -LiteralPath $vendor -PathType Container) 'Vendored engine directory is missing.'
Assert-True (Test-Path -LiteralPath (Join-Path $vendor 'LICENSE') -PathType Leaf) 'Vendored MIT license is missing.'
Assert-True ((Get-Content -LiteralPath (Join-Path $vendor 'UPSTREAM.md') -Raw).Contains($expectedCommit)) 'Vendored provenance does not pin the reviewed commit.'
foreach ($required in @('Win11Debloat.ps1', 'Config\DefaultSettings.json', 'Config\Apps.json', 'Assets', 'Regfiles', 'Schemas', 'Scripts')) {
    Assert-True (Test-Path -LiteralPath (Join-Path $vendor $required)) "Vendored engine missing $required."
}

$runner = Get-Content -LiteralPath $scriptPath -Raw
Assert-True (-not ($runner -match 'Invoke-WebRequest|github\.com|archive/|Expand-MiniAppsOptimizeArchive')) 'Active Optimize runner still contains an upstream download/extraction path.'
Assert-True ($runner -match 'Copy-MiniAppsOptimizeEngine') 'Optimize runner does not use the bundled engine copy helper.'
Assert-True ($runner -match '-RunDefaults\s+-Silent\s+-SkipExplorerRestart\s+-LogPath') 'Optimize runner no longer invokes the reviewed default CLI command.'
Assert-True (-not ($runner -match 'SkipRegistryBackup')) 'Optimize runner disables the Registry backup.'
$projectText = Get-Content -LiteralPath $csproj -Raw
Assert-True ($projectText -match 'Condition="''\$\(TargetFramework\)'' == ''net48''"') 'Project has no net48-specific content group.'
Assert-True ($projectText -match 'ThirdParty\\Win11Debloat') 'Project does not package the bundled engine.'
Assert-True ($projectText -match 'Engine\\Debloat') 'Project does not place the bundled engine under the short runtime path.'
if (-not [string]::IsNullOrWhiteSpace($PublishedPath)) {
    $publishedEngine = Join-Path ([System.IO.Path]::GetFullPath($PublishedPath)) 'Engine\Debloat'
    Assert-True (Test-Path -LiteralPath (Join-Path $publishedEngine 'Win11Debloat.ps1') -PathType Leaf) 'Published net48 output lacks the bundled entry point.'
    Assert-True (Test-Path -LiteralPath (Join-Path $publishedEngine 'LICENSE') -PathType Leaf) 'Published net48 output lacks the MIT license.'
    Assert-True (Test-Path -LiteralPath (Join-Path $publishedEngine 'UPSTREAM.md') -PathType Leaf) 'Published net48 output lacks provenance.'
}

. $copyHelper
$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ('MiniApps-BundledEngineFixture-' + [Guid]::NewGuid().ToString('N'))
$work = Join-Path $fixture ('o-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
$destination = Join-Path $work 'src'
try {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    Copy-MiniAppsOptimizeEngine -SourcePath $vendor -DestinationPath $destination
    Assert-True (Test-Path -LiteralPath (Join-Path $destination 'Win11Debloat.ps1') -PathType Leaf) 'Private engine copy has no entry point.'
    Assert-True (Test-Path -LiteralPath (Join-Path $destination 'LICENSE') -PathType Leaf) 'Private engine copy has no license.'
    $sourceFiles = @(Get-ChildItem -LiteralPath $vendor -Recurse -File)
    $destinationFiles = @(Get-ChildItem -LiteralPath $destination -Recurse -File)
    Assert-True ($sourceFiles.Count -eq $destinationFiles.Count) 'Private engine copy does not contain every bundled file.'
    $longest = ($destinationFiles | ForEach-Object { $_.FullName.Length } | Measure-Object -Maximum).Maximum
    Assert-True ($longest -le 259) "Bundled engine working path exceeds legacy MAX_PATH: $longest."

    $original = Get-Content -LiteralPath (Join-Path $vendor 'Config\DefaultSettings.json') -Raw | ConvertFrom-Json
    $profile = Get-Content -LiteralPath (Join-Path $destination 'Config\DefaultSettings.json') -Raw | ConvertFrom-Json
    $profile.Settings = @($profile.Settings | Where-Object { $_.Name -ne 'CreateRestorePoint' })
    Assert-True ($original.Version -eq '1.0' -and $profile.Version -eq $original.Version) 'Default profile schema changed.'
    Assert-True (@($original.Settings | Where-Object Name -eq 'CreateRestorePoint').Count -eq 1) 'Upstream profile no longer has the expected restore-point entry.'
    Assert-True (-not (@($profile.Settings | Where-Object Name -eq 'CreateRestorePoint'))) 'MiniApps profile did not remove CreateRestorePoint.'
    $expectedNames = @($original.Settings | Where-Object Name -ne 'CreateRestorePoint' | ForEach-Object Name)
    $actualNames = @($profile.Settings | ForEach-Object Name)
    Assert-True (($expectedNames -join '|') -eq ($actualNames -join '|')) 'MiniApps profile differs from upstream Default beyond CreateRestorePoint.'
    $profile | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $destination 'Config\DefaultSettings.json') -Encoding UTF8
    $entry = Join-Path $destination 'Win11Debloat.ps1'
    $entryText = Get-Content -LiteralPath $entry -Raw
    $anchor = [regex]'(?m)^Invoke-AllChanges\r?$'
    Assert-True ($anchor.Matches($entryText).Count -eq 1) 'Pinned upstream task reporting anchor changed.'
    $bridge = Get-Content -LiteralPath (Join-Path $project 'MiniApps\Scripts\Optimize-TaskBridge.ps1') -Raw
    $entryText = $anchor.Replace($entryText, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $bridge + "`r`n" + $match.Value })
    [void][ScriptBlock]::Create($entryText)
    Assert-True $true 'Copied upstream entry point with task bridge does not parse.'

    $missing = Join-Path $fixture 'missing'
    try { Copy-MiniAppsOptimizeEngine -SourcePath $missing -DestinationPath (Join-Path $work 'missing-copy'); throw 'Expected missing bundled engine failure.' }
    catch { Assert-True $_.Exception.Message.Contains('Bundled Win11Debloat engine is missing') 'Missing bundle error is not actionable.' }
}
finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

Write-Output "Bundled Win11Debloat engine fixture passed: $tests checks."
