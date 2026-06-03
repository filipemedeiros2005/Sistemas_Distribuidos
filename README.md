# OneHealth

Distributed urban-monitoring system developed for the Distributed Systems course
(3rd year, 2nd semester). Simulates city-scale environmental sensing
(temperature, humidity, particulate matter, noise, luminosity), feeds the
readings through an asynchronous message broker and a synchronous validation
service, persists them in a relational database, and exposes a desktop
dashboard for live monitoring and historical analysis.

This branch (`tp2/fase1/DesenvolvimentoProtocolo2`) hosts the second
deliverable (TP2), rewritten from scratch on top of the binary protocol
established in TP1.

The TP2 requirements are met as follows: **RPC** at two points (Gateway ↔
pre-processing service, Server ↔ analysis service), **Publish/Subscribe** over
RabbitMQ between sensors and gateways, **persistence** in PostgreSQL of both
readings and analysis results, and a **dashboard** that visualizes data and
triggers parameterized analyses (time window, data type, sensor id). Carried
over from TP1: concurrent handling with **threads + mutex**, a simulated live
**video** feed, and CSV-based gateway configuration. The analysis service is
written in **Python**, satisfying the multi-language bonus.

---

## Architecture

Five long-lived processes plus the simulated sensors:

```
                            ┌──────────────────────────────┐
                            │       RabbitMQ broker        │
                            │  exchange onehealth.telemetry│
                            └──────┬──────────────┬────────┘
                                   ▲              │
                          publish  │              │ subscribe
                                   │              ▼
   ┌──────────┐                ┌───┴───────┐  ┌────────────────────┐
   │ Sensor   │ ──────────────▶│ (broker)  │  │ Gateway            │
   │  (C#)    │  20-byte CRC16 │           │  │  (C#)              │
   │  CSV ▶   │  packet over   │           │  │  • consume by zone │
   │  RabbitMQ│  AMQP topic    │           │  │  • UPSERT sensors  │
   └──────────┘                └───────────┘  │  • call preproc gRPC│
                                              │  • publish aggregated│
                                              └─────┬───────────────┘
                                                    │ gRPC :50051
                                                    ▼
                                              ┌──────────────────────┐
                                              │ Pre-processor (C#)   │
                                              │ ASP.NET gRPC         │
                                              │ 4 validations        │
                                              └──────────────────────┘
                                                    │
        ┌────────────── RabbitMQ exchange onehealth.aggregated ──────┘
        ▼
   ┌──────────────────────────┐         ┌───────────────────────────┐
   │ Server (C#)              │ ◀──TCP──│ Dashboard (Avalonia, C#)  │
   │ • aggregated → PostgreSQL│  :5006  │ • Tabs: Telemetry/Analysis│
   │ • AnalysisCoordinator    │         │ • LiveCharts2 (SkiaSharp) │
   │ • gRPC client            │─gRPC───▶│                           │
   └──────────────────────────┘  :50052 └───────────────────────────┘
                                  ▼
                            ┌──────────────────────┐
                            │ Analysis (Python)    │
                            │ pandas + scikit-learn│
                            └──────────────────────┘
```

### Component responsibilities

| Process | Language | Listens / Connects to | Responsibility |
|---|---|---|---|
| **Sensor** | C# | publishes to RabbitMQ | Reads simulated readings either from a CSV file at real-time pace (`auto`) or typed at the terminal (`manual`), classifies each as `Data` or `Alert`, publishes to `onehealth.telemetry` with a topology-aware routing key, emits `Status` heartbeats every 30 s, and a `Bye` on graceful shutdown. |
| **Gateway** | C# | subscribes to RabbitMQ; talks to PostgreSQL + Pre-processor | Bound by zone (`zone.<Z>.#`); authorizes every packet against its CSV allow-list (mutex-guarded) before anything else; maintains the `sensors` registry in PostgreSQL; calls the Pre-processor for every `Data` packet (others bypass); republishes accepted packets to the `onehealth.aggregated` exchange. |
| **Pre-processor** | C# (ASP.NET gRPC) | port `50051` | Stateless: rejects NaN/Inf, converts °F/K → °C, enforces physical bounds per data type, drops timestamps too far in the future. Sensor authorization happens upstream in the Gateway. |
| **Server** | C# | RabbitMQ; PostgreSQL; TCP `:5006`; gRPC `:50052`; TCP `:9000` | Persists every `aggregated` packet into the `telemetry` table; exposes a TCP `AnalysisCoordinator` for the Dashboard; resolves zones to sensor ids; delegates heavy queries to Python via gRPC; serves a simulated live video feed over raw TCP. |
| **Analysis** | Python 3.11+ (gRPC) | port `50052` | Reads from PostgreSQL via psycopg + pandas; supports `AVG`, `STDDEV`, `ANOMALY_RATE`, and `FORECAST` (linear regression via scikit-learn, fitted on non-anomalous readings). |
| **Dashboard** | C# / Avalonia 12 | TCP `:5006`, `:9000` | Tabbed UI: live telemetry table, sensor registry (online/offline), and historical analysis panel. Renders forecast series via LiveCharts2 over SkiaSharp; opens a per-sensor live video window from the Sensors tab. |

