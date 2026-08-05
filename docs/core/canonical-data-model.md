# Canonical Data Model

**Namespace:** `ElpisEdgeConnect.Core.Model`
**Status:** LOCKED (per `ARCHITECTURE_BLUEPRINT.md` §4.1)
**Milestone:** A1

The canonical data model is the single normalized representation of a data point as it flows through the Elpis EdgeConnect pipeline. Every source adapter produces `CanonicalDataPoint` instances; every transform operates on them; every sink adapter consumes them. No protocol-specific payload format ever crosses the boundary between adapters and the routing engine.

This is the most important type in the entire platform. Get it right once; change it almost never.

---

## Why a canonical model

Without a canonical model you get `M × N` complexity: every source protocol (FOCAS2, MT-LINKi, Modbus, OPC UA, Siemens S7, custom drivers, …) would need a custom translation path into every sink protocol (MQTT, HTTP, TCP, OPC UA Server, database writers, …). Dozens of protocols on each side means hundreds of translation paths, each with its own bugs and its own maintenance cost.

With a canonical model you get `M + N` complexity: every source normalizes to `CanonicalDataPoint` once, every sink accepts `CanonicalDataPoint` once, and the pipeline in the middle is protocol-agnostic. Adding a new source only requires reading from that source and producing canonical points. Adding a new sink only requires consuming canonical points and writing them. Source adapters never know what sinks exist, and vice versa.

This is the foundation for every other architectural commitment in the blueprint. Sections 5 (transforms), 6 (store-and-forward), 9 (diagnostics), and 19 (route execution) all assume `CanonicalDataPoint` as the thing being transformed, buffered, routed, and delivered.

---

## The type

```csharp
public sealed record CanonicalDataPoint
{
    public required string GatewayId { get; init; }
    public required string SourceInstanceId { get; init; }
    public required string ProtocolName { get; init; }
    public required string DeviceId { get; init; }
    public string? DeviceName { get; init; }

    public required string TagName { get; init; }
    public required string TagPath { get; init; }
    public string? OriginalTagName { get; init; }

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
```

A `sealed record` with `required` init properties, designed to be:

- **Immutable.** Once constructed, fields cannot change. Multiple sinks can consume the same instance concurrently without copying or locking.
- **Thread-safe by construction.** No mutable state; no synchronization required.
- **Value-equal.** Two points with identical field values are equal (inherited from `record`), which simplifies testing and deduplication.
- **Minimally allocated.** The fast path (see `CanonicalDataPointFactory.CreatePoint`) constructs one record per point with no intermediate allocations.

---

## Field reference

### Identity fields

| Field | Type | Meaning |
|-------|------|---------|
| `GatewayId` | `string` (required) | Stable identifier of the gateway instance that produced this point. Matches the gateway's `data/identity.json`. |
| `SourceInstanceId` | `string` (required) | Identifier of the source connector instance that emitted this point (e.g., `"focas-jyoti17"`). Uniquely identifies the source within the gateway. |
| `ProtocolName` | `string` (required) | Protocol module name (e.g., `"focas2"`, `"mtlinki"`, `"modbus"`). Lowercase, matches the adapter's declared protocol. |
| `DeviceId` | `string` (required) | Identifier of the physical device behind the source (e.g., `"Jyoti17CNC"`). |
| `DeviceName` | `string?` | Human-readable device name. Optional. |

### Tag fields

| Field | Type | Meaning |
|-------|------|---------|
| `TagName` | `string` (required) | Canonical tag name after any tag-mapping transform. This is the name downstream transforms, sinks, and consumers should use. |
| `TagPath` | `string` (required) | Hierarchical path for the tag (e.g., `"Spindle/Speed"`). Used for browse, grouping, and topic generation. For flat namespaces this equals `TagName`. |
| `OriginalTagName` | `string?` | The source-native tag name before any mapping (e.g., Fanuc's `"Spindle/Speed"` or Brother's `"spindle_rpm"`). Preserved for diagnostics and traceability. |

### Value fields

| Field | Type | Meaning |
|-------|------|---------|
| `Value` | `object?` (required) | The value. Boxed. May be `null` when `ValueType` is `Null`. See the consistency rule below. |
| `ValueType` | `CanonicalValueType` (required) | The declared canonical type of `Value`. See the full enum below. |
| `Unit` | `string?` | Engineering unit string (e.g., `"rpm"`, `"mm"`, `"%"`). Free-form but consistent per tag. |

### Quality fields

