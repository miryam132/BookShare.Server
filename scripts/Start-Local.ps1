param(
    [string]$DatabaseHost = 'localhost',
    [int]$DatabasePort = 5432,
    [string]$DatabaseName = 'bookshare',
    [string]$DatabaseUser = 'bookshare',
    [switch]$UpdateDatabase,
    [switch]$Seed
)

$ErrorActionPreference = 'Stop'
if ($UpdateDatabase -and $Seed) { throw 'Run migration and seed as separate controlled operations.' }
$previousMaintenance = $env:Maintenance__Database
$previousSeedEnabled = $env:DemoSeed__Enabled
$seedPrevious = @{}
$previousConnection = $env:ConnectionStrings__BookShare
$previousJwtKey = $env:Jwt__SigningKey
$databasePassword = Read-Host 'PostgreSQL password' -AsSecureString
$passwordPointer = [IntPtr]::Zero
try {
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($databasePassword)
    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    $connection['Host'] = $DatabaseHost
    $connection['Port'] = $DatabasePort
    $connection['Database'] = $DatabaseName
    $connection['Username'] = $DatabaseUser
    $connection['Password'] = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $connection['Timeout'] = 5
    $env:ConnectionStrings__BookShare = $connection.ConnectionString
    $connection.Clear()
    if ($UpdateDatabase -or $Seed) {
        $confirmed = Read-Host 'Type the exact target database name to authorize this maintenance operation'
        if ($confirmed -cne $DatabaseName) { throw 'Target database confirmation did not match.' }
        $env:Maintenance__Database = $DatabaseName
        if ($Seed) {
            foreach ($role in @('Admin', 'Owner', 'Borrower1', 'Borrower2')) {
                $variable = 'DemoSeed__' + $role + 'Password'
                $seedPrevious[$variable] = [Environment]::GetEnvironmentVariable($variable, 'Process')
                $secret = Read-Host "Demo $role password (distinct, 16-128 characters; save privately)" -AsSecureString
                $pointer = [IntPtr]::Zero
                try {
                    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
                    [Environment]::SetEnvironmentVariable($variable, [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer), 'Process')
                } finally {
                    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
                    $secret.Dispose()
                }
            }
            $env:DemoSeed__Enabled = 'true'
        }
        Push-Location (Join-Path $PSScriptRoot '..')
        try {
            $operation = if ($Seed) { '--maintenance-seed' } else { '--maintenance-migrate' }
            dotnet run --project src/BookShare.API --no-launch-profile -- $operation
            if ($LASTEXITCODE -ne 0) { throw 'Database maintenance failed.' }
        }
        finally { Pop-Location }
    }
    else {
        if ([string]::IsNullOrWhiteSpace($env:Jwt__SigningKey)) {
            $keyBytes = New-Object byte[] 32
            $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
            try { $rng.GetBytes($keyBytes) }
            finally { $rng.Dispose() }
            $env:Jwt__SigningKey = [Convert]::ToBase64String($keyBytes)
            Write-Host 'Using a temporary JWT signing key; tokens expire when this API session ends.'
        }
        dotnet run --project (Join-Path $PSScriptRoot '../src/BookShare.API') --launch-profile http
    }
    if ($LASTEXITCODE -ne 0) { throw 'The API exited unsuccessfully.' }
}
finally {
    $env:ConnectionStrings__BookShare = $previousConnection
    $env:Jwt__SigningKey = $previousJwtKey
    $env:Maintenance__Database = $previousMaintenance
    $env:DemoSeed__Enabled = $previousSeedEnabled
    foreach ($variable in $seedPrevious.Keys) {
        [Environment]::SetEnvironmentVariable($variable, $seedPrevious[$variable], 'Process')
    }
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    $databasePassword.Dispose()
}
