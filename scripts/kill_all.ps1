# kill_all.ps1 — Windows equivalent of kill_all.sh. Stops every OneHealth
# service whose PID was recorded by run_all.ps1.
#
# Background Windows services (RabbitMQ, postgresql-x64-18) stay running
# for the next session. Stop those manually with:
#   Stop-Service RabbitMQ
#   Stop-Service postgresql-x64-18

$ErrorActionPreference = 'Continue'
$PidDir = Join-Path $env:TEMP 'onehealth-pids'

function Log($msg) { Write-Host "[kill_all] $msg" -ForegroundColor Cyan }

if (-not (Test-Path $PidDir)) {
    Log "No PID directory at $PidDir — nothing to do."
    exit 0
}

$pidFiles = Get-ChildItem $PidDir -Filter *.pid -ErrorAction SilentlyContinue
if (-not $pidFiles) {
    Log 'No PID files found — nothing to do.'
    exit 0
}

foreach ($f in $pidFiles) {
    $name = $f.BaseName
    $procId = Get-Content $f.FullName
    try {
        Stop-Process -Id $procId -ErrorAction Stop
        Log "Stopped $name (PID $procId)"
    }
    catch {
        Log "$name (PID $procId) already gone or could not be stopped"
    }
    Remove-Item $f.FullName -Force
}

Log 'Done. Background services (RabbitMQ, postgresql-x64-18) stay running.'
