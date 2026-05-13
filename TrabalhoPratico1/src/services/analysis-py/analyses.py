"""Pure analysis functions over a measurements DataFrame.

Schema expected on every DataFrame:
    sensor_id : int
    data_type : str
    value     : float
    timestamp : datetime64[ns, UTC] (only needed for FORECAST)
    msg_type  : str ('DATA' or 'ALERT'), only needed for ANOMALY_RATE
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Iterable, List

import numpy as np
import pandas as pd
from sklearn.linear_model import LinearRegression


@dataclass
class SeriesPoint:
    ts: int          # unix seconds
    value: float
    label: str


@dataclass
class AnalysisOutput:
    summary: str
    metrics: dict = field(default_factory=dict)
    series: List[SeriesPoint] = field(default_factory=list)


def _label(sensor_id: int, data_type: str) -> str:
    return f"S{sensor_id}/{data_type}"


def avg(df: pd.DataFrame) -> AnalysisOutput:
    if df.empty:
        return AnalysisOutput(summary="empty window")
    grouped = df.groupby(["sensor_id", "data_type"])["value"].mean()
    metrics = {_label(s, dt): float(v) for (s, dt), v in grouped.items()}
    summary = "AVG: " + ", ".join(f"{k}={v:.2f}" for k, v in metrics.items())
    return AnalysisOutput(summary=summary, metrics=metrics)


def stddev(df: pd.DataFrame) -> AnalysisOutput:
    if df.empty:
        return AnalysisOutput(summary="empty window")
    grouped = df.groupby(["sensor_id", "data_type"])["value"].std(ddof=0)
    metrics = {_label(s, dt): float(v) for (s, dt), v in grouped.items()}
    summary = "STDDEV: " + ", ".join(f"{k}={v:.2f}" for k, v in metrics.items())
    return AnalysisOutput(summary=summary, metrics=metrics)


def anomaly_rate(df: pd.DataFrame) -> AnalysisOutput:
    if df.empty:
        return AnalysisOutput(summary="empty window")
    grouped = df.groupby("sensor_id").agg(
        total=("msg_type", "count"),
        alerts=("msg_type", lambda s: int((s == "ALERT").sum())),
    )
    grouped["rate"] = grouped["alerts"] / grouped["total"].clip(lower=1)
    metrics = {f"S{sid}": float(row["rate"]) for sid, row in grouped.iterrows()}
    summary = "ANOMALY_RATE: " + ", ".join(f"{k}={v:.2%}" for k, v in metrics.items())
    return AnalysisOutput(summary=summary, metrics=metrics)


def forecast(df: pd.DataFrame, horizon: int = 5) -> AnalysisOutput:
    if df.empty:
        return AnalysisOutput(summary="empty window")

    points: List[SeriesPoint] = []
    metrics: dict = {}

    for (sid, dt), grp in df.groupby(["sensor_id", "data_type"]):
        grp = grp.sort_values("timestamp")
        if len(grp) < 2:
            continue

        t0 = grp["timestamp"].iloc[0]
        x_seconds = (grp["timestamp"] - t0).dt.total_seconds().values.reshape(-1, 1)
        y = grp["value"].values.astype(float)

        model = LinearRegression().fit(x_seconds, y)
        slope = float(model.coef_[0])
        intercept = float(model.intercept_)

        last_t = float(x_seconds[-1, 0])
        future_x = np.arange(last_t + 1.0, last_t + 1.0 + horizon).reshape(-1, 1)
        future_y = model.predict(future_x)

        label = _label(int(sid), str(dt))
        metrics[f"{label}.slope"] = slope
        metrics[f"{label}.intercept"] = intercept

        t0_ts = int(t0.timestamp())
        for x_row, y_pred in zip(future_x[:, 0], future_y):
            points.append(SeriesPoint(ts=t0_ts + int(x_row), value=float(y_pred), label=label))

    summary = f"FORECAST: horizon={horizon}, series={len(points)}"
    return AnalysisOutput(summary=summary, metrics=metrics, series=points)


KINDS = {"AVG": avg, "STDDEV": stddev, "ANOMALY_RATE": anomaly_rate, "FORECAST": forecast}


def run(kind: str, df: pd.DataFrame, options: dict | None = None) -> AnalysisOutput:
    options = options or {}
    fn = KINDS.get(kind.upper())
    if fn is None:
        return AnalysisOutput(summary=f"unknown kind: {kind}")
    if kind.upper() == "FORECAST":
        horizon = int(options.get("horizon", "5"))
        return fn(df, horizon=horizon)
    return fn(df)