| Field | Type | Meaning |
|-------|------|---------|
| `Quality` | `DataQuality` (required) | Quality classification. Follows OPC UA semantics. |
| `QualityReason` | `string?` | Free-text reason for a non-Good quality (e.g., `"read timeout"`). Empty or null for `Good`. |

### Timestamp fields

| Field | Type | Meaning |
|-------|------|---------|
| `DeviceTimestamp` | `DateTime` (required, UTC) | The timestamp the device reported for this value, if the protocol provides it. If the protocol doesn't, set equal to `GatewayTimestamp`. |
| `GatewayTimestamp` | `DateTime` (required, UTC) | The timestamp at which the gateway acquired or emitted this value. Always set by the adapter or factory. |

Both timestamps are always UTC. Do not store local time.

### Metadata and sequence

| Field | Type | Meaning |
|-------|------|---------|
| `Metadata` | `IReadOnlyDictionary<string, object>?` | Optional key/value annotations added by the source adapter, transforms, or enrichment steps (e.g., `site`, `line`, `shift`). |
| `SequenceNumber` | `long` | Monotonically increasing sequence number per source instance, assigned by `CanonicalDataPointFactory`. Drives buffer ordering and sink cursor advancement. Starts at 1; `0` is reserved as "no committed cursor". Not marked `required` per blueprint §4.1 — the factory sets it on every production construction path, and test fixtures may omit it when the sequence is not yet meaningful. |

---

## `CanonicalValueType` catalog

The canonical type system is intentionally small. Every industrial protocol value must be mappable to one of these after normalization. Adding a new type is an architectural change and requires blueprint revision.

| Type | Purpose | .NET backing type |
|------|---------|-------------------|
| `Null` | `Value` is null (typically paired with `Bad` or `Uncertain` quality) | — |
| `Boolean` | Boolean true/false | `bool` |
| `Integer` | 32-bit signed integer | `int` |
| `Long` | 64-bit signed integer | `long` |
| `Float` | 32-bit floating point | `float` |
| `Double` | 64-bit floating point | `double` |
| `String` | UTF-8 string | `string` |
| `DateTime` | UTC `DateTime` | `DateTime` |
| `ByteArray` | Raw binary payload | `byte[]` |
| `Array` | Homogeneous array of a primitive type | `Array` |
| `Object` | Structured object (rare; prefer flattening) | `IReadOnlyDictionary<string, object>` |

Protocol-specific types (Fanuc BCD, Modbus word, Siemens S5TIME, etc.) must be converted by the adapter to one of these before construction. This conversion is the adapter's responsibility; Core never sees the native types.

---

## `DataQuality` catalog

Follows OPC UA semantics so that OPC UA sinks can map directly.

| Quality | Meaning |
|---------|---------|
| `Unknown` | Default; quality not yet determined |
| `Good` | Value is known-good and fresh |
| `Uncertain` | Value is reported but accuracy cannot be confirmed |
| `Bad` | Value is known-bad (read failure, device in alarm, protocol error) |
| `Stale` | Value is valid but older than the staleness threshold |

Adapters producing `Bad` or `Uncertain` points should populate `QualityReason` with a short, structured reason string.

### Quality state machine (adapter responsibility)

The adapter is responsible for emitting points with the correct Quality
across the lifecycle of an upstream device read. The canonical state
machine each source adapter implements is:

```
                                ┌───────────────┐
                                │ device read   │
                                │ attempt       │
                                └───────┬───────┘
                                        │
              ┌─────────────────────────┼─────────────────────────┐
              │                         │                         │
       success │                  failure (per-tag /        timeout / source
              │                  per-block /                degraded
              ▼                  protocol error)                    │
   ┌────────────────────┐                │                         │
   │ adapter currently  │                ▼                         ▼
   │ in Degraded state? │      ┌──────────────────┐      ┌──────────────────┐
   └────────┬─────┬─────┘      │ emit Quality=Bad │      │ emit Quality=    │
            │ no  │ yes        │ with QualityReason│     │ Uncertain (only  │
            ▼     ▼            │ + ValueType=Null  │     │ for sources that │
       Good   Uncertain        │ + Value=null      │     │ can read but     │
                               │ + Unit preserved  │     │ can't verify)    │
                               └──────────────────┘      └──────────────────┘
```

Concrete adapter rules (locked):

1. **Successful read, adapter Running** → `Quality=Good`, `QualityReason=null`.
2. **Successful read, adapter Degraded** (e.g., recent outer-poll exceptions
   not yet recovered) → `Quality=Uncertain`, with a `QualityReason` explaining
   the degraded source (e.g., `"adapter in Degraded state — recent transaction failures"`).
