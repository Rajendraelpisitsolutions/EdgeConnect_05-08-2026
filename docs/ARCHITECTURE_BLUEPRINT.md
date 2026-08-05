# Elpis EdgeConnect — Architecture Blueprint v1

**Status:** Locked
**Last updated:** 2026-04-07
**Owners:** Sudhakar

---

## 1. Product Definition

**Elpis EdgeConnect** is a protocol-agnostic Industrial Edge Integration Platform. It runs as a Windows service on the factory floor, collects data from industrial devices via multiple southbound protocols, normalizes it through a canonical data pipeline, and delivers it to one or more northbound systems.

### Design principles (locked)

1. **Protocol-agnostic core** — The runtime does not know about any specific protocol.
2. **Canonical internal data model** — All device data becomes a normalized `CanonicalDataPoint` before routing.
3. **Source → Pipeline → Sink** — One source can fan out to many sinks through named routes.
4. **Routes are first-class** — Routes are the primary product concept, not a config footnote.
5. **Modular assemblies, not dynamic plugins** — Clean module boundaries without runtime plugin discovery complexity.
6. **License-gated activation** — Licensing enforced at three layers: packaging, runtime activation, UI/API.
7. **Store-and-forward is non-negotiable** — Edge means unreliable networks. Data survives disconnects.
8. **Design for OPC UA Server now, implement later** — Contracts must support pull/browse sinks even if first implementations are push-only.
9. **Per-adapter isolation** — One failing adapter never affects any other adapter, route, or sink.
10. **Real customer protocols first** — FOCAS2, MT-LINKi, MTConnect, Brother/custom HTTP, Modbus TCP, MQTT, HTTP sink from day one.

---

## 2. Key Concepts and Vocabulary

Terms are locked. Use these consistently in code, config, UI, docs, and conversation.

| Term | Definition |
|------|------------|
| **Gateway** | A running instance of Elpis EdgeConnect on a specific machine with a unique identity. |
| **Protocol Module** | A compiled assembly implementing support for a specific protocol (e.g., `ElpisEdgeConnect.Sources.Focas2`). Shipped or withheld based on edition. |
| **Source Adapter** | The implementation inside a protocol module that reads data from devices (`ISourceAdapter`). |
| **Sink Adapter** | The implementation inside a protocol module that delivers data to a destination (`ISinkAdapter`). |
| **Connector Instance** | A specific configured use of a protocol module — e.g., `focas-jyoti17` is a FOCAS2 source instance, `mqtt-eremos-main` is an MQTT sink instance. |
| **Canonical Data Point** | The normalized internal record that flows through the pipeline. All adapters read/write in this form. |
| **Route** | A named data flow: one source → filter → transforms → one or more sinks. Routes are the unit a customer thinks in. |
| **Transform Step** | A single stage in a route's transformation pipeline (tag mapping, deadband, rate limit, etc.). |
| **Pipeline** | The complete flow: acquisition → normalization → transforms → buffer → delivery. |
| **License** | Signed file defining which modules, features, and limits are active for a gateway. |

### Protocol Module vs Connector Instance

This distinction matters in licensing, UI, diagnostics, and config design:

- A customer licenses **protocol modules** (e.g., "FOCAS2 support").
- A customer configures **connector instances** (e.g., three FOCAS2 machines and two MT-LINKi sources).
- Licensing caps the number of instances per protocol module.
- Diagnostics are reported per connector instance.
- UI shows modules on a licensing/installation page and instances on the operational page.

---

## 3. Layered Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Management Layer                            │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐  │
│  │  Admin UI    │  │  Local API   │  │  Windows Service Host    │  │
│  └──────┬───────┘  └──────┬───────┘  └────────────┬─────────────┘  │
└─────────┼─────────────────┼───────────────────────┼────────────────┘
          │                 │                       │
┌─────────▼─────────────────▼───────────────────────▼────────────────┐
│                        Core Runtime                                 │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────────┐   │
│  │  License   │  │   Config   │  │   Route    │  │ Diagnostics│   │
│  │  Manager   │  │   Manager  │  │   Engine   │  │  Collector │   │
│  └────────────┘  └────────────┘  └──────┬─────┘  └────────────┘   │
│                                         │                          │
│  ┌────────────┐  ┌─────────────────────▼──────────────────────┐   │
│  │  Security  │  │            Transform Pipeline               │   │
│  │  Secrets   │  │   Map → Filter → Deadband → RateLimit → …  │   │
│  └────────────┘  └──────────────────────┬───────────────────────┘   │
│                                         │                          │
│  ┌────────────────────────┐  ┌─────────▼────────────────────────┐  │
│  │  Canonical Data Model  │  │  Store-and-Forward Buffer        │  │
│  │  CanonicalDataPoint    │  │  (SQLite persistent queue)        │  │
│  └────────────────────────┘  └──────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
          ▲                                            │
          │                                            ▼
┌─────────┴──────────────┐              ┌──────────────────────────┐
│   Southbound Adapters  │              │    Northbound Adapters   │
│                        │              │                          │
│  • Focas2              │              │  • MQTT                  │
│  • MtLinki             │              │  • HTTP/HTTPS            │
│  • MTConnect           │              │  • TCP Socket            │
│  • BrotherHttp         │              │  • OPC UA Server (v5)    │
│  • Modbus TCP          │              │  • OPC UA Client (v5)    │
│  • Siemens S7 (v5)     │              │                          │
│  • OPC UA Client (v5)  │              │                          │
│  • Custom drivers      │              │                          │
└────────────────────────┘              └──────────────────────────┘
```

### Layer responsibilities

**Management layer** — user-facing: service host, REST API, admin web UI. Never contains protocol logic.

**Core runtime** — protocol-agnostic engine: routing, transforms, licensing, diagnostics, config, security, buffer. Never references any protocol module directly.

**Source/Sink adapters** — protocol-specific. Reference only `ElpisEdgeConnect.Core`. Talk canonical model. Isolated failure domains.

---

## 4. Core Contracts

### 4.1 Canonical Data Model

```csharp
public sealed record CanonicalDataPoint
{
    public required string GatewayId { get; init; }
    public required string SourceInstanceId { get; init; }  // e.g., "focas-jyoti17"
    public required string ProtocolName { get; init; }      // e.g., "focas2"
    public required string DeviceId { get; init; }          // e.g., "Jyoti17CNC"
    public string? DeviceName { get; init; }

    public required string TagName { get; init; }           // canonical name after mapping
    public required string TagPath { get; init; }           // hierarchical path
    public string? OriginalTagName { get; init; }           // pre-mapping name

    public required object? Value { get; init; }
    public required CanonicalValueType ValueType { get; init; }
    public string? Unit { get; init; }

    public required DataQuality Quality { get; init; }
    public string? QualityReason { get; init; }

    public required DateTime DeviceTimestamp { get; init; }
    public required DateTime GatewayTimestamp { get; init; }

    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
    public long SequenceNumber { get; init; }
}

public enum CanonicalValueType
{
    Boolean, Integer, Long, Float, Double, String,
    DateTime, ByteArray, Array, Object, Null
}

public enum DataQuality
{
    Good, Uncertain, Bad, Stale, Unknown
}
```

### 4.2 Source Adapter Contract

```csharp
public interface ISourceAdapter : IAsyncDisposable
{
    string InstanceId { get; }
    string ProtocolName { get; }
    SourceCapabilities Capabilities { get; }
    AdapterState State { get; }

    Task InitializeAsync(SourceConfiguration config, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);

    Task<AdapterHealth> CheckHealthAsync(CancellationToken ct);

    // Polling sources
    Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct);

    // Subscription sources (OPC UA, event-driven devices)
    IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(CancellationToken ct);

    // Tag browsing where supported
    Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct);

    // Configuration validation
    Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct);
}

[Flags]
public enum SourceCapabilities
{
    None         = 0,
    Polling      = 1 << 0,
    Subscription = 1 << 1,
    Browse       = 1 << 2,
    WriteBack    = 1 << 3,
    TestConnect  = 1 << 4
}
```

### 4.3 Sink Adapter Contract

```csharp
public interface ISinkAdapter : IAsyncDisposable
{
    string InstanceId { get; }
    string ProtocolName { get; }
    SinkCapabilities Capabilities { get; }
    AdapterState State { get; }

    Task InitializeAsync(SinkConfiguration config, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);

    Task<AdapterHealth> CheckHealthAsync(CancellationToken ct);

    // Push-mode sinks (MQTT, HTTP, TCP)
    Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct);

    // Pull-mode sinks (OPC UA Server)
    Task UpdateCurrentValuesAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct);

    Task<ValidationResult> ValidateConfigAsync(SinkConfiguration config, CancellationToken ct);
}

[Flags]
public enum SinkCapabilities
{
    None          = 0,
    Push          = 1 << 0,   // MQTT, HTTP, TCP
    Pull          = 1 << 1,   // OPC UA Server exposes values
    Browse        = 1 << 2,   // Exposes node structure
    Batch         = 1 << 3,   // Supports batched publishing
    Transactional = 1 << 4,   // Ack/nack per message
    TestConnect   = 1 << 5
}

