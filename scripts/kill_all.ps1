# kill_all.ps1 — Windows equivalent of kill_all.sh. Stops every OneHealth
# service whose PID was recorded by run_all.ps1, in two ordered waves so
# the sensor's BYE has time to reach the gateway and update Postgres.
#
# Note: Windows has no real SIGTERM. Stop-Process is closer to SIGKILL,
# so the BYE from the sensor's shutdown handler will not fire reliably
# under this script (unlike on macOS/Linux). The wave ordering still
# helps for in-flight DATA messages: sensors stop first, the gateway
# keeps draining for 2 s, then everything else stops.
#
# The background Windows services (RabbitMQ, postgresql-x64-18) are also
# stopped at the end for a fully clean shutdown. run_all.ps1 restarts them.

$ErrorActionPreference = 'Continue'
$PidDir = Join-Path $env:TEMP 'onehealth-pids'

function Log($msg) { Write-Host "[kill_all] $msg" -ForegroundColor Cyan }

function Stop-Pid($pidFile) {
    $name = $pidFile.BaseName
    $procId = Get-Content $pidFile.FullName
    try {
        Stop-Process -Id $procId -ErrorAction Stop
        Log "Stopped $name (PID $procId)"
    }
    catch {
        Log "$name (PID $procId) already gone or could not be stopped"
    }
    Remove-Item $pidFile.FullName -Force
}

if (-not (Test-Path $PidDir)) {
    Log "No PID directory at $PidDir — nothing to do."
    exit 0
}

$pidFiles = Get-ChildItem $PidDir -Filter *.pid -ErrorAction SilentlyContinue
if (-not $pidFiles) {
    Log 'No PID files found — nothing to do.'
    exit 0
}

# Wave 1: sensors first, so any DATA already published is consumed before
# the gateway dies. (BYE itself won't reach the broker — see note above.)
$sensorFiles = $pidFiles | Where-Object { $_.BaseName -like 'sensor_*' }
foreach ($f in $sensorFiles) { Stop-Pid $f }

Start-Sleep -Seconds 2

# Wave 2: everything else.
$restFiles = $pidFiles | Where-Object { $_.BaseName -notlike 'sensor_*' }
foreach ($f in $restFiles) { Stop-Pid $f }

# Stop the background brokers too, for a fully clean shutdown.
Log 'Stopping background services (RabbitMQ, postgresql-x64-18)...'
Stop-Service RabbitMQ          -ErrorAction SilentlyContinue
Stop-Service postgresql-x64-18 -ErrorAction SilentlyContinue

Log 'Done. All OneHealth services and brokers stopped.'
