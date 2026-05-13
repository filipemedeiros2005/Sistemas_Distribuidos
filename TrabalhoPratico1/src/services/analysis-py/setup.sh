#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

if [ ! -d .venv ]; then
    python3 -m venv .venv
fi
# shellcheck source=/dev/null
. .venv/bin/activate

pip install --upgrade pip
pip install -r requirements.txt

make gen

echo "[setup] venv ready, stubs in pb/"