public sealed record PublishResult
{
    public bool Success { get; init; }
    public int AcceptedCount { get; init; }
    public int RejectedCount { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Latency { get; init; }
}
```

### 4.4 Route Contract

```csharp
public sealed record Route
{
    public required string RouteId { get; init; }
    public required string Name { get; init; }
    public required string SourceInstanceId { get; init; }
    public required TagFilter Filter { get; init; }
    public TransformProfile? Transforms { get; init; }
    public required IReadOnlyList<string> SinkInstanceIds { get; init; }
    public required BufferPolicy Buffer { get; init; }
    public required DeliveryPolicy Delivery { get; init; }
    public bool Enabled { get; init; }
}

public sealed record TagFilter
{
    public IReadOnlyList<string> Include { get; init; } = ["*"];
    public IReadOnlyList<string>? Exclude { get; init; }
}

public sealed record TransformProfile
{
    public IReadOnlyDictionary<string, string>? TagMapping { get; init; }
    public IReadOnlyDictionary<string, double>? Deadband { get; init; }
    public IReadOnlyDictionary<string, int>? RateLimitMs { get; init; }
    public IReadOnlyDictionary<string, object>? EnrichmentTags { get; init; }
}

public sealed record BufferPolicy
{
    public BufferMode Mode { get; init; }  // None | InMemory | StoreAndForward
    public int MaxDepth { get; init; }
    public TimeSpan MaxAge { get; init; }
    public DropPolicy OnOverflow { get; init; }  // DropOldest | DropNewest | Block
}

public sealed record DeliveryPolicy
{
    public DeliveryMode Mode { get; init; }  // AtMostOnce | AtLeastOnce
    // Required acknowledgment boundary (§19.7): None=0 | LocalTransport=1 | Broker=2 | Application=3.
    // Absent in legacy configs => None (deserializes without failure; existing behavior retained).
    // Validation rejects a route whose Required boundary exceeds its sink's advertised boundary.
    public DeliveryAcknowledgementBoundary RequiredAcknowledgementBoundary { get; init; }
    public int MaxRetries { get; init; }
    public TimeSpan RetryBackoff { get; init; }
    public bool FanoutParallel { get; init; }  // parallel vs sequential sink delivery
}
```

---

## 5. The Transform Pipeline

The pipeline is designed for the full feature set but implemented in stages. Each `ITransformStep` is independent, ordered, and per-route.

```csharp
public interface ITransformStep
{
    string Name { get; }
    IReadOnlyList<CanonicalDataPoint> Apply(
        IReadOnlyList<CanonicalDataPoint> input,
        TransformContext context);
}
```

### Ordered stages (all designed, implementation phased)

| Step | Phase | Purpose |
|------|-------|---------|
| `TagMappingStep` | 1 | Rename source tags to canonical names |
| `FilterStep` | 1 | Include/exclude tags by pattern |
| `DeadbandStep` | 1 | Suppress values that haven't changed by threshold |
| `RateLimitStep` | 1 | Cap publish frequency per tag |
| `EnrichmentStep` | 2 | Add static metadata (site, line, shift, etc.) |
| `UnitConversionStep` | 2 | Convert units (e.g., mm → inches) |
| `DerivedFieldStep` | 3 | Compute new tags from expressions |
| `AggregationStep` | 3 | Time-window aggregation (min/max/avg) |
| `AlarmEvaluationStep` | 3 | Evaluate alarm rules and emit alarm events |

The pipeline infrastructure lands in Phase 1. Individual steps land as scheduled.

---

## 6. Store-and-Forward Buffer

**Non-negotiable for edge deployment.** Every route configured with `BufferMode.StoreAndForward` gets a persistent queue.

### Design

- **Backend**: SQLite per route (`data/buffer/{routeId}.db`) for isolation
- **Hot path**: When sink is healthy, bypass storage — publish directly from in-memory channel
- **Cold path**: On sink failure or backpressure, persist to SQLite with sequence numbers
- **Drain**: On sink recovery, drain persisted messages in sequence order, then resume hot path
- **Compression**: Optional per-row compression (LZ4) for bandwidth-constrained environments
- **Retention**: Max size (MB) and max age (days) per route — oldest dropped first
- **Observability**: Current depth, oldest message age, drain rate, drops (exposed via diagnostics)

### Contract

```csharp
public interface IMessageBuffer : IAsyncDisposable
{
    string BufferId { get; }
    Task EnqueueAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct);
    Task<IReadOnlyList<CanonicalDataPoint>> DequeueBatchAsync(int maxCount, CancellationToken ct);
    Task AckAsync(long upToSequence, CancellationToken ct);
    Task<BufferStats> GetStatsAsync();
}

public sealed record BufferStats
{
    public long CurrentDepth { get; init; }
    public long TotalEnqueued { get; init; }
    public long TotalDrained { get; init; }
    public long TotalDropped { get; init; }
    public DateTime? OldestMessageAt { get; init; }
    public long SizeBytes { get; init; }
}
```

---

## 7. Licensing — Three Enforcement Layers

### 7.1 Layer 1: Packaging

Build and ship edition-specific installers. Each edition includes only the protocol module assemblies for that tier. Customers on Starter edition literally don't receive the Modbus or OPC UA assemblies.

| Edition | Source Modules | Sink Modules | Max Sources | Max Sinks |
|---------|----------------|--------------|-------------|-----------|
| **Starter** | 2 of {Focas2, MtLinki, MTConnect, BrotherHttp} | MQTT | 10 | 2 |
| **Professional** | All base + Modbus | MQTT, HTTP, TCP | 50 | 5 |
| **Enterprise** | All + S7, OPC UA Client | All + OPC UA Server | Unlimited | Unlimited |
| **Custom** | Per contract | Per contract | Per contract | Per contract |

### 7.2 Layer 2: Runtime Activation

Signed license file (RSA-signed JSON) controls what activates inside the binary. Even if a customer gets a Professional-edition binary, the license file determines the active modules.

```json
{
  "licenseId": "LIC-2026-0042",
  "customer": "Menon Manufacturing",
  "gatewayId": "GW-MENON-001",
  "edition": "Professional",
  "issuedAt": "2026-04-07",
  "expiresAt": "2027-04-07",
  "limits": {
    "maxSourceInstances": 50,
    "maxSinkInstances": 5,
    "maxRoutes": 100
  },
  "modules": {
    "source.focas2":      { "enabled": true,  "maxInstances": 20 },
    "source.mtlinki":     { "enabled": true,  "maxInstances": 10 },
    "source.mtconnect":   { "enabled": true,  "maxInstances": 10 },
    "source.brotherhttp": { "enabled": true,  "maxInstances": 5 },
    "source.modbus":      { "enabled": true,  "maxInstances": 10 },
    "source.s7":          { "enabled": false },
    "source.opcua":       { "enabled": false },
    "sink.mqtt":          { "enabled": true },
    "sink.http":          { "enabled": true },
    "sink.tcp":           { "enabled": true },
    "sink.opcuaserver":   { "enabled": false }
  },
  "features": {
    "storeAndForward":    true,
    "transforms.basic":   true,
    "transforms.advanced": false,
    "remoteManagement":   false,
    "tagBrowser":         true
  },
  "signature": "base64-rsa-signature-over-payload"
}
```

**Enforcement points:**
- **Adapter registration** — DI only registers adapters for licensed modules
- **Config validation** — Loading a source instance for an unlicensed module produces a clear error and the instance is marked `Blocked: Unlicensed`
- **Route activation** — Routes referencing blocked instances are marked unhealthy with explicit reason
- **Instance count enforcement** — Configuring the 11th Modbus instance on a 10-instance license is rejected at config-apply time
- **Graceful degradation** — Unlicensed modules never crash the service. Everything else keeps running.

### 7.3 Layer 3: UI/API Permission

- Admin UI hides or disables options for unlicensed modules
- REST API returns `403 Forbidden` with a reason code when creating resources for unlicensed modules
- License info is exposed at `GET /api/license` for the UI to drive its feature flags
- Expiry warnings shown 30/7/1 days before expiration
- Post-expiration: service continues running existing config (never cut data flow), but blocks all changes

### 7.4 Grace periods and offline operation

- Licenses work fully offline — no phone-home required
- 30-day expiration grace: degraded mode (warnings, no new config)
- After grace: config locked, data flow continues

---

## 8. Configuration Model

### 8.1 Structure

```json
{
  "Gateway": {
    "GatewayId": "GW-MENON-001",
    "GatewayName": "Menon Factory Floor",
    "Site": "Menon Mumbai Plant",
    "LicenseFile": "license.json",
    "LogLevel": "Information",
    "DataPath": "data/",
    "ManagementApi": {
      "Enabled": true,
      "Port": 8443,
      "RequireAuth": true,
      "TlsCertPath": "certs/api.pfx"
    },
    "HealthCheckPort": 8080,
    "Watchdog": { "Enabled": true, "RestartOnFailure": true }
  },

  "Sources": [
    {
      "InstanceId": "focas-jyoti17",
      "ProtocolName": "focas2",
      "Enabled": true,
      "DeviceId": "Jyoti17CNC",
      "DeviceName": "Jyoti 17 CNC",
      "Connection": { "IpAddress": "192.168.2.34", "Port": 8193, "TimeoutSeconds": 10 },
      "Polling": { "IntervalMs": 5000, "MaxConsecutiveErrors": 10 },
      "Tags": ["mill", "bay-1", "focas2"]
    }
  ],

  "Sinks": [
    {
      "InstanceId": "mqtt-eremos-main",
      "ProtocolName": "mqtt",
      "Enabled": true,
      "Connection": {
        "BrokerHost": "eremos.example.com",
        "BrokerPort": 8883,
        "ClientId": "edgeconnect-menon-001",
        "UseTls": true,
        "Username": "env:MQTT_USER",
        "Password": "env:MQTT_PASSWORD"
      },
      "Publishing": {
        "TopicPrefix": "eremos/menon/cnc",
        "BatchSize": 100,
        "BatchIntervalMs": 250,
        "QoS": 1
      }
    }
  ],

  "Routes": [
    {
      "RouteId": "jyoti17-to-eremos",
      "Name": "Jyoti 17 → EREMOS",
      "SourceInstanceId": "focas-jyoti17",
      "Filter": { "Include": ["*"] },
      "Transforms": {
        "TagMapping": {
          "CncState_path1_CNC": "machine.state",
          "Spindle/Speed": "spindle.speed"
        },
        "Deadband": { "spindle.speed": 0.5, "spindle.load": 1.0 },
        "EnrichmentTags": { "site": "Menon", "line": "Bay-1" }
      },
      "SinkInstanceIds": ["mqtt-eremos-main"],
      "Buffer": { "Mode": "StoreAndForward", "MaxDepth": 100000, "MaxAgeDays": 7 },
      "Delivery": { "Mode": "AtLeastOnce", "RequiredAcknowledgementBoundary": "LocalTransport", "MaxRetries": 5, "FanoutParallel": true },
      "Enabled": true
    }
  ]
}
```

### 8.2 Configuration versioning

Config management supports a **draft → validate → apply → rollback** lifecycle even before a full DB-backed config store lands:

- **Draft** — new config written to `config/drafts/{versionId}.json`
- **Validate** — full validation run: schema, license checks, adapter-specific validation, route integrity
- **Apply** — on success, promoted to `config/current.json`, previous version kept as `config/history/{versionId}.json`
- **Rollback** — restore any prior version from history
- **Audit** — every apply is logged with who/when/diff in `config/history/audit.log`
- **Versions retained**: last 20 applied configs

Hot-reload: config changes are applied live for sources, sinks, and routes where the protocol supports it. Restart-requiring changes are flagged at validation time.

---

## 9. Diagnostics

Three observability dimensions, per the earlier recommendation. Every layer has a consistent health model.

### 9.1 Source diagnostics (gateway ↔ device)

Per source instance:
- Connection state (`Disconnected`, `Connecting`, `Connected`, `Error`, `Blocked`)
- Last successful read / last error
- Read count, error count, consecutive errors
- Average latency, p95 latency
- Data points produced per second
- Protocol-specific fields (e.g., FOCAS2 handle status, MT-LINKi last HTTP code)
- Raw sample capture (toggleable, rate-limited) for debugging

### 9.2 Pipeline diagnostics (inside gateway)

Per route:
- Input rate (points/sec from source)
- Filtered-out count (by include/exclude)
- Deadband-suppressed count
- Rate-limited count
- Transform errors
- Buffer depth (current, peak, oldest age)
- Buffer writes, drains, drops
- End-to-end latency (acquisition → delivery)

### 9.3 Sink diagnostics (gateway ↔ destination)

Per sink instance:
- Connection state
- Last successful publish / last error
- Sent count, failed count, retry count
- Batch size distribution
- Publish latency (average, p95)
- Ack/nack counts (where supported)
- Backpressure events
- Queue depth (for batching sinks)

### 9.4 Contracts

```csharp
public interface IDiagnosticsCollector
{
    void RecordSourceEvent(string sourceInstanceId, SourceEvent evt);
    void RecordRouteEvent(string routeId, RouteEvent evt);
    void RecordSinkEvent(string sinkInstanceId, SinkEvent evt);

