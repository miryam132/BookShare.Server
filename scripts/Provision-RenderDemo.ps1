param([ValidateSet('Migrate', 'Seed')][string]$Operation = 'Migrate')

$ErrorActionPreference = 'Stop'
# This helper is intentionally pinned to the separately approved cloud demo.
$databaseName = 'bookshare_demo'
$databaseHost = 'dpg-dbb5vjajnfac73f43mmg-a.frankfurt-postgres.render.com'
$databaseUser = 'bookshare_demo_user'
$roles = @('Admin', 'Owner', 'Borrower1', 'Borrower2')
$names = @('ConnectionStrings__BookShare', 'Maintenance__Database', 'DemoSeed__Enabled')
$names += $roles | ForEach-Object { 'DemoSeed__' + $_ + 'Password' }
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
try {
    docker image inspect bookshare-api:stage9 --format '{{.Id}}' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Start Docker and ensure the verified bookshare-api:stage9 image exists.' }
    Write-Host "Cloud demo only: $databaseHost / $databaseName. Local application database is not used."
    $confirmation = Read-Host "Type $databaseName to confirm $Operation"
    if ($confirmation -cne $databaseName) { throw 'Target confirmation did not match.' }
    $secret = Read-Host 'Cloud PostgreSQL password (hidden)' -AsSecureString
    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
        $connection['Host'] = $databaseHost
        $connection['Port'] = 5432
        $connection['Database'] = $databaseName
        $connection['Username'] = $databaseUser
        $connection['Password'] = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        $connection['SSL Mode'] = 'VerifyFull'
        $connection['Include Error Detail'] = $false
        $env:ConnectionStrings__BookShare = $connection.ConnectionString
    } finally {
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
        $secret.Dispose()
        $connection.Clear()
    }
    $env:Maintenance__Database = $databaseName
    if ($Operation -eq 'Migrate') {
        docker run --rm --env ConnectionStrings__BookShare --env Maintenance__Database bookshare-api:stage9 --maintenance-migrate
        if ($LASTEXITCODE -ne 0) { throw 'Cloud migration failed. No seed was attempted.' }
    } else {
        foreach ($role in $roles) {
            $secret = Read-Host "Demo $role password (distinct, 16-128 characters; save in your password manager)" -AsSecureString
            $pointer = [IntPtr]::Zero
            try {
                $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
                $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
                if ([string]::IsNullOrWhiteSpace($plain) -or $plain.Length -lt 16 -or $plain.Length -gt 128) {
                    throw "Demo $role password must contain 16-128 characters. No seed was attempted."
                }
                foreach ($earlierRole in $roles) {
                    if ($earlierRole -eq $role) { break }
                    $earlier = [Environment]::GetEnvironmentVariable(('DemoSeed__' + $earlierRole + 'Password'), 'Process')
                    if ([string]::Equals($plain, $earlier, [StringComparison]::Ordinal)) {
                        throw 'Demo passwords must all be different. No seed was attempted.'
                    }
                }
                [Environment]::SetEnvironmentVariable(('DemoSeed__' + $role + 'Password'), $plain, 'Process')
            } finally {
                $plain = $null
                $earlier = $null
                if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
                $secret.Dispose()
            }
        }
        $env:DemoSeed__Enabled = 'true'
        $arguments = @('run', '--rm', '--env', 'ConnectionStrings__BookShare', '--env', 'Maintenance__Database', '--env', 'DemoSeed__Enabled')
        foreach ($role in $roles) { $arguments += @('--env', ('DemoSeed__' + $role + 'Password')) }
        $arguments += @('bookshare-api:stage9', '--maintenance-seed')
        & docker @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Cloud seed failed. No repeat was attempted.' }
        & docker @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Repeated cloud seed failed.' }
        Write-Host 'Seed and repeated seed completed. Keep demo passwords privately; existing passwords are never reset.'
    }
} finally {
    $connection.Clear()
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
