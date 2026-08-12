# Source Adapter Contract

**Interface:** `ElpisEdgeConnect.Core.Adapters.ISourceAdapter`
**Status:** LOCKED (per `ARCHITECTURE_BLUEPRINT.md` §4.2)
**Milestone:** A2

A source adapter is a component that reads data from an industrial device or system and converts it into `CanonicalDataPoint` instances. Every protocol module that brings data into the gateway implements `ISourceAdapter`: FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, custom drivers, and every future protocol.

This document is the reference for adapter authors — both the core team writing in-house protocol modules and partners writing custom adapters. Read it end-to-end before starting a new adapter. Follow it exactly.

---

## 1. The contract

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

    Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct);

    IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(CancellationToken ct);

    Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct);

    Task<ValidationResult> ValidateConfigAsync(
        SourceConfiguration config, CancellationToken ct);
}
```

> **Note on parameter names and ordering:** The interface uses `ct` (not
> `cancellationToken`) as the parameter name because it is part of the
> public contract locked in blueprint §4.2, and C# treats parameter names
> as part of the API surface for callers using named arguments. Method
> order also matches the blueprint: lifecycle first, then data methods,
> then `ValidateConfigAsync` last. Do not rename or reorder without a
> blueprint revision.

Every method on this interface is a contract obligation. Skipping, weakening, or silently failing any of them is a bug.

---

## 2. Responsibilities (what every adapter MUST do)

1. **Be isolated.** An exception in one adapter must never propagate to any other adapter, to the routing engine, or to the host. Catch everything at the adapter boundary and turn it into an `AdapterException` with a well-formed error code.

2. **Use the canonical data model.** Emit data only as `CanonicalDataPoint`. Construct points via `CanonicalDataPointFactory` so gateway identity and monotonic sequence numbers are correct. Never emit protocol-specific types.

3. **Wrap all outbound errors.** Do not let raw `IOException`, `SocketException`, `HttpRequestException`, or protocol-specific exception types cross the adapter boundary. Every outbound error must be an `AdapterException` (or subclass) carrying a structured `AdapterError` with a stable code.

4. **Honor cancellation.** Every async method accepts a `CancellationToken`. Pass it to every inner async call. Abort work promptly when cancellation is requested. Do not swallow `OperationCanceledException` silently — either propagate it or transition to `Stopping` state cleanly.

5. **Respect declared capabilities.** If your adapter declares `SourceCapabilities.Polling`, `PollAsync` must work. If it does not declare `Subscription`, `SubscribeAsync` must throw `NotSupportedException`. Do not pretend to support capabilities you don't.

6. **Follow the state machine.** Transitions between `AdapterState` values must respect the table in `AdapterStateTransitions`. The runtime checks this; do not try to work around it.

7. **Be thread-safe for the lifecycle methods that matter.** `State` and `CheckHealthAsync` are called concurrently with `PollAsync` or subscription streaming. Guard shared state appropriately.

8. **Initialize lazily enough to not block DI registration.** `InitializeAsync` may block; a constructor may not. Do not open sockets, touch files, or call DLLs from a constructor.

9. **Be idempotent where natural.** `StopAsync` on an already-stopped adapter should succeed. `StartAsync` after `StopAsync` should produce a clean restart. `InitializeAsync` called twice should either succeed or throw with a clear `CORE.ADAPTER_ALREADY_INITIALIZED` error.

10. **Emit UTC timestamps.** `DeviceTimestamp` and `GatewayTimestamp` on every emitted point must be UTC. Convert from device-local time if necessary.

---

## 3. Properties

### `InstanceId`

The stable identifier for this source connector instance, e.g., `"focas-jyoti17"`. Must match the `InstanceId` field on the `SourceConfiguration` that was passed to `InitializeAsync`. Used everywhere in diagnostics and logging.

### `ProtocolName`

The protocol module name, lowercase, e.g., `"focas2"`, `"mtlinki"`, `"modbus"`, `"mtconnect"`, `"brotherhttp"`. Must match the `ProtocolName` field on the `SourceConfiguration` and the `AdapterManifest` of the protocol module.

### `Capabilities`

A `SourceCapabilities` flag set declaring which optional operations this adapter supports:

| Capability | Meaning | Required methods |
|------------|---------|------------------|
| `Polling` | Adapter supports periodic polling | `PollAsync` must work |
| `Subscription` | Adapter supports event-driven streaming | `SubscribeAsync` must work |
| `Browse` | Adapter can enumerate available tags on the device | `BrowseTagsAsync` must work |
| `WriteBack` | Adapter can write values back to the device (not in v1) | future |
| `TestConnect` | Adapter can verify connectivity without starting | `ValidateConfigAsync` performs a live test |

Most first-party protocol modules declare `Polling | TestConnect`. OPC UA Client declares `Polling | Subscription | Browse | TestConnect`. Modbus declares `Polling | Browse | TestConnect`. Declare only the capabilities your adapter actually implements.

### `State`

The current `AdapterState` lifecycle state. Must only transition through states allowed by `AdapterStateTransitions`. Reflect state changes immediately and atomically — a health check called during a transition should see either the old or the new state, never a corrupt intermediate.

Valid states: `Created` → `Initializing` → `Initialized` → `Starting` → `Running` → (`Degraded` ↔ `Running`) → `Stopping` → `Stopped`. Failure can transition most states to `Failed`. License gating can place the adapter in `Blocked`.

---

## 4. Methods

### `ValidateConfigAsync`

Validates a `SourceConfiguration` without starting the adapter or touching the device. Called by the configuration manager during draft validation (blueprint §8.2) and by the Configuration Copilot when guiding a user through setup (blueprint §17.2 Agent 2).

Checks to perform:

- **Schema validation:** every required field is present and well-typed.
- **Semantic validation:** IP addresses are valid, port numbers are in range, poll intervals are reasonable, certificate paths resolve.
- **License validation:** the adapter's module is licensed, instance limits are not exceeded.
- **Optional live test:** if `Capabilities` includes `TestConnect`, attempt a lightweight connection (but do not start polling).

Return:
- `ValidationResult.Success()` if everything passes.
- `ValidationResult.SuccessWithWarnings(...)` if the config is acceptable but has warnings (e.g., "PollIntervalMs is lower than recommended for this protocol").
- `ValidationResult.Failure(code, message, path)` for any fatal issue.

This method must not throw except for truly catastrophic errors (out-of-memory, etc.). All expected failures go through `ValidationResult`.

### `InitializeAsync`

Applies a validated configuration to the adapter. Moves state from `Created` → `Initializing` → `Initialized` (or `Failed`).

Responsibilities:

- Store the configuration internally.
- Construct any long-lived helpers (HTTP clients, native DLL handles, certificate stores).
- Construct the `CanonicalDataPointFactory` for this instance.
- Do NOT open sockets to the device. That is `StartAsync`'s job. `InitializeAsync` should be safe to call while the device is offline.

May throw `AdapterException` with a `Configuration`, `License`, or `Internal` category on failure. The runtime will transition state to `Failed` and log the error.

### `StartAsync`

Begins data acquisition. Moves state from `Initialized` → `Starting` → `Running` (or `Failed`).

Responsibilities:

- Open connections to the device.
- Authenticate.
- Begin the polling loop (for polling adapters) or open the subscription (for subscription adapters).
- Start any background tasks (keep-alive, reconnection).

Must be fast enough to not block startup of other adapters. Target: under 5 seconds for healthy devices. If the device is slow or unreachable, transition to `Degraded` rather than blocking `StartAsync` indefinitely.

### `StopAsync`

Stops data acquisition cleanly. Moves state from `Running` or `Degraded` → `Stopping` → `Stopped` (or `Failed`).

Responsibilities:

- Signal any polling or subscription loops to stop.
- Close device connections gracefully.
- Flush any in-memory buffers.
- Release native resources (DLL handles, certificates, file locks).
- Return once the adapter is fully stopped.

Target graceful shutdown: under 10 seconds. After 10 seconds the runtime may force-terminate the adapter.

Must be idempotent: calling `StopAsync` on an already-stopped adapter must succeed without error.

### `CheckHealthAsync`

Returns a point-in-time `AdapterHealth` snapshot without blocking running operations. Called by the diagnostics collector (C4, blueprint §9.1) and the Diagnostic Copilot (blueprint §17.2 Agent 1).

Responsibilities:

- Return the current `State`.
- Compute a `HealthLevel`: `Healthy` for nominal operation, `Degraded` for transient failures, `Unhealthy` for sustained failure.
- Include the most recent error (if any) as `LastError`.
- Include protocol-specific metrics in `Metrics`: handle count, latency p50/p95, error counters, last successful read timestamp, etc.
- Return quickly — under 50 ms target. This method is called frequently.

Must not throw. If the adapter cannot determine its own health, return `HealthLevel.Unknown` with a `Detail` explaining why.

### `PollAsync`

Polls the device once and returns the canonical points read. Only valid when `Capabilities` includes `Polling`. The routing engine calls `PollAsync` on a schedule defined by `SourceConfiguration.PollIntervalMs`.

Responsibilities:

- Read all configured tags in a single call.
- Construct `CanonicalDataPoint` instances via the factory.
- Tag points with UTC timestamps.
- Set `Quality = Good` for successful reads, `Bad` with a `QualityReason` for failures.
- Return the list of points, even if some are `Bad`. Empty list is acceptable if nothing is available.

Error handling:
- Transient failures (timeouts, single-tag read errors) should be represented as `Bad` quality points — not thrown.
- Connection failures that affect the whole poll may throw `AdapterException` with `Retryable = true`. The runtime will retry according to the adapter's circuit breaker policy.
- Configuration or license failures should not be thrown from `PollAsync`; they should have been caught in `ValidateConfigAsync`.

Performance: aim for acquisition latency under 200 ms p95 on healthy devices (blueprint §18.3).

### `SubscribeAsync`

Opens a subscription to the device and yields canonical points as they arrive. Only valid when `Capabilities` includes `Subscription`. Typically used for OPC UA Client, event-driven devices, and future protocols with push semantics.

Responsibilities:

- Open the subscription and keep it alive.
- Yield `CanonicalDataPoint` instances as events arrive.
- Honor cancellation: when `ct` is signaled, close the subscription and complete the enumerable.
- Mark the implementation with `[EnumeratorCancellation]` on the cancellation parameter for proper cancellation flow.

A subscription-mode adapter should not also be polled by the routing engine; it uses one mode or the other. If your adapter supports both, pick the mode at configuration time and reject calls to the inactive method with `InvalidOperationException`.

### `BrowseTagsAsync`

Enumerates the tags available on the device. Only valid when `Capabilities` includes `Browse`. Used by:

- The Configuration Copilot (blueprint §17.2 Agent 2) to offer users the list of available tags during setup.
- The Tag Mapping Assistant (blueprint §17.2 Agent 3) to propose canonical mappings.
- The admin UI tag browser (blueprint §14 Phase 4).

Return a list of `TagDefinition` records with name, path, value type, unit, description, and any protocol-specific metadata. Return an empty list if the device supports browse but has no tags; throw `AdapterException` with `NotSupported` if the protocol doesn't support browse at all (better yet: don't declare the `Browse` capability in the first place).

---

## 5. Error taxonomy

Every error thrown by an adapter must be an `AdapterException` carrying an `AdapterError` with:

- A stable `Code` in the form `{PROTOCOL}.{CATEGORY_SUBCATEGORY}` (e.g., `FOCAS2.HANDLE_EXHAUSTED`, `MODBUS.TIMEOUT`).
- An appropriate `Category` from `ErrorCategory`.
- A human-readable `Message` that does not include credentials or PII.
- A correct `Retryable` flag.
- Optionally: `SuggestedBackoff` and `Context`.

### Error code naming convention (locked)

```
{PROTOCOL}.{CATEGORY_SUBCATEGORY}
```

- `PROTOCOL` is the uppercase protocol name: `FOCAS2`, `MTLINKI`, `MODBUS`, `MTCONNECT`, `BROTHERHTTP`, `OPCUA`.
- `CATEGORY_SUBCATEGORY` is in SCREAMING_SNAKE_CASE.

### Categories and retryable flag

| Category | When to use | Retryable |
|----------|-------------|-----------|
| `Configuration` | Bad config — the user must fix it (missing field, invalid enum value, unresolvable path) | `false` |
| `Authentication` | Bad credentials or missing auth | `false` (until config changes) |
| `Network` | Transient network issue (timeout, connection reset, DNS failure) | `true` |
| `Protocol` | Protocol-level error (bad framing, unexpected response, checksum mismatch) | `true` with caution |
| `DeviceState` | Device in wrong state (powered off, in alarm, in maintenance) | `true` after device recovers |
| `ResourceExhausted` | Too many handles, rate-limited, slot exhaustion (common on FOCAS2) | `true` with backoff |
| `License` | Blocked by licensing | `false` |
| `Internal` | Bug in the adapter or runtime | `false` |

### Example error codes

```csharp
// Static catalog in your protocol module
public static class Focas2Errors
{
    public const string HandleExhausted = "FOCAS2.HANDLE_EXHAUSTED";
    public const string ConnectionFailed = "FOCAS2.CONNECTION_FAILED";
    public const string ReadTimeout = "FOCAS2.READ_TIMEOUT";
    public const string InvalidHandle = "FOCAS2.INVALID_HANDLE";
    public const string DllNotFound = "FOCAS2.DLL_NOT_FOUND";
    public const string ParameterOutOfRange = "FOCAS2.PARAMETER_OUT_OF_RANGE";
}
```

Throw site:

```csharp
throw new AdapterException(new AdapterError
{
    Code = Focas2Errors.HandleExhausted,
    Category = ErrorCategory.ResourceExhausted,
    Message = "No free FOCAS2 handles available on controller",
    Retryable = true,
    SuggestedBackoff = TimeSpan.FromSeconds(5),
    Context = $"Controller {_config.IpAddress}:{_config.Port}",
});
```

Or use the convenience factories on `AdapterException`:

```csharp
throw AdapterException.Network(
    code: "MODBUS.TIMEOUT",
    message: "Read timeout after 3 seconds",
    inner: socketException);