    Task<SourceDiagnostics> GetSourceDiagnosticsAsync(string instanceId);
    Task<RouteDiagnostics> GetRouteDiagnosticsAsync(string routeId);
    Task<SinkDiagnostics> GetSinkDiagnosticsAsync(string instanceId);
    Task<GatewayDiagnostics> GetGatewayDiagnosticsAsync();
}
```

All diagnostics exposed via:
- Local REST API (`/api/diagnostics/...`)
- Admin UI panels
- Prometheus `/metrics` endpoint (optional)
- MQTT `$gateway/{gatewayId}/diagnostics` topic (for remote visibility)

---

## 10. Gateway Identity and Provisioning

Every gateway instance must establish identity before handling data:

### 10.1 Identity fields

- **GatewayId** — stable UUID assigned at first start or by customer
- **CustomerId / Site** — binding to customer organization
- **LicenseId** — binding to license file
- **DeviceCertificate** — X.509 cert for cloud authentication (optional, Phase 4+)
- **Version / Build** — binary version metadata
- **Install timestamp**

Persisted at `data/identity.json` and exposed at `GET /api/gateway/identity`.

### 10.2 Provisioning flow

**First start:**
1. Check for `data/identity.json` — if missing, generate `GatewayId` (UUID)
2. Check for `license.json` — if missing, run in "unlicensed" mode (no adapters activate)
3. If license present, validate signature and bind to gateway
4. Customer enters customer/site metadata via admin UI or CLI (`edgeconnect provision`)
5. Identity file is written, gateway is ready

**Later:** Remote fleet management (Phase 5) uses this identity to push config and receive diagnostics from EREMOS central.

---

## 11. Reliability Requirements

All non-negotiable for factory-floor deployment.

| Requirement | How |
|-------------|-----|
| **Per-adapter isolation** | Each adapter runs in its own task scope with a catch-all. Exceptions never propagate to the runtime. |
| **Circuit breakers** | Polly-based per adapter. Trips after N consecutive failures, cools down, retries. |
| **Backpressure control** | Bounded channels between stages. Overflow policy is per-route. |
| **Graceful degradation** | Unhealthy components marked `Degraded`, not crashed. Running components keep running. |
| **Store-and-forward** | Mandatory for routes marked critical. |
| **Watchdog** | Windows Service recovery configuration + optional external watchdog process. |
| **Graceful shutdown** | On SIGTERM/stop: stop sources, drain pipeline, flush sinks, close storage, exit. Max 30s. |
| **Hot reload** | Config changes applied without restart where safe. Restart required changes flagged at validation. |
| **Config validation before apply** | Full validation including license, protocol-specific rules, route integrity. |
| **Resource limits** | Per-adapter memory and CPU limits (soft, monitored). Warnings on breach. |

---

## 12. Project Structure (Locked)

```
ElpisEdgeConnect.sln
│
├── src/
│   ├── ElpisEdgeConnect.Core/                 // Runtime foundation
│   │   ├── Adapters/            (contracts, capabilities, health)
│   │   ├── Model/               (CanonicalDataPoint, TagDefinition, etc.)
│   │   ├── Pipeline/            (transform steps, pipeline runner)
│   │   ├── Routing/             (Route, RouteEngine, TagFilter)
│   │   ├── Buffer/              (IMessageBuffer, InMemory, Sqlite)
│   │   ├── Licensing/           (LicenseManager, signature validation)
│   │   ├── Configuration/       (config models, versioning, validation)
│   │   ├── Diagnostics/         (collectors, stats, health)
│   │   └── Security/            (secrets, TLS helpers)
│   │
│   ├── ElpisEdgeConnect.Sources.Focas2/
│   ├── ElpisEdgeConnect.Sources.MtLinki/
│   ├── ElpisEdgeConnect.Sources.MTConnect/
│   ├── ElpisEdgeConnect.Sources.BrotherHttp/
│   ├── ElpisEdgeConnect.Sources.Modbus/
│   │
│   ├── ElpisEdgeConnect.Sinks.Mqtt/
│   ├── ElpisEdgeConnect.Sinks.Http/
│   ├── ElpisEdgeConnect.Sinks.Tcp/
│   │
│   ├── ElpisEdgeConnect.Host/                 // Windows service host + DI
│   │   ├── Program.cs
│   │   ├── AdapterRegistration.cs             // License-gated DI
│   │   ├── GatewayHostedService.cs
│   │   └── appsettings.json
│   │
│   └── ElpisEdgeConnect.Api/                  // Local management API
│       ├── Controllers/
│       │   ├── GatewayController.cs
│       │   ├── SourcesController.cs
│       │   ├── SinksController.cs
│       │   ├── RoutesController.cs
│       │   ├── DiagnosticsController.cs
│       │   ├── LicenseController.cs
│       │   └── ConfigController.cs
│       └── wwwroot/                           // Admin UI (static)
│
└── tests/
    ├── ElpisEdgeConnect.Core.Tests/
    ├── ElpisEdgeConnect.Sources.Focas2.Tests/
    ├── ElpisEdgeConnect.Sources.Modbus.Tests/
    ├── ElpisEdgeConnect.Sinks.Mqtt.Tests/
    └── ElpisEdgeConnect.Integration.Tests/
```

### Dependency rules

- `Core` references **nothing** from the rest of the solution
- `Sources.*` and `Sinks.*` reference only `Core`
- `Host` references `Core` + all `Sources.*` + all `Sinks.*`
- `Api` references `Core` + `Host`
- **No cross-references between Sources, between Sinks, or between Source/Sink modules**

---

## 13. Adapter SDK Conventions

Every adapter module (built in-house or by third parties later) must follow:

### 13.1 Adapter manifest (embedded resource)

```json
{
  "protocolName": "focas2",
  "displayName": "Fanuc FOCAS2",
  "version": "1.0.0",
  "type": "Source",
  "capabilities": ["Polling", "TestConnect"],
  "configSchema": "config-schema.json",
  "licenseKey": "source.focas2",
  "documentation": "https://docs.elpis.io/adapters/focas2",
  "supportContact": "support@elpis.io"
}
```

### 13.2 Required deliverables per adapter

- Manifest (embedded)
- JSON Schema for configuration validation
- Implementation of `ISourceAdapter` or `ISinkAdapter`
- Health check implementation
- Unit tests for config validation
- Integration test with mock device (where feasible)
- Documentation page (setup, config, troubleshooting, error codes)
- Error taxonomy (well-defined error codes, not free-text)

### 13.3 Error taxonomy

```csharp
public sealed record AdapterError
{
    public required string Code { get; init; }      // e.g., "FOCAS2.HANDLE_EXHAUSTED"
    public required ErrorCategory Category { get; init; }
    public required string Message { get; init; }
    public bool Retryable { get; init; }
    public TimeSpan? SuggestedBackoff { get; init; }
}

