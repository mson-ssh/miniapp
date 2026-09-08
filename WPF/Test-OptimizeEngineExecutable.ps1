param([string]$Dotnet = 'dotnet')

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$project = Join-Path $projectRoot 'MiniApps.OptimizeEngine\MiniApps.OptimizeEngine.csproj'
$builder = Join-Path $projectRoot 'MiniApps.OptimizeEngine\Build-Payload.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('MiniApps-OptimizeEngineFixture-' + [Guid]::NewGuid().ToString('N'))
$tests = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:tests++
}

try {
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    & $Dotnet build $project -c Release -f net48 -t:Rebuild
    if ($LASTEXITCODE -ne 0) { throw 'Optimize engine build failed.' }
    $engine = Join-Path $projectRoot 'MiniApps.OptimizeEngine\bin\Release\net48\MiniApps.OptimizeEngine.exe'
    Assert-True (Test-Path -LiteralPath $engine -PathType Leaf) 'Optimize engine EXE was not built.'

    $isolated = Join-Path $fixture 'isolated'
    [IO.Directory]::CreateDirectory($isolated) | Out-Null
    $isolatedEngine = Join-Path $isolated 'MiniApps.OptimizeEngine.exe'
    Copy-Item -LiteralPath $engine -Destination $isolatedEngine
    $verify = @(& $isolatedEngine --verify)
    Assert-True ($LASTEXITCODE -eq 0) 'Standalone Optimize engine verification failed.'
    Assert-True (($verify -join "`n") -match 'files=352;sha256=[a-f0-9]{64};commit=6012b02ea282f23ea943946206762fd430025c6f') 'Standalone verification metadata is incomplete.'
    Assert-True (@(Get-ChildItem -LiteralPath $isolated -File).Count -eq 1) 'Verify mode extracted files beside the standalone EXE.'

    $invalid = Start-Process -FilePath $isolatedEngine -ArgumentList '--run' -WindowStyle Hidden -Wait -PassThru
    Assert-True ($invalid.ExitCode -eq 64) 'Optimize engine accepted --run without the required --silent flag.'

    $payloadA = Join-Path $fixture 'payload-a.zip'
    $payloadB = Join-Path $fixture 'payload-b.zip'
    & $builder -ProjectRoot $projectRoot -OutputPath $payloadA
    & $builder -ProjectRoot $projectRoot -OutputPath $payloadB
    $hashA = (Get-FileHash -LiteralPath $payloadA -Algorithm SHA256).Hash
    $hashB = (Get-FileHash -LiteralPath $payloadB -Algorithm SHA256).Hash
    Assert-True ($hashA -eq $hashB) 'Optimize payload build is not deterministic.'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($payloadA)
    try {
        Assert-True ($archive.Entries.Count -eq 352) 'Optimize payload contains an unexpected number of files.'
        Assert-True (@($archive.Entries | Where-Object { $_.FullName.StartsWith('/') -or $_.FullName.Contains('..') }).Count -eq 0) 'Optimize payload contains an unsafe path.'
    }
    finally { $archive.Dispose() }
}
finally {
    $fullFixture = [IO.Path]::GetFullPath($fixture)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($fullFixture.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($fullFixture) -match '^MiniApps-OptimizeEngineFixture-[a-f0-9]{32}$' -and
        (Test-Path -LiteralPath $fullFixture)) {
        Remove-Item -LiteralPath $fullFixture -Recurse -Force
    }
}
Write-Output "Optimize engine executable fixture passed: $tests checks."
