"""gRPC entry-point for the AnalysisService.

Loads telemetry from PostgreSQL into a pandas DataFrame and delegates the
math to the pure functions in ``analyses.py``.
"""

from __future__ import annotations

import logging
import os
import signal
import time
import warnings
from concurrent import futures
from datetime import datetime, timezone

import grpc
import pandas as pd
import psycopg

import analyses
from pb import analysis_pb2 as pb
from pb import analysis_pb2_grpc as pb_grpc


DSN = os.getenv(
    "ANALYSIS_DB_DSN",
    "host=localhost port=5432 user=postgres password=postgres dbname=onehealth",
)
PORT = int(os.getenv("ANALYSIS_PORT", "50052"))
MAX_WORKERS = int(os.getenv("ANALYSIS_MAX_WORKERS", "8"))

# pandas >=2.2 nags when the DBAPI connection isn't SQLAlchemy; the warning is
# noise here — psycopg3 cursors stream rows the same way and we want zero copy.
warnings.filterwarnings(
    "ignore",
    message="pandas only supports SQLAlchemy.*",
    category=UserWarning,
)


def _to_naive_utc(unix_ts: int) -> datetime:
    return datetime.fromtimestamp(unix_ts, tz=timezone.utc).replace(tzinfo=None)


def _load(req: pb.AnalysisRequest) -> pd.DataFrame:
    where: list[str] = []
    params: list = []

    if req.from_unix_ts > 0:
        where.append("timestamp >= %s")
        params.append(_to_naive_utc(req.from_unix_ts))
    if req.to_unix_ts > 0:
        where.append("timestamp <= %s")
        params.append(_to_naive_utc(req.to_unix_ts))
    if list(req.data_types):
        where.append("data_type = ANY(%s)")
        params.append([dt.upper() for dt in req.data_types])
    if list(req.sensor_ids):
        where.append("sensor_id = ANY(%s)")
        params.append([int(s) for s in req.sensor_ids])

    sql = "SELECT sensor_id, msg_type, data_type, value, timestamp FROM telemetry"
    if where:
        sql += " WHERE " + " AND ".join(where)
    sql += " ORDER BY timestamp ASC"

    # The telemetry table has no `zona` column, so req.zona is accepted but
    # silently unused. Filter by sensor_ids upstream if a zona-level cut is needed.
    with psycopg.connect(DSN) as conn:
        df = pd.read_sql_query(sql, conn, params=tuple(params) if params else None)

    if not df.empty and "timestamp" in df.columns and df["timestamp"].dt.tz is None:
        df["timestamp"] = df["timestamp"].dt.tz_localize("UTC")
    return df


class AnalysisServicer(pb_grpc.AnalysisServiceServicer):
    def RunAnalysis(self, request: pb.AnalysisRequest, context) -> pb.AnalysisResult:
        try:
            df = _load(request)
        except psycopg.Error as ex:
            context.abort(grpc.StatusCode.UNAVAILABLE, f"db error: {ex}")
            return pb.AnalysisResult()

        out = analyses.run(request.kind, df, dict(request.options))

        result = pb.AnalysisResult(
            kind=request.kind.upper(),
            summary_text=out.summary,
            produced_unix_ts=int(time.time()),
        )
        for k, v in out.metrics.items():
            result.metrics[k] = float(v)
        for sp in out.series:
            result.series.add(ts=int(sp.ts), value=float(sp.value), label=sp.label)
        return result

    def ListAvailableAnalyses(self, request, context) -> pb.AvailableAnalyses:
        return pb.AvailableAnalyses(kinds=list(analyses.KINDS.keys()))


def serve() -> None:
    logging.basicConfig(level=logging.INFO, format="[analysis] %(message)s")
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=MAX_WORKERS))
    pb_grpc.add_AnalysisServiceServicer_to_server(AnalysisServicer(), server)
    server.add_insecure_port(f"[::]:{PORT}")
    server.start()
    host = next((kv.split("=", 1)[1] for kv in DSN.split() if kv.startswith("host=")), "?")
    logging.info(f"gRPC listening on :{PORT} (db host={host})")

    stop = lambda *_: server.stop(grace=2).wait()
    signal.signal(signal.SIGINT, stop)
    signal.signal(signal.SIGTERM, stop)
    server.wait_for_termination()


if __name__ == "__main__":
    serve()