public enum ErrorCategory
{
    Configuration,     // Bad config — user must fix
    Authentication,    // Bad credentials
    Network,           // Transient network issue
    Protocol,          // Protocol-level error
    DeviceState,       // Device in wrong state (alarm, powered off)
    ResourceExhausted, // Too many handles, rate-limited
    License,           // Blocked by licensing
    Internal           // Bug in adapter
}
```

---

## 14. Implementation Phases

Phases are engineering milestones — not MVP gates. Each phase delivers working, deployable value.

### Phase 1 — Core Platform Foundation

Deliverables:
- `ElpisEdgeConnect.Core` with all contracts (`ISourceAdapter`, `ISinkAdapter`, `Route`, pipeline, diagnostics, licensing)
- Canonical data model
- Route engine with basic transform pipeline (tag mapping, filter, deadband, rate limit)
- In-memory buffer + SQLite store-and-forward buffer
- License manager with signature validation
- Configuration model + draft/validate/apply/rollback
- Diagnostics collector
- `ElpisEdgeConnect.Host` skeleton (Windows service)
- Unit test coverage for all Core components

**Exit criteria:** Mock source adapter + mock sink adapter running through a real route with real transforms, diagnostics, and buffering. No real protocols yet.

### Phase 2 — Migrate Real Customer Protocols

Deliverables:
- `Sources.Focas2` — refactored from current `Focas2DllDataSource`
- `Sources.MtLinki` — refactored from current `MtLinkiRestDataSource`
- `Sources.MTConnect` — refactored from current `MTConnectDataSource`
- `Sources.BrotherHttp` — refactored from current `BrotherHttpDataSource`
- `Sinks.Mqtt` — refactored from current `MqttPublisherService` (preserving EREMOS PerTag mode)
- Full migration of existing Menon customer configuration to new model
- End-to-end test: existing functionality works identically through new architecture

**Exit criteria:** Menon customer can swap from old `FanucCncDataBridge` to `ElpisEdgeConnect` with zero behavior change but gains Core runtime benefits (diagnostics, licensing, store-and-forward).

### Phase 3 — Commercial Expansion

Deliverables:
- `Sources.Modbus` (Modbus TCP) — first truly new source
- `Sinks.Http` — POST canonical data to REST endpoints with batching and retry
- `Sinks.Tcp` — framed TCP socket sink
- License-gated adapter registration wired into `Host`
- Edition-based installer builds (Starter/Professional/Enterprise)
- Route-level diagnostics API
- Initial customer deployment with Modbus + MQTT + HTTP

**Exit criteria:** A customer can buy a Professional license, install, configure a Modbus device plus a FOCAS2 machine, fan out to MQTT and HTTP simultaneously, and see end-to-end diagnostics.

### Phase 4 — Operability and UI

Deliverables:
- `ElpisEdgeConnect.Api` — full REST management API (sources, sinks, routes, config, diagnostics, license)
- Admin web UI with these screens:
  - Overview (gateway status, health summary, throughput)
  - Sources (list, add, edit, test, browse tags, diagnostics)
  - Sinks (list, add, edit, test, diagnostics)
  - Routes (list, visual data-flow editor, payload preview, diagnostics)
  - Diagnostics (source/pipeline/sink drill-down, message trace)
  - Logs (filter by adapter/severity/date)
  - License (current license, modules, limits, expiry)
  - Configuration (draft/apply/rollback, version history)
- Config backup/restore
- Authentication on API + UI

**Exit criteria:** A non-developer can install EdgeConnect, provision a gateway, configure protocols, create routes, and monitor health entirely through the web UI.

### Phase 5 — Advanced Capabilities

Deliverables:
- `Sources.OpcUaClient` (read and subscribe from OPC UA servers)
- `Sources.S7` (Siemens S7 via Sharp7 or similar)
- `Sinks.OpcUaServer` (expose canonical data as OPC UA server — pull-mode sink)
- `Sinks.OpcUaClient` (write to OPC UA servers)
- Advanced transforms: unit conversion, derived fields, aggregation, alarm evaluation
- Remote fleet management (connection to EREMOS central for config push + diagnostics streaming)
- Tag browser for OPC UA and Modbus
- Simulation mode (fake source + fake sink for testing)
- Replay mode (replay buffered history for debugging)

**Exit criteria:** Enterprise customers can deploy dozens of gateways, manage them centrally from EREMOS, and integrate with OPC UA-based SCADA/MES systems.

---

## 15. Open Questions (to resolve before Phase 1 coding)

1. **UI technology** — Blazor Server, Blazor WASM, or React + REST? (Blazor keeps us single-stack; React gives broader talent pool.)
2. **License signing key custody** — How are private signing keys managed? HSM, secure key vault, or developer machine?
3. **Admin UI auth model** — Local accounts, Windows auth, SSO, or all three?
4. **SQLite library choice** — `Microsoft.Data.Sqlite` (official) or `LiteDB` (simpler, embedded NoSQL)?
5. **Metrics export** — Prometheus only, or also OpenTelemetry?
6. **Remote management protocol** — MQTT control topics, gRPC, or REST polling from gateway to EREMOS?

These don't block Phase 1 but should be answered during it.

---

## 16. Out of Scope (v1)

These are explicitly not in the v1 plan:

- Dynamic plugin discovery at runtime (protocols are compile-time assemblies)
- Multi-tenancy within a single gateway (one gateway = one customer site)
- Real-time stream processing beyond basic transforms (no Flink/Kafka Streams equivalent)
- Built-in historian/time-series database (use downstream systems for that)
- Machine learning at the edge (future add-on)
- Mobile apps (web UI only)
- Non-Windows hosts (Linux later if demand)

---

## 17. AI Agents

Elpis EdgeConnect ships with a suite of AI agents that make the platform faster to configure, easier to debug, and friendlier to learn. AI lives in the **decision-support and productivity layer** — it never makes autonomous changes to the data path.

### 17.1 Design Principles (locked)

1. **AI in the decision-support layer only** — AI never decides whether to publish data, transform values at runtime, or modify routes autonomously. The data pipeline stays deterministic, replayable, and auditable.
2. **Grounded, never hallucinated** — Every AI response is grounded in real data: gateway diagnostics, config, logs, or docs. No freeform knowledge claims without a citable source.
3. **Tool-use over free-text** — Agents interact with the gateway via structured tool calls to the management API, not by generating SQL, shell commands, or code to execute.
4. **Propose, never silently act** — Any state-changing action (create source, apply config, restart adapter) requires explicit user confirmation. Agents suggest; humans decide.
5. **Read-only by default** — Agents have read access by default. Write access is per-agent, per-tool, per-permission, and always logged.
6. **Local-LLM capable from day one** — Every agent must work with a local LLM (Ollama, llama.cpp) for air-gapped customers. Cloud LLMs are optional, not required.
7. **Data sovereignty controlled by customer** — Per-gateway config determines what data can leave the gateway. Regulated customers can block all cloud AI providers entirely.
8. **Full audit trail** — Every AI interaction (prompts, tool calls, responses, user confirmations) is logged for compliance and debugging.
9. **Explainable and citable** — Every agent response references the evidence it used: specific diagnostic values, log lines, config fields, or doc sections.
10. **Fails gracefully** — If the AI provider is unreachable, the agent is disabled but the gateway continues full operation. AI is never on the critical path.

### 17.2 Agent Catalog

Six agents, organized by audience and function. All agents share the same infrastructure: provider abstraction, tool-use framework, permission model, and audit logging.

#### Agent 1: Diagnostic Copilot

**Audience:** Plant operators, support engineers
**Purpose:** Investigate "why isn't data arriving?" and similar runtime questions
**Mode:** Interactive chat in admin UI
**Risk:** Low (read-only against live gateway state)

**Capabilities:**
- Reads source/pipeline/sink diagnostics across all three dimensions
- Reads recent logs with pattern-based search
- Reads recent config changes to correlate incidents with changes
- Correlates signals across multiple adapters to identify root cause
- Explains findings in plain language with evidence
- Suggests remediation steps (never executes them without confirmation)

**Example interaction:**
> *User:* "Jyoti18 hasn't sent data for 10 minutes"
>
> *Agent:* "Source `focas-jyoti18` is polling successfully — last read 3 seconds ago, 2,400 reads since startup. Pipeline is processing normally. The MQTT sink `mqtt-eremos-main` has been disconnected since 14:32 with error code `MQTT.AUTH_REJECTED`. Your store-and-forward buffer for route `jyoti18-to-eremos` has queued 1,847 messages so no data has been lost. Likely cause: MQTT broker credentials changed or expired. Want me to show the current MQTT sink configuration so you can verify credentials?"

**Tools exposed:**
- `GetSourceDiagnostics(instanceId)`
- `GetSinkDiagnostics(instanceId)`
- `GetRouteDiagnostics(routeId)`
- `GetGatewayHealth()`
- `SearchLogs(pattern, timeRange, adapterFilter)`
- `GetRecentConfigChanges(timeRange)`
- `GetAdapterErrorHistory(instanceId, count)`

**Permissions:** Read-only on diagnostics, logs, config. No write access.

**License gating:** Included in Professional and Enterprise editions.

---

#### Agent 2: Configuration Copilot

**Audience:** Plant engineers, sales engineers, customer onboarding
**Purpose:** Guide users through configuring new sources, sinks, and routes via conversation
**Mode:** Interactive chat in admin UI, scoped to configuration pages
**Risk:** Medium (proposes config changes; never applies without user confirmation)

**Capabilities:**
- Walks users through adding a new source or sink step-by-step
- Tests device connections before committing config
- Calls `BrowseTags` on protocols that support it to discover available data points
- Suggests sensible defaults based on the customer's existing gateway setup (other sources, naming conventions, polling intervals, route patterns)
- Creates draft configurations for user review
- Validates configurations against license limits and schema before proposing apply
- Explains protocol-specific fields in plain language

**Example interaction:**
> *User:* "I need to add a new Fanuc machine in Bay 3"
>
> *Agent:* "I'll help you add a FOCAS2 source. What's the machine's IP address? I'll test the connection and detect the controller."
>
> *User:* "192.168.2.45"
>
> *Agent:* "Connected. This is a Fanuc 0i-TF Plus controller on port 8193. I found 247 available data points. Your existing FOCAS2 machines (Jyoti17, Jyoti18, Ace16) all collect the same 12 tags. Want me to use the same tag set for consistency, and name this instance `focas-bay3`?"
>
> *User:* "Yes, and add it to the EREMOS route"
>
> *Agent:* "I've drafted the configuration — a new source instance `focas-bay3` and added it to route `main-eremos-route`. Your Professional license has 38 of 50 source instances used, so this is within limits. Ready to review the draft before applying?"

**Tools exposed:**
- `TestSourceConnection(protocol, connectionParams)`
- `BrowseTags(tempInstanceId)`
- `GetExistingSources()`
- `GetExistingSinks()`
- `GetExistingRoutes()`
- `GetLicenseLimits()`
- `GetTagMappingHistory()`
- `CreateDraftSource(config)` — creates draft, does not apply
- `CreateDraftSink(config)` — creates draft, does not apply
- `CreateDraftRoute(config)` — creates draft, does not apply
- `ValidateDraft(draftId)`
- `RequestUserConfirmation(draftId, summary)` — shows diff for user approval

**Permissions:** Read access to config, license, existing instances. Write access only to the `config/drafts/` folder. Apply requires explicit user confirmation via UI.

**License gating:** Included in Professional and Enterprise editions.

---

#### Agent 3: Tag Mapping Assistant

**Audience:** Plant engineers, integrators
**Purpose:** Propose canonical tag name mappings when a new source is added, to unify naming across heterogeneous machines
**Mode:** Invoked from the source configuration screen, produces a mapping table for review
**Risk:** Low (output is a proposed mapping reviewed by the user before applying)

**Capabilities:**
- Reads all tags discovered from a new source (via `BrowseTags`)
- Reads the customer's existing canonical tag schema across all other sources
- Uses fuzzy matching + semantic similarity to propose mappings from source tags to canonical names
- Flags low-confidence mappings for explicit user review
- Identifies tags with no obvious canonical equivalent and offers to create new canonical names
- Learns from user corrections and improves future mapping suggestions
- Supports bulk apply to the source's `TagMapping` transform step

**Example interaction:**
> *Agent:* "I analyzed the 84 tags discovered on Siemens 840D source `s7-bay5`. Based on your existing canonical schema (used across 12 other machines), here are my suggested mappings:
>
> **High confidence (72 tags):**
> - `sActSpeed` → `spindle.speed`
> - `sActLoad` → `spindle.load`
> - `aaIm[X1]` → `axis.x.position`
> - `aaIm[Y1]` → `axis.y.position`
> - ... [68 more]
>
> **Medium confidence (6 tags) — please review:**
> - `dpSBL` → `machine.single_block_mode` (similar to Fanuc `SigSBK`)
> - `dpDRF` → `machine.dry_run_mode` (similar to Fanuc `SigMDRN`)
> - ... [4 more]
>
> **No canonical equivalent (6 tags) — suggest new names:**
> - `cyclePartStatus` — suggest new canonical: `machine.part_status`
> - `toolRemainLife` — suggest new canonical: `tool.remaining_life`
> - ... [4 more]
>
> Want to review the full mapping table and apply it to the source?"

**Tools exposed:**
- `GetDiscoveredTags(sourceInstanceId)`
- `GetCanonicalSchema()` — all canonical names in use across the gateway
- `GetTagMappingHistory()` — past user-confirmed mappings for learning
- `ProposeMappings(discoveredTags, canonicalSchema)` — LLM reasoning
- `CreateDraftTagMapping(sourceInstanceId, mappingTable)`
- `ValidateMapping(mappingTable)`

**Permissions:** Read access to all source tag definitions and canonical schema. Write access only to draft mappings. Applying a mapping requires user confirmation and modifies the source's `TransformProfile.TagMapping`.

**License gating:** Included in Professional and Enterprise editions.

---

#### Agent 4: Protocol Onboarding Assistant

**Audience:** Internal development team, custom adapter partners
**Purpose:** Scaffold new protocol adapters that follow the adapter SDK conventions
**Mode:** CLI tool (`edgeconnect-adapter-gen`) and optionally a developer portal web UI
**Risk:** Low (output is code for developer review, never runs against production)

**Capabilities:**
- Generates complete adapter project scaffolding conforming to `ElpisEdgeConnect.Sources.*` or `ElpisEdgeConnect.Sinks.*` structure
- Produces adapter manifest, config model, config JSON schema, error taxonomy, diagnostics integration, unit test scaffold, documentation stub
- References existing adapters as pattern templates (consistency with FOCAS2, MT-LINKi, Modbus, etc.)
- Marks all protocol-specific logic as `TODO(human)` with clear guidance comments
- Suggests appropriate capabilities (Polling/Subscription/Browse/TestConnect) based on protocol characteristics
- Generates error codes following the locked `PROTOCOL.CATEGORY` naming convention
- Generates standard diagnostic field list wired to `IDiagnosticsCollector`

**Example usage:**
```bash
edgeconnect-adapter-gen create \
  --name Siemens.S7 \
  --type Source \
  --capabilities Polling,Browse,TestConnect \
  --license-key source.s7 \
  --description "Siemens S7-300/400/1200/1500 via Sharp7"
