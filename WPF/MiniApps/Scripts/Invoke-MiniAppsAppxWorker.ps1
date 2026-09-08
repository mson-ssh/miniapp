param(
    [Parameter(Mandatory)][string]$AppPattern,
    [Parameter(Mandatory)][string]$TargetUser,
    [Parameter(Mandatory)][string]$ResultPath,
    [Parameter(Mandatory)][string]$LogPath
)

$ErrorActionPreference = 'Stop'
$workerMutexName = 'Global\MiniApps.DebloatAppxWorker'
$gate = New-Object Threading.Mutex($false, $workerMutexName)
$acquired = $false
$startedAt = [DateTimeOffset]::UtcNow
$errors = New-Object Collections.Generic.List[string]
$success = $false

try {
    # Parallel Optimize lanes may both need Appx. Wait briefly so Registry work can
    # remain concurrent while repository mutations stay serialized.
    try { $acquired = $gate.WaitOne(120000) }
    catch [Threading.AbandonedMutexException] { $acquired = $true }
    if (-not $acquired) { throw 'Another MiniApps Appx worker held the repository lock for more than 120 seconds.' }

    $getPackageParams = @{ Name = $AppPattern; ErrorAction = 'Continue'; ErrorVariable = '+operationErrors' }
    $removePackageParams = @{ ErrorAction = 'Continue'; ErrorVariable = '+operationErrors' }
    switch ($TargetUser) {
        'AllUsers' {
            $getPackageParams.AllUsers = $true
            $removePackageParams.AllUsers = $true
        }
        'CurrentUser' { }
        default {
            $account = New-Object Security.Principal.NTAccount($TargetUser)
            $sid = $account.Translate([Security.Principal.SecurityIdentifier]).Value
            $getPackageParams.User = $sid
            $removePackageParams.User = $sid
        }
    }

    $operationErrors = @()
    foreach ($package in @(Get-AppxPackage @getPackageParams)) {
        $removePackageParams.Package = $package.PackageFullName
        $null = Remove-AppxPackage @removePackageParams
    }
    if ($TargetUser -eq 'AllUsers') {
        $provisioned = @(Get-AppxProvisionedPackage -Online -ErrorAction Continue -ErrorVariable +operationErrors |
            Where-Object { $_.PackageName -like $AppPattern })
        foreach ($package in $provisioned) {
            $null = Remove-AppxProvisionedPackage -Online -AllUsers -PackageName $package.PackageName `
                -ErrorAction Continue -ErrorVariable +operationErrors
        }
    }
    foreach ($record in $operationErrors) { $errors.Add($record.ToString()) }
    $success = $errors.Count -eq 0
}
catch {
    $errors.Add($_.Exception.ToString())
}
finally {
    $endedAt = [DateTimeOffset]::UtcNow
    $result = [ordered]@{
        SchemaVersion = 1
        WorkerPid = $PID
        AppPattern = $AppPattern
        TargetUser = $TargetUser
        StartedAtUtc = $startedAt
        EndedAtUtc = $endedAt
        Success = $success
        Errors = @($errors)
    }
    try {
        $resultDirectory = Split-Path -Parent $ResultPath
        [IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
        $temporary = $ResultPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporary -Encoding UTF8
        Move-Item -LiteralPath $temporary -Destination $ResultPath -Force
        if ($errors.Count -gt 0) {
            [IO.Directory]::CreateDirectory((Split-Path -Parent $LogPath)) | Out-Null
            @($errors) | Set-Content -LiteralPath $LogPath -Encoding UTF8
        }
    }
    finally {
        if ($acquired) { $gate.ReleaseMutex() }
        $gate.Dispose()
    }
}

if ($success) { exit 0 }
exit 1
