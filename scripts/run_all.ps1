# run_all.ps1 — Windows equivalent of run_all.sh. Installs missing
# dependencies via winget and starts the OneHealth stack.
#
# Idempotent: rerunning on a provisioned host skips the install steps.
# The first run may need to be invoked from an elevated PowerShell so
# winget can install Erlang, RabbitMQ, and PostgreSQL services.

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$LogDir   = Join-Path $RepoRoot 'logs'
$PidDir   = Join-Path $env:TEMP 'onehealth-pids'
New-Item -ItemType Directory -Force -Path $LogDir, $PidDir | Out-Null

# --- Helpers ---------------------------------------------------------------
function Log  ($msg) { Write-Host "[run_all] $msg" -ForegroundColor Cyan }
function Ok   ($msg) { Write-Host "[ok]      $msg" -ForegroundColor Green }
function Fail ($msg) { Write-Host "[err]     $msg" -ForegroundColor Red; exit 1 }

function Ensure-Cmd($cmd, $wingetId) {
    if (Get-Command $cmd -ErrorAction SilentlyContinue) { return }
    Log "Installing $wingetId via winget..."
    winget install --id $wingetId --silent `
        --accept-source-agreements --accept-package-agreements | Out-Null
}

function Start-Svc($name, $exe, $argList) {
    Log "Starting $name..."
    $p = Start-Process `
        -FilePath $exe `
        -ArgumentList $argList `
        -RedirectStandardOutput "$LogDir\$name.log" `
        -RedirectStandardError  "$LogDir\$name-err.log" `
        -WindowStyle Hidden `
        -PassThru
    $p.Id | Out-File "$PidDir\$name.pid" -Encoding ASCII -Force
    Ok "$name PID=$($p.Id)  (log: $LogDir\$name.log)"
}

# --- 1) Winget + dependencies ---------------------------------------------
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Fail "winget not found. Install 'App Installer' from the Microsoft Store first."
}

Ensure-Cmd dotnet  'Microsoft.DotNet.SDK.9'
Ensure-Cmd python  'Python.Python.3.11'
Ensure-Cmd psql    'PostgreSQL.PostgreSQL.18'

# RabbitMQ depends on Erlang; install both if the service is missing.
if (-not (Get-Service -Name RabbitMQ -ErrorAction SilentlyContinue)) {
    Log 'Installing Erlang and RabbitMQ...'
    winget install --id 'Erlang.Erlang'    --silent --accept-package-agreements --accept-source-agreements | Out-Null
    winget install --id 'RabbitMQ.Server'  --silent --accept-package-agreements --accept-source-agreements | Out-Null
}

# Start background services (idempotent)
Start-Service postgresql-x64-18 -ErrorAction SilentlyContinue
Start-Service RabbitMQ          -ErrorAction SilentlyContinue

# Wait briefly for ports to open
$timeout = 30
do {
    $pg = Test-NetConnection 127.0.0.1 -Port 5432 -InformationLevel Quiet -WarningAction SilentlyContinue
    $rq = Test-NetConnection 127.0.0.1 -Port 5672 -InformationLevel Quiet -WarningAction SilentlyContinue
    if ($pg -and $rq) { break }
    Start-Sleep -Seconds 1
    $timeout--
} while ($timeout -gt 0)
if ($timeout -le 0) { Fail 'PostgreSQL or RabbitMQ did not come up.' }
Ok 'PostgreSQL + RabbitMQ are up.'

# Create the database on first run
$dbs = psql -lqt
if (-not ($dbs -match 'onehealth')) {
    Log "Creating 'onehealth' database..."
    createdb onehealth
}

# --- 2) Python venv -------------------------------------------------------
$AnalysisDir = Join-Path $RepoRoot 'src\services\analysis-py'
$VenvPython  = Join-Path $AnalysisDir '.venv\Scripts\python.exe'
if (-not (Test-Path $VenvPython)) {
    Log 'Provisioning Python venv...'
    Push-Location $AnalysisDir
    python -m venv .venv
    & $VenvPython -m pip install --upgrade pip
    & $VenvPython -m pip install -r requirements.txt
    & $VenvPython -m grpc_tools.protoc `
        -I "$RepoRoot\src\protos" `
        --python_out=. --grpc_python_out=. `
        "$RepoRoot\src\protos\analysis.proto"
    Pop-Location
}

# --- 2.5) Guard against a previous instance still holding our ports --------
# Starting a second stack on top of a live one silently routes data to the old
# services (and crashes the new ones with "address already in use"). Abort early
# and tell the user to clean up first.
$busy = @()
foreach ($port in 50051, 50052, 5006, 9000) {
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
        $busy += $port
    }
}
if ($busy.Count -gt 0) {
    Fail "Ports already in use: $($busy -join ', '). A previous OneHealth stack is still running. Stop it first with .\scripts\kill_all.ps1, then re-run."
}

# --- 3) Build the .NET solution -------------------------------------------
Log 'Building .NET solution...'
dotnet build $RepoRoot --nologo -v minimal *> "$LogDir\build.log"
if ($LASTEXITCODE -ne 0) { Fail "Build failed — see $LogDir\build.log" }
Ok 'Build succeeded.'

# --- 4) Start services in order -------------------------------------------
Start-Svc preprocessor "$RepoRoot\src\OneHealth.Preprocessor\bin\Debug\net9.0\OneHealth.Preprocessor.exe" @()
Start-Sleep 3
Start-Svc analysis     $VenvPython @("$AnalysisDir\server.py")
Start-Sleep 3
Start-Svc server       "$RepoRoot\src\OneHealth.Server\bin\Debug\net9.0\OneHealth.Server.exe" @()
Start-Sleep 3

# Two gateways: 5001 = North zone (101, 102, 999), 5002 = South zone (103, 104).
Start-Svc gateway_5001 "$RepoRoot\src\OneHealth.Gateway\bin\Debug\net9.0\OneHealth.Gateway.exe" @('5001')
Start-Svc gateway_5002 "$RepoRoot\src\OneHealth.Gateway\bin\Debug\net9.0\OneHealth.Gateway.exe" @('5002')
Start-Sleep 2

# Four auto sensors. 999 is NOT started here — it is the manual terminal sensor:
#   dotnet run --project src\OneHealth.Sensor -- 999 manual
Start-Svc sensor_101   "$RepoRoot\src\OneHealth.Sensor\bin\Debug\net9.0\OneHealth.Sensor.exe" @('101', 'auto')
Start-Svc sensor_102   "$RepoRoot\src\OneHealth.Sensor\bin\Debug\net9.0\OneHealth.Sensor.exe" @('102', 'auto')
Start-Svc sensor_103   "$RepoRoot\src\OneHealth.Sensor\bin\Debug\net9.0\OneHealth.Sensor.exe" @('103', 'auto')
Start-Svc sensor_104   "$RepoRoot\src\OneHealth.Sensor\bin\Debug\net9.0\OneHealth.Sensor.exe" @('104', 'auto')
Start-Sleep 3

Start-Svc dashboard    "$RepoRoot\src\OneHealth.Dashboard\bin\Debug\net9.0\OneHealth.Dashboard.exe" @()

Ok 'All services started — the Dashboard window should appear within a few seconds.'
Log "PIDs:  $PidDir"
Log "Logs:  $LogDir"
Log ''
Log 'Stop everything (including the Dashboard) with .\scripts\kill_all.ps1'
