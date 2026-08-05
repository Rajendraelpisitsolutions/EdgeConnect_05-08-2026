# Sink Adapter Contract

**Interface:** `ElpisEdgeConnect.Core.Adapters.ISinkAdapter`
**Status:** LOCKED (per `ARCHITECTURE_BLUEPRINT.md` §4.3)
**Milestone:** A2

A sink adapter is a component that delivers `CanonicalDataPoint` data to an external destination. Every protocol module that sends data out of the gateway implements `ISinkAdapter`: MQTT, HTTP/HTTPS, TCP socket, OPC UA Server, database writers, and every future delivery target.

This document is the reference for adapter authors. It complements `docs/adapter-sdk/source-adapter-contract.md` and the canonical data model reference in `docs/core/canonical-data-model.md`.

The sink contract is designed to support **two fundamentally different delivery modes** from day one:

- **Push mode** — the sink actively sends data out (MQTT publish, HTTP POST, TCP write). The routing engine calls `PublishAsync` with batches.
- **Pull mode** — the sink exposes current values for external clients to read on their own schedule (OPC UA Server). The routing engine calls `UpdateCurrentValuesAsync` to refresh what the sink exposes.

A single sink may support both modes. The distinction is critical because OPC UA Server (planned for Phase 5) cannot be retrofitted into a push-only contract without a breaking change.

---

