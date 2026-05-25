#!/usr/bin/env bash
# kill_all.sh — stop every OneHealth service started by run_all.sh.
#
# Reads the PID files dropped under /tmp/onehealth-pids, sends SIGTERM so
# each service can publish its BYE / close its AMQP connection cleanly,
# then SIGKILLs anything still alive after a short grace period.
#
# Background services managed by brew (rabbitmq, postgresql@18) are left
# untouched — they're expected to persist across runs. Stop them manually
# with:  brew services stop rabbitmq postgresql@18

set -euo pipefail

PID_DIR="/tmp/onehealth-pids"
log() { printf '\033[1;34m[kill_all]\033[0m %s\n' "$*"; }

if [ ! -d "$PID_DIR" ] || [ -z "$(ls -A "$PID_DIR" 2>/dev/null)" ]; then
    log "No PID files in $PID_DIR — nothing to do."
    exit 0
fi

# Graceful shutdown first
for f in "$PID_DIR"/*.pid; do
    [ -f "$f" ] || continue
    pid=$(cat "$f")
    name=$(basename "$f" .pid)
    if kill -0 "$pid" 2>/dev/null; then
        log "SIGTERM $name (PID $pid)"
        kill -TERM "$pid" 2>/dev/null || true
    else
        log "$name (PID $pid) already gone."
    fi
done

# Give services up to 3 seconds to flush BYE messages and close cleanly
sleep 3

# Force-kill any stragglers
for f in "$PID_DIR"/*.pid; do
    [ -f "$f" ] || continue
    pid=$(cat "$f")
    if kill -0 "$pid" 2>/dev/null; then
        log "SIGKILL $(basename "$f" .pid) (PID $pid)"
        kill -KILL "$pid" 2>/dev/null || true
    fi
    rm -f "$f"
done

log "Done. Brew services (rabbitmq, postgresql@18) stay running."