```

**Output:** Complete project `ElpisEdgeConnect.Sources.S7/` added to the solution with:
- Project file wired to Core
- `manifest.json`
- `S7SourceConfiguration.cs`
- `S7ConfigSchema.json`
- `S7SourceAdapter.cs` with `ISourceAdapter` implementation stubbed with TODOs
- `Errors/S7Errors.cs` with error taxonomy
- `S7Diagnostics.cs` wired to `IDiagnosticsCollector`
- `S7SourceAdapterTests.cs` test scaffold
- `docs/adapters/s7.md` documentation stub

**Generated code marks protocol-specific logic:**
```csharp
public async Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct)
{
    // TODO(human): Implement Siemens S7 data acquisition.
    // Expected behavior:
    //   1. Connect to _config.IpAddress:102 using Sharp7
    //   2. Read each tag in _config.Tags from the specified DB/M/I/Q area
    //   3. Convert readings to CanonicalDataPoint via _pointFactory
    //   4. Set DataQuality.Good on success
    //   5. Throw AdapterException with Retryable=true for transient network errors
    // See docs/adapter-sdk/polling-adapters.md for patterns
    // Reference implementation: ElpisEdgeConnect.Sources.Modbus for similar TCP polling
    throw new NotImplementedException();
}
```

**Tools exposed (to the LLM):**
- `GetExistingAdapters()` — read reference implementations
- `GetAdapterSdkDocs()` — load SDK conventions
- `GetConfigSchemaTemplate()` — load standard schema structure
- `GetErrorTaxonomyConventions()` — load naming rules
- `WriteProjectFiles(projectName, files)` — generate to disk

**Permissions:** Local filesystem write to the repo's `src/` and `tests/` folders. Never runs against production gateways.

**License gating:** Not customer-facing. Internal tool, not licensed.

---

#### Agent 5: Intelligent Alerting

**Audience:** Plant operators, on-call engineers, site managers
**Purpose:** Interpret existing deterministic alerts in plain language with causal reasoning
**Mode:** On-demand "Explain this alert" action in the admin UI; optional daily digest email
**Risk:** Low (interpretation-only, never generates or suppresses alerts)

**Capabilities:**
- Takes a deterministic alert (e.g., "MQTT sink disconnected 17 times in 30 min") and interprets it
- Correlates across source/pipeline/sink dimensions to identify root cause
- Correlates with recent config changes, recent log patterns, and related adapters
- Distinguishes between similar-looking causes (e.g., auth failure vs. network loss vs. broker overload)
- References past similar incidents from the audit log
- Returns structured interpretation: summary, likely cause, evidence, suggested actions, confidence score
- Optionally generates end-of-shift or daily digest summarizing the day's incidents with explanations

**IMPORTANT:** This agent does NOT generate alerts. Alerts are raised by deterministic rules (thresholds, state changes, error counts) that customers can audit and tune. The agent only explains alerts that have already been raised.

**Example interaction:**
> *Alert (from deterministic rule):* "Route `jyoti17-to-eremos` buffer depth exceeded 10,000 at 14:47"
>
> *Agent (on click "Explain"):* "The route buffer is growing because the HTTP sink `http-backup` has been unable to deliver since 14:32 — 15 minutes before the buffer alarm. Root cause looks like HTTP endpoint rejection: the sink logged 47 responses with status 401 Unauthorized in the last 15 minutes. The MQTT sink on the same route (`mqtt-eremos-main`) is healthy, so data is still reaching EREMOS via that path. Store-and-forward is preserving messages for HTTP, so no data loss.
>
> **Likely cause:** HTTP sink auth token expired or rotated on the backend
> **Evidence:** 47x HTTP 401 in sink logs; MQTT sink healthy on same route; buffer growth started precisely when HTTP 401s began
> **Suggested action:** Verify HTTP sink bearer token; refresh `env:API_TOKEN` if rotated
> **Confidence:** High (88%)
> **Similar past incident:** 2026-01-14 — same pattern, resolved by token rotation"

**Tools exposed:**
- Same tools as Diagnostic Copilot (read-only diagnostics, logs, config history)
- `GetAlertHistory(timeRange)` — past alerts and their resolutions
- `GetAuditLog(timeRange)`
- `FindSimilarIncidents(alertPattern)` — pattern match against past incidents

**Permissions:** Read-only. Never modifies alerts, config, or state.

**License gating:** Included in Professional and Enterprise editions.

---

#### Agent 6: Documentation Copilot

**Audience:** All users — from first-time evaluators to experienced operators
**Purpose:** Answer product questions grounded in real documentation, with gateway-context awareness
**Mode:** Chat sidebar in admin UI; also published on the public docs site
**Risk:** Very low (read-only against docs and optionally gateway state)

**Capabilities:**
- RAG over the product documentation corpus (concept guides, how-tos, protocol references, troubleshooting)
- Grounds every answer with citations to specific doc sections
- Version-aware — retrieves docs matching the installed gateway version, not the latest
- When embedded in admin UI: gateway-context aware — knows the customer's edition, licensed modules, current config, and recent state
- When on public docs site: operates without gateway context, answers general product questions
- Answers personalized to the customer's actual setup (e.g., "OPC UA requires Enterprise edition; you're on Professional")
- Says "I don't know" when docs don't cover a question, and offers to open a support ticket
- Captures user thumbs-up/down feedback to surface doc gaps for improvement

**Example interaction (gateway-context mode):**
> *User:* "How do I add an OPC UA source?"
>
> *Agent:* "OPC UA Client is an Enterprise edition feature. Your gateway `GW-MENON-001` is on Professional edition, so this source type is not currently available on your license. [Citation: docs/licensing/editions.md#enterprise]
>
> If you'd like to upgrade, the OPC UA Client adapter supports both polling and subscription modes against OPC UA servers, with full browse support. Configuration requires: endpoint URL, security mode, certificate, and node selection. [Citation: docs/adapters/opcua-client.md]
>
> Want me to connect you with sales about an Enterprise upgrade, or would you like to see the general OPC UA Client documentation?"

**Example interaction (public docs mode):**
> *User:* "What's the difference between Bundled and PerTag MQTT publish modes?"
>
> *Agent:* "Bundled mode publishes all tags from a single data collection as one JSON payload on one MQTT topic. PerTag mode publishes each tag to its own topic with the pattern `{prefix}/{machineId}/{tagName}`. [Citation: docs/sinks/mqtt.md#publish-modes]
>
> PerTag is the recommended mode for integration with EREMOS and similar per-tag subscribing systems. Bundled is useful when downstream consumers want an atomic snapshot of machine state. [Citation: docs/sinks/mqtt.md#when-to-use]"

**Tools exposed:**
- `SearchDocs(query, version)` — vector search over docs corpus
- `GetDoc(path, version)` — retrieve specific doc page
- `GetGatewayIdentity()` — (gateway-context mode only)
- `GetLicenseInfo()` — (gateway-context mode only)
- `GetEnabledModules()` — (gateway-context mode only)
- `RecordFeedback(questionId, rating, comment)`

**Permissions:** Read-only on docs corpus and (when embedded) gateway identity/license/modules. No access to live telemetry or sensitive config.

**License gating:** **Free on all editions** including Starter. Docs assistance is table-stakes and should never be paywalled.

### 17.3 Agent Comparison Summary

| Agent | Audience | Phase | Mode | Write Access | Risk | License |
|-------|----------|-------|------|--------------|------|---------|
| Diagnostic Copilot | Operators | 4.5 | Chat | None | Low | Pro+ |
| Configuration Copilot | Engineers | 4.5 | Chat | Draft only | Medium | Pro+ |
| Tag Mapping Assistant | Engineers | 4.5 | Action | Draft only | Low | Pro+ |
| Protocol Onboarding | Internal devs | 2-3 | CLI | Repo files | Low | Internal tool |
| Intelligent Alerting | Operators | 4.5 | On-demand | None | Low | Pro+ |
| Documentation Copilot | Everyone | 4 | Chat | None | Very low | Free (all editions) |

### 17.4 Module Structure

All AI functionality lives in a single module: `ElpisEdgeConnect.AI`. It is a compile-time assembly like any protocol module, activated by license flag `features.aiAssistant`.

```
src/ElpisEdgeConnect.AI/
├── ElpisEdgeConnect.AI.csproj
│
├── Core/
│   ├── IAgent.cs                          // Base agent contract
│   ├── IAgentContext.cs                   // Gateway identity, license, permissions
│   ├── AgentResponse.cs                   // Standard response format with citations
│   ├── AgentRequest.cs
│   ├── Citation.cs                        // Evidence reference (doc, diagnostic, log line)
│   ├── ConfidenceScore.cs
│   └── AgentAuditLog.cs                   // Full interaction audit trail
│
├── Providers/
│   ├── IAiProvider.cs                     // Provider abstraction
│   ├── ProviderCapabilities.cs            // ToolUse, Streaming, Vision, etc.
│   ├── AnthropicProvider.cs               // Claude via API
│   ├── OpenAIProvider.cs                  // GPT via API
│   ├── OllamaProvider.cs                  // Local LLM via Ollama
│   ├── LlamaCppProvider.cs                // Local LLM via llama.cpp
│   └── CustomEndpointProvider.cs          // Customer-hosted LLM endpoint
│
├── Tools/
│   ├── ITool.cs                           // Tool contract (name, schema, handler)
│   ├── ToolRegistry.cs                    // Per-agent tool registration
│   ├── ToolInvocation.cs                  // Tool call record for audit
│   ├── ToolPermission.cs                  // Read-only / draft-write / etc.
│   │
│   ├── DiagnosticsTools/
│   │   ├── GetSourceDiagnosticsTool.cs
│   │   ├── GetSinkDiagnosticsTool.cs
│   │   ├── GetRouteDiagnosticsTool.cs
│   │   ├── GetGatewayHealthTool.cs
│   │   └── SearchLogsTool.cs
│   │
│   ├── ConfigTools/
│   │   ├── GetExistingSourcesTool.cs
│   │   ├── GetExistingSinksTool.cs
│   │   ├── GetExistingRoutesTool.cs
│   │   ├── TestSourceConnectionTool.cs
│   │   ├── BrowseTagsTool.cs
│   │   ├── CreateDraftSourceTool.cs
│   │   ├── CreateDraftSinkTool.cs
│   │   ├── CreateDraftRouteTool.cs
│   │   └── ValidateDraftTool.cs
│   │
│   ├── LicenseTools/
│   │   ├── GetLicenseInfoTool.cs
│   │   └── GetLicenseLimitsTool.cs
│   │
│   ├── TagMappingTools/
│   │   ├── GetCanonicalSchemaTool.cs
│   │   ├── GetTagMappingHistoryTool.cs
│   │   ├── ProposeMappingsTool.cs
│   │   └── CreateDraftTagMappingTool.cs
│   │
│   ├── DocsTools/
│   │   ├── SearchDocsTool.cs
│   │   ├── GetDocTool.cs
│   │   └── RecordFeedbackTool.cs
│   │
│   └── ScaffoldingTools/                  // Used by Protocol Onboarding Assistant
│       ├── GetExistingAdaptersTool.cs
│       ├── GetAdapterSdkDocsTool.cs
│       └── WriteProjectFilesTool.cs
│
├── Rag/
│   ├── IDocumentStore.cs                  // Vector store abstraction
│   ├── SqliteVectorStore.cs               // sqlite-vec backed, runs at edge
│   ├── DocumentIndexer.cs                 // Builds index at install time
│   ├── DocumentChunker.cs                 // Markdown-aware chunking
│   ├── IEmbeddingProvider.cs              // Embedding abstraction
│   ├── LocalEmbeddingProvider.cs          // Sentence transformers local
│   └── CloudEmbeddingProvider.cs          // OpenAI/Cohere when allowed
│
├── Agents/
│   ├── DiagnosticCopilot.cs
│   ├── ConfigurationCopilot.cs
│   ├── TagMappingAssistant.cs
│   ├── ProtocolOnboardingAssistant.cs
│   ├── IntelligentAlertingAgent.cs
│   └── DocumentationCopilot.cs
│
├── Prompts/
│   ├── SystemPrompts/                     // Per-agent system prompts
│   │   ├── DiagnosticCopilot.system.md
│   │   ├── ConfigurationCopilot.system.md
│   │   ├── TagMappingAssistant.system.md
│   │   ├── ProtocolOnboarding.system.md
│   │   ├── IntelligentAlerting.system.md
│   │   └── DocumentationCopilot.system.md
│   └── Templates/                         // Reusable prompt fragments
│
├── Security/
│   ├── DataSovereigntyPolicy.cs           // What data can leave the gateway
│   ├── PiiRedactor.cs                     // Strip sensitive data before sending to cloud LLMs
│   ├── AgentPermissionManager.cs
│   └── PromptInjectionDefense.cs          // Sanitize user input and tool outputs
│
└── Configuration/
    ├── AiSettings.cs                      // Top-level AI config
    ├── ProviderSettings.cs                // Per-provider config
    └── AgentSettings.cs                   // Per-agent enable/disable, model, etc.
