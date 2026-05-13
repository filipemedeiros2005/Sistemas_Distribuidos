import math
from datetime import datetime, timedelta, timezone

import pandas as pd
import pytest

import analyses


def _frame(rows):
    df = pd.DataFrame(rows)
    if "timestamp" in df.columns:
        df["timestamp"] = pd.to_datetime(df["timestamp"], utc=True)
    return df


def test_avg_per_sensor_and_type():
    df = _frame([
        {"sensor_id": 101, "data_type": "TEMP", "value": 20.0, "msg_type": "DATA"},
        {"sensor_id": 101, "data_type": "TEMP", "value": 22.0, "msg_type": "DATA"},
        {"sensor_id": 101, "data_type": "HUM",  "value": 50.0, "msg_type": "DATA"},
        {"sensor_id": 102, "data_type": "TEMP", "value": 30.0, "msg_type": "DATA"},
    ])
    out = analyses.avg(df)
    assert out.metrics["S101/TEMP"] == pytest.approx(21.0)
    assert out.metrics["S101/HUM"] == pytest.approx(50.0)
    assert out.metrics["S102/TEMP"] == pytest.approx(30.0)


def test_stddev_zero_variance_returns_zero():
    df = _frame([
        {"sensor_id": 101, "data_type": "TEMP", "value": 20.0, "msg_type": "DATA"},
        {"sensor_id": 101, "data_type": "TEMP", "value": 20.0, "msg_type": "DATA"},
        {"sensor_id": 101, "data_type": "TEMP", "value": 20.0, "msg_type": "DATA"},
    ])
    out = analyses.stddev(df)
    assert out.metrics["S101/TEMP"] == pytest.approx(0.0)


def test_stddev_known_population():
    df = _frame([
        {"sensor_id": 101, "data_type": "TEMP", "value": v, "msg_type": "DATA"}
        for v in [10.0, 20.0, 30.0]
    ])
    out = analyses.stddev(df)
    expected = math.sqrt(((10 - 20) ** 2 + 0 + (30 - 20) ** 2) / 3)
    assert out.metrics["S101/TEMP"] == pytest.approx(expected)


def test_anomaly_rate_counts_alerts():
    df = _frame([
        {"sensor_id": 101, "msg_type": "DATA", "data_type": "TEMP", "value": 20.0},
        {"sensor_id": 101, "msg_type": "DATA", "data_type": "TEMP", "value": 20.0},
        {"sensor_id": 101, "msg_type": "ALERT", "data_type": "TEMP", "value": 75.0},
        {"sensor_id": 102, "msg_type": "DATA",  "data_type": "HUM", "value": 50.0},
    ])
    out = analyses.anomaly_rate(df)
    assert out.metrics["S101"] == pytest.approx(1 / 3)
    assert out.metrics["S102"] == pytest.approx(0.0)


def test_forecast_linear_series_predicts_next_n():
    t0 = datetime(2026, 5, 13, 12, 0, 0, tzinfo=timezone.utc)
    rows = [
        {"sensor_id": 101, "data_type": "TEMP", "value": float(20 + i),
         "timestamp": t0 + timedelta(seconds=i)}
        for i in range(5)
    ]
    df = _frame(rows)
    out = analyses.forecast(df, horizon=3)

    label_series = [p for p in out.series if p.label == "S101/TEMP"]
    assert len(label_series) == 3
    expected_values = [25.0, 26.0, 27.0]
    for point, expected in zip(label_series, expected_values):
        assert point.value == pytest.approx(expected, abs=1e-6)

    assert out.metrics["S101/TEMP.slope"] == pytest.approx(1.0)


def test_forecast_skips_single_point_series():
    df = _frame([
        {"sensor_id": 101, "data_type": "TEMP", "value": 20.0,
         "timestamp": datetime(2026, 5, 13, tzinfo=timezone.utc)}
    ])
    out = analyses.forecast(df, horizon=5)
    assert out.series == []


def test_run_dispatches_to_kind():
    df = _frame([
        {"sensor_id": 101, "data_type": "TEMP", "value": 25.0, "msg_type": "DATA"},
    ])
    out = analyses.run("avg", df)
    assert out.metrics["S101/TEMP"] == pytest.approx(25.0)


def test_run_rejects_unknown_kind():
    out = analyses.run("LIGHT_SPEED_CHECK", pd.DataFrame())
    assert "unknown kind" in out.summary


def test_empty_frame_returns_safe_payload():
    df = pd.DataFrame()
    for kind in ["AVG", "STDDEV", "ANOMALY_RATE", "FORECAST"]:
        out = analyses.run(kind, df)
        assert "empty" in out.summary
        assert out.metrics == {}