### Wire formats

- **Sensor → Gateway:** custom 20-byte binary packet, little-endian, with
  CRC-16/CCITT-FALSE checksum (`OneHealth.Common.TelemetryPacket`). The 20-byte
  budget is inherited from TP1 and verified by xUnit round-trip tests.
- **Gateway ↔ Pre-processor:** gRPC over HTTP/2 (`preprocessing.proto`).
- **Server ↔ Python:** gRPC over HTTP/2 (`analysis.proto`).
- **Dashboard ↔ Server (analysis):** plain-text pipe-delimited over TCP, e.g.
  `KIND=AVG|WINDOW=60|ZONA=ZONE_NORTH|TYPES=TEMP`. Response shape:
  `OK|kind=...|summary=...|metrics=...` or `ERROR|reason=...`.
- **Dashboard ↔ Server (video):** raw TCP on `:9000`. The client sends the
  4-byte sensor id, then receives a continuous stream of 16×16 grayscale
  frames (256 bytes each) — a simulated CCTV feed, no codec.

### Routing keys (RabbitMQ topic exchanges)

Both `onehealth.telemetry` (sensor → gateway) and `onehealth.aggregated`
(gateway → server) use the same scheme:

```
zone.<ZONE>.type.<DATA_TYPE>.sensor.<ID>
```

Examples:

- `zone.ZONE_NORTH.type.TEMP.sensor.101`
- `zone.ZONE_SOUTH.type.PM25.sensor.103`
- `zone.ZONE_NORTH.type.HEARTBEAT.sensor.101` (envelope packets)

A gateway responsible for the North zone binds with the wildcard
`zone.ZONE_NORTH.#`.

---

## Technology stack

