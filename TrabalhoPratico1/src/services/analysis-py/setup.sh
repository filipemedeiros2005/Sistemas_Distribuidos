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

mkdir -p pb
python -m grpc_tools.protoc \
    -I ../../protos \
    --python_out=pb \
    --grpc_python_out=pb \
    ../../protos/analysis.proto

# grpc_tools generates absolute imports (`import analysis_pb2`) that only resolve
# if pb/ is on PYTHONPATH. Rewrite the import in the _grpc file to be relative
# so the package works as a self-contained directory.
sed -i.bak -e 's/^import analysis_pb2 as/from . import analysis_pb2 as/' pb/analysis_pb2_grpc.py
rm -f pb/analysis_pb2_grpc.py.bak
touch pb/__init__.py

echo "[setup] venv ready, stubs in pb/"
