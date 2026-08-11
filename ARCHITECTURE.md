# Elpis EdgeConnect v2.0 — Architecture

## Overview
Industrial Edge Integration Platform — a high-performance .NET 8 service that acquires CNC machine data from **five configurable sources** (MT-LINKi, FOCAS2, MTConnect, Brother HTTP, HTTP REST) and publishes it to an MQTT broker for downstream consumers (SCADA, dashboards, MES, analytics).

## Data Source Architecture

```
┌────────────────────────────────────────────────────────────────────┐
│                    Per-Machine Configuration                        │
│         Each machine selects ONE data source type:                  │
│                                                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐             │
│  │  MT-LINKi    │  │  FOCAS2 DLL  │  │  HTTP REST   │             │
│  │  REST API    │  │  P/Invoke    │  │  Simulator   │             │
│  │              │  │              │  │              │             │
│  │ GET /api/v1/ │  │ Direct call  │  │ GET /focas2/ │             │
│  │ equipment/   │  │ to Fwlib64   │  │ endpoints    │             │
│  │ CNC/monit..  │  │              │  │              │             │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘             │
│         │                  │                  │                     │
│         └──────────────────┼──────────────────┘                     │
│                            ▼                                        │
│                 ┌────────────────────┐                              │
│                 │ IMachineDataSource │ (Strategy Pattern)            │
│                 │  .CollectDataAsync()│                              │
│                 └─────────┬──────────┘                              │
│                           ▼                                         │
│                ┌─────────────────────┐                              │
│                │ MachinePollerService │ (one per machine, own timer) │
│                └─────────┬───────────┘                              │
│                          ▼                                          │
│              ┌───────────────────────┐                              │
│              │ Channel<MqttPayload>  │ (bounded, back-pressure)     │
│              └───────────┬───────────┘                              │
│                          ▼                                          │
│              ┌───────────────────────┐                              │
│              │ MqttPublisherService  │ (batched, QoS, reconnect)    │
│              └───────────┬───────────┘                              │
│                          ▼                                          │
│                   MQTT Broker                                       │
└────────────────────────────────────────────────────────────────────┘
```

## Data Source Types

### 1. MT-LINKi REST API (`DataSourceType: "MtLinki"`)
- **Best for:** Factories already running Fanuc MT-LINKi
- **How it works:** Calls MT-LINKi's REST API on port 3000
- **API pattern:** `GET {BaseUrl}/api/v1/equipment/CNC/monitorings`
- **Additional endpoints:** `/monitorings/ALARM/logs`, `/monitorings/PRODUCTION/logs`
- **Advantages:** No additional load on CNCs, supports non-Fanuc machines, standard HTTP, no native DLLs
- **Requirements:** MT-LINKi Server PC with REST API enabled
- **Config:** `MtLinki.BaseUrl`, `MtLinki.ApiVersion`, `MtLinki.EquipmentType`, `MtLinki.MachineIdentifier`

### 2. FOCAS2 DLL (`DataSourceType: "Focas2Dll"`)
- **Best for:** Small setups without middleware, lowest latency requirements
- **How it works:** Calls Fanuc's native FOCAS2 library via P/Invoke over Ethernet
- **Advantages:** Direct connection, ~5ms latency, no middleware needed
- **Requirements:** `Fwlib64.dll` (Windows) or `libfwlib32.so` (Linux) from Fanuc
- **Config:** `Focas2.IpAddress`, `Focas2.Port`, `Focas2.KeepAlive`

### 3. HTTP REST (`DataSourceType: "HttpRest"`)
- **Best for:** Development/testing with CNC Simulator, custom REST wrappers
- **How it works:** HTTP GET calls to `/focas2/*` endpoints
- **Advantages:** Simulator compatibility, backward compatibility, no DLLs needed
- **Config:** `BaseUrl`, `Username`, `Password`, `AuthType`

## Mixed Deployments
Machines in the same factory can use **different** data source types:

```json
{
  "Machines": [
    { "MachineId": "CNC-001", "DataSourceType": "MtLinki",   "MtLinki": { "BaseUrl": "http://192.168.2.199:3000" } },
    { "MachineId": "CNC-002", "DataSourceType": "Focas2Dll", "Focas2":  { "IpAddress": "192.168.1.102" } },
    { "MachineId": "SIM-001", "DataSourceType": "HttpRest",  "BaseUrl": "http://localhost:5050/" }
  ]
}
```

## Project Structure

```
src/ElpisEdgeConnect/
├── Configuration/
│   ├── AppSettings.cs          — Concurrency, channel, health checks
│   ├── MachineConfig.cs        — Per-machine config (with MtLinki/Focas2 sections)
│   └── MqttSettings.cs         — MQTT broker settings
├── DataSources/
│   ├── IMachineDataSource.cs   — Core abstraction interface + DataSourceType enum
│   ├── DataSourceFactory.cs    — Creates correct implementation per machine
│   ├── MtLinkiRestDataSource.cs   — MT-LINKi REST API client
│   ├── HttpRestDataSource.cs      — HTTP REST client (simulator compat)
│   ├── Focas2DllDataSource.cs     — FOCAS2 P/Invoke wrapper
│   └── Focas2/
│       └── Focas2Interop.cs       — Native DLL P/Invoke declarations
├── Models/
│   ├── CncMachineData.cs       — Domain model (axes, spindle, alarms, etc.)
│   └── MqttPayload.cs          — MQTT wire model
├── Services/
│   ├── MachineManagerService.cs — Orchestrator (poller lifecycle, hot-reload)
│   ├── MachinePollerService.cs  — Per-machine polling loop (data-source agnostic)
│   └── MqttPublisherService.cs  — MQTT publishing with batching & retry
├── Resilience/
│   └── ResiliencePolicies.cs   — Polly retry + circuit breaker
├── Security/
│   └── SecretProvider.cs       — Env var + AES-256 password resolution
└── Program.cs                  — Entry point & DI composition root
```

## Key Design Decisions

1. **Strategy Pattern** for data sources — `IMachineDataSource` interface allows seamless swapping
2. **Per-machine independence** — each machine has its own poller, timer, and data source
3. **Hot-reload** — add/remove/modify machines in `appsettings.json` without restart
4. **Back-pressure** — bounded channel prevents memory exhaustion if MQTT is slow
5. **Resilience** — Polly retry + circuit breaker on HTTP data source; connection retry on FOCAS2
6. **No MongoDB dependency** — MT-LINKi data source uses the official REST API, keeping dependencies minimal

## Dependencies

| Package | Purpose |
|---------|---------|
| `MQTTnet 4.3.7` | MQTT publishing |
| `Polly 8.4.2` | Retry + circuit breaker |
| `Serilog` | Structured logging |

## Configuration UI
React-based configuration UI (`config-ui.jsx`) with:
- Data source type selector (visual cards: MT-LINKi / FOCAS2 / HTTP)
- Context-sensitive connection forms per data source type
- Live API endpoint preview for MT-LINKi
- Tag/data point browser with license enforcement
- Export to `appsettings.json`
