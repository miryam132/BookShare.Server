# Production entry point smoke check. Never connects to a database.
param([switch]$Docker)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$serverRoot = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $serverRoot 'src/BookShare.API/bin/Stage7/Debug/net10.0/BookShare.API.dll'
if (!$Docker -and !(Test-Path -LiteralPath $dll)) { throw 'Build Stage7 output first.' }
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$start = [Diagnostics.ProcessStartInfo]::new('dotnet', ('"' + $dll + '"'))
$start.WorkingDirectory = Split-Path $dll -Parent
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Production'
$start.EnvironmentVariables['ASPNETCORE_URLS'] = "http://127.0.0.1:$port"
$start.EnvironmentVariables['Hosting__BehindTlsProxy'] = 'true'
$start.EnvironmentVariables['ConnectionStrings__BookShare'] = 'Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1'
# Override inherited origins; only this test origin is configured in the child.
foreach ($key in @($start.EnvironmentVariables.Keys)) {
    if ($key -like 'Cors__*') { $start.EnvironmentVariables.Remove($key) }
}
$start.EnvironmentVariables['Cors__AllowedOrigins__0'] = 'https://client.example'
$bytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
$start.EnvironmentVariables['Jwt__SigningKey'] = [Convert]::ToBase64String($bytes)
$containerName = 'bookshare-hosting-test-' + [Guid]::NewGuid().ToString('N')
if ($Docker) {
    $start.FileName = 'docker'
    # Forward only named child variables; no secret values appear in command arguments.
    $start.Arguments = "run --rm --name $containerName -p 127.0.0.1:${port}:10000 --env ASPNETCORE_ENVIRONMENT --env Hosting__BehindTlsProxy --env ConnectionStrings__BookShare --env Cors__AllowedOrigins__0 --env Jwt__SigningKey bookshare-api:stage9"
    $start.WorkingDirectory = $serverRoot
}
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(2)
function Send([string]$method, [string]$path, [string]$origin) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($method), "http://127.0.0.1:$port$path")
    try {
        if ($origin) { $request.Headers.Add('Origin', $origin) }
        if ($method -eq 'OPTIONS') {
            $request.Headers.Add('Access-Control-Request-Method', 'POST')
            $request.Headers.Add('Access-Control-Request-Headers', 'authorization,content-type')
        }
        return $client.SendAsync($request).GetAwaiter().GetResult()
    } finally { $request.Dispose() }
}
try {
    $null = $process.Start()
    # Drain output privately so the child cannot block; never print logs or secrets.
    $output = $process.StandardOutput.ReadToEndAsync()
    $errors = $process.StandardError.ReadToEndAsync()
    $ready = $false
    for ($i = 0; $i -lt 200; $i++) {
        if ($process.HasExited) { throw 'Production host exited during startup.' }
        try {
            $response = Send 'GET' '/api/health' ''
            $ready = [int]$response.StatusCode -eq 200
            $response.Dispose()
            if ($ready) { break }
        } catch { }
        Start-Sleep -Milliseconds 100
    }
    if (!$ready) { throw 'Production host did not become ready.' }
    $response = Send 'OPTIONS' '/api/loans' 'https://client.example'
    try {
        if ([int]$response.StatusCode -ne 204 -or ($response.Headers.GetValues('Access-Control-Allow-Origin') -join '') -ne 'https://client.example') { throw 'Allowed preflight failed.' }
        if ($response.Headers.Contains('Access-Control-Allow-Credentials')) { throw 'Unexpected credential sharing.' }
    } finally { $response.Dispose() }
    $response = Send 'OPTIONS' '/api/loans' 'https://unrelated.example'
    try { if ($response.Headers.Contains('Access-Control-Allow-Origin')) { throw 'Unrelated origin was allowed.' } } finally { $response.Dispose() }
    $response = Send 'GET' '/api/users/me' 'https://client.example'
    try {
        if ([int]$response.StatusCode -ne 401 -or !$response.Headers.Contains('Access-Control-Allow-Origin')) { throw 'Protected endpoint/CORS order failed.' }
    } finally { $response.Dispose() }
    $response = Send 'GET' '/swagger/index.html' ''
    try { if ([int]$response.StatusCode -ne 404) { throw 'Production Swagger was exposed.' } } finally { $response.Dispose() }
    if ($Docker) {
        $runtimeUser = & docker exec $containerName id -u
        if ($LASTEXITCODE -ne 0 -or $runtimeUser -eq '0') { throw 'Container must run as non-root.' }
        & docker exec $containerName test -w /app/logs
        if ($LASTEXITCODE -ne 0) { throw 'Non-root log directory is not writable.' }
        Write-Host 'PASS: Docker image HTTP on port 10000, non-root runtime, restricted CORS/preflight, 401 and disabled Swagger. No database accessed.'
    } else {
        Write-Host 'PASS: production entry point, TLS edge mode, restricted CORS/preflight, 401 and disabled Swagger. No database accessed.'
    }
} finally {
    $client.Dispose()
    if ($Docker) { & docker stop --time 5 $containerName *> $null }
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