## 1. The contract

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

    Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct);

    Task UpdateCurrentValuesAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct);

    Task<ValidationResult> ValidateConfigAsync(
        SinkConfiguration config, CancellationToken ct);
}
```

> **Note on parameter names and ordering:** The interface uses `ct` (not
> `cancellationToken`) as the parameter name because it is part of the
> public contract locked in blueprint §4.3, and C# treats parameter names
> as part of the API surface for callers using named arguments. Method
> order also matches the blueprint: lifecycle first, then delivery methods,
> then `ValidateConfigAsync` last. Do not rename or reorder without a
> blueprint revision.

The lifecycle, validation, initialization, and health methods mirror `ISourceAdapter` exactly. The delivery methods — `PublishAsync` and `UpdateCurrentValuesAsync` — are what differ.

---

## 2. Responsibilities (what every adapter MUST do)

The responsibilities in the source adapter contract apply here identically:

1. **Be isolated.** One sink failure must never affect any other sink or route.
2. **Consume canonical points.** Do not define parallel payload types.
3. **Wrap all outbound errors.** `AdapterException` with well-formed codes.
4. **Honor cancellation.** Every async method respects `CancellationToken`.
5. **Respect declared capabilities.** Don't claim push if you only pull.
6. **Follow the state machine.** Use only legal transitions per `AdapterStateTransitions`.
7. **Be thread-safe on lifecycle methods.** `CheckHealthAsync` may run concurrently with `PublishAsync`.
8. **Initialize lazily.** Constructors do not open network connections; `InitializeAsync` may.
9. **Be idempotent.** Stop-then-start cycles must work cleanly.

Sink-specific responsibilities:

10. **Report publish results honestly.** `PublishResult.Success` drives per-sink cursor advancement (blueprint §19.4). Never return `Success = true` for a publish that didn't actually succeed.
11. **Handle partial batches explicitly.** If the underlying protocol can accept some points and reject others, the sink is responsible for bookkeeping. From the routing engine's perspective, `Success` is all-or-nothing.
12. **Do not buffer indefinitely on failure.** If the destination is down, return `Success = false` and let the route's store-and-forward buffer (blueprint §6) hold the backlog. Do not build your own hidden queue.
13. **Respect backpressure.** If publishing takes longer than the incoming rate, the routing engine's buffer absorbs the difference. Do not block `PublishAsync` for unbounded time — use the cancellation token to bail out.

---

## 3. Push vs Pull mode

The two modes exist because sinks fall into two categories:

### Push-mode sinks

Examples: MQTT, HTTP/HTTPS, TCP socket, Kafka, database writers.

The sink actively sends data to its destination. The routing engine calls `PublishAsync` with batches of points; the sink returns a `PublishResult` describing the outcome. The per-sink cursor in the route buffer advances when `PublishResult.Success == true`.

Declare `SinkCapabilities.Push`. Typically also declare `Batch` (most push sinks support batching for efficiency).

### Pull-mode sinks

Examples: OPC UA Server, Modbus Server, exposed current-value APIs.

The sink exposes data for external clients to read on their own schedule. It does not actively send anything. The routing engine calls `UpdateCurrentValuesAsync` whenever new canonical points arrive, and the sink's internal state is updated so that the next client read sees the fresh value. No cursor advancement, no batch receipt — pull sinks don't have "delivered yet" semantics in the traditional sense.

Declare `SinkCapabilities.Pull`. Typically also declare `Browse` (pull sinks usually expose a browse-able tag hierarchy).

### Hybrid sinks

A single sink may support both modes if the protocol allows it. For example, a future MQTT variant that also exposes a retained-message snapshot could declare both `Push` and `Pull`. Rare but permitted.

### Which method the routing engine calls

The routing engine looks at `Capabilities`:

- If `Push` is set, it calls `PublishAsync` and uses the result for cursor advancement.
- If `Pull` is set, it calls `UpdateCurrentValuesAsync` and does not track cursors for this sink (there is nothing to catch up on — the sink always exposes the latest values).
- If both are set, the routing engine uses push semantics for cursor advancement and also calls `UpdateCurrentValuesAsync` for the pull-mode consumers.

A sink that declares neither `Push` nor `Pull` is a bug and will be rejected at validation time.

---

## 4. Properties

### `InstanceId`

Stable identifier for the sink connector instance (e.g., `"mqtt-eremos-main"`, `"http-backup"`). Must match the `InstanceId` on the `SinkConfiguration`.

### `ProtocolName`

Lowercase protocol module name: `"mqtt"`, `"http"`, `"tcp"`, `"opcua-server"`. Matches the module's manifest.

### `Capabilities`

A `SinkCapabilities` flag set declaring which operations this adapter supports:

| Capability | Meaning | Required methods |
|------------|---------|------------------|
| `Push` | Sink actively pushes data out | `PublishAsync` must work |
| `Pull` | Sink exposes current values for external reads | `UpdateCurrentValuesAsync` must work |
| `Browse` | Sink exposes a browse-able node structure (typical for pull sinks) | future discovery API |
| `Batch` | Sink supports batched publishing efficiently | `PublishAsync` receives batches |
| `Transactional` | Sink returns per-message ack/nack | `PublishResult` carries accepted/rejected counts |
| `TestConnect` | Sink can verify connectivity without starting | `ValidateConfigAsync` performs a live test |

First-party examples:

- **MQTT sink:** `Push | Batch | TestConnect` (and `Transactional` for QoS 1/2)
- **HTTP sink:** `Push | Batch | TestConnect`
- **TCP sink:** `Push | TestConnect`
- **OPC UA Server sink (Phase 5):** `Pull | Browse`

### `State`

Same `AdapterState` lifecycle as source adapters. See `docs/adapter-sdk/source-adapter-contract.md` §6 for the full state machine.

---

## 5. Methods

### `ValidateConfigAsync`

Validates a `SinkConfiguration` without starting the adapter. Same semantics as the source adapter equivalent — check schema, semantics, and license. Optionally perform a live connectivity test if `TestConnect` is declared.

For sinks, pay particular attention to:
- **Authentication credentials** — missing username/password is a common failure
- **Certificate paths** — TLS cert files must exist and be readable
- **Broker/endpoint reachability** — a `TestConnect` validation should attempt to open a connection
- **Topic or endpoint templates** — validate that template placeholders (`{machineId}`, `{tagName}`) resolve correctly

Return `ValidationResult.Success()`, `SuccessWithWarnings(...)`, or `Failure(...)`.

### `InitializeAsync`

Applies the configuration. Construct long-lived helpers (MQTT client, HTTP client, TCP connection factory) but do NOT open the actual network connection — that happens in `StartAsync`.

### `StartAsync`

Opens the connection to the destination. Begin any background tasks (keep-alive, reconnection loops). After this returns, the sink must be ready to accept `PublishAsync` calls (or `UpdateCurrentValuesAsync` for pull sinks).

Target: under 5 seconds for healthy destinations. If the destination is slow or unreachable, transition to `Degraded` rather than blocking. The routing engine's buffer will hold data until the sink recovers.

### `StopAsync`

Stop publishing cleanly. Responsibilities:

- Flush any in-flight batches if possible.
- Close the connection gracefully (respect protocol-specific close handshakes for MQTT, TCP, etc.).
- Release native resources.
- Return once the sink is fully stopped.

Must be idempotent. Target graceful shutdown under 10 seconds.

### `CheckHealthAsync`

Return a non-blocking `AdapterHealth` snapshot. For sinks, include:

- Current connection state
- Last successful publish timestamp
- Publish rate (points/sec over the last N seconds)
- Error counters
- Pending message count in the sink's internal queue (if any — avoid large internal queues)

Must return in under 50 ms. Must not throw.

### `PublishAsync` (push mode)

Publishes a batch of canonical points. Only valid when `Capabilities` includes `Push`. The routing engine calls this with a batch of up to `SinkConfiguration.BatchSize` points (typically 100), batched on the `BatchIntervalMs` interval (typically 250 ms).

Responsibilities:

- Serialize each canonical point to the sink's wire format (JSON for MQTT/HTTP, binary for TCP, etc.).
- Send the batch as efficiently as the protocol allows.
- Return a `PublishResult` describing the outcome.
- Respect cancellation — if cancelled mid-publish, abort quickly.

**Cursor advancement and retry semantics (LOCKED, blueprint §19.4):**

- The `PublishResult.Success` flag is all-or-nothing for cursor advancement. If any critical part of the batch fails, return `Success = false`.
- The `AcceptedCount` and `RejectedCount` fields are informational. The routing engine does not advance the cursor partially based on them.
- Retry is tracked **per sink, per batch, in-memory only**. A gateway restart resets the retry state but the buffer cursors persist, so retried batches come from the buffer with the correct starting sequence.
- Partial-acceptance is the sink's internal problem. If your protocol rejects some points and accepts others:
  - **Option A (preferred):** return `Success = true` with `AcceptedCount + RejectedCount == total`, log the rejections to diagnostics, and record the rejected points in an error sink or dead-letter queue. The cursor advances past the batch normally.
  - **Option B:** return `Success = false` with an `AdapterError` in the `Error` field. The batch is retried in its entirety. Use this only when the rejection is transient.
  - **Never silently drop points.** A dropped point is a data loss bug.

**Performance:**

- Target p95 publish latency: 50 ms for a batch of 100 points against a healthy destination (blueprint §18.3).
- Target throughput: the sink should not be the bottleneck under sustained source load (blueprint §18.2 — up to 25k points/sec on a Large tier gateway).
- Batching is the main throughput lever. Protocols that support multi-message pipelining (MQTT QoS 0, HTTP/2 multiplexing, TCP stream writes) should use it.

### `UpdateCurrentValuesAsync` (pull mode)

Updates the exposed current value set for pull-mode sinks. Only valid when `Capabilities` includes `Pull`.

For OPC UA Server, this means updating the node tree so that the next client read returns the fresh value. For a REST API that exposes a "latest values" endpoint, this means updating an internal dictionary keyed by tag name.

Responsibilities:

- Update the sink's internal current-value state.
- Do not block waiting for external clients to read the new value. Pull sinks are asynchronous by nature.
- Return quickly — this method is called on every arriving batch.
- Do not throw except for catastrophic failures. There is no retry semantics for pull-mode updates because the concept doesn't apply — the sink always exposes the latest value it received.

Pull sinks do not participate in cursor advancement. The routing engine does not track which updates have been "delivered" because there is no delivery.

### `IAsyncDisposable.DisposeAsync`

Release any remaining resources. Called after `StopAsync`. Must be safe to call multiple times.

---

## 6. `PublishResult` reference

The result record drives cursor advancement in the route buffer:

```csharp
public sealed record PublishResult
{
    public required bool Success { get; init; }
    public int AcceptedCount { get; init; }
    public int RejectedCount { get; init; }
    public AdapterError? Error { get; init; }
    public TimeSpan Latency { get; init; }
}
```

Convenience factories:

```csharp
// All points accepted
return PublishResult.Successful(count: points.Count, latency: stopwatch.Elapsed);