```

### Rules

- **Never throw raw `Exception`** past the adapter boundary. Wrap it.
- **Never include credentials, tokens, or PII** in error messages.
- **Define error codes in a static catalog** in your protocol module (e.g., `Focas2Errors.cs`), not as string literals at throw sites.
- **Document every error code** in the module's documentation alongside its retry guidance.

---

## 6. Lifecycle and state machine

The `AdapterState` enum has 10 values. The runtime enforces legal transitions via `AdapterStateTransitions`. Do not try to transition through illegal states.

```
   Created ──► Initializing ──► Initialized ──► Starting ──► Running
                    │                │                         │ ▲
                    │                │                         ▼ │
                    │                │                       Degraded
                    │                │                         │
                    ▼                ▼                         ▼
                 Failed          Stopped                   Stopping
                    ▲                ▲                         │
                    │                │                         ▼
                    └────────────────┴──────────────────── Stopped
```

- **`Created`** — the adapter has been instantiated but not initialized. May transition to `Initializing`, `Blocked`, or `Failed`.
- **`Initializing`** — `InitializeAsync` is running. May transition to `Initialized` or `Failed`.
- **`Initialized`** — ready to start, not yet running. May transition to `Starting`, `Stopped`, or `Failed`.
- **`Starting`** — `StartAsync` is running. May transition to `Running` or `Failed`.
- **`Running`** — normal operation. May transition to `Degraded`, `Stopping`, or `Failed`.
- **`Degraded`** — running but experiencing transient failures. May transition back to `Running` on recovery, to `Stopping`, or to `Failed`.
- **`Stopping`** — `StopAsync` is running. May transition to `Stopped` or `Failed`.
- **`Stopped`** — cleanly stopped; can be restarted. May transition to `Starting` or `Initializing`.
- **`Failed`** — unrecoverable failure. May transition to `Initializing` (recovery attempt) or `Stopped`.
- **`Blocked`** — license or policy blocks activation. May transition to `Initializing` (license reinstated) or `Stopped`.

See `tests/ElpisEdgeConnect.Core.Tests/Adapters/AdapterStateTransitionsTests.cs` for the exhaustive transition table and tests.

---

## 7. Isolation and failure handling

One failing adapter must never affect any other adapter. The runtime provides the outer circuit breaker (per blueprint §11 "Per-adapter isolation"), but the adapter itself must hold up its end:

- **Catch every exception at the `PollAsync` / `SubscribeAsync` boundary.** Turn it into `Bad`-quality points or a retryable `AdapterException`. Never let a native exception from a DLL crash the runtime.
- **Use a per-adapter `CancellationTokenSource`.** Do not share cancellation with other adapters or the host.
- **Own your threads.** If your adapter spawns background tasks, track them and cancel them in `StopAsync`.
- **Bound your resource usage.** Do not cache unbounded state. Do not allocate unbounded lists during a single poll.
- **Handle `OperationCanceledException` gracefully.** It is not an error — it is a signal to stop. Do not log it as an error, do not emit a `Bad` point.

---

## 8. Configuration

Every source adapter defines its own configuration type derived from `SourceConfiguration`:

```csharp
public sealed record Focas2SourceConfiguration : SourceConfiguration
{
    public required string IpAddress { get; init; }
    public int Port { get; init; } = 8193;
    public int TimeoutSeconds { get; init; } = 10;
    public string? DllPath { get; init; }
    public bool KeepAlive { get; init; }
    public int MaxConsecutiveErrors { get; init; } = 10;
    public IReadOnlyList<string> DataPoints { get; init; } = [];
}
```

Conventions:

- **Inherit from `SourceConfiguration`.** Do not define a parallel config type.
- **Use `required` init properties for mandatory fields.** The compiler enforces that the config cannot be constructed without them.
- **Provide sensible defaults for optional fields.** Adapter authors are the experts on reasonable defaults.
- **Ship a JSON Schema** alongside the config type in `docs/config-schemas/{protocol}.schema.json`. The schema is consumed by the validation manager and by the Configuration Copilot for UI generation.
- **Validate structurally in `ValidateConfigAsync`.** Type-level enforcement catches missing fields; runtime validation catches semantic issues.

---

## 9. Minimal example adapter

This is the skeleton the Protocol Onboarding Assistant (blueprint §17.2 Agent 4) will generate when scaffolding a new adapter. Protocol-specific logic is marked `TODO(human)`.

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sources.Example;

public sealed class ExampleSourceAdapter : ISourceAdapter
{
    private ExampleSourceConfiguration? _config;
    private CanonicalDataPointFactory? _factory;
    private AdapterState _state = AdapterState.Created;

    public string InstanceId => _config?.InstanceId
        ?? throw new InvalidOperationException("Adapter not initialized");

    public string ProtocolName => "example";

    public SourceCapabilities Capabilities =>
        SourceCapabilities.Polling | SourceCapabilities.TestConnect;

    public AdapterState State => _state;

    public Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
    {
        if (_state != AdapterState.Created && _state != AdapterState.Stopped
            && _state != AdapterState.Failed)
        {
            throw AdapterException.Configuration(
                "EXAMPLE.ALREADY_INITIALIZED",
                $"Cannot initialize adapter in {_state} state");
        }

        _state = AdapterState.Initializing;

        _config = (ExampleSourceConfiguration)config;
        _factory = new CanonicalDataPointFactory(
            gatewayId: "TODO: resolve from gateway identity",
            sourceInstanceId: _config.InstanceId,
            protocolName: ProtocolName,
            deviceId: _config.DeviceId,
            deviceName: _config.DeviceName);

        // TODO(human): construct long-lived helpers (HTTP client, DLL handle, etc.)
        // Do NOT open sockets here — that is StartAsync's job.

        _state = AdapterState.Initialized;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _state = AdapterState.Starting;

        // TODO(human): open connection to device, authenticate, prepare for polling

        _state = AdapterState.Running;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        if (_state == AdapterState.Stopped)
        {
            return Task.CompletedTask;
        }

        _state = AdapterState.Stopping;

        // TODO(human): close connection, release resources, cancel background tasks

        _state = AdapterState.Stopped;
        return Task.CompletedTask;
    }

    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
    {
        return Task.FromResult(new AdapterHealth
        {
            State = _state,
            Level = _state == AdapterState.Running
                ? HealthLevel.Healthy
                : HealthLevel.Unknown,
            CheckedAt = DateTime.UtcNow,
            // TODO(human): populate LastSuccessAt, LastError, Metrics
        });
    }

    public async Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct)
    {
        if (_state != AdapterState.Running && _state != AdapterState.Degraded)
        {
            throw AdapterException.Configuration(
                "EXAMPLE.NOT_RUNNING",
                $"Cannot poll in {_state} state");
        }

        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        try
        {
            // TODO(human): read tags from device
            // For each tag:
            //   points.Add(_factory!.CreatePoint(
            //       tagName: mappedName,
            //       tagPath: path,
            //       value: deviceValue,
            //       valueType: CanonicalValueType.Double,
            //       quality: DataQuality.Good,
            //       deviceTimestamp: deviceTime ?? now,
            //       gatewayTimestamp: now,
            //       unit: "rpm"));
            await Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw AdapterException.Network(
                "EXAMPLE.POLL_FAILED",
                $"Failed to poll {_config!.DeviceId}",
                ex);
        }

        return points;
    }

    public async IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Not supported for this adapter
        throw new NotSupportedException(
            "ExampleSourceAdapter does not declare the Subscription capability");

        #pragma warning disable CS0162 // unreachable
        yield break;
        #pragma warning restore CS0162
    }

    public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
    {
        throw new NotSupportedException(
            "ExampleSourceAdapter does not declare the Browse capability");
    }

    public Task<ValidationResult> ValidateConfigAsync(
        SourceConfiguration config, CancellationToken ct)
    {
        if (config is not ExampleSourceConfiguration cfg)
        {
            return Task.FromResult(ValidationResult.Failure(
                "EXAMPLE.CONFIG_WRONG_TYPE",
                $"Expected ExampleSourceConfiguration but got {config.GetType().Name}"));
        }

        // TODO(human): validate protocol-specific fields
        // - IP address format
        // - Port range
        // - Required fields populated
        // - Optional live connectivity test if TestConnect is supported

        return Task.FromResult(ValidationResult.Success());
    }

    public ValueTask DisposeAsync()
    {
        // TODO(human): release any remaining resources
        return ValueTask.CompletedTask;
    }
}
```