```

### 17.5 Provider Strategy

Every agent must work with any provider. Customers pick based on their constraints:

| Provider | When to use | Data leaves gateway? |
|----------|-------------|----------------------|
| **Anthropic (Claude)** | Cloud-allowed customers, best reasoning quality | Yes |
| **OpenAI (GPT)** | Cloud-allowed customers, GPT preference | Yes |
| **Ollama (local)** | Air-gapped, regulated, cost-sensitive | No |
| **llama.cpp (local)** | Resource-constrained edge hardware | No |
| **Custom endpoint** | Customer-hosted LLM (Azure OpenAI in their tenant, self-hosted vLLM, etc.) | Per customer |

**Ollama with Llama 3 8B or Phi-3 Mini** is the recommended default for air-gapped deployments — good enough for the agent tasks described here and runs on modest hardware.

### 17.6 Configuration

Per-gateway AI settings in `appsettings.json`:

```json
{
  "AI": {
    "Enabled": true,
    "Provider": "local",
    "Providers": {
      "local": {
        "Type": "Ollama",
        "Endpoint": "http://localhost:11434",
        "Model": "llama3:8b",
        "EmbeddingModel": "nomic-embed-text"
      },
      "anthropic": {
        "Type": "Anthropic",
        "ApiKey": "env:ANTHROPIC_API_KEY",
        "Model": "claude-sonnet-4-6"
      }
    },
    "DataSovereignty": {
      "AllowTelemetryExport": false,
      "AllowConfigExport": false,
      "AllowLogExport": false,
      "RedactPii": true
    },
    "Agents": {
      "DiagnosticCopilot":       { "Enabled": true, "Provider": "local" },
      "ConfigurationCopilot":    { "Enabled": true, "Provider": "local" },
      "TagMappingAssistant":     { "Enabled": true, "Provider": "local" },
      "IntelligentAlerting":     { "Enabled": true, "Provider": "local" },
      "DocumentationCopilot":    { "Enabled": true, "Provider": "local" }
    },
    "AuditLog": {
      "Enabled": true,
      "RetentionDays": 90,
      "IncludePrompts": true,
      "IncludeToolCalls": true,
      "IncludeResponses": true
    }
  }
}
```

### 17.7 Security Considerations

**Prompt injection defense** — Tool outputs and log content are untrusted input to the LLM. The `PromptInjectionDefense` layer wraps all tool outputs in clear delimiters, instructs the model to treat them as data not instructions, and strips obvious injection patterns before passing to the model.

**PII redaction** — When a cloud provider is used, `PiiRedactor` scans outbound content for credentials, API keys, IP addresses, and customer-identifying strings. Matches are replaced with placeholders before transmission. Regulated customers can enable strict mode which blocks any outbound content containing even suspected PII.

**Tool permission enforcement** — Every tool declares its permission level. The agent framework checks tool permissions against the agent's granted permissions before every invocation. Agents cannot elevate their own permissions.

**Audit integrity** — The audit log is append-only and includes a content hash chain so tampering is detectable. Required for regulated customers.

**Kill switch** — A single config flag disables all AI features instantly without restart. Useful for incident response.

### 17.8 Phase Placement

AI agents are built in two waves, aligned with platform phases:

**Phase 2-3: Protocol Onboarding Assistant**
Build as a CLI tool (`edgeconnect-adapter-gen`) once 2-3 adapters exist as template references. This is the first AI feature shipped because it accelerates the development of Phase 2 and Phase 3 adapters themselves. Internal tool only.

**Phase 4: Documentation Copilot**
Ships alongside the admin UI. Requires the management API and docs corpus. Free on all editions.

**Phase 4.5: Interactive Agents**
After Phase 4 lands the management API and UI, build the four interactive agents that depend on that infrastructure:
- Diagnostic Copilot
- Configuration Copilot
- Tag Mapping Assistant
- Intelligent Alerting

These share infrastructure (provider abstraction, tool registry, audit log, permission model) so they ship together as the "EdgeConnect Copilot" add-on for Professional and Enterprise editions.

**Phase 5+: Fleet-level AI (future)**
Once EREMOS central has multiple gateways reporting, fleet-wide agents become possible:
- Cross-customer pattern detection (anonymized)
- Fleet health summarization
- Capacity/license trend forecasting for sales

Not in the v1 scope, but the canonical data model and gateway identity locked in Phase 1 make it possible later.

### 17.9 Commercial Packaging

| Edition | AI Features |
|---------|-------------|
| **Starter** | Documentation Copilot only |
| **Professional** | Documentation Copilot + EdgeConnect Copilot (Diagnostic, Configuration, Tag Mapping, Intelligent Alerting) |
| **Enterprise** | Everything in Professional + priority model access + fleet-level features (when available) |
| **Internal** | Protocol Onboarding Assistant (not sold — dev tool) |

The EdgeConnect Copilot bundle is the commercial differentiator. Marketed as "the gateway that helps you operate it."

---

## 18. Performance and Scale Targets

These targets anchor Phase 1 and Phase 2 design decisions. They represent **expected production envelopes**, not hard limits. If an implementation choice cannot meet these targets, it must be revisited.

### 18.1 Gateway Sizing Tiers

EdgeConnect is designed to run on three classes of hardware. Each tier has its own performance envelope.

| Tier | Typical Hardware | Max Sources | Max Sinks | Max Routes | Peak Points/sec |
|------|------------------|-------------|-----------|------------|-----------------|
| **Small** | Industrial PC, 2 cores, 4 GB RAM, SSD | 10 | 3 | 20 | 500 |
| **Medium** | Rack server / mini PC, 4 cores, 8 GB RAM, SSD | 50 | 5 | 100 | 5,000 |
| **Large** | Server, 8+ cores, 16+ GB RAM, SSD | 200 | 10 | 500 | 25,000 |

A single gateway is not expected to exceed the Large tier. Beyond that, customers deploy multiple gateway instances with partitioned device lists.

### 18.2 Throughput Targets

Measured at the pipeline level (source → route → sink), steady state:

| Metric | Small | Medium | Large |
|--------|-------|--------|-------|
| Sustained points/sec per gateway | 500 | 5,000 | 25,000 |
| Peak burst points/sec (30 sec) | 2,000 | 20,000 | 100,000 |
| Concurrent polling sources | 10 | 50 | 200 |
| Concurrent sink publishes | 3 | 10 | 20 |
| Routes processing in parallel | 20 | 100 | 500 |

### 18.3 Latency Targets

End-to-end latency from source poll to sink publish (measured at p95):

| Stage | Target (p95) | Hard ceiling |
|-------|--------------|--------------|
| Source acquisition (per poll) | < 200 ms | 1,000 ms |
| Normalization (per point) | < 100 μs | 1 ms |
| Transform pipeline (per point, 4 steps) | < 500 μs | 5 ms |
| Route dispatch (per batch) | < 10 ms | 50 ms |
| Sink publish (MQTT, healthy broker, batch of 100) | < 50 ms | 500 ms |
| **End-to-end (healthy path)** | **< 1 second** | **2 seconds** |

Store-and-forward path latency is unbounded by design — recovery can take hours if the sink is down for hours. What matters is throughput on drain, not latency.

### 18.4 Store-and-Forward Targets

Measured per route's SQLite buffer:

| Metric | Target |
|--------|--------|
| Enqueue throughput (single route) | ≥ 5,000 points/sec |
| Drain throughput on recovery (single route) | ≥ 10,000 points/sec |
| Max buffer depth per route (default) | 1,000,000 points |
| Max buffer size on disk per route | 500 MB |
| Buffer overhead per point (on disk, compressed) | < 200 bytes |
| Recovery time from 1 hour outage (5,000 pts/sec source) | < 2 minutes to catch up |
| Recovery time from 24 hour outage (1,000 pts/sec source) | < 30 minutes to catch up |

### 18.5 Resource Envelope

Steady-state resource usage (not peak), per gateway tier:

| Resource | Small | Medium | Large |
|----------|-------|--------|-------|
| CPU (average, all cores) | < 10% | < 25% | < 40% |
| RAM (excluding OS) | < 500 MB | < 2 GB | < 6 GB |
| Disk I/O (sustained write) | < 5 MB/s | < 20 MB/s | < 50 MB/s |
| Network out (to sinks, compressed) | < 1 Mbps | < 10 Mbps | < 50 Mbps |
| SQLite buffer disk (all routes) | < 1 GB | < 10 GB | < 50 GB |

**Hard ceilings** — if a gateway exceeds these under normal load, something is wrong:

| Resource | Ceiling |
|----------|---------|
| RAM | 80% of system RAM |
| CPU sustained | 70% of all cores |
| Disk | 80% of allocated data partition |
| Open file handles | 2,000 |
| Open sockets | 500 |

Gateway health check degrades to `Warning` at 70% of ceilings and `Critical` at 90%.

### 18.6 Adapter-Specific Targets

Each protocol has its own realistic envelope. These are guidelines, not universal:

| Protocol | Points per poll | Poll interval (min) | Notes |
|----------|-----------------|---------------------|-------|
| FOCAS2 (per controller) | 20-50 | 1 sec (3-5 sec recommended) | Handle-constrained; aggressive polling breaks CNCs |
| MT-LINKi | 10-100 | 5 sec | REST API rate-limited upstream |
| MTConnect | 50-200 | 1 sec | XML parsing cost scales with document size |
| Brother HTTP | 10-30 | 5 sec | Multiple endpoints per poll |
| Modbus TCP | 10-500 | 500 ms | Register reads are cheap; network round-trips dominate |
| OPC UA Client (subscription) | N/A | Event-driven | Subscription rate limit is server-controlled |

### 18.7 Scale Testing Strategy

Phase 1 exit criteria include a synthetic load test using a mock source adapter that generates configurable points/sec. Phase 2 exit adds real-adapter load testing against a simulator lab (FOCAS2 mock, Modbus simulator).

Every release must pass:
- Medium-tier sustained load (5,000 pts/sec) for 24 hours with stable RAM
- Large-tier burst load (100,000 pts/sec for 30 seconds) without data loss
- Store-and-forward recovery test: 1 hour simulated outage, verify zero data loss and bounded recovery time
- Leak test: 7-day continuous run, verify no memory growth beyond 10% of initial

---

## 19. Route Execution Semantics

Routes are the product's most visible primitive, and their runtime behavior must be unambiguous. This section locks the semantic questions that were implicit in the route contract.

### 19.1 Execution Model Overview

A route is a **directed data flow** with exactly one source and one or more sinks, joined by a transform pipeline and a buffer. The runtime model is:

```
Source  ──poll/subscribe──►  Acquisition Queue (per source)
                                      │
                                      ▼
                             Normalization
                                      │
                                      ▼
                       ┌──────────────┴──────────────┐
                       ▼                             ▼
                    Route A                       Route B
                       │                             │
                       ▼                             ▼
                  Transforms                   Transforms
                       │                             │
                       ▼                             ▼
                  Route Buffer                 Route Buffer
                       │                             │
                       ▼                             ▼
              ┌────────┴────────┐           ┌────────┴────────┐
              ▼                 ▼           ▼                 ▼
         Sink 1            Sink 2      Sink 2            Sink 3
