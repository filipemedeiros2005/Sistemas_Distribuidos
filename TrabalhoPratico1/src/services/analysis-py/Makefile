.PHONY: setup gen test run clean

VENV := .venv
PY   := $(VENV)/bin/python

setup:
	./setup.sh

gen:
	mkdir -p pb
	$(PY) -m grpc_tools.protoc -I ../../protos \
		--python_out=pb --grpc_python_out=pb \
		../../protos/analysis.proto
	@touch pb/__init__.py
	@sed -i.bak -e 's/^import analysis_pb2 as/from . import analysis_pb2 as/' pb/analysis_pb2_grpc.py
	@rm -f pb/analysis_pb2_grpc.py.bak

test:
	$(PY) -m pytest -v

run:
	$(PY) server.py

clean:
	rm -rf pb/*.py __pycache__ */__pycache__
