"""
Pure statistical functions used by the analysis gRPC service.

Each function takes a pandas DataFrame whose columns mirror the SQL
projection from the ``telemetry`` table — ``unix_ts``, ``value``,
``data_type``, ``sensor_id``, ``is_anomaly`` — and returns a result dict:

    {
        'summary': str,                # human-readable line for the Dashboard
        'metrics': dict[str, float],   # scalar metrics, become gRPC `metrics`
        'series':  list[dict]          # optional, only for FORECAST
    }

Keeping the math in pure functions (no DB, no gRPC) means tests can feed
synthetic DataFrames straight in without spinning up Postgres.
"""

from __future__ import annotations

import numpy as np
from sklearn.linear_model import LinearRegression


def compute_avg(df) -> dict:
    """Mean of the ``value`` column over the whole window."""
    n = len(df)
    if n == 0:
        return {'summary': 'AVG: no data in window.', 'metrics': {}}

    avg = float(df['value'].mean())
    types = sorted(df['data_type'].unique())
    return {
        'summary': f"AVG across {n} measurement(s), {len(types)} type(s): {avg:.2f}",
        'metrics': {'avg': avg, 'count': float(n)},
    }


def compute_stddev(df) -> dict:
    """
    Population standard deviation (ddof=0) — the window is treated as the
    whole population, not a sample. Project-wide convention.
    """
    n = len(df)
    if n == 0:
        return {'summary': 'STDDEV: no data in window.', 'metrics': {}}

    std = float(df['value'].std(ddof=0))
    mean = float(df['value'].mean())
    return {
        'summary': f"STDDEV (ddof=0) across {n} measurements: {std:.2f} (mean {mean:.2f})",
        'metrics': {'stddev': std, 'mean': mean, 'count': float(n)},
    }


def compute_anomaly_rate(df) -> dict:
    """Fraction of rows flagged as anomalies by the sensor / preprocessor."""
    n = len(df)
    if n == 0:
        return {'summary': 'ANOMALY_RATE: no data in window.', 'metrics': {}}

    alerts = int(df['is_anomaly'].sum())
    rate = alerts / n
    return {
        'summary': f"ANOMALY_RATE: {alerts}/{n} = {rate:.4f} ({rate * 100:.2f}%)",
        'metrics': {'rate': rate, 'total_alerts': float(alerts), 'count': float(n)},
    }


def compute_forecast(df, horizon: int = 10) -> dict:
    """
    Linear regression on (unix_ts, value), projecting ``horizon`` points
    into the future at the average historical spacing. Returns both the
    historical and forecast points in the ``series`` field so the Dashboard
    can render them as a single chart with two visually distinct labels.
    """
    n = len(df)
    if n < 2:
        return {
            'summary': f"FORECAST needs at least 2 points, got {n}.",
            'metrics': {},
        }

    X = df['unix_ts'].to_numpy().reshape(-1, 1).astype(float)
    y = df['value'].to_numpy().astype(float)

    model = LinearRegression()
    model.fit(X, y)

    # Step = average spacing of the input series; fall back to 1 minute.
    step = float((X[-1, 0] - X[0, 0]) / (n - 1)) if n > 1 else 60_000.0
    if step <= 0:
        step = 60_000.0

    future_ts = np.array(
        [X[-1, 0] + step * i for i in range(1, horizon + 1)]
    ).reshape(-1, 1)
    predictions = model.predict(future_ts)

    r2 = float(model.score(X, y))
    slope = float(model.coef_[0])
    intercept = float(model.intercept_)

    historical = [
        {'ts': int(t), 'value': float(v), 'label': 'historical'}
        for t, v in zip(X[:, 0], y)
    ]
    forecast = [
        {'ts': int(t), 'value': float(v), 'label': 'forecast'}
        for t, v in zip(future_ts[:, 0], predictions)
    ]

    return {
        'summary': (
            f"FORECAST: linear, slope={slope:.6g}/ms, "
            f"R²={r2:.4f}, {horizon} point(s) ahead"
        ),
        'metrics': {
            'slope': slope,
            'intercept': intercept,
            'r2': r2,
            'horizon': float(horizon),
        },
        'series': historical + forecast,
    }
