param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$Client,
    [switch]$Seed
)
$ErrorActionPreference = 'Stop'
if ($Client -and $Seed) { throw 'Choose Client or Seed, not both.' }
# Create a NEW local cluster; never connect to the application's PostgreSQL service.
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('bookshare-stage7-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$data = Join-Path $testRoot 'data'
$passwordFile = Join-Path $testRoot 'password.tmp'
$randomBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($randomBytes) } finally { $rng.Dispose() }
$testPassword = [Convert]::ToBase64String($randomBytes)
[IO.File]::WriteAllText($passwordFile, $testPassword, [Text.UTF8Encoding]::new($false))
$previousConnection = $env:ConnectionStrings__BookShare
$previousPassword = $env:PGPASSWORD
$started = $false
$testExit = 1
try {
    & (Join-Path $PostgresBin 'initdb.exe') -D $data -U bookshare_test -A scram-sha-256 --pwfile=$passwordFile --encoding=UTF8 --locale=C *> (Join-Path $testRoot 'initdb.log')
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the isolated PostgreSQL test cluster.' }
    Remove-Item -LiteralPath $passwordFile
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    # Wait only for pg_ctl itself, not the long-lived postgres child process.
    $pgStart = Start-Process -FilePath (Join-Path $PostgresBin 'pg_ctl.exe') -WindowStyle Hidden -PassThru -ArgumentList @('-D', ('"' + $data + '"'), '-l', ('"' + (Join-Path $testRoot 'postgres.log') + '"'), '-o', ('"-h 127.0.0.1 -p ' + $port + '"'), '-w', 'start') -RedirectStandardOutput (Join-Path $testRoot 'start.log') -RedirectStandardError (Join-Path $testRoot 'start-error.log')
    $started = $true
    $null = $pgStart.WaitForExit(10000)
    & (Join-Path $PostgresBin 'pg_isready.exe') -h 127.0.0.1 -p $port -q
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the isolated PostgreSQL test cluster.' }
    $env:PGPASSWORD = $testPassword
    & (Join-Path $PostgresBin 'createdb.exe') -h 127.0.0.1 -p $port -U bookshare_test bookshare_test_stage7
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated test database.' }
    $env:ConnectionStrings__BookShare = "Host=127.0.0.1;Port=$port;Database=bookshare_test_stage7;Username=bookshare_test;Password=$testPassword"
    Push-Location (Split-Path $PSScriptRoot -Parent)
    try {
        if ($Seed) {
            dotnet run --project tests/BookShare.IntegrationTests --no-restore -p:BaseOutputPath=bin/Stage7/ -- --seed
        } elseif ($Client) {
            dotnet run --project tests/BookShare.IntegrationTests --no-restore -p:BaseOutputPath=bin/Stage7/ -- --client
        } else {
            dotnet run --project tests/BookShare.IntegrationTests --no-restore -p:BaseOutputPath=bin/Stage7/ -- --postgres
        }
        $testExit = $LASTEXITCODE
    } finally { Pop-Location }
} finally {
    $env:ConnectionStrings__BookShare = $previousConnection
    $env:PGPASSWORD = $previousPassword
    if (Test-Path -LiteralPath $passwordFile) { Remove-Item -LiteralPath $passwordFile }
    if ($started) {
        & (Join-Path $PostgresBin 'pg_ctl.exe') -D $data -m fast -w stop *> (Join-Path $testRoot 'stop.log')
    }
    Write-Host "Isolated test cluster stopped; artifacts retained at $testRoot"
}
exit $testExit
