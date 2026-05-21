# OneHealth

Distributed urban-monitoring system developed for the Distributed Systems course
(3rd year, 2nd semester). Simulates city-scale environmental sensing
(temperature, humidity, particulate matter, noise, luminosity), feeds the
readings through an asynchronous message broker and a synchronous validation
service, persists them in a relational database, and exposes a desktop
dashboard for live monitoring and historical analysis.

This branch (`tp2/fase1/DesenvolvimentoProtocolo2`) hosts the second
deliverable (TP2), rewritten from scratch on top of the binary protocol
established in TP1. Work-in-progress; the final tag will be `v1.0`.

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
                                              │ 5 validations        │
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
| **Sensor** | C# | publishes to RabbitMQ | Reads simulated readings from a CSV file at real-time pace, classifies each as `Data` or `Alert`, publishes to `onehealth.telemetry` with a topology-aware routing key, emits `Status` heartbeats every 30 s. |
| **Gateway** | C# | subscribes to RabbitMQ; talks to PostgreSQL + Pre-processor | Bound by zone (`zone.<Z>.#`); maintains the `sensors` registry in PostgreSQL; calls the Pre-processor for every `Data` packet (others bypass); republishes accepted packets to the `onehealth.aggregated` exchange. |
| **Pre-processor** | C# (ASP.NET gRPC) | port `50051` | Stateless: rejects NaN/Inf, converts °F/K → °C, enforces physical bounds per data type, drops timestamps too far in the future, denies unregistered sensors. |
| **Server** | C# | RabbitMQ; PostgreSQL; TCP `:5006`; gRPC `:50052` | Persists every `aggregated` packet into the `telemetry` table; exposes a TCP `AnalysisCoordinator` for the Dashboard; resolves zones to sensor ids; delegates heavy queries to Python via gRPC. |
| **Analysis** | Python 3.11 (gRPC) | port `50052` | (Day 5) Reads from PostgreSQL via `pandas.read_sql_query`; supports `AVG`, `STDDEV`, `ANOMALY_RATE`, and `FORECAST` (linear regression). |
| **Dashboard** | C# / Avalonia 12 | TCP `:5006` | Tabbed UI: live telemetry table and historical analysis panel. Renders forecast series via LiveCharts2 over SkiaSharp. |

### Wire formats

- **Sensor → Gateway:** custom 20-byte binary packet, little-endian, with
  CRC-16/CCITT-FALSE checksum (`OneHealth.Common.TelemetryPacket`). The 20-byte
  budget is inherited from TP1 and verified by xUnit round-trip tests.
- **Gateway ↔ Pre-processor:** gRPC over HTTP/2 (`preprocessing.proto`).
- **Server ↔ Python:** gRPC over HTTP/2 (`analysis.proto`).
- **Dashboard ↔ Server:** plain-text pipe-delimited over TCP, e.g.
  `KIND=AVG|WINDOW=60|ZONA=ZONE_NORTH|TYPES=TEMP`. Response shape:
  `OK|kind=...|summary=...|metrics=...` or `ERROR|reason=...`.

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
| Tests | xUnit (C#), pytest (Python) | Parallel runners, theory tests, Day 5+ coverage |

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
├── scripts/                      — run_all / kill_all (added on Day 7)
└── src/
    ├── OneHealth.Common/         — TelemetryPacket, CRC16, enums, schemas
    ├── OneHealth.Sensor/         — CSV reader, classifier, publisher, heartbeat
    ├── OneHealth.Gateway/        — consumer, gRPC client, registry, aggregator
    ├── OneHealth.Preprocessor/   — ASP.NET gRPC service (5 validations)
    ├── OneHealth.Server/         — aggregated consumer, persistence, TCP coordinator
    ├── OneHealth.Dashboard/      — Avalonia desktop app
    ├── OneHealth.Tests/          — xUnit test suite
    ├── protos/                   — preprocessing.proto, analysis.proto
    └── services/
        └── analysis-py/          — Python analysis service (Day 5)
```

---

## Prerequisites

Run-time dependencies expected on the host machine:

- .NET SDK 9.0.305 (pinned in `global.json`)
- RabbitMQ 4.x + Erlang 28.x
- PostgreSQL 16+ (development is on 18.3) with trust auth on `localhost`
- Python 3.11+ (only needed once Day 5 lands)

Quick macOS install (Day 7 scripts will automate this):

```bash
brew install --cask dotnet-sdk
brew install rabbitmq postgresql@18 python@3.11
brew services start rabbitmq
brew services start postgresql@18
createdb onehealth
```

---

## How to run (manual, current state)

The end-to-end pipeline already works without Python. Each process must be
started in its own terminal, in this order:

```bash
# 1. Pre-processor (gRPC server on :50051)
dotnet run --project src/OneHealth.Preprocessor

# 2. Server (consumes aggregated, listens on TCP :5006)
dotnet run --project src/OneHealth.Server

# 3. Gateway for the North zone (port id 5001)
dotnet run --project src/OneHealth.Gateway -- 5001

# 4. One or more sensors
dotnet run --project src/OneHealth.Sensor -- 101 auto
dotnet run --project src/OneHealth.Sensor -- 102 auto

# 5. Dashboard
dotnet run --project src/OneHealth.Dashboard
```

Telemetry will start landing in PostgreSQL almost immediately:

```bash
psql -d onehealth -c "SELECT count(*) FROM telemetry;"
```

In the Dashboard:

- **Analysis tab → Ping** validates the TCP coordinator.
- **Analysis tab → Run Analysis** issues a query; with Python still missing
  (Day 5), it returns `ERROR|reason=analysis_unavailable` — expected.

When Day 5 ships, the Python service will be started before the Server:

```bash
cd src/services/analysis-py
./setup.sh         # creates .venv and generates gRPC stubs
source .venv/bin/activate
python server.py
```

Day 7 will wrap all of this in `scripts/run_all.sh` / `run_all.ps1` with
automatic dependency detection and installation.

---

## Tests

```bash
dotnet test
```

Currently: 23/23 xUnit cases green — covers the binary packet (CRC tampering,
malformed input, round-trip) and the pre-processor's five validations.

---

## Development status

| Day | Theme | Status |
|---|---|---|
| 1 | Solution scaffold, `TelemetryPacket`, .proto contracts, LiveCharts2 spike | ✅ |
| 2 | Sensor publisher + heartbeat, Pre-processor with 4 validations + tests | ✅ |
| 3 | Gateway: consumer, gRPC client, sensors registry + auth, aggregated republish | ✅ |
| 4 | Server: aggregated consumer → PostgreSQL, AnalysisCoordinator (TCP) + Dashboard mini-spike | ✅ |
| 5 | Python analysis service + Dashboard wires up real responses | ⏳ |
| 6 | Dashboard polish (LiveCharts on Analysis, DataGrid on Telemetry) + end-to-end smoke test | ⏳ |
| 7 | `run_all`/`kill_all` with auto-install, cross-platform validation, technical report, submission | ⏳ |

---

## License

See [LICENSE](./LICENSE).