```

Each route runs in its own asynchronous task with its own bounded channel and buffer. Routes do not block each other. A source fans out to all routes that reference it; a sink receives data from all routes that target it.

### 19.2 Fanout Semantics

**Question:** If one sink fails in a multi-sink route, does success to the other sinks commit immediately, or is the delivery transactional?

**Answer (locked):** Fanout is **independent per sink, not transactional**. Success to one sink commits immediately regardless of the state of other sinks.

Rationale: Transactional fanout across heterogeneous sinks (MQTT + HTTP + TCP) is impractical and forces the slowest sink to gate the fastest. Industrial edge customers prefer independent reliability per sink over atomicity across sinks.

**Implementation:**
- Each sink gets its own per-sink buffer position tracker within the route's buffer
- A point is "committed" when all target sinks have independently acknowledged (or dropped per policy)
- Per-sink acknowledgment advances only that sink's position marker
- The route buffer entry is released only when **all** sinks have committed past it

**Consequence:** A failing sink does not block a healthy sink. A failing sink's backlog grows in its per-sink position tracking; the route buffer holds entries until the slowest sink catches up or exceeds retention.

### 19.3 Buffer Granularity

**Question:** Is buffering per route or per route-sink pair?

**Answer (locked):** Buffer storage is **per route** (one SQLite database per route). Position tracking is **per route-sink pair** (one cursor per sink within that route).

Rationale: Per-route storage keeps the number of SQLite files bounded and manageable (50 routes = 50 files, not 200). Per-sink cursors allow independent progress and recovery without duplicating payloads.

**Layout:**

```
data/buffer/
├── route-jyoti17-to-eremos.db        (one file per route)
│   ├── points table                  (canonical data points)
│   │   ├── sequence INTEGER PRIMARY KEY
│   │   ├── payload BLOB              (compressed canonical point)
│   │   ├── enqueued_at INTEGER
│   │   └── expires_at INTEGER
│   └── sink_cursors table            (one row per sink in this route)
│       ├── sink_instance_id TEXT PRIMARY KEY
│       ├── committed_sequence INTEGER
│       ├── last_attempt_at INTEGER
│       └── last_error TEXT
│
├── route-ace16-to-eremos.db
│   └── ...
```

A point is eligible for deletion when **min(sink_cursors.committed_sequence) > point.sequence** OR when retention policy (age/size) triggers eviction.

### 19.4 Retry Tracking

**Question:** How is per-sink retry tracked?

**Answer (locked):** Retry is tracked **per-sink, per-batch**, not per individual point.

**Flow:**
1. Route worker reads next batch of uncommitted points for sink S (sequence > `sink_cursors[S].committed_sequence`)
2. Calls `S.PublishAsync(batch)`
3. On success: advance `committed_sequence`, reset retry state
4. On failure:
   - Increment retry counter in memory (not persisted — restart resets)
   - Compute backoff per `DeliveryPolicy.RetryBackoff` (exponential with cap)
   - Schedule next attempt
   - After `MaxRetries` exhausted: mark sink as `Degraded`, emit diagnostic event, continue with stored batch (buffer retains data for eventual recovery)
5. On permanent sink failure (e.g., config error): mark sink as `Failed`, stop retry loop, require manual intervention

**Retry state is in-memory only.** A gateway restart resumes from `committed_sequence` with fresh retry counters. The buffer itself is durable; the retry schedule is not.

**Per-point retry is not supported** — if a sink rejects a batch partially (some points accepted, others rejected), the sink adapter must handle that internally and report per-point results via `PublishResult.AcceptedCount / RejectedCount`. The route worker only sees batch-level success/failure for its cursor advancement.

### 19.5 Live vs Replay Ordering

**Question:** When replaying from buffer after a sink recovers, do live messages wait, merge, or bypass?

**Answer (locked):** **Replay is sequential and live messages wait.** The route maintains strict sequence order per sink.

Rationale: Industrial consumers (SCADA, historians, MES) depend on correct ordering. A burst of recent data arriving before older buffered data would produce misleading timelines and break downstream consumers that assume monotonic timestamps.

**Behavior on sink recovery:**
1. Sink reconnects, state transitions `Degraded → Connected`
2. Route worker detects recovery
3. Worker begins draining buffer for this sink starting at `committed_sequence + 1`
4. Drain runs at full throughput until `committed_sequence` reaches the current write head
5. During drain, newly arriving live points **enqueue to the buffer normally** — they do not bypass
6. Once drain catches up to the write head, the route enters **hot path**: new points are published directly AND written to buffer in the same transaction (for audit and in case of re-failure)
7. If the buffer is configured with `BufferMode.None` for the route, the hot path skips buffer writes entirely

**Implication:** During long outages the buffer fills. If buffer retention is exceeded, oldest points are dropped per `DropPolicy` — but the remaining points are always delivered in order.

**Fresh-data priority mode (future, not v1):** A per-route flag could allow "live bypass" mode where recent points publish immediately and historical drain runs in parallel. This sacrifices ordering for freshness and is useful for dashboards but dangerous for historians. **Not in v1.** Customers who need it can run two parallel routes with different buffer policies.

### 19.6 Ordering Guarantees

The runtime provides the following ordering guarantees:

| Guarantee | Provided? | Scope |
|-----------|-----------|-------|
| Per-point order within a single source | **Yes** | Source acquisition preserves device order |
| Per-point order within a single route-sink pair | **Yes** | Sequence number preserves order through buffer and replay |
| Per-point order across multiple sinks in a fanout route | **No** | Sinks progress independently |
| Per-point order across multiple routes | **No** | Routes are independent workers |
| Global order across the entire gateway | **No** | Not meaningful — not attempted |

**Sequence numbers** are monotonic per source and carried in `CanonicalDataPoint.SequenceNumber`. Downstream consumers can rely on per-source ordering but not cross-source ordering.

### 19.7 Delivery Guarantees

`DeliveryPolicy.Mode` defines the semantic:

| Mode | Behavior |
|------|----------|
| **AtMostOnce** | Publish is attempted once. On failure, the point is dropped and counted. No retry, no buffer. Use for high-rate non-critical telemetry (e.g., UI dashboards). |
| **AtLeastOnce** | Publish is retried until successful or buffer exhausted. Duplicates possible on retry. Default for all production routes. |
| **ExactlyOnce** | Not supported in v1. Would require idempotency keys and sink-side deduplication, which most sinks don't support. Revisit in v2. |

`AtLeastOnce` is the only mode compatible with store-and-forward.

**Acknowledgment boundary (amended 2026-07-13, ADR-0036).** `AtLeastOnce` guarantees
depend on how far the destination protocol can acknowledge a publish. A sink exposes
a `DeliveryAcknowledgementBoundary` of `None | LocalTransport | Broker | Application`,
and a route declares a **`RequiredAcknowledgementBoundary`** (a protocol-neutral field
added to `DeliveryPolicy`; the `AtMostOnce | AtLeastOnce` enum is unchanged).
**Route validation rejects when `route.RequiredAcknowledgementBoundary >
sink.AcknowledgementBoundary`** — a required boundary the sink cannot meet — not the
`AtLeastOnce`+protocol pairing generally. A `LocalTransport` destination — e.g.
**Sparkplug B v1, QoS 0** (`Mode = AtLeastOnce, Required = LocalTransport`) — supports
durable store-and-forward and retry of observable failures but cannot satisfy a
`Broker`/`Application` requirement. Store-and-forward (§19 / locked #8) is unaffected:
no data is dropped to enforce this. UIs present the boundary qualifier, never a bare
"AtLeastOnce".

### 19.8 Backpressure

When a sink cannot keep up with the source's acquisition rate, backpressure must propagate **without blocking the source**.

**Locked behavior:**
1. Route buffer has a bounded in-memory channel upstream of the SQLite store
2. When in-memory channel is full, new points **spill directly to SQLite** (bypass channel)
3. When SQLite buffer reaches `MaxDepth` or `MaxSize`, `DropPolicy` takes effect:
   - `DropOldest`: evict oldest points, accept new (default — preserves freshness)
   - `DropNewest`: reject new points, preserve backlog (rarely used)
   - `Block`: halt source acquisition until buffer drains (use only when data loss is worse than source stalling)
4. Drop events emit diagnostic metrics and logs with the point count dropped

**Sources are never blocked by sinks directly.** The buffer absorbs mismatch. If the buffer cannot absorb, the drop policy decides. The source polling loop runs on its own schedule regardless of sink state.

### 19.9 Route Lifecycle States

| State | Meaning |
|-------|---------|
| `Configured` | Route exists in config but has not started |
| `Starting` | Route is initializing (validating config, opening buffer, connecting) |
| `Running` | Route is processing data on the hot path |
| `Draining` | Sink(s) recovered, route is draining buffered backlog |
| `Degraded` | At least one sink is failing; buffer is growing; data preserved |
| `Stopping` | Route is shutting down (graceful flush in progress) |
| `Stopped` | Route has stopped; buffer is retained |
| `Failed` | Route cannot run due to config error or unrecoverable failure |
| `Blocked` | Route references unlicensed or disabled module |

Transitions are logged and exposed via diagnostics API. The UI shows per-route state.

---

## Appendix A: Decisions Summary

Decisions are labeled by status:
- **LOCKED** — Architectural commitments. Changing these would require rewriting the blueprint and is not permitted during Phase 1-5 implementation.
- **FLEXIBLE** — Implementation choices that can change during Phase 1 without blueprint revision.
- **OPEN** — Unresolved questions to answer before or during Phase 1.

### Architecturally Locked Decisions

| Decision | Choice | Status |
|----------|--------|--------|
| Architecture style | Protocol-agnostic platform with canonical data model | **LOCKED** |
| Canonical data model | Single normalized `CanonicalDataPoint` record for all flows | **LOCKED** |
| Route-first design | Routes are the primary product primitive, not a config footnote | **LOCKED** |
| Source → Pipeline → Sink flow | One source can fan out to many sinks via named routes | **LOCKED** |
| Module delivery | Compile-time assemblies, per-edition installers | **LOCKED** |
| Module activation | License-gated at DI registration | **LOCKED** |
| Licensing enforcement | 3 layers: packaging, runtime, UI/API | **LOCKED** |
| License signature | RSA-signed JSON | **LOCKED** |
| License mode | Fully offline, no phone-home | **LOCKED** |
| License expiration behavior | Continue data flow, block config changes | **LOCKED** |
| Store-and-forward | Mandatory, SQLite-backed, per-route | **LOCKED** |
| Buffer granularity | Per-route storage, per-sink cursors | **LOCKED** |
| Fanout semantics | Independent per sink, not transactional | **LOCKED** |
| Replay ordering | Sequential per sink; live messages wait for drain | **LOCKED** |
| Delivery modes | AtMostOnce and AtLeastOnce; ExactlyOnce not in v1. Broker-acked AtLeastOnce requires a Broker/Application ack boundary; local-transport-only destinations (e.g. Sparkplug B v1 QoS 0) get durable S&F but not broker-acked AtLeastOnce (§19.7, ADR-0036) | **LOCKED** |
| Per-adapter isolation | One failing adapter never affects others | **LOCKED** |
| Sink capabilities | Push and Pull modes (OPC UA Server forward-compatible) | **LOCKED** |
| Diagnostics | 3-way: source, pipeline, sink | **LOCKED** |
| Identity | Per-gateway UUID + customer/site binding | **LOCKED** |
| Config format | JSON files with draft/apply/rollback versioning | **LOCKED** |
| First customer protocols | FOCAS2, MT-LINKi, MTConnect, BrotherHttp, Modbus TCP, MQTT, HTTP | **LOCKED** |
| AI in data path | Never — decision-support only | **LOCKED** |
| AI tool-use pattern | Structured tool calls to management API, never free-text code generation | **LOCKED** |
| AI state changes | Always proposed, never autonomous; user confirmation required | **LOCKED** |
| AI audit logging | Mandatory, append-only with hash chain | **LOCKED** |
| AI local-LLM support | Mandatory from day one; no cloud-only features | **LOCKED** |
| Doc Copilot edition | Free on all editions including Starter | **LOCKED** |
| EdgeConnect Copilot edition | Professional and Enterprise only | **LOCKED** |
| Gateway sizing tiers | Small / Medium / Large with defined envelopes | **LOCKED** |

### Implementation-Flexible Decisions

These may change during Phase 1 implementation without blueprint revision, as long as the locked decisions above are respected.

| Decision | Current Choice | Status |
|----------|----------------|--------|
| Target framework | .NET 8 (re-evaluate annually) | **FLEXIBLE** |
| Host | Windows service (Linux later) | **FLEXIBLE** |
| Management API tech | Local REST via Kestrel | **FLEXIBLE** |
| Config hot-reload scope | Where protocol supports it | **FLEXIBLE** |
| AI agent count (v1) | 6 agents | **FLEXIBLE** |
| AI module | Single `ElpisEdgeConnect.AI` assembly | **FLEXIBLE** |
| AI provider default | Local LLM (Ollama) | **FLEXIBLE** |
| AI phase placement | Protocol Onboarding in Phase 2-3; Doc Copilot in Phase 4; interactive agents in Phase 4.5 | **FLEXIBLE** |
| Backpressure spill-to-disk | In-memory channel first, SQLite on overflow | **FLEXIBLE** |
| Sequence number scope | Per-source monotonic | **FLEXIBLE** |

### Open Questions

Must be resolved before or during Phase 1 kickoff.

| Question | Owner | Needed by |
|----------|-------|-----------|
| Admin UI framework — Blazor Server / Blazor WASM / React + REST | Product + Eng | Phase 4 start |
| License signing key custody — HSM, vault, or dev machine | Security + Ops | Phase 1 mid |
| Admin UI auth model — local accounts, Windows auth, SSO, combination | Product + Security | Phase 4 start |
| SQLite library choice — `Microsoft.Data.Sqlite` vs `LiteDB` vs `SQLitePCL` | Eng | Phase 1 mid |
| Metrics export — Prometheus only, OpenTelemetry, or both | Eng | Phase 1 end |
| Remote management protocol (Phase 5) — MQTT control topics, gRPC, or REST polling | Eng + EREMOS team | Phase 5 start |
| Local vector store for Doc Copilot RAG — `sqlite-vec`, LiteDB vector, custom | Eng | Phase 4 start |
| Local embedding model — `nomic-embed-text`, `all-minilm`, other | Eng | Phase 4 start |
| Local LLM default — Llama 3 8B, Phi-3 Mini, Qwen, other | Eng + Product | Phase 4.5 start |
| JSON schema validation library | Eng | Phase 1 mid |
