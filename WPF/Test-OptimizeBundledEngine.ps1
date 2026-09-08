param(
    [string]$ProjectRoot = $PSScriptRoot,
    [string]$PublishedPath = '',
    [string]$PublicPath = ''
)

$ErrorActionPreference = 'Stop'
$project = [System.IO.Path]::GetFullPath($ProjectRoot)
$vendor = Join-Path $project 'ThirdParty\Win11Debloat'
$scriptPath = Join-Path $project 'MiniApps\Scripts\Optimize-Defaults.ps1'
$copyHelper = Join-Path $project 'MiniApps\Scripts\Copy-OptimizeEngine.ps1'
$configureHelper = Join-Path $project 'MiniApps\Scripts\Configure-OptimizeEngine.ps1'
$workerMonitor = Join-Path $project 'MiniApps\Scripts\Optimize-WorkerMonitor.ps1'
$appxWorker = Join-Path $project 'MiniApps\Scripts\Invoke-MiniAppsAppxWorker.ps1'
$wingetWorker = Join-Path $project 'MiniApps\Scripts\Invoke-MiniAppsWingetWorker.ps1'
$parallelEngines = Join-Path $project 'MiniApps\Scripts\Optimize-ParallelEngines.ps1'
$csproj = Join-Path $project 'MiniApps\MiniApps.csproj'
$engineProject = Join-Path $project 'MiniApps.OptimizeEngine\MiniApps.OptimizeEngine.csproj'
$payloadBuilder = Join-Path $project 'MiniApps.OptimizeEngine\Build-Payload.ps1'
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
Assert-True ($runner -match 'Set-MiniAppsOptimizeEngineConfiguration') 'Optimize runner does not configure its private engine copy.'
Assert-True ($runner -match "featureNames.Count -ne 16") 'Optimize runner does not validate the reviewed 16-feature lane.'
Assert-True ($runner -match '\$featureCommand = [^\r\n]+-RunDefaults') 'Feature lane bypasses the upstream Default compatibility filter.'
Assert-True ($runner -match '-RemoveApps -Apps Default -SkipRegistryBackup') 'Optimize runner does not invoke the reviewed Default app-removal lane.'
Assert-True ($runner -match '-MiniAppsAppxWorkerPath') 'Optimize runner does not pass the external Appx worker path.'
Assert-True (Test-Path -LiteralPath $workerMonitor -PathType Leaf) 'Optimize worker monitor is missing.'
Assert-True (Test-Path -LiteralPath $appxWorker -PathType Leaf) 'External Appx worker is missing.'
Assert-True (Test-Path -LiteralPath $wingetWorker -PathType Leaf) 'External WinGet worker is missing.'
Assert-True (Test-Path -LiteralPath $parallelEngines -PathType Leaf) 'Parallel engine orchestrator is missing.'
Assert-True ($runner -match 'Invoke-MiniAppsParallelEngines') 'Optimize runner does not start its two engine lanes together.'
Assert-True ($runner -match "Name = 'Features'") 'Optimize runner lacks the feature lane.'
Assert-True ($runner -match "Name = 'RemoveApps'") 'Optimize runner lacks the app-removal lane.'
Assert-True ($runner -match '\$featureCommand = [^\r\n]+\$commonArguments[^\r\n]+-MiniAppsLane Features') 'Feature lane no longer retains Registry backup.'
Assert-True ($runner.IndexOf('Set-MiniAppsOptimizeEngineConfiguration') -lt $runner.IndexOf("Write-Output 'MINIAPPS_STAGE:Applying'")) 'Durable backup configuration must finish before the Applying stage.'
Assert-True (-not ($runner -match 'Move-Item[\s\S]+Backups')) 'Optimize runner still moves its only Registry backup out of disposable work after applying changes.'
$projectText = Get-Content -LiteralPath $csproj -Raw
Assert-True ($projectText -match 'Condition="''\$\(TargetFramework\)'' == ''net48''"') 'Project has no net48-specific content group.'
Assert-True ($projectText -match 'MiniApps\.OptimizeEngine\\MiniApps\.OptimizeEngine\.csproj') 'Developer net48 does not build the silent Optimize engine.'
Assert-True ($projectText.Contains('<ItemGroup Condition="''$(TargetFramework)'' == ''net48'' and ''$(MiniAppsEdition)'' == ''Developer''">')) 'Optimize engine is not restricted to Developer net48.'
Assert-True (-not ($projectText -match 'ThirdParty\\Win11Debloat')) 'Main WPF project still packages the loose Win11Debloat tree.'
Assert-True (-not ($projectText -match '<Content\s+Include="\.\.\\ThirdParty\\Win11Debloat')) 'Vendored engine is still registered as WPF Content.'
Assert-True (Test-Path -LiteralPath $engineProject -PathType Leaf) 'Silent Optimize engine project is missing.'
Assert-True (Test-Path -LiteralPath $payloadBuilder -PathType Leaf) 'Optimize payload builder is missing.'
$engineProjectText = Get-Content -LiteralPath $engineProject -Raw
Assert-True ($engineProjectText.Contains('<EmbeddedResource Include="$(PayloadPath)" LogicalName="MiniApps.OptimizeEngine.Payload.zip"')) 'Optimize payload is not embedded in the engine EXE.'
if (-not [string]::IsNullOrWhiteSpace($PublishedPath)) {
    $publishedRoot = [System.IO.Path]::GetFullPath($PublishedPath)
    $publishedEngineExe = Join-Path $publishedRoot 'MiniApps.OptimizeEngine.exe'
    Assert-True (Test-Path -LiteralPath $publishedEngineExe -PathType Leaf) 'Developer net48 output lacks MiniApps.OptimizeEngine.exe.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $publishedRoot 'Engine'))) 'Developer output still contains a loose engine tree.'
    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $publishedRoot 'Scripts') -Filter '*Optimize*' -File -ErrorAction SilentlyContinue).Count -eq 0) 'Developer output still contains loose Optimize scripts.'
    $verifyOutput = @(& $publishedEngineExe --verify)
    Assert-True ($LASTEXITCODE -eq 0 -and ($verifyOutput -join "`n") -match 'MINIAPPS_ENGINE_VERIFY:files=352;sha256=[a-f0-9]{64}') 'Embedded Optimize payload verification failed.'

    Add-Type -AssemblyName PresentationCore
    $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $publishedRoot 'MiniApps.exe'))
    $contentFiles = @($assembly.GetCustomAttributesData() |
        Where-Object { $_.AttributeType.FullName -eq 'System.Windows.Resources.AssemblyAssociatedContentFileAttribute' } |
        ForEach-Object { [string]$_.ConstructorArguments[0].Value })
    Assert-True (-not ($contentFiles -contains 'mainwindow.xaml')) 'Published assembly still registers an external mainwindow.xaml content file.'
    $resourceStream = $assembly.GetManifestResourceStream('MiniApps.g.resources')
    Assert-True ($null -ne $resourceStream) 'Published assembly lacks compiled WPF resources.'
    $resourceReader = New-Object System.Resources.ResourceReader($resourceStream)
    try { $resourceKeys = @($resourceReader.GetEnumerator() | ForEach-Object { [string]$_.Key }) }
    finally { $resourceReader.Close(); $resourceStream.Dispose() }
    Assert-True ($resourceKeys -contains 'mainwindow.baml') 'Published assembly lacks the compiled MiniApps MainWindow resource.'
}
if (-not [string]::IsNullOrWhiteSpace($PublicPath)) {
    $publicRoot = [System.IO.Path]::GetFullPath($PublicPath)
    Assert-True (Test-Path -LiteralPath (Join-Path $publicRoot 'MiniApps.exe') -PathType Leaf) 'Public output lacks MiniApps.exe.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $publicRoot 'MiniApps.OptimizeEngine.exe'))) 'Public output unexpectedly contains the Developer Optimize engine.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $publicRoot 'Engine'))) 'Public output unexpectedly contains a loose engine tree.'
    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $publicRoot 'Scripts') -Filter '*Optimize*' -File -ErrorAction SilentlyContinue).Count -eq 0) 'Public output unexpectedly contains Optimize scripts.'
}

