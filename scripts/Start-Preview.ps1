# Disposable local preview. Never uses the application or Render database.
param([string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin')
$ErrorActionPreference = 'Stop'
$server = Split-Path $PSScriptRoot -Parent
$client = Join-Path (Split-Path $server -Parent) 'BookShare.Client'
$dll = Join-Path $server 'src/BookShare.API/bin/Stage7/Debug/net10.0/BookShare.API.dll'
if (!(Test-Path -LiteralPath $dll)) { throw 'Build Stage7 output first.' }
if (Get-NetTCPConnection -LocalPort 5173 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 5173 is already in use.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('bookshare-preview-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$data = Join-Path $root 'data'
$passwordFile = Join-Path $root 'password.tmp'
$processes = @()
$started = $false
$names = @('PGPASSWORD', 'ConnectionStrings__BookShare', 'Maintenance__Database', 'DemoSeed__Enabled', 'Jwt__SigningKey', 'ASPNETCORE_URLS', 'ASPNETCORE_ENVIRONMENT', 'Hosting__BehindTlsProxy', 'Cors__AllowedOrigins__0', 'VITE_API_BASE_URL')
$roles = @('Admin', 'Owner', 'Borrower1', 'Borrower2')
$names += $roles | ForEach-Object { 'DemoSeed__' + $_ + 'Password' }
$names += @(Get-ChildItem Env: | Where-Object Name -like 'Cors__*' | Select-Object -ExpandProperty Name)
$names = @($names | Select-Object -Unique)
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
function New-Secret {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes); return [Convert]::ToBase64String($bytes) } finally { $rng.Dispose() }
}
function Free-Port {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}
try {
    $dbPassword = New-Secret
    [IO.File]::WriteAllText($passwordFile, $dbPassword, [Text.UTF8Encoding]::new($false))
    & (Join-Path $PostgresBin 'initdb.exe') -D $data -U preview -A scram-sha-256 --pwfile=$passwordFile --encoding=UTF8 --locale=C *> (Join-Path $root 'init.log')
    if ($LASTEXITCODE -ne 0) { throw 'Preview cluster initialization failed.' }
    Remove-Item -LiteralPath $passwordFile
    $dbPort = Free-Port
    $apiPort = Free-Port
    $pg = Start-Process -FilePath (Join-Path $PostgresBin 'pg_ctl.exe') -WindowStyle Hidden -PassThru -ArgumentList @('-D', ('"' + $data + '"'), '-l', ('"' + (Join-Path $root 'postgres.log') + '"'), '-o', ('"-h 127.0.0.1 -p ' + $dbPort + '"'), '-w', 'start') -RedirectStandardOutput (Join-Path $root 'start.log') -RedirectStandardError (Join-Path $root 'start-error.log')
    $started = $true
    $null = $pg.WaitForExit(15000)
    & (Join-Path $PostgresBin 'pg_isready.exe') -h 127.0.0.1 -p $dbPort -q
    if ($LASTEXITCODE -ne 0) { throw 'Preview cluster startup failed.' }
    $env:PGPASSWORD = $dbPassword
    & (Join-Path $PostgresBin 'createdb.exe') -h 127.0.0.1 -p $dbPort -U preview bookshare_preview
    if ($LASTEXITCODE -ne 0) { throw 'Preview database creation failed.' }
    $env:ConnectionStrings__BookShare = "Host=127.0.0.1;Port=$dbPort;Database=bookshare_preview;Username=preview;Password=$dbPassword"
    $env:Maintenance__Database = 'bookshare_preview'
    $env:DemoSeed__Enabled = 'true'
    $credentials = @{}
    foreach ($role in $roles) {
        $secret = New-Secret
        [Environment]::SetEnvironmentVariable(('DemoSeed__' + $role + 'Password'), $secret, 'Process')
        $credentials[$role] = ConvertTo-SecureString $secret -AsPlainText -Force
    }
    # Windows DPAPI: encrypted for this Windows account, never added to Git.
    $credentials | Export-Clixml -LiteralPath (Join-Path $root 'demo-credentials.xml')
    dotnet $dll --maintenance-migrate
    if ($LASTEXITCODE -ne 0) { throw 'Preview migration failed.' }
    dotnet $dll --maintenance-seed
    if ($LASTEXITCODE -ne 0) { throw 'Preview seed failed.' }
    $env:DemoSeed__Enabled = $null
    $env:Maintenance__Database = $null
    foreach ($role in $roles) { [Environment]::SetEnvironmentVariable(('DemoSeed__' + $role + 'Password'), $null, 'Process') }
    $env:Jwt__SigningKey = New-Secret
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$apiPort"
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $env:Hosting__BehindTlsProxy = 'true'
    foreach ($entry in @(Get-ChildItem Env: | Where-Object Name -like 'Cors__*')) { Remove-Item -LiteralPath ('Env:' + $entry.Name) }
    $env:Cors__AllowedOrigins__0 = 'http://127.0.0.1:5173'
    $apiProcess = Start-Process dotnet -WindowStyle Hidden -PassThru -WorkingDirectory (Split-Path $dll -Parent) -ArgumentList ('"' + $dll + '"') -RedirectStandardOutput (Join-Path $root 'api.log') -RedirectStandardError (Join-Path $root 'api-error.log')
    $processes += $apiProcess
    # Only the API child receives database credentials and the JWT signing key.
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    $env:VITE_API_BASE_URL = "http://127.0.0.1:$apiPort"
    $node = (Get-Command node.exe).Source
    $vite = Join-Path $client 'scripts/vite.mjs'
    $webProcess = Start-Process $node -WindowStyle Hidden -PassThru -WorkingDirectory $client -ArgumentList @(('"' + $vite + '"'), 'dev') -RedirectStandardOutput (Join-Path $root 'client.log') -RedirectStandardError (Join-Path $root 'client-error.log')
    $processes += $webProcess
    Write-Host 'Local preview: http://127.0.0.1:5173'
    Write-Host 'Register your own account in the UI. Four demo accounts and 12 books are seeded.'
    Write-Host "Encrypted local demo credentials and logs: $root"
    Write-Host 'Keep this process running. Stop it with Ctrl+C to stop the preview and isolated cluster.'
    while (!$apiProcess.HasExited -and !$webProcess.HasExited) { Start-Sleep -Seconds 2 }
    throw 'A preview process stopped. Inspect the local logs.'
} finally {
    foreach ($process in $processes) { if (!$process.HasExited) { Stop-Process -Id $process.Id } }
    if ($started) { & (Join-Path $PostgresBin 'pg_ctl.exe') -D $data -m fast -w stop *> (Join-Path $root 'stop.log') }
    if (Test-Path -LiteralPath $passwordFile) { Remove-Item -LiteralPath $passwordFile }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