---

## 10. Testing guidance

Every adapter must include:

1. **Unit tests for `ValidateConfigAsync`** covering missing fields, invalid values, and successful validation.
2. **Unit tests for the state machine** verifying transitions match `AdapterStateTransitions`.
3. **Unit tests for `CheckHealthAsync`** in each lifecycle state.
4. **Integration tests against a simulator or mock device** exercising `PollAsync` or `SubscribeAsync` end-to-end.
5. **A consistency check** verifying every emitted point passes `CanonicalDataPoint.IsConsistent()`.
6. **Error path tests** verifying every declared error code can be produced under its triggering condition.
7. **Cancellation tests** verifying that cancelling `PollAsync` or `SubscribeAsync` mid-operation aborts cleanly.
8. **Idempotency tests** verifying `StopAsync → StartAsync → StopAsync` cycles work.

Reference: `tests/ElpisEdgeConnect.Core.Tests/Adapters/AdapterStateTransitionsTests.cs` (state machine coverage pattern).

---

## 11. Adapter SDK deliverables checklist

Every protocol module must ship with:

- [ ] Adapter manifest (`manifest.json`) per blueprint §13.1
- [ ] Configuration model derived from `SourceConfiguration`
- [ ] JSON Schema for the configuration
- [ ] `ISourceAdapter` implementation
- [ ] Static error code catalog (`{Protocol}Errors.cs`)
- [ ] Unit tests for config validation
- [ ] Unit tests for state machine
- [ ] Integration tests with mock/simulator
- [ ] Documentation page in `docs/adapters/{protocol}.md` with:
  - Overview
  - Prerequisites
  - Configuration reference
  - Troubleshooting
  - Error code catalog
  - Limitations

---

## 12. Related reading

- `ARCHITECTURE_BLUEPRINT.md` §4.2 — the contract definition
- `ARCHITECTURE_BLUEPRINT.md` §11 — reliability requirements
- `ARCHITECTURE_BLUEPRINT.md` §13 — adapter SDK conventions
- `ARCHITECTURE_BLUEPRINT.md` §17.2 Agent 4 — Protocol Onboarding Assistant
- `docs/core/canonical-data-model.md` — the data contract
- `docs/adapter-sdk/sink-adapter-contract.md` — the complementary sink contract