// Publish failed entirely
return PublishResult.Failed(
    error: new AdapterError
    {
        Code = "MQTT.AUTH_REJECTED",
        Category = ErrorCategory.Authentication,
        Message = "Broker rejected credentials",
        Retryable = false,
    },
    latency: stopwatch.Elapsed);
```

Manual construction for partial results:

```csharp
return new PublishResult
{
    Success = true,                        // cursor advances
    AcceptedCount = 80,
    RejectedCount = 20,                    // 20 points rejected by endpoint but logged to diagnostics
    Latency = stopwatch.Elapsed,
    Error = new AdapterError
    {
        Code = "HTTP.PARTIAL_REJECT",
        Category = ErrorCategory.Protocol,
        Message = "20 points rejected by endpoint as malformed",
        Retryable = false,
    },
};
```

---

## 7. Error taxonomy

Same rules as source adapters. See `docs/adapter-sdk/source-adapter-contract.md` §5 for the full taxonomy. Sink-specific examples:

```csharp
public static class MqttErrors
{
    public const string ConnectionRefused = "MQTT.CONNECTION_REFUSED";
    public const string AuthRejected = "MQTT.AUTH_REJECTED";
    public const string TlsHandshakeFailed = "MQTT.TLS_HANDSHAKE_FAILED";
    public const string PublishTimeout = "MQTT.PUBLISH_TIMEOUT";
    public const string QosNotSupported = "MQTT.QOS_NOT_SUPPORTED";
    public const string TopicNotAuthorized = "MQTT.TOPIC_NOT_AUTHORIZED";
}