| Layer | Choice | Why |
|---|---|---|
| Runtime | .NET 9.0 (pinned via `global.json`) | Modern async, source generators, `PeriodicTimer` |
| Desktop UI | Avalonia 12.0.1 | Cross-platform XAML, runs natively on macOS ARM |
| Charts | LiveChartsCore.SkiaSharpView.Avalonia 2.1.0-dev-570 | The only release line that targets Avalonia 12 |
| Messaging | RabbitMQ 4.3 + Erlang 28 | Native Homebrew install, full async client (RabbitMQ.Client 7.0.0) |
| RPC | gRPC over HTTP/2 (Grpc.Net 2.66, Google.Protobuf 3.28) | Strict contracts, polyglot (C# ↔ Python) |
| Persistence | PostgreSQL 18.3 | Trust auth on localhost, JSONB for forecast series |
| Analysis | Python 3.11 + pandas + scikit-learn | Best-in-class numerical tooling for the Server's heavy queries |
| Tests | xUnit (C#), pytest (Python) | 27 xUnit cases (packet, pre-processor, mutex guard) and 14 pytest cases (analysis functions) |

---

## Repository structure

```
RepositorioProjetos/
├── OneHealth.sln                 — solution coordinates the 7 .NET projects
├── global.json                   — pins SDK 9.0.305 (rollForward: latestFeature)
├── README.md                     — this file
├── data/
│   ├── simulation/               — sensor_<id>.csv, one row per measurement
│   └── gateway_configs/          — gw_<port>.csv, sensors a gateway is authoritative for
├── scripts/                      — run_all / kill_all (.sh and .ps1)
└── src/
    ├── OneHealth.Common/         — TelemetryPacket, CRC16, enums, limits, schemas
    ├── OneHealth.Sensor/         — CSV/manual reader, classifier, publisher, heartbeat
    ├── OneHealth.Gateway/        — consumer, gRPC client, registry, auth guard, aggregator
    ├── OneHealth.Preprocessor/   — ASP.NET gRPC service (4 validations)
    ├── OneHealth.Server/         — aggregated consumer, persistence, TCP coordinator, video
    ├── OneHealth.Dashboard/      — Avalonia desktop app (Telemetry, Sensors, Analysis)
    ├── OneHealth.Tests/          — xUnit test suite
    ├── protos/                   — preprocessing.proto, analysis.proto
    └── services/
        └── analysis-py/          — Python analysis service
```

---

## Prerequisites

Run-time dependencies expected on the host machine:

- .NET SDK 9.0.305 (pinned in `global.json`)
- RabbitMQ 4.x + Erlang 28.x
- PostgreSQL 16+ (development is on 18.3) with trust auth on `localhost`
- Python 3.11+ (for the analysis service)

Quick macOS install (the `run_all` script automates this on first run):

```bash
brew install --cask dotnet-sdk
brew install rabbitmq postgresql@18 python@3.11
brew services start rabbitmq
brew services start postgresql@18
createdb onehealth
```

---

## How to run

### Automated (recommended)

`scripts/run_all.sh` auto-installs any missing dependencies (via Homebrew),
builds the solution, and starts the whole stack in the background — the
pre-processor, the Python analysis service, the server, two gateways
(North/South zones), four auto sensors (101–104), and the Dashboard. It works
from any directory inside the repo (it resolves the repo root via git) and
refuses to start if a previous stack is still holding the ports:

```bash
scripts/run_all.sh
```

PID files land in `/tmp/onehealth-pids/` and per-service logs in `logs/`.
Stop everything with an ordered shutdown (sensors first, so each publishes its
BYE and is marked `OFFLINE` before the gateway closes):

```bash
scripts/kill_all.sh
```

Windows equivalents: `scripts/run_all.ps1` / `scripts/kill_all.ps1` (winget).

### Manual (one terminal per service)

Useful to watch each service's logs live. Order matters — each downstream
service needs the previous one already listening:

```bash
# 1. Pre-processor (gRPC server on :50051)
dotnet run --project src/OneHealth.Preprocessor

# 2. Analysis service (gRPC server on :50052)
cd src/services/analysis-py && ./setup.sh && source .venv/bin/activate && python server.py

# 3. Server (consumes aggregated, listens on TCP :5006)
dotnet run --project src/OneHealth.Server

# 4. Gateways, one per zone (5001 = North, 5002 = South)
dotnet run --project src/OneHealth.Gateway -- 5001
dotnet run --project src/OneHealth.Gateway -- 5002

# 5. Auto sensors (read their CSV in a loop)
dotnet run --project src/OneHealth.Sensor -- 101 auto

# 5b. A manual sensor, typing readings as "<TYPE> <value>" (e.g. TEMP 25.5)
dotnet run --project src/OneHealth.Sensor -- 999 manual

# 6. Dashboard
dotnet run --project src/OneHealth.Dashboard
```

Telemetry starts landing in PostgreSQL almost immediately:

```bash
psql -d onehealth -c "SELECT count(*) FROM telemetry;"
```

In the Dashboard:

- **Analysis tab → Ping** validates the TCP coordinator.
- **Analysis tab → Run Analysis** issues a query (AVG, STDDEV, ANOMALY_RATE,
  FORECAST). If the analysis service is down, it returns
  `ERROR|reason=analysis_unavailable` and the rest of the pipeline keeps running.

---

## Tests

```bash
dotnet test                                                  # 27 xUnit cases
( cd src/services/analysis-py && .venv/bin/python -m pytest ) # 10 pytest cases
```

xUnit covers the binary packet (CRC tampering, malformed input, round-trip),
the pre-processor's four validations, and the gateway's authorization guard
(including a concurrent-access stress test of its mutex). pytest covers the
pure analysis functions (AVG, STDDEV, ANOMALY_RATE, FORECAST) over synthetic
DataFrames.

---

## Dashboard

Three tabs:

- **Telemetry** — the most recent 50 measurements (auto-refresh every 2 s):
  time, sensor, type, value, anomaly flag.
- **Sensors** — the registry (online/offline, last seen), with a per-sensor
  **Live** button that opens the simulated video feed.
- **Analysis** — runs `AVG`, `STDDEV`, `ANOMALY_RATE` or `FORECAST`, filtered by
  zone, data type, sensor, and time window. Every analysis renders a chart
  (value points plus reference lines / forecast / highlighted anomalies) and is
  persisted to `analysis_results` for browsing in the history list.

## Status

Feature-complete for the TP2 requirements. End-to-end validation (build/tests,
ordered startup, SQL pipeline checks, Dashboard, failure modes, graceful
shutdown, automation scripts) all passing.

---

## License

See [LICENSE](./LICENSE).
