#!/usr/bin/env bash
# Sets up the analysis-py virtual environment and regenerates the gRPC
# stubs from src/protos/analysis.proto. Idempotent — re-running on an
# already-provisioned tree is a fast no-op for pip and a clean overwrite
# for the generated python files.
#
# Usage:
#   ./setup.sh            # full setup (venv + deps + stubs)
#   ./setup.sh --no-deps  # only regenerate the stubs (when the .proto changes)

set -euo pipefail

cd "$(dirname "$0")"
SCRIPT_DIR="$(pwd)"
PROTOS_DIR="$(cd "${SCRIPT_DIR}/../../protos" && pwd)"

SKIP_DEPS=0
for arg in "$@"; do
    case "$arg" in
        --no-deps) SKIP_DEPS=1 ;;
        *) echo "unknown flag: $arg" >&2; exit 1 ;;
    esac
done

# 1) Create venv if it doesn't exist yet.
if [ ! -d ".venv" ]; then
    echo "[setup] Creating .venv ..."
    python3 -m venv .venv
fi

# 2) Activate the venv so subsequent commands use it.
# shellcheck disable=SC1091
source .venv/bin/activate

# 3) Install / refresh pip itself, then the project dependencies.
if [ "$SKIP_DEPS" -eq 0 ]; then
    echo "[setup] Upgrading pip ..."
    pip install --quiet --upgrade pip
    echo "[setup] Installing requirements.txt ..."
    pip install --quiet -r requirements.txt
fi

# 4) Regenerate the gRPC stubs for analysis.proto. Output files
#    (analysis_pb2.py and analysis_pb2_grpc.py) sit next to server.py.
echo "[setup] Generating gRPC stubs from analysis.proto ..."
python -m grpc_tools.protoc \
    -I "$PROTOS_DIR" \
    --python_out=. \
    --grpc_python_out=. \
    "$PROTOS_DIR/analysis.proto"

echo "[setup] Done. To activate manually: source $SCRIPT_DIR/.venv/bin/activate"
