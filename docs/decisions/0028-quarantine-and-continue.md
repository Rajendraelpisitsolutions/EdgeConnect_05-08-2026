# ADR-0028: Quarantine-and-Continue — an un-serializable point is skipped and reported, never fatal to the route

**Status:** Accepted (2026-06-01)
**Date:** 2026-06-01
**Framing:** This ADR locks how the store-and-forward buffer handles a single
`CanonicalDataPoint` whose value cannot be written to the wire. The point is
**quarantined** (skipped, counted, and surfaced as a loud structured event) and
the rest of the batch is delivered normally. A single malformed point must
**never** strand a route. This is the policy the operator chose explicitly
after the failure mode below took down live MTConnect delivery.

## Context

A production incident (2026-06-01) exposed a class of bug with catastrophic blast
radius. MTConnect's `production/parts_count` was emitted as a boxed `long` while
its canonical `ValueType` was `Integer`. `BinaryWriterFormat.WriteValue` did a
hard unbox (`(int)value!`), which threw `InvalidCastException` on a boxed `long`.

That exception is **not** a `SqliteException`, so it escaped the buffer's
`WriteBatchLocked` catch, propagated out of `IMessageBuffer.EnqueueAsync`, out of
`RouteWorker.RunIntakePumpAsync` (which catches only `OperationCanceledException`),
and killed the route's intake pump. The route's `totalEnqueued` stayed at `0` and
**all** delivery on that route silently stopped. The failure was invisible because
a fire-and-forget pump task's unobserved exception does not crash the process.

Two distinct problems were entangled:

1. **The trigger** — a benign int/long boxing mismatch that should never have
   thrown (fixed separately: `BinaryWriterFormat` now coerces numeric boxing via
   `Convert.*`, matching the already-correct `MessagePackFormat`).
2. **The amplifier** — *any* per-point serialization failure killing the entire
   route. This ADR governs the amplifier.

The operator was offered three policies for a point that genuinely cannot be
serialized (quarantine-and-continue / fault-the-route-visibly / hybrid) and chose
**quarantine-and-continue, made loud**. The reasoning: for an edge data platform,
availability of the rest of the data outweighs the loss of one already-unstorable
point, provided the loss is never silent.

## Decision

### Rule 1 — A serialization failure is skipped, never propagated

When the buffer cannot serialize a point, it catches the exception (any exception
that is not `OperationCanceledException`), skips that point, and continues the
batch. The serialization exception must **never** escape the buffer write path. A
single malformed point can never fault the route, stall the intake pump, or stop
delivery of any other point.

`SqliteException` (a genuine storage/IO failure) is explicitly **out of scope** —
it still propagates and surfaces as a route fault, because it is not a per-point
data-quality problem and retrying is the correct response.

### Rule 2 — Survivors keep contiguous sequence numbers

When a point is skipped mid-batch, the surviving points are assigned **contiguous**
buffer sequence numbers (no gap where the skipped point would have been). Per-sink
cursors must observe an unbroken sequence; a hole would complicate replay and
ordering accounting for no benefit. The skipped point simply never receives a
sequence.

### Rule 3 — Quarantine is counted as a distinct signal

Every quarantine is counted in `BufferStats.Quarantined` (buffer-internal,
cumulative) and surfaced on `RouteHealthSnapshot.QuarantinedPointCount`. This
counter is **distinct from** `DroppedByCapacity` / `BackpressureDropCount`. A
backpressure drop is a **capacity** signal (scale or tune the buffer); a quarantine
is a **data-quality** signal (fix the upstream adapter/mapping). Conflating them
into one number destroys the operator's ability to tell "I'm overloaded" from "I'm
emitting garbage." (This is the same anti-conflation discipline as ADR-0027.)

### Rule 4 — Quarantine is loud: a structured per-point event

Each skipped point is reported as a structured `RoutePointQuarantinedEvent`
(`RouteId`, `TagName`, `Reason`, `ObservedAtUtc`) carrying the tag and the
serialization error. It surfaces at `/api/v1/diagnostics/events` as
`BUFFER.POINT_QUARANTINED` (severity Warning) and in the route's per-route event
ring. Silent skipping is forbidden — the operator must always be able to see which
tag was dropped and why (platform principle P6).

### Rule 5 — Reporting never blocks or breaks the data path

The quarantine observer is supplied at buffer **construction** (not via the locked
`IMessageBuffer` contract), invoked **outside** the writer mutex after the batch
commits, and a throwing observer is swallowed. Diagnostics observation must never
stall or break enqueue. This mirrors the existing `IRoutingEngineDiagnostics`
non-blocking contract.

### Rule 6 — The serializer fails only on genuinely un-representable values

The serializer must **coerce** benign representational differences (e.g. a boxed
`long` for an `Integer` tag, a boxed `int` for a `Double` tag) rather than throw,
so quarantine is reserved for values that genuinely cannot be represented on the
wire (an unsupported metadata runtime type, a non-UTC `DateTime`, an out-of-range
numeric). A type-box mismatch is not a data-quality failure and must not cost a
point. `BinaryWriterFormat` and `MessagePackFormat` must stay behaviourally
identical here.

## Consequences

**Positive:**

- One malformed point can never take down a route. The blast radius of a
  data-quality bug is one tag, not an entire source's delivery.
- The loss is honest and explainable — counted separately, surfaced as a loud
  event naming the tag and reason. An operator can act on it.
- The `IMessageBuffer` C2a contract is preserved (no signature change); the
  reporting path is an implementation detail wired by `DefaultRouteBufferFactory`.
- The mechanism generalises to every adapter and every future value type — it is
  in the buffer, the single serialization chokepoint.

**Negative:**

- A quarantined point is **lost** (it was already unstorable, but the loss is
  real). Acceptable per the availability-over-single-point trade chosen here, and
  mitigated by Rule 4's loudness.
- Quarantine can mask a systemic upstream bug behind a steadily-climbing counter
  if nobody watches it. Mitigation: the count is on the route health surface and
  the events timeline; a future enhancement may add a flood-threshold escalation
  (the "hybrid" option, deferred — see below).

**Forbidden patterns:**

- Letting a per-point serialization (or other non-storage) exception propagate out
  of the buffer write path.
- Skipping a point without both counting it (Rule 3) and emitting an event
  (Rule 4) — silent loss is forbidden.
- Folding quarantines into the backpressure/capacity drop counter.
- Faulting the whole route because one point could not be serialized.
- Blocking the writer mutex on the quarantine observer, or letting a throwing
  observer break enqueue.
- Adding a `Convert`-style coercion so loose it silently reinterprets a genuinely
  wrong value (e.g. parsing an arbitrary string into a number) — coercion is for
  numeric boxing width only.

## Deferred

The **hybrid escalation** option (quarantine normally, but escalate the route to a
visible degraded/failed state if quarantines exceed a threshold over a rolling
window, so a route cannot silently shed most of its data) was considered and
deferred. The count + event surface is sufficient for launch; revisit if a
real-world "route quietly drops 90% of its data" scenario appears.

## Reference

- ADR-0027 — Route Health Surface (same anti-conflation discipline: distinct
  signals are never collapsed into one number; `QuarantinedPointCount` is its own
  dimension, not folded into Drops)
- ADR-0023 — Explain Why Data Is Missing (a climbing quarantine count is a
  first-class "why is data missing" cause)
- Platform principle P6 — operational product (the loss must be visible to the
  operator, not silent)
- `docs/sessions/2026-06-01-data-delivery-fixes-handoff.md` — the incident, the
  three root causes, and the fixes
- Commits `3f147fc` (serializer coercion), `3d8a2c2` (quarantine-and-continue)