public static class HttpSinkErrors
{
    public const string EndpointUnreachable = "HTTP.ENDPOINT_UNREACHABLE";
    public const string AuthFailed = "HTTP.AUTH_FAILED";
    public const string ServerError = "HTTP.SERVER_ERROR";
    public const string RateLimited = "HTTP.RATE_LIMITED";
    public const string PartialReject = "HTTP.PARTIAL_REJECT";
}
```

Retryable classification for common sink failures:

| Failure | Category | Retryable |
|---------|----------|-----------|
| Connection refused (broker down) | `Network` | `true` |
| TLS handshake failed | `Network` | `true` (often) — unless cert is invalid, then `Configuration` `false` |
| Auth rejected | `Authentication` | `false` — user must fix credentials |
| Publish timeout | `Network` | `true` |
| QoS not supported | `Protocol` | `false` — config mismatch |
| HTTP 5xx | `Network` | `true` |
| HTTP 401/403 | `Authentication` | `false` |
| HTTP 429 rate limited | `ResourceExhausted` | `true` with `SuggestedBackoff` |
| TCP connection reset | `Network` | `true` |

---

## 8. Backpressure and buffering

**Do not build your own hidden queue inside the sink.** The store-and-forward buffer (blueprint §6, Milestone C2) handles persistent buffering. The sink's role is to either:

- Publish the current batch successfully, or
- Return `Success = false` and let the buffer hold it.

If you build your own internal queue:
- Memory growth becomes invisible to the routing engine's backpressure control
- Restart loses the queued data (the buffer is durable; your queue is not)
- Cursor advancement semantics break (the routing engine thinks the point is delivered when it's really sitting in your queue)

The exception: small in-flight state is acceptable. A sink may hold a handful of in-flight batches for protocol reasons (TCP window, HTTP/2 pipelining, MQTT QoS 1/2 in-flight). Keep this bounded — typically under 10 batches — and account for it in `CheckHealthAsync`.

---

## 9. Configuration

Every sink adapter defines its own configuration type derived from `SinkConfiguration`:

```csharp
public sealed record MqttSinkConfiguration : SinkConfiguration
{
    public required string BrokerHost { get; init; }
    public int BrokerPort { get; init; } = 1883;
    public string? ClientId { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool UseTls { get; init; }
    public string? CaCertificatePath { get; init; }
    public string? ClientCertificatePath { get; init; }
    public MqttProtocolVersion ProtocolVersion { get; init; } = MqttProtocolVersion.V311;
    public MqttQualityOfServiceLevel QoS { get; init; } = MqttQualityOfServiceLevel.AtLeastOnce;
    public bool Retain { get; init; }
    public string TopicPrefix { get; init; } = "";
    public string PublishMode { get; init; } = "PerTag";
}
```

Conventions match source adapters. See `docs/adapter-sdk/source-adapter-contract.md` §8.

---

## 10. Minimal example push sink

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sinks.Example;

public sealed class ExamplePushSinkAdapter : ISinkAdapter
{
    private ExampleSinkConfiguration? _config;
    private AdapterState _state = AdapterState.Created;

    public string InstanceId => _config?.InstanceId
        ?? throw new InvalidOperationException("Sink not initialized");

    public string ProtocolName => "example";

    public SinkCapabilities Capabilities =>
        SinkCapabilities.Push | SinkCapabilities.Batch | SinkCapabilities.TestConnect;

    public AdapterState State => _state;

    public Task InitializeAsync(SinkConfiguration config, CancellationToken ct)
    {
        _state = AdapterState.Initializing;
        _config = (ExampleSinkConfiguration)config;
        // TODO(human): construct long-lived HTTP client or similar
        _state = AdapterState.Initialized;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _state = AdapterState.Starting;
        // TODO(human): open connection, authenticate
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
        // TODO(human): flush in-flight batches, close connection
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
            // TODO(human): populate sent count, failure count, last success timestamp
        });
    }

    public async Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct)
    {
        if (_state != AdapterState.Running && _state != AdapterState.Degraded)
        {
            return PublishResult.Failed(
                new AdapterError
                {
                    Code = "EXAMPLE_SINK.NOT_RUNNING",
                    Category = ErrorCategory.Internal,
                    Message = $"Cannot publish in {_state} state",
                    Retryable = false,
                },
                TimeSpan.Zero);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            // TODO(human): serialize and send the batch
            await Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PublishResult.Failed(
                new AdapterError
                {
                    Code = "EXAMPLE_SINK.PUBLISH_FAILED",
                    Category = ErrorCategory.Network,
                    Message = "Failed to publish batch",
                    Retryable = true,
                    Context = ex.Message,
                },
                sw.Elapsed);
        }

        return PublishResult.Successful(points.Count, sw.Elapsed);
    }

    public Task UpdateCurrentValuesAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct)
    {
        throw new NotSupportedException(
            "ExamplePushSinkAdapter does not declare the Pull capability");
    }

    public Task<ValidationResult> ValidateConfigAsync(
        SinkConfiguration config, CancellationToken ct)
    {
        if (config is not ExampleSinkConfiguration cfg)
        {
            return Task.FromResult(ValidationResult.Failure(
                "EXAMPLE_SINK.CONFIG_WRONG_TYPE",
                $"Expected ExampleSinkConfiguration but got {config.GetType().Name}"));
        }

        // TODO(human): validate endpoint, credentials, TLS cert paths
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

## 11. Minimal example pull sink (OPC UA Server sketch)

```csharp
public sealed class ExamplePullSinkAdapter : ISinkAdapter
{
    private readonly Dictionary<string, CanonicalDataPoint> _currentValues = new();
    private readonly object _valuesLock = new();
    private AdapterState _state = AdapterState.Created;

