#!/usr/bin/env bash
# run_all.sh — install missing dependencies (via Homebrew) and start the
# entire OneHealth stack in a deterministic order.
#
# Idempotent: rerunning on an already-provisioned machine is a fast no-op
# for the install steps and just (re)launches the services.
#
# Pairs with kill_all.sh, which reads the same PID directory.

set -euo pipefail

# Resolve the repo root from the script's own location (prefer git, fall back
# to the parent of scripts/) and cd into it. This makes the script behave
# identically no matter which directory the terminal was in when it launched.
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null || dirname "$SCRIPT_DIR")"
cd "$REPO_ROOT"

LOG_DIR="${REPO_ROOT}/logs"
PID_DIR="/tmp/onehealth-pids"
mkdir -p "$LOG_DIR" "$PID_DIR"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
log()  { printf '\033[1;34m[run_all]\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m[ok]\033[0m      %s\n' "$*"; }
err()  { printf '\033[1;31m[err]\033[0m     %s\n' "$*" >&2; }

need_brew() {
    if ! command -v brew >/dev/null 2>&1; then
        err "Homebrew not found. Install from https://brew.sh first, then re-run."
        exit 1
    fi
}

# Install a brew formula (or cask) only if the given command is missing.
ensure_cmd() {
    local cmd="$1" pkg="$2" kind="${3:-formula}"
    if command -v "$cmd" >/dev/null 2>&1; then return 0; fi
    log "Installing $pkg via brew ($kind)..."
    if [ "$kind" = "cask" ]; then
        brew install --cask "$pkg"
    else
        brew install "$pkg"
    fi
}

start_svc() {
    local name="$1"; shift
    log "Starting $name..."
    "$@" > "$LOG_DIR/${name}.log" 2>&1 &
    echo $! > "$PID_DIR/${name}.pid"
    ok "$name PID=$(cat "$PID_DIR/${name}.pid")  (log: ${LOG_DIR}/${name}.log)"
}

# ---------------------------------------------------------------------------
# 1) Dependency check / auto-install
# ---------------------------------------------------------------------------
need_brew

ensure_cmd dotnet       dotnet-sdk   cask
ensure_cmd python3      python@3.11
ensure_cmd psql         postgresql@18
ensure_cmd rabbitmqctl  rabbitmq

# Bring background services up
brew services start postgresql@18 >/dev/null 2>&1 || true
brew services start rabbitmq      >/dev/null 2>&1 || true

# Wait until both ports are accepting connections
log "Waiting for PostgreSQL (5432) and RabbitMQ (5672)..."
for _ in $(seq 1 30); do
    if lsof -nP -iTCP:5432 -sTCP:LISTEN >/dev/null 2>&1 \
    && lsof -nP -iTCP:5672 -sTCP:LISTEN >/dev/null 2>&1; then
        ok "PostgreSQL + RabbitMQ are up."
        break
    fi
    sleep 1
done

# Create the database the first time we run
if ! psql -lqt | cut -d '|' -f 1 | grep -qw onehealth; then
    log "Creating 'onehealth' database..."
    createdb onehealth
fi

# ---------------------------------------------------------------------------
# 2) Python venv (delegates to setup.sh, idempotent)
# ---------------------------------------------------------------------------
ANALYSIS_DIR="${REPO_ROOT}/src/services/analysis-py"
if [ ! -d "${ANALYSIS_DIR}/.venv" ]; then
    log "Provisioning Python venv..."
    ( cd "$ANALYSIS_DIR" && ./setup.sh )
fi

# ---------------------------------------------------------------------------
# 2.5) Guard against a previous instance still holding our ports. Starting a
# second stack on top of a live one silently routes data to the old services
# (and crashes the new ones with "address already in use"), which is very
# confusing to debug. Abort early and tell the user to clean up first.
# ---------------------------------------------------------------------------
busy=""
for port in 50051 50052 5006 9000; do
    if lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
        busy="${busy} ${port}"
    fi
done
if [ -n "$busy" ]; then
    err "Ports already in use:${busy}. A previous OneHealth stack is still running."
    err "Stop it first:  ./scripts/kill_all.sh   (or close stray terminals), then re-run."
    exit 1
fi

# ---------------------------------------------------------------------------
# 3) Build the .NET solution once (binaries are reused across services)
# ---------------------------------------------------------------------------
log "Building .NET solution..."
if ( cd "$REPO_ROOT" && dotnet build --nologo -v minimal >"$LOG_DIR/build.log" 2>&1 ); then
    ok "Build succeeded."
else
    err "Build failed — see ${LOG_DIR}/build.log"
    exit 1
fi

# ---------------------------------------------------------------------------
# 4) Start services in dependency order
# ---------------------------------------------------------------------------
start_svc preprocessor "${REPO_ROOT}/src/OneHealth.Preprocessor/bin/Debug/net9.0/OneHealth.Preprocessor"
sleep 3
start_svc analysis     "${ANALYSIS_DIR}/.venv/bin/python" "${ANALYSIS_DIR}/server.py"
sleep 3
start_svc server       "${REPO_ROOT}/src/OneHealth.Server/bin/Debug/net9.0/OneHealth.Server"
sleep 3
# Two gateways: 5001 = North zone (101, 102, 999), 5002 = South zone (103, 104).
start_svc gateway_5001 "${REPO_ROOT}/src/OneHealth.Gateway/bin/Debug/net9.0/OneHealth.Gateway" 5001
start_svc gateway_5002 "${REPO_ROOT}/src/OneHealth.Gateway/bin/Debug/net9.0/OneHealth.Gateway" 5002
sleep 2

# Four auto sensors. 999 is intentionally NOT started here — it is the manual
# terminal sensor, launched by hand:
#   dotnet run --project src/OneHealth.Sensor -- 999 manual
start_svc sensor_101   "${REPO_ROOT}/src/OneHealth.Sensor/bin/Debug/net9.0/OneHealth.Sensor" 101 auto
start_svc sensor_102   "${REPO_ROOT}/src/OneHealth.Sensor/bin/Debug/net9.0/OneHealth.Sensor" 102 auto
start_svc sensor_103   "${REPO_ROOT}/src/OneHealth.Sensor/bin/Debug/net9.0/OneHealth.Sensor" 103 auto
start_svc sensor_104   "${REPO_ROOT}/src/OneHealth.Sensor/bin/Debug/net9.0/OneHealth.Sensor" 104 auto
sleep 3

# Dashboard last: by now the backend is serving, so its window opens against a
# live system. It is a GUI app but launches fine in the background on macOS;
# its PID is tracked like the rest, so kill_all.sh tears it down too.
start_svc dashboard    "${REPO_ROOT}/src/OneHealth.Dashboard/bin/Debug/net9.0/OneHealth.Dashboard"

ok "All services started — the Dashboard window should appear within a few seconds."
log "PIDs:  $PID_DIR"
log "Logs:  $LOG_DIR"
log ""
log "Stop everything (including the Dashboard) with kill_all.sh."
