"""
OneHealth analysis gRPC service.

Serves the ``AnalysisService`` contract defined in
``src/protos/analysis.proto``. Reads from the same PostgreSQL database the
C# Server writes to, runs the statistical/forecasting computations from
``analyses.py`` over the requested slice of telemetry, and returns the
shaped ``AnalysisResult``.

Listens on TCP port 50052. Stops gracefully on SIGINT / SIGTERM so the
shared ``run_all`` / ``kill_all`` orchestration (Day 7) can manage it
together with the other services.
"""

from __future__ import annotations

import getpass
import os
import signal
import sys
import threading
import time
from concurrent import futures

import grpc
import pandas as pd
import psycopg

import analysis_pb2 as pb
import analysis_pb2_grpc as pb_grpc

from analyses import (
    compute_anomaly_rate,
    compute_avg,
    compute_forecast,
    compute_stddev,
)

PORT = 50052
DEFAULT_DSN = f"host=localhost dbname=onehealth user={getpass.getuser()}"
PG_DSN = os.environ.get('ONEHEALTH_PG_DSN', DEFAULT_DSN)

SUPPORTED_KINDS = ('AVG', 'STDDEV', 'ANOMALY_RATE', 'FORECAST')


class AnalysisServiceImpl(pb_grpc.AnalysisServiceServicer):
    """
    gRPC servicer. One PostgreSQL connection is opened per request — short
    queries on a single-host trust-auth setup, so a long-lived pool would
    add complexity for very little win.
    """

    def __init__(self, dsn: str) -> None:
        self._dsn = dsn

    def RunAnalysis(self, request, context):
        kind = (request.kind or '').upper()
        if kind not in SUPPORTED_KINDS:
            context.abort(
                grpc.StatusCode.INVALID_ARGUMENT,
                f"Unknown kind: {kind!r}. Supported: {SUPPORTED_KINDS}",
            )

        df = self._load_df(request)

        if len(df) == 0:
            return pb.AnalysisResult(
                kind=kind,
                summary_text=f"{kind}: no data in the requested window.",
                produced_unix_ts=_now_ms(),
            )

        horizon = _parse_int(request.options.get('HORIZON'), default=10)

        if kind == 'AVG':
            return _to_proto(kind, compute_avg(df))
        if kind == 'STDDEV':
            return _to_proto(kind, compute_stddev(df))
        if kind == 'ANOMALY_RATE':
            return _to_proto(kind, compute_anomaly_rate(df))
        if kind == 'FORECAST':
            return _to_proto(kind, compute_forecast(df, horizon=horizon))

        # Defensive: we already validated `kind` above, but better a clean
        # gRPC error than a silent crash if the table ever drifts.
        context.abort(grpc.StatusCode.INTERNAL, f"Unhandled kind: {kind}")

    def ListAvailableAnalyses(self, request, context):
        out = pb.AvailableAnalyses()
        out.kinds.extend(SUPPORTED_KINDS)
        return out

    def _load_df(self, request) -> pd.DataFrame:
        query = (
            "SELECT unix_ts, value, data_type, sensor_id, is_anomaly "
            "FROM telemetry WHERE unix_ts BETWEEN %s AND %s"
        )
        params: list = [int(request.from_unix_ts), int(request.to_unix_ts)]

        sensor_ids = list(request.sensor_ids)
        if sensor_ids:
            query += " AND sensor_id = ANY(%s)"
            params.append([int(s) for s in sensor_ids])

        data_types = list(request.data_types)
        if data_types:
            query += " AND data_type = ANY(%s)"
            params.append(data_types)

        query += " ORDER BY unix_ts"

        with psycopg.connect(self._dsn) as conn:
            with conn.cursor() as cur:
                cur.execute(query, params)
                rows = cur.fetchall()

        return pd.DataFrame(
            rows,
            columns=['unix_ts', 'value', 'data_type', 'sensor_id', 'is_anomaly'],
        )


def _to_proto(kind: str, result: dict) -> pb.AnalysisResult:
    """Marshal an analyses.py result dict into the gRPC message."""
    proto = pb.AnalysisResult(
        kind=kind,
        summary_text=result['summary'],
        produced_unix_ts=_now_ms(),
    )
    for k, v in result.get('metrics', {}).items():
        proto.metrics[k] = float(v)
    for sp in result.get('series', []):
        proto.series.append(pb.SeriesPoint(
            ts=int(sp['ts']),
            value=float(sp['value']),
            label=str(sp.get('label', '')),
        ))
    return proto


def _now_ms() -> int:
    return int(time.time() * 1000)


def _parse_int(value, default: int) -> int:
    try:
        return int(value) if value is not None else default
    except (TypeError, ValueError):
        return default


def main() -> int:
    print(f"[BOOT] OneHealth analysis service")
    print(f"[BOOT] PostgreSQL DSN: {PG_DSN}", flush=True)

    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    pb_grpc.add_AnalysisServiceServicer_to_server(
        AnalysisServiceImpl(PG_DSN), server,
    )
    server.add_insecure_port(f'[::]:{PORT}')
    server.start()
    print(f"[BOOT] gRPC server listening on :{PORT}", flush=True)

    stop_event = threading.Event()

    def _on_signal(signum, _frame):
        sig = signal.Signals(signum).name
        print(f"\n[SHUTDOWN] {sig} received, stopping...", flush=True)
        stop_event.set()

    signal.signal(signal.SIGINT, _on_signal)
    signal.signal(signal.SIGTERM, _on_signal)

    stop_event.wait()
    server.stop(grace=2).wait()
    print("[SHUTDOWN] Goodbye.", flush=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
