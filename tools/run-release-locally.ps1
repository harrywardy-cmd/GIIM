<#
.SYNOPSIS
    Builds GIIM exactly as the deployment pipeline does and runs it on this PC, on one address.

.DESCRIPTION
    Builds the web UI into the API, publishes the API and workers, builds the database migration bundle and
    applies it to the local Docker SQL Server, then starts both apps. Open the printed address in a browser.
    Press Ctrl+C to stop.

    Differences from Azure: it uses the local database, the development sign-in (not Microsoft sign-in), the
    sample Intune file, plain http, and keeps sign-in cookie keys on this PC. Everything else (the built UI served
    by the API, security headers, caching, health checks, migrations) is what Azure runs.

.EXAMPLE
    ./tools/run-release-locally.ps1              # build and run on http://localhost:5090
    ./tools/run-release-locally.ps1 -NoBuild     # run the last build again
#>
[CmdletBinding()]
param(
    [int] $Port = 5090,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts/release'
$connection = 'Server=localhost,1433;Database=Giim;User Id=sa;Password=Giim-Dev-Passw0rd!;TrustServerCertificate=True'
$workersPort = $Port + 1

function Step([string] $text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Assert-Success([string] $what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." } }

if (-not $NoBuild) {
    Step 'Building the web UI'
    Push-Location (Join-Path $root 'src/Giim.Web')
    try {
        if (-not (Test-Path node_modules)) { npm ci --no-audit --no-fund; Assert-Success 'npm ci' }
        npm run build; Assert-Success 'Web build'
    }
    finally { Pop-Location }

    if (Test-Path $out) { Remove-Item $out -Recurse -Force }

    Step 'Publishing the API (with the web UI) and the workers'
    dotnet publish (Join-Path $root 'src/Giim.Api') --configuration Release --output "$out/api" --nologo --verbosity quiet
    Assert-Success 'Publishing the API'
    New-Item -ItemType Directory -Force "$out/api/wwwroot" | Out-Null
    Copy-Item (Join-Path $root 'src/Giim.Web/dist/*') "$out/api/wwwroot" -Recurse -Force
    dotnet publish (Join-Path $root 'src/Giim.Workers') --configuration Release --output "$out/workers" --nologo --verbosity quiet
    Assert-Success 'Publishing the workers'

    Step 'Building the database migrations'
    Push-Location $root
    try {
        dotnet tool restore | Out-Null
        dotnet ef migrations bundle --project src/Giim.Infrastructure --startup-project src/Giim.Infrastructure `
            --configuration Release --output "$out/migrations/efbundle.exe" --force
        Assert-Success 'Building the migration bundle'
    }
    finally { Pop-Location }
}

Step 'Checking the local database is running'
$sql = docker ps --filter name=giim-sql --format '{{.Status}}'
if (-not $sql) {
    Push-Location $root
    try { docker compose up -d --wait; Assert-Success 'Starting SQL Server (is Docker Desktop running?)' }
    finally { Pop-Location }
}

Step 'Updating the database'
& "$out/migrations/efbundle.exe" --connection $connection
Assert-Success 'Database migrations'

Step 'Starting GIIM'
$logs = Join-Path $out 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Giim = $connection
$env:Giim__PublicBaseUrl = "http://localhost:$Port"

$env:ASPNETCORE_URLS = "http://localhost:$workersPort"
$workers = Start-Process dotnet -ArgumentList 'Giim.Workers.dll' -WorkingDirectory "$out/workers" -PassThru -NoNewWindow `
    -RedirectStandardOutput "$logs/workers.log" -RedirectStandardError "$logs/workers.err.log"
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$api = Start-Process dotnet -ArgumentList 'Giim.Api.dll' -WorkingDirectory "$out/api" -PassThru -NoNewWindow `
    -RedirectStandardOutput "$logs/api.log" -RedirectStandardError "$logs/api.err.log"

try {
    foreach ($check in @("http://localhost:$Port/health", "http://localhost:$workersPort/health")) {
        $healthy = $false
        foreach ($attempt in 1..30) {
            try { if ((Invoke-WebRequest $check -UseBasicParsing -TimeoutSec 5).Content -eq 'Healthy') { $healthy = $true; break } } catch { }
            Start-Sleep -Seconds 1
        }
        if (-not $healthy) { throw "$check did not report Healthy. See the logs in $logs" }
    }

    Write-Host ''
    Write-Host "GIIM is running: http://localhost:$Port" -ForegroundColor Green
    Write-Host "Workers health:  http://localhost:$workersPort/health"
    Write-Host "Logs:            $logs"
    Write-Host 'Press Ctrl+C to stop.'
    while (-not $api.HasExited -and -not $workers.HasExited) { Start-Sleep -Seconds 1 }
    Write-Host 'An app stopped unexpectedly. See the logs above.' -ForegroundColor Red
}
finally {
    foreach ($process in @($api, $workers)) { if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force } }
    foreach ($name in 'ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'ConnectionStrings__Giim', 'Giim__PublicBaseUrl') {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
    Write-Host 'Stopped.'
}