    public SinkCapabilities Capabilities =>
        SinkCapabilities.Pull | SinkCapabilities.Browse;

    // ... lifecycle methods same as push sink ...

    public Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct)
    {
        throw new NotSupportedException(
            "Pull-mode sink does not support PublishAsync");
    }

    public Task UpdateCurrentValuesAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct)
    {
        lock (_valuesLock)
        {
            foreach (var point in points)
            {
                _currentValues[point.TagName] = point;
            }
        }

        // TODO(human): notify the embedded OPC UA server that nodes have new values
        return Task.CompletedTask;
    }

    // Internal method called by the embedded OPC UA server when a client reads a node
    internal CanonicalDataPoint? ReadCurrentValue(string tagName)
    {
        lock (_valuesLock)
        {
            return _currentValues.TryGetValue(tagName, out var point) ? point : null;
        }
    }
}
```

---

## 12. Testing guidance

Every sink adapter must include:

1. **Unit tests for `ValidateConfigAsync`** covering missing fields, invalid values, and successful validation.
2. **Unit tests for the state machine** verifying transitions match `AdapterStateTransitions`.
3. **Unit tests for `CheckHealthAsync`** in each lifecycle state.
4. **Integration tests against a simulator or mock destination** exercising `PublishAsync` or `UpdateCurrentValuesAsync` end-to-end.
5. **Publish result correctness tests** — every code path that constructs a `PublishResult` is tested.
6. **Failure tests** — verify that every declared error code can be produced under its triggering condition, and that the right `Retryable` flag is set.
7. **Cancellation tests** — verify that cancelling `PublishAsync` mid-operation aborts cleanly and does not leak connections.
8. **Backpressure tests** — verify that a slow destination does not cause unbounded memory growth in the sink. The sink should return `Success = false` and let the buffer hold the backlog.
9. **Partial-acceptance tests** (if the protocol supports it) — verify that the sink handles partial rejects per the policy documented in §5.
10. **Idempotency tests** — `StopAsync → StartAsync → StopAsync` cycles must work.

---

## 13. Adapter SDK deliverables checklist

Same as source adapters. See `docs/adapter-sdk/source-adapter-contract.md` §11.

---

## 14. Related reading

- `ARCHITECTURE_BLUEPRINT.md` §4.3 — the contract definition
- `ARCHITECTURE_BLUEPRINT.md` §6 — store-and-forward buffer
- `ARCHITECTURE_BLUEPRINT.md` §19.2 — fanout semantics
- `ARCHITECTURE_BLUEPRINT.md` §19.4 — retry tracking
- `ARCHITECTURE_BLUEPRINT.md` §19.7 — delivery modes
- `docs/core/canonical-data-model.md` — the data contract
- `docs/adapter-sdk/source-adapter-contract.md` — the complementary source contract
