# 06 — Codebase tour

A 10-minute mental model of where things live.

## The 30-second version

```
EdgeConnect = protocol-agnostic edge integration platform.

A data point moves:
  Device (FOCAS2 / Modbus / MTConnect / Brother / S7 / OPC UA)
   -> Source adapter           normalises into CanonicalDataPoint
   -> Pipeline                 filter / map / deadband / rate-limit
   -> Routing                  fan out to one or more sinks
   -> Buffer (per-route)       SQLite store-and-forward
   -> Sink adapter             publishes to MQTT / OPC UA Server
```

Routes are the primary product concept (NOT a config footnote). Adapters are protocol-specific compile-time projects activated by license. Core never references any protocol module — adapters reference Core, never the other way around.

## Layer-by-layer

### `src/ElpisEdgeConnect.Core/` — protocol-agnostic runtime

| Folder | What |
|---|---|
| `Adapters/` | `ISourceAdapter`, `ISinkAdapter`, the adapter state machine. |
| `Buffer/` | `IMessageBuffer`, SQLite-backed store-and-forward. |
| `Configuration/` | `GatewayConfiguration`, `SourceInstanceConfig`, `SinkInstanceConfig`, `RouteConfig`, `IConfigurationManager` (draft → apply → rollback). |
| `Diagnostics/` | 3-way diagnostics collector (source / pipeline / sink), bundle support. |
| `Licensing/` | `LicenseManager`, `LicenseSignatureValidator`, `CanonicalJson` (single source of truth for offline signing + drift detection). |
| `Model/` | `CanonicalDataPoint`, `CanonicalDataPointBuilder`. The lingua franca. |
| `Pipeline/` | Transform steps (FilterStep, DeadbandStep, RateLimitStep, TagMappingStep) + the orchestrator. |
| `Routing/` | `RouteWorker`, `FanoutDispatcher`, replay logic, backpressure. |

### `src/ElpisEdgeConnect.Sources.*` — adapter modules

One project per protocol: `Focas2`, `BrotherHttp`, `ModbusTcp`, `MTConnect`, `OpcUaClient`, `S7`. Each contains an `*SourceAdapter`, a `*SourceConfiguration` typed record, a tag map, and protocol-specific connection / decoding helpers.

### `src/ElpisEdgeConnect.Sinks.*` — sink modules

`Mqtt` + `OpcUaServer`. Same shape — adapter + typed config + helpers.

### `src/ElpisEdgeConnect.Host/` — headless runtime

The DI composition root. `EdgeConnectComposition.ConfigureRuntimeAsync` is the locked startup sequence; both Host and Studio call it.

### `src/ElpisEdgeConnect.Management/` — Connectivity Studio

The Blazor Server admin + REST API.

| Folder | What |
|---|---|
| `Api/` | Minimal API endpoints (`/api/v1/config/`, `/api/v1/sources/`, etc.) and the C# services behind them. |
| `Backup/` | Diagnostic bundle generation + ADR-0020 redaction engine. |
| `Components/` | Blazor pages + shared components. Pages live in `Components/Pages/`. |
| `Components/Pages/SourceWizards/` | The per-protocol Add Source wizards. |
| `Wizards/` | Testable POCO state machines behind the Razor pages. |
| `Hosting/` | `ManagementHostingExtensions.AddConnectivityStudio` — the central DI wire-up. |

### `tools/` — CLI utilities

| Tool | Purpose |
|---|---|
| `LicenseGen/` | RSA keygen + signed-license issuance. |
| `ValidateConfig/` | JSON-schema validation for `current.json`. |
| `ValidateSidecar/` | Validates the chip-3 sidecar YAML against its schema. |
| `bulk-provision/` | Offline whole-gateway-config generator (chip-3). |
| `SchemaGen/` | Emits the canonical schema documents under `docs/config-schemas/`. |
| `ModbusByteOrderProbe/` | Word-order / byte-order discovery helper. |
| `ModbusCsvImport/` | Bulk per-tag importer for Modbus sources. |
| `ModbusSoakRunner/` | Long-running stress harness for the Modbus adapter. |

### `tests/` — test projects per layer

Same shape as `src/`. xUnit + FluentAssertions; NSubstitute when interfaces need mocking (rare — the codebase prefers hand-rolled fakes in test files). Integration tests under `tests/ElpisEdgeConnect.Integration.Tests/` use mock adapters, not real protocols.

## Where to look when…

| Question | Look here |
|---|---|
| What's locked vs. flexible? | `docs/ARCHITECTURE_BLUEPRINT.md` Appendix A + `docs/decisions/` |
| What was Phase 1 supposed to deliver? | `docs/PHASE1_EXECUTION_PLAN.md` |
| What's the per-tag MQTT contract? | `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md` |
| How does config draft → apply → rollback work? | `IConfigurationManager` + `docs/PHASE4_EXECUTION_PLAN.md` |
| What's the latest in-flight context? | The newest file in `docs/sessions/` |
| Why did we lock decision X? | ADR file in `docs/decisions/` (numbered sequentially) |
| What's the latest commit history shape? | `git log --oneline -50` |
| Anti-patterns I shouldn't violate? | `CLAUDE.md` §9 — refuse-list |

## Things that are not in this codebase

| Thing | Why not |
|---|---|
| The license private key | In the repo owner's password manager. Not in git. |
| Production licenses | Issued out-of-band per customer. |
| The Studio's customer-facing branding assets | Marketing surface lives in `docs/marketing/` and `docs/marketing/web/`, separate concern. |
| EREMOS V2 receiver source | Separate project at `C:\dev\EREMOS_V2\`. Contracts shared via `C:\dev\shared-knowledge\`. |
| Adapter test devices | Bring your own (physical or simulated) or use `tools/ModbusSimulator/` for Modbus. |

## Done?

Continue to [07-conventions.md](07-conventions.md).
