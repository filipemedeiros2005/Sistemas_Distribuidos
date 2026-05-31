#!/usr/bin/env bash
# kill_all.sh — stop every OneHealth service started by run_all.sh.
#
# Shutdown happens in two waves so the BYE pipeline can complete:
#   1. SIGTERM every sensor first so it publishes BYE while the gateway
#      and the rest of the pipeline are still up to consume it.
#   2. Wait 2 s for BYE → broker → gateway → Postgres UPSERT.
#   3. SIGTERM everything else (gateway, server, preprocessor, analysis).
#   4. Wait 3 s for AMQP/gRPC channels to close cleanly.
#   5. SIGKILL any stragglers.
#
# The background brokers (rabbitmq, postgresql@18) are also stopped at the
# end for a fully clean shutdown. run_all.sh restarts them on the next run.

set -euo pipefail
shopt -s nullglob   # empty globs expand to nothing instead of literal text

# Anchor to the repo root regardless of the caller's working directory, so the
# script behaves the same whether launched from the repo root or a subfolder.
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null || dirname "$SCRIPT_DIR")"

PID_DIR="/tmp/onehealth-pids"
log() { printf '\033[1;34m[kill_all]\033[0m %s\n' "$*"; }

if [ ! -d "$PID_DIR" ] || [ -z "$(ls -A "$PID_DIR" 2>/dev/null)" ]; then
    log "No PID files in $PID_DIR — nothing to do."
    exit 0
fi

send_term() {
    local f="$1"
    [ -f "$f" ] || return 0
    local pid name
    pid=$(cat "$f")
    name=$(basename "$f" .pid)
    if kill -0 "$pid" 2>/dev/null; then
        log "SIGTERM $name (PID $pid)"
        kill -TERM "$pid" 2>/dev/null || true
    else
        log "$name (PID $pid) already gone."
    fi
}

# Wave 1: sensors first so the BYE has somewhere to land.
for f in "$PID_DIR"/sensor_*.pid; do send_term "$f"; done

# Give BYE a moment to travel sensor → broker → gateway → Postgres.
sleep 2

# Wave 2: everything else.
for f in "$PID_DIR"/*.pid; do
    name=$(basename "$f" .pid)
    case "$name" in
        sensor_*) continue ;;   # already terminated in wave 1
    esac
    send_term "$f"
done

# Grace period for AMQP/gRPC channel close on the rest of the stack.
sleep 3

# Force-kill any stragglers and clear the PID directory.
for f in "$PID_DIR"/*.pid; do
    [ -f "$f" ] || continue
    pid=$(cat "$f")
    if kill -0 "$pid" 2>/dev/null; then
        log "SIGKILL $(basename "$f" .pid) (PID $pid)"
        kill -KILL "$pid" 2>/dev/null || true
    fi
    rm -f "$f"
done

# Stop the background brokers too, for a fully clean shutdown.
log "Stopping background services (rabbitmq, postgresql@18)..."
brew services stop rabbitmq      >/dev/null 2>&1 || true
brew services stop postgresql@18 >/dev/null 2>&1 || true

log "Done. All OneHealth services and brokers stopped."
