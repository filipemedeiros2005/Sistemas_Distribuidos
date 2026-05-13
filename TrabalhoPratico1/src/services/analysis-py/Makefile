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

test:
	$(PY) -m pytest -v

run:
	$(PY) server.py

clean:
	rm -rf pb/*.py __pycache__ */__pycache__