. $copyHelper
. $configureHelper
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
    $durableLogs = Join-Path $fixture 'durable-logs'
    New-Item -ItemType Directory -Path $durableLogs -Force | Out-Null
$durableBackup = Set-MiniAppsOptimizeEngineConfiguration -EngineRoot $destination -LogDirectory $durableLogs
    Assert-True ([System.IO.Path]::GetFullPath($durableBackup) -eq [System.IO.Path]::GetFullPath((Join-Path $durableLogs 'Backups'))) 'Private engine did not resolve Registry backups under the durable run directory.'
    Assert-True (Test-Path -LiteralPath $durableBackup -PathType Container) 'Durable Registry backup directory was not created before apply.'
$patchedEntry = Get-Content -LiteralPath (Join-Path $destination 'Win11Debloat.ps1') -Raw
Assert-True ($patchedEntry -match '\[switch\]\$MiniApps') 'Vendored engine has no first-class MiniApps host mode.'
Assert-True ($patchedEntry -match '\$script:RegistryBackupsPath = \[IO.Path\]::GetFullPath\(\$MiniAppsBackupPath\)') 'MiniApps host mode does not route Registry backups to its durable path.'
Assert-True ($patchedEntry -match '\. \(\[IO.Path\]::GetFullPath\(\$MiniAppsBridgePath\)\)') 'MiniApps host mode does not load its reporting adapter directly.'
Assert-True ($patchedEntry -match 'MiniAppsAppxWorkerPath') 'MiniApps host mode does not validate and expose the Appx worker path.'
Assert-True ($patchedEntry -match 'MiniAppsLane') 'MiniApps host mode does not scope task reporting by parallel lane.'
Assert-True ($patchedEntry -match "MiniAppsLane -eq 'Features'[\s\S]+Params\.Remove\('RemoveApps'\)[\s\S]+Params\.Remove\('Apps'\)") 'Feature lane does not remove app work after upstream Default filtering.'
    $importSettingsText = Get-Content -LiteralPath (Join-Path $destination 'Scripts\FileIO\Import-Settings.ps1') -Raw
    Assert-True ($importSettingsText -match 'MinVersion' -and $importSettingsText -match 'MaxVersion' -and $importSettingsText -match 'ModernStandbySupported') 'Upstream Default import no longer enforces Windows compatibility.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $destination 'Backups'))) 'Disposable engine unexpectedly contains the active Registry backup directory.'

    $profile = Get-Content -LiteralPath (Join-Path $destination 'Config\DefaultSettings.json') -Raw | ConvertFrom-Json
    Assert-True ($original.Version -eq '1.0' -and $profile.Version -eq $original.Version) 'Default profile schema changed.'
    Assert-True (@($original.Settings | Where-Object Name -eq 'CreateRestorePoint').Count -eq 1) 'Upstream profile no longer has the expected restore-point entry.'
    Assert-True (-not (@($profile.Settings | Where-Object Name -eq 'CreateRestorePoint'))) 'MiniApps profile did not remove CreateRestorePoint.'
    $expectedNames = @($original.Settings | Where-Object Name -ne 'CreateRestorePoint' | ForEach-Object Name)
    $actualNames = @($profile.Settings | ForEach-Object Name)
    Assert-True (($expectedNames -join '|') -eq ($actualNames -join '|')) 'MiniApps profile differs from upstream Default beyond CreateRestorePoint.'
    $entry = Join-Path $destination 'Win11Debloat.ps1'
    $entryText = Get-Content -LiteralPath $entry -Raw
    $anchor = [regex]'(?m)^Invoke-AllChanges\r?$'
    Assert-True ($anchor.Matches($entryText).Count -eq 1) 'Vendored engine apply entry point changed.'
    [void][ScriptBlock]::Create($entryText)
    Assert-True $true 'Copied MiniApps Debloat entry point does not parse.'

    $appRemovalText = Get-Content -LiteralPath (Join-Path $destination 'Scripts\AppRemoval\Remove-SelectedApps.ps1') -Raw
    Assert-True ($appRemovalText -match 'MiniAppsAppTimeoutSeconds') 'MiniApps Appx removal has no bounded execution time.'
    Assert-True ($appRemovalText -match 'MiniAppsRemoveAppsTimeoutSeconds') 'MiniApps RemoveApps group has no total deadline.'
    Assert-True ($appRemovalText -match 'Write-MiniAppsTask ''START'' \$miniAppsTaskId') 'MiniApps Appx removal has no lane-aware per-package activity reporting.'
    Assert-True ($appRemovalText -match '\[Diagnostics\.Process\]::Start') 'MiniApps Appx removal does not launch an external OS process.'
    Assert-True ($appRemovalText -match 'Wait-MiniAppsOptimizeWorker') 'MiniApps Appx removal does not monitor the external worker deadline.'
    Assert-True ($appRemovalText -match 'Write-MiniAppsTask ''OVERDUE'' \$miniAppsTaskId') 'MiniApps Appx removal does not expose the lane-aware overdue state.'
    Assert-True ($appRemovalText -match 'Invoke-MiniAppsWingetWorker\.ps1') 'MiniApps WinGet removal is not isolated in an external worker.'
    Assert-True ($appRemovalText -match 'WinGet worker for \$app exceeded') 'MiniApps WinGet worker has no overdue boundary.'

    # A blocked durable destination must fail before a caller can enter Applying.
    $blocked = Join-Path $fixture 'blocked-log-path'
    Set-Content -LiteralPath $blocked -Value 'file blocks directory creation' -Encoding UTF8
    $blockedEngine = Join-Path $work 'blocked-engine'
    Copy-MiniAppsOptimizeEngine -SourcePath $vendor -DestinationPath $blockedEngine
    $enteredApplying = $false
    try {
        Set-MiniAppsOptimizeEngineConfiguration -EngineRoot $blockedEngine -LogDirectory $blocked | Out-Null
        $enteredApplying = $true
    }
    catch { Assert-True ($_.Exception.Message -match 'directory|path|item') 'Blocked durable backup failure is not actionable.' }
    Assert-True (-not $enteredApplying) 'Optimize could enter Applying after durable backup preparation failed.'

    $missing = Join-Path $fixture 'missing'
    try { Copy-MiniAppsOptimizeEngine -SourcePath $missing -DestinationPath (Join-Path $work 'missing-copy'); throw 'Expected missing bundled engine failure.' }
    catch { Assert-True $_.Exception.Message.Contains('Bundled Win11Debloat engine is missing') 'Missing bundle error is not actionable.' }
}
finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

Write-Output "Bundled Win11Debloat engine fixture passed: $tests checks."
