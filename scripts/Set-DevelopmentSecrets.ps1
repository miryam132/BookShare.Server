# Values travel on stdin to User Secrets, never in command arguments or Git.
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/BookShare.API'
$secret = Read-Host 'Local PostgreSQL connection string (hidden; not the Render database)' -AsSecureString
$pointer = [IntPtr]::Zero
$previousEncoding = $OutputEncoding
try {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    $connection = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ([string]::IsNullOrWhiteSpace($connection)) { throw 'Connection string is required.' }
    $bytes = New-Object byte[] 48
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $settings = @{
        'ConnectionStrings:BookShare' = $connection
        'Jwt:SigningKey' = [Convert]::ToBase64String($bytes)
    }
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
    $settings | ConvertTo-Json -Compress | dotnet user-secrets set --project $project *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Could not save development secrets. No values were printed.' }
    Write-Host 'Development secrets saved outside the repository. This does not migrate or seed any database.'
} finally {
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    $secret.Dispose()
    $settings = $null
    $connection = $null
    if ($bytes) { [Array]::Clear($bytes, 0, $bytes.Length) }
    $OutputEncoding = $previousEncoding
}
