# ADR-0021: Route Flight Recorder — always-on bounded event log of state transitions

**Status:** Proposed (2026-05-30)
**Date:** 2026-05-30
**Framing:** Every route gets a bounded, always-on ring of structured state-transition events. This is the "black box recorder" for a route — when an operator asks "what happened?" the recorder is the source. It is **distinct from the Live Data Tap** (ADR-0018): the tap captures *data points* (high-volume, privacy-sensitive → demand-driven); the recorder captures *state events* (low-volume, no PII → safe to always run).

## Context

The 2026-05-30 diagnostic-strategy review pass with the operator and ChatGPT identified the Route Flight Recorder as a Tier-1 must-have. The naming comes from aviation: a black box that runs continuously, captures a bounded history of significant events, and is read after an incident.

Today an operator who notices a route stopped delivering data has no history. Logs may have rotated, the route's runtime state shows the *current* condition only, and the diagnostic page (per ADR-0017) is demand-driven — it captures only while the operator is watching, so it can't show what happened 10 minutes ago.

The asymmetry is the load-bearing observation: **data points are high-volume + privacy-sensitive + reproducible** (replay the same source → get the same point), so demand-driven capture is right (ADR-0017). **State events are low-volume + no PII + non-reproducible** (you cannot replay a sink disconnect that happened 10 minutes ago), so always-on capture is right.

The Flight Recorder is the always-on counterpart to the demand-driven Live Data Tap. They compose: the Live Data Tap shows what's flowing now while you watch; the Flight Recorder shows what happened to the route over the recent past.

## Decision

The Route Flight Recorder conforms to the following five rules.

### Rule 1 — Per-route, bounded ring

Each configured route has its own Flight Recorder instance. Each recorder is a bounded ring of structured events. Default bound: **500 events per route**. Eviction is silent (oldest dropped when ring is full).

The 500-event bound is sized for typical operational reality: a healthy route emits ~10 events per day (startup, subscription created, occasional reconnect, hot reconfigure). 500 events covers ~50 days for healthy routes and the most recent activity for failing routes (which emit more).

For routes flapping at high frequency (>500 events/hour), the ring fills quickly and shows only the recent past. That's acceptable — the operator's question is "what happened recently?" not "what happened over 50 days?"

### Rule 2 — What constitutes a "significant event"

The recorder captures **structured state transitions**, not per-point data. The enumerated event categories:

- **Lifecycle**: `RouteStarted`, `RouteStopped`, `RouteEnabled`, `RouteDisabled`
- **Connection**: `SourceConnected`, `SourceDisconnected`, `SinkConnected`, `SinkDisconnected`
- **Subscription / pump**: `SubscriptionCreated`, `SubscriptionRecreated`, `PollLoopStarted`, `PollLoopFaulted`
- **Throughput / pressure**: `QueueDepthExceededThreshold`, `BackpressureActivated`, `BackpressureCleared`, `BufferFull`
- **Data flow**: `FirstPointObserved`, `NoPointsInWindow` (e.g., expected 1/sec, observed 0 for 30s)
- **Configuration**: `ConfigApplied`, `HotReloadInvoked`, `RouteReconfigured`
- **Faults**: `FaultRaised`, `FaultCleared` (composes with the existing fault registry — events carry the fault code)
- **External**: `CertificateNearingExpiry`, `LicenseExpiringSoon` (when relevant to this route)

Per-point events (a single tag value arriving, a single notification) are **never** captured by the recorder. Those belong to the Live Data Tap (ADR-0018), which is demand-driven by design.

### Rule 3 — Event schema is structured, not free-text

Each event is a structured record:

```csharp
public sealed record FlightRecorderEvent
{
    public required Guid EventId { get; init; }
    public required string RouteId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required FlightRecorderCategory Category { get; init; }
    public required FlightRecorderEventKind Kind { get; init; }
    public required FlightRecorderSeverity Severity { get; init; }
    public string? RelatedFaultCode { get; init; }
    public string? RelatedEntityId { get; init; } // source/sink/transform that emitted
    public IReadOnlyDictionary<string, string>? StructuredContext { get; init; }
}
```