3. **Block / per-tag read failure** → adapter emits ONE point per affected tag
   with `Quality=Bad`, `Value=null`, `ValueType=Null` (per the
   `CanonicalValueType` catalog above — `Null` is "typically paired with `Bad`
   or `Uncertain` quality"). `QualityReason` carries the underlying error code
   or message (e.g., `"Illegal Data Address"`, `"timeout"`). `Unit` is preserved
   so downstream sinks keep their per-tag unit metadata even during outages.
4. **Adapter never emits stale Good points.** When the upstream is unreachable,
   the adapter SHOULD emit Bad points at the configured scan rate (downstream
   clients then see "this tag is currently unavailable" instead of consuming a
   timestamp-drifting last-good value).
5. **`Stale` is reserved for buffer / sink-side use.** Source adapters do not
   produce `Stale` — that's for replay paths where the buffer surfaces an old
   value whose age has exceeded a configured staleness threshold. (Not used
   by any current sink; reserved for future historian / EREMOS integration.)
6. **`Unknown` is the default-construction-only value.** Adapters should never
   ship `Unknown` to the routing engine — it's there to make `CanonicalDataPointBuilder`
   construct partially-built points without requiring callers to set Quality
   before every field. Tests that intentionally exercise `Unknown` should mark
   the path as test-only.

#### Adapter implementation cookbook

Pattern for a polling adapter with per-block reads (Modbus is the reference):

```csharp
// Inside the per-block poll handler:
if (!result.IsSuccess) {
    var ts = _time.GetUtcNow().UtcDateTime;
    var reason = result.Error?.Message ?? result.Error?.Code ?? "<no detail>";
    foreach (var tag in block.Tags) {
        emittedPoints.Add(_factory.CreatePoint(
            tagName: tag.Name,
            tagPath: tag.Path,
            value: null,
            valueType: CanonicalValueType.Null,
            quality: DataQuality.Bad,
            deviceTimestamp: ts,
            gatewayTimestamp: ts,
            unit: tag.Unit,
            qualityReason: reason));
    }
    return;  // skip the decode loop; failure was reported as Bad points
}

// In the success path:
var quality = State == AdapterState.Degraded
    ? DataQuality.Uncertain
    : DataQuality.Good;
emittedPoints.Add(_factory.CreatePoint(
    tagName: tag.Name,
    tagPath: tag.Path,
    value: decodedValue,
    valueType: decodedValueType,
    quality: quality,
    /* timestamps, unit, ... */));
```

Pattern for a subscription-based adapter (FOCAS2, MTConnect, future OPC UA Client):
the same rules apply, but per-tag failures typically surface as collector-level
exceptions that bubble to the outer PollAsync, which transitions the adapter
to Degraded via `RecordFailure`. Successful reads after that emit Uncertain
until the next clean cycle calls `RecordSuccess` and lifts the adapter back to Running.

---

## The Value/ValueType consistency rule

**Rule:** `Value` must be consistent with `ValueType`. Specifically:

| `ValueType` | Expected `Value` runtime type |
|-------------|-------------------------------|
| `Null` | `null` (and only `null`) |
| `Boolean` | `bool` |
| `Integer` | `int` |
| `Long` | `long` |
| `Float` | `float` |
| `Double` | `double` |
| `String` | `string` (may be empty; must not be `null` — use `Null` type for missing strings) |
| `DateTime` | `DateTime` (expected to be UTC; local time is a bug) |
| `ByteArray` | `byte[]` |
| `Array` | Any `Array` (element type discovered by reflection) |
| `Object` | `IReadOnlyDictionary<string, object>` |

**Enforcement policy (locked):**

> **The correspondence between `Value` and `ValueType` is the producing adapter's responsibility. The runtime does NOT validate it on the hot path.**

Rationale:
- The hot path runs at up to 25,000 points/sec on Large-tier gateways (per blueprint §18.2). A runtime type check on every construction would double allocation cost and introduce branch mispredictions at exactly the wrong point.
- The correspondence is an *adapter contract obligation*. A misbehaving adapter is the bug; the runtime is not the right layer to correct adapter bugs.
- `CanonicalDataPoint` is immutable, so a violation surfaces immediately the moment any downstream code attempts to cast `Value`. Bugs fail fast and visibly.

**What the runtime does provide:**

The `CanonicalDataPoint.IsConsistent()` instance method performs the check. It returns `true` if all of the following hold, `false` otherwise:

1. The runtime type of `Value` matches the declared `ValueType` per the table above.
2. When `ValueType` is `DateTime`, the `Value`'s `DateTimeKind` is `Utc`. Local and Unspecified kinds are rejected.
3. `DeviceTimestamp.Kind == DateTimeKind.Utc`.
4. `GatewayTimestamp.Kind == DateTimeKind.Utc`.

The related `TryValidateConsistency(out string? reason)` method returns the same boolean plus a human-readable reason string when inconsistent — the reason cites the specific check that failed.

These helpers are intended for:
- **Unit and integration tests** — adapter authors should include a consistency check in their test suite.
- **Debug builds** — an optional future runtime flag may enable automatic consistency checking in development environments.
- **Diagnostic agents** — the Diagnostic Copilot (blueprint §17) can use this helper to flag suspect points during investigation.

**What the runtime does NOT do:**

- Does not call `IsConsistent()` during normal pipeline execution.
- Does not throw on construction when a violation is present.
- Does not silently coerce mismatched values.

Adapter authors: write tests that call `IsConsistent()` on every tag your adapter produces. Consider adding a test helper that verifies this for a whole batch of points.

---

## Construction paths

Three ways to construct a `CanonicalDataPoint`, in increasing order of convenience and decreasing order of raw speed:

### 1. Direct record initialization

```csharp
var point = new CanonicalDataPoint
{
    GatewayId = "GW-MENON-001",
    SourceInstanceId = "focas-jyoti17",
    ProtocolName = "focas2",
    DeviceId = "Jyoti17CNC",
    DeviceName = "Jyoti 17 CNC",
    TagName = "spindle.speed",
    TagPath = "Spindle/Speed",
    OriginalTagName = "Spindle/Speed",
    Value = 3500.0,
    ValueType = CanonicalValueType.Double,
    Unit = "rpm",
    Quality = DataQuality.Good,
    QualityReason = null,
    DeviceTimestamp = now,
    GatewayTimestamp = now,
    Metadata = null,
    SequenceNumber = 1,
};
```

The fastest path. Every required field must be set or the compiler rejects the code (thanks to `required` init properties). Intended for tests that need precise control.

### 2. `CanonicalDataPointFactory.CreatePoint` (fast path)

```csharp
var factory = new CanonicalDataPointFactory(
    gatewayId: "GW-MENON-001",
    sourceInstanceId: "focas-jyoti17",
    protocolName: "focas2",
    deviceId: "Jyoti17CNC",
    deviceName: "Jyoti 17 CNC");

var point = factory.CreatePoint(
    tagName: "spindle.speed",
    tagPath: "Spindle/Speed",
    value: 3500.0,
    valueType: CanonicalValueType.Double,
    quality: DataQuality.Good,
    deviceTimestamp: now,
    gatewayTimestamp: now,
    unit: "rpm");
```

The **recommended path for production adapters**. The factory is cached once per source instance and reused for every point. It:

- Fills in gateway, source, protocol, and device identity automatically
- Allocates a monotonic sequence number atomically (thread-safe via `Interlocked.Increment`)
- Has no per-call heap allocations beyond the `CanonicalDataPoint` record itself

Use this path in hot loops.

### 3. Builder path

```csharp
var point = factory.NewBuilder()
    .WithTag("spindle.speed", "Spindle/Speed")
    .WithValue(3500.0, CanonicalValueType.Double)
    .WithUnit("rpm")
    .WithGoodQuality(now)
    .Build();
```

The most ergonomic path. Slightly slower than `CreatePoint` because each call allocates a builder instance. Recommended when:
- The adapter's logic is complex and readability matters more than allocation cost
- Partial construction is needed (e.g., fields computed conditionally)
- Writing tests

`Build()` throws `InvalidOperationException` if any required field is missing.

### Concurrency guarantees

- `CanonicalDataPoint` itself is **immutable and thread-safe**.
- `CanonicalDataPointFactory` is **thread-safe**. Multiple threads can call `NextSequence`, `CreatePoint`, or `NewBuilder` concurrently. Sequence numbers remain strictly monotonic. Verified by a 1,000,000-point stress test in `CanonicalDataPointFactoryTests.SequenceNumbers_MonotonicUnderConcurrentLoad`.
- `CanonicalDataPointBuilder` is **not thread-safe**. One builder per construction. Do not share builders between threads.

---

## Sequence numbers

Sequence numbers serve the buffer and cursor subsystems (blueprint §19.3, §19.6). Important properties:

1. **Monotonic per source instance.** Every call to `NextSequence()` on a given factory returns a strictly greater value than the previous call.
2. **Not unique across sources.** Two different sources running on the same gateway will both produce sequences starting at 1. Cross-source ordering is explicitly not guaranteed (blueprint §19.6).
3. **Not persisted across restarts.** A gateway restart resets the factory's counter to 0 and new points start at 1. The buffer subsystem maintains its own persistent sequence space separately.
4. **Start at 1.** The value `0` is reserved to mean "no committed cursor" in the buffer. A real point never has sequence 0.

Adapters must not manually allocate sequence numbers. Always go through the factory.

---

## Metadata conventions

`Metadata` is an optional key-value dictionary for annotations. Conventions:

- **Keys are lowercase with underscores:** `site`, `line`, `shift`, `operator_id`, `work_order`.
- **Values are JSON-serializable primitives:** strings, numbers, booleans. Avoid nested objects.
- **Metadata is attached by:** the source adapter (device metadata), transforms (enrichment steps), and the routing engine (route metadata).
- **Metadata is not used for control flow.** The routing engine does not branch on metadata values. Sinks do, but transforms generally don't.
- **Metadata should be small.** Dozens of keys is fine; hundreds is a smell.

---

## Examples

### A healthy spindle speed reading from FOCAS2

```csharp
var point = new CanonicalDataPoint
{
    GatewayId = "GW-MENON-001",
    SourceInstanceId = "focas-jyoti17",
    ProtocolName = "focas2",
    DeviceId = "Jyoti17CNC",
    DeviceName = "Jyoti 17 CNC",
    TagName = "spindle.speed",
    TagPath = "Spindle/Speed",
    OriginalTagName = "Spindle/Speed",
    Value = 3500.0,
    ValueType = CanonicalValueType.Double,
    Unit = "rpm",
    Quality = DataQuality.Good,
    DeviceTimestamp = DateTime.UtcNow,
    GatewayTimestamp = DateTime.UtcNow,
    Metadata = null,
    SequenceNumber = 42,
};
```

### A failed read due to timeout

```csharp
var point = factory.CreatePoint(
    tagName: "spindle.speed",
    tagPath: "Spindle/Speed",
    value: null,
    valueType: CanonicalValueType.Null,
    quality: DataQuality.Bad,
    deviceTimestamp: DateTime.UtcNow,
    gatewayTimestamp: DateTime.UtcNow,
    qualityReason: "FOCAS2.READ_TIMEOUT after 3 retries");
```

Note the pairing: `Value = null` + `ValueType = Null` + `Quality = Bad` + `QualityReason` populated. This is the canonical shape for failure points.

### An enriched point after transforms

```csharp
var enriched = point with
{
    TagName = "machine.spindle.rpm",     // tag mapping applied
    Metadata = new Dictionary<string, object>
    {
        ["site"] = "Menon",
        ["line"] = "Bay-1",
        ["shift"] = 2,
    },
};
```

The transform pipeline uses the record `with` expression to produce derived points without mutating the original. Multiple transforms chain naturally.

---

## Testing guidance

Every adapter implementation must include tests that verify:

1. **Every emitted point passes `IsConsistent()`.** Write a helper that calls it on every point the adapter produces during a test.
2. **Sequence numbers are monotonic.** Use the factory, not manual allocation.
3. **Failure paths produce `Bad` quality with a non-null `QualityReason`.** Avoid the trap of silently emitting `Good` quality with a garbage value.
4. **Timestamps are UTC.** Local time is a bug.
5. **Tag names match the adapter's mapping table.** If the adapter does tag mapping, verify `OriginalTagName` and `TagName` differ as expected.

Reference tests: `tests/ElpisEdgeConnect.Core.Tests/Model/CanonicalDataPointTests.cs`, `CanonicalDataPointBuilderTests.cs`, `CanonicalDataPointFactoryTests.cs`.

---

## Related reading

- `ARCHITECTURE_BLUEPRINT.md` §4.1 — the contract definition
- `ARCHITECTURE_BLUEPRINT.md` §19.6 — ordering guarantees
- `docs/adapter-sdk/source-adapter-contract.md` — how source adapters produce canonical points
- `docs/adapter-sdk/sink-adapter-contract.md` — how sink adapters consume canonical points

---

## Change control

The `CanonicalDataPoint` type, the `CanonicalValueType` enum, and the `DataQuality` enum are **LOCKED** per blueprint Appendix A. Changes require blueprint revision and must be reviewed against:

- Backward compatibility with all existing adapters
- Performance impact (hot path benchmarks in `tests/ElpisEdgeConnect.Benchmarks`)
- Storage impact (buffer serialization format in C2)
- Wire format impact (sink adapters that serialize to JSON, Protobuf, etc.)

Adding a new optional nullable field is acceptable. Renaming, removing, or changing semantics of existing fields is a breaking change.
