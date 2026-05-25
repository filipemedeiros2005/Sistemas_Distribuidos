# End-to-end smoke test (manual)

This document walks through a clean end-to-end run of the OneHealth stack
on macOS. It is the canonical "did everything still wire up?" check before
each commit that touches more than one component, and is also the script
that goes into the technical report.

Day 7 will replace it with `run_all.sh` / `kill_all.sh`; until then,
follow these steps in eight separate terminal windows / panes.

## 0 · Prerequisites (one-time)

```bash
brew install --cask dotnet-sdk
brew install rabbitmq postgresql@18 python@3.11
brew services start rabbitmq
brew services start postgresql@18
createdb onehealth
```

Verify:

```bash
psql -d onehealth -c "SELECT current_database();"
lsof -nP -iTCP:5672 -sTCP:LISTEN | head -2     # rabbitmq
lsof -nP -iTCP:5432 -sTCP:LISTEN | head -2     # postgres
```

The first run of the Python service also needs the venv:

```bash
cd src/services/analysis-py
./setup.sh
```

## 1 · Reset state (clean slate)

```bash
# Drop the auto-created tables so the C# services recreate them
# with the canonical schema:
psql -d onehealth -c "DROP TABLE IF EXISTS analysis_results, telemetry, sensors;"

# Empty any leftover RabbitMQ queues from previous runs:
/opt/homebrew/opt/rabbitmq/sbin/rabbitmqctl purge_queue oh.gateway.5001 || true
/opt/homebrew/opt/rabbitmq/sbin/rabbitmqctl purge_queue oh.server.aggregated || true
```

## 2 · Start the pre-processor (gRPC, port 50051)

```bash
dotnet run --project src/OneHealth.Preprocessor
```

Expect: `Now listening on: http://localhost:50051`.

## 3 · Start the Python analysis service (gRPC, port 50052)

```bash
cd src/services/analysis-py
source .venv/bin/activate
python server.py
```

Expect: `[BOOT] gRPC server listening on :50052`.

## 4 · Start the Server (RabbitMQ consumer + TCP coordinator, port 5006)

```bash
dotnet run --project src/OneHealth.Server
```

Expect three lines:

```
[BOOT] PostgreSQL connected; 'telemetry' table ready.
[BOOT] 'analysis_results' table ready.
[COORDINATOR] Listening on tcp://127.0.0.1:5006
```

## 5 · Start the Gateway for the North zone (port id 5001)

```bash
dotnet run --project src/OneHealth.Gateway -- 5001
```

Expect: `[BOOT] Connected. Listening for telemetry...` and a `[BIND]` line
showing the wildcard `zone.ZONE_NORTH.#`.

## 6 · Start one or two sensors

```bash
dotnet run --project src/OneHealth.Sensor -- 101 auto
# In another terminal, optional:
dotnet run --project src/OneHealth.Sensor -- 102 auto
```

Expect (per sensor): `[BOOT] Connected.`, `[PUB ] HELLO sent.`, then a
stream of `[PUB #NNNN]` lines roughly one per second.

## 7 · Start the Dashboard

```bash
dotnet run --project src/OneHealth.Dashboard
```

The window should open immediately. The **Telemetry** tab fills in after
the first 2-second refresh tick.

## 8 · Run analyses + verify the loop closes

In the Dashboard:

1. Switch to the **Analysis** tab.
2. Click **Ping** → response status reads `PONG|server=OneHealth.Server|status=ready`.
3. Pick `AVG` + `ZONE_NORTH` + `TEMP` in the form, click **Run Analysis**.
   The history list (left) gains a new `#N AVG …` row and gets auto-selected.
4. Pick `FORECAST` + `ZONE_NORTH` + `TEMP` + leave window at 60, click again.
   The chart (right) renders two line series — `historical` and `forecast`.

Cross-check from the terminal:

```bash
psql -d onehealth -c "SELECT count(*) AS rows, sum((is_anomaly)::int) AS anomalies FROM telemetry;"
psql -d onehealth -c "SELECT id, kind, substring(summary, 1, 60) FROM analysis_results ORDER BY id;"
```

Telemetry rows should grow continuously; `analysis_results` should contain
one row per Dashboard click.

## 9 · Clean shutdown

Press Ctrl+C in each terminal in this order:

1. Sensors (emit `BYE` → gateway updates `sensors.status = OFFLINE`)
2. Gateway
3. Server
4. Python analysis
5. Pre-processor
6. Dashboard

Background services stay running across runs:

```bash
brew services list  # rabbitmq, postgresql@18 should both stay "started"
```

## Common failure modes

| Symptom | Diagnose with | Likely fix |
|---|---|---|
| `analysis_unavailable` | curl localhost:50052 | Python service not running or stuck — restart |
| `zone_empty` | `psql -c "SELECT * FROM sensors"` | Gateway hasn't UPSERTed yet — wait for next `Hello`/`Status` |
| Gateway log floods `[RETRY]` | Pre-processor log | Pre-processor crashed or unreachable; restart it |
| Empty Telemetry tab in Dashboard | `psql -c "SELECT count(*) FROM telemetry"` | Server isn't consuming; check `[BOOT]` lines in server log |
| Dashboard chart empty for AVG | (expected) | Only `FORECAST` produces a series; non-FORECAST shows metrics only |