`StructuredContext` is bounded (max 1 KB serialised) and carries kind-specific structured data — e.g., for `QueueDepthExceededThreshold`: `{"observed":823, "capacity":1000, "thresholdPct":80}`. Free-text fields are forbidden; the schema must support deterministic rendering and machine-comparison across bundles.

### Rule 4 — Always-on, low per-event cost

The recorder is always-on and adds **constant per-route memory** (500 × event size, ~50 KB per route). Events arrive at low frequency by construction (per Rule 2 — no per-point events). The cost is bounded and predictable.

The recorder writes to its ring synchronously (single lock-free or cheap-lock structure per route). It MUST NOT block any hot-path operation — if the lock is contended, the event is silently dropped and a `FlightRecorderEventDropped` counter increments. (Bounded ring + low event rate makes contention extremely rare; the safety valve exists for correctness.)

### Rule 5 — Survives restart for crashable-event categories

Some events MUST persist across host restart — most importantly the events leading up to a crash. The recorder periodically (every 30 s) flushes its ring to a per-route file under the diagnostics directory. On host startup, the recorder loads the flushed ring as the initial state.

This means after a crash an operator can open the Flight Recorder and see the events that preceded the crash. The flush is best-effort (a crash mid-flush loses ≤30 s of events; acceptable). Survives-restart means the recorder is the only diagnostic surface in EdgeConnect that materially uses disk; the storage cost is bounded (per-route file × number of routes × ~50 KB).

## Consequences

**Positive:**

- "What happened to this route in the last hour?" becomes answerable without re-running a failing scenario with diagnostics open
- The Bundle (ADR-0020) gains a structured event timeline that support engineers can read without a customer-side debug session
- Route Timeline (ADR-0026) has a backing event source — the recorder is the dataset Timeline renders
- Explain Why Data Is Missing (ADR-0023) can walk the recent recorder events when answering "why" — instead of guessing, it can point at the actual `SinkDisconnected` event 12 seconds ago
- P7 (explain outcomes) is structurally supported — the recorder is the evidence base behind the explanation

**Negative:**

- Adds per-route disk I/O (~50 KB flush every 30 s × per route). At 100 routes that's 5 MB / 30 s = ~170 KB/s sustained. Trivial for normal hardware; flagged for embedded gateway profiles.
- Schema changes to `FlightRecorderEvent` need versioning so a v0.3 recorder can load a v0.2 flushed ring. The `bundleSpecVersion` from ADR-0020 extends here.
- The discipline of "no free-text fields" requires care from adapter authors — they want to write `_logger.LogWarning("MQTT broker rejected publish: {detail}", reason)` style log lines, but the recorder needs `StructuredContext`. The adapter SDK guide will need a "how to emit a Flight Recorder event" section.

**Forbidden patterns:**

- A per-point event in the recorder (use the Live Data Tap)
- A free-text message field on `FlightRecorderEvent` (use structured `StructuredContext`)
- A bounded ring without the `FlightRecorderEventDropped` counter — silent drops without observability are an evidence-loss footgun

## Reference

- ADR-0017 — demand-driven diagnostic surfaces (the recorder is the always-on counterpart)
- ADR-0018 — Live Data Tap (per-point capture; lives alongside the recorder)
- ADR-0020 — Diagnostic Bundle (the bundle includes the recorder's flushed ring)
- ADR-0023 — Explain Why Data Is Missing (consumes recorder events to walk causality)
- ADR-0024 — What Changed (consumes recorder events that mark `ConfigApplied`, `RouteReconfigured`)
- ADR-0026 — Route Timeline (renders the recorder events as a unified visual timeline)
- Platform principle P4 — preserve the explainability data path (recorder events ARE that data path for state)
- Platform principle P7 — surfaces explain outcomes (recorder is the evidence source)
- `docs/sessions/2026-05-30-diagnostic-strategy-handoff.md`
