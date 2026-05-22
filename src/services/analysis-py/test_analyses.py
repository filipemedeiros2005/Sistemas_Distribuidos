"""
pytest cases for analyses.py.

Pure mathematical validation — no PostgreSQL, no gRPC, no I/O of any kind.
Synthetic DataFrames are constructed directly via the ``_df`` helper.
"""

import numpy as np
import pandas as pd
import pytest

from analyses import (
    compute_anomaly_rate,
    compute_avg,
    compute_forecast,
    compute_stddev,
)


def _df(values, anomalies=None, types=None, sensors=None, ts_step_ms=60_000):
    """Build a telemetry-shaped DataFrame for a list of values."""
    n = len(values)
    return pd.DataFrame({
        'unix_ts':    [i * ts_step_ms for i in range(n)],
        'value':      values,
        'data_type':  (types or ['TEMP'] * n),
        'sensor_id':  (sensors or [101] * n),
        'is_anomaly': (anomalies or [False] * n),
    })


# ---- compute_avg ------------------------------------------------------------

def test_avg_basic():
    result = compute_avg(_df([10.0, 20.0, 30.0]))
    assert result['metrics']['avg']   == pytest.approx(20.0)
    assert result['metrics']['count'] == 3


def test_avg_single_value():
    result = compute_avg(_df([42.0]))
    assert result['metrics']['avg']   == pytest.approx(42.0)
    assert result['metrics']['count'] == 1


# ---- compute_stddev ---------------------------------------------------------

def test_stddev_uses_population_formula():
    # Population std of (10, 20, 30) is sqrt(200/3) ≈ 8.165, not the sample
    # std (which would be sqrt(100) = 10).
    result = compute_stddev(_df([10.0, 20.0, 30.0]))
    assert result['metrics']['stddev'] == pytest.approx(np.sqrt(200 / 3))
    assert result['metrics']['mean']   == pytest.approx(20.0)


def test_stddev_constant_series_is_zero():
    result = compute_stddev(_df([5.0, 5.0, 5.0, 5.0]))
    assert result['metrics']['stddev'] == pytest.approx(0.0)


# ---- compute_anomaly_rate ---------------------------------------------------

def test_anomaly_rate_counts_correctly():
    df = _df(
        values=[1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
        anomalies=[False] * 9 + [True],
    )
    result = compute_anomaly_rate(df)
    assert result['metrics']['rate']         == pytest.approx(0.1)
    assert result['metrics']['total_alerts'] == 1
    assert result['metrics']['count']        == 10


def test_anomaly_rate_all_anomalies():
    df = _df([1, 2, 3], anomalies=[True, True, True])
    assert compute_anomaly_rate(df)['metrics']['rate'] == pytest.approx(1.0)


# ---- compute_forecast -------------------------------------------------------

def test_forecast_on_a_straight_line_stays_linear():
    # y = 2 * i (where i is the row index). Linear regression on
    # (unix_ts, value) should produce a perfect R² and continue the trend.
    df = _df([2.0 * i for i in range(10)])
    result = compute_forecast(df, horizon=5)

    assert result['metrics']['slope'] > 0
    assert result['metrics']['r2']    == pytest.approx(1.0, abs=1e-9)

    series = result['series']
    historical = [p for p in series if p['label'] == 'historical']
    forecast   = [p for p in series if p['label'] == 'forecast']

    assert len(historical) == 10
    assert len(forecast)   == 5

    # ts_step_ms=60_000 in the fixture. Last historical ts is 9 * 60000 = 540000;
    # first forecast ts is 540000 + 60000 = 600000.
    # The value should continue the linear trend: y(i=10) = 2*10 = 20.
    assert forecast[0]['ts']    == 600_000
    assert forecast[0]['value'] == pytest.approx(20.0, abs=1e-6)


def test_forecast_horizon_controls_output_count():
    df = _df([float(i) for i in range(5)])

    assert len(compute_forecast(df, horizon=1)['series'])  == 5 + 1
    assert len(compute_forecast(df, horizon=20)['series']) == 5 + 20


def test_forecast_with_too_few_points():
    result = compute_forecast(_df([1.0]), horizon=5)
    assert result['metrics']            == {}
    assert 'FORECAST needs' in result['summary']


# ---- empty input ------------------------------------------------------------

def test_empty_dataframe_returns_safely():
    empty = _df([])
    assert compute_avg(empty)['metrics']          == {}
    assert compute_stddev(empty)['metrics']       == {}
    assert compute_anomaly_rate(empty)['metrics'] == {}
    assert compute_forecast(empty)['metrics']     == {}
