# Data-delivery fixes — completion handoff

**Date:** 2026-06-01
**Status:** **DONE — data flows end-to-end again.** Operator confirmed "That fixed it"
after deploying the build. MTConnect + FOCAS deliver to the live broker; Modbus
delivers and now survives source edits; malformed points are skipped-and-reported
instead of killing a route.

## What triggered the session

Operator reports, in order:
1. MTConnect → MQTT: "we receive data from MTConnect but it doesn't reach MQTT."
2. The live data-monitoring "tap" page is missing (designed, never built — still open).
3. "Modbus Simulator to MQTT is also not working now which was working earlier.
   Something broke recently." → **"Fix it now."**

These turned out to be **three independent root causes** plus one operator-side
red herring. All three are now fixed and on `master`.

## Root causes & fixes

### A — Serializer killed routes on a benign type mismatch (`3f147fc`)

`BinaryWriterFormat.WriteValue` hard-unboxed numerics (`(int)value!`). MTConnect's
`production/parts_count` is emitted as a boxed **`long`** but declared
`ValueType.Integer` → `InvalidCastException` (you cannot unbox a boxed `long` to
`int`). That exception is **not** a `SqliteException`, so it escaped the buffer,
killed the route's intake pump, and `totalEnqueued` stayed `0` — **silent** total
outage for that route.

**Why "worked earlier, broke recently":** the sibling `MessagePackFormat` already
coerced via `Convert.ToInt32(...)`; switching the production serializer to the
hand-rolled `BinaryWriterFormat` (the C2a benchmark winner) introduced the hard
casts. So it regressed when the serializer was switched, not in this session.

**Fix:** `WriteValue` now coerces (`Convert.ToInt32/ToInt64/ToSingle/ToDouble/
ToBoolean`, invariant culture) — lossless and byte-stable, matching MessagePack.
`MTConnectTagMap.PartsCount.ValueType` corrected to `Long` (its true type; the
parser reads it via `TryGetLong`), avoiding int-overflow on coercion.

### B — Hot-reload didn't rebind routes when a source restarted (`78646e4`)

Live diagnostics on the operator's gateway showed the Modbus route `Running`,
source `pointsObserved` **frozen at 1023** (≈ the 1024 intake-channel capacity),
`buffer.totalEnqueued = 0`, `pipeline.pointsIn = 0`. The source observed points but
the route's intake pump **never read one**.

**Cause:** editing a source in Studio triggers a hot-reload **Restart** of that
source, which `SourceSupervisor` re-creates with a **brand-new intake channel**.
`RuntimeReloadCoordinator` only re-registers routes that are *themselves* in the
change set — the route's own config text didn't change, so it kept its **old,
now-completed** channel reader. Its pump parked forever in `WaitToReadAsync` on the
dead channel while the new channel filled to capacity and back-pressured the source
to a halt. Exactly matches "worked earlier, broke after I edited the source."

**Fix:** the coordinator now synthesizes a **Route Restart** for every enabled,
currently-registered, cross-record-valid route bound to a source the reload is
restarting — mirroring the M.P2.3 startup-skip recovery pass and its dedup
discipline (classifier/recovery intent wins; pristine plan never mutated).

### C — Quarantine-and-continue: a bad point no longer kills a route (`3d8a2c2`)

The amplifier behind A: *any* per-point serialization failure killed the whole
route, silently. Operator chose the **quarantine-and-continue (loud)** policy.
Now the buffer skips an un-serializable point (survivors keep contiguous
sequences), counts it (`BufferStats.Quarantined` /
`RouteHealthSnapshot.QuarantinedPointCount`, a data-quality signal distinct from
backpressure), and emits a structured `RoutePointQuarantinedEvent` →
`BUFFER.POINT_QUARANTINED` at `/diagnostics/events`. **Locked in ADR-0028.**

### Red herring — operator was watching the wrong broker

The `MQTT-EREMOS` sink targets remote broker **`20.197.8.189:1883`**, not local
Mosquitto. `mosquitto_sub -h 127.0.0.1` showed nothing regardless of pipeline
health. On the real broker, MTConnect + FOCAS were flowing the whole time. Use
`mosquitto_sub -h 20.197.8.189 -p 1883 -t "eremos/#" -v`.

## How it was verified

- A: live test gateway (MTConnect demo → local MQTT) — 133 messages delivered incl.
  `production_parts_count`; 928 Core + 53 MTConnect tests green.
- B: deterministic unit regression
  (`Reconcile_RestartSource_CascadesRebindOfDependentRoute`) asserts the dependent
  route lands in `RestartedInstances` after a source-only edit.
- C: buffer skip/contiguity/counter/callback tests (3), collector accumulation +
  distinct-from-backpressure (1), aggregator event mapping (1).
- Operator confirmed end-to-end after deploying the build.

## Test totals at close

928 Core, 152 Host, 877 Management — all green; solution builds 0 warnings / 0
errors.

## Still open (not blockers)

- **Live data-monitoring "tap" page** (operator issue #2). Mockups exist
  (`docs/sessions/2026-05-30-ux-mockups/1-tap-stream.html` etc.), never built.
  This was the surface the operator went looking for to self-diagnose and couldn't
  find.
- **Route-detail inline `lastErrorCode/Message/AtUtc` stay `null`** on a worker
  fault. Cosmetic only: the fault reason already surfaces at `/diagnostics/events`
  as a `ROUTE.STATE_CHANGED` Error carrying the reason in its summary
  (`RouteEventAggregator.MapStateChange`). With quarantine (C) absorbing the common
  case, worker faults are now rare. Fill the inline fields if desired.
- **Hybrid quarantine escalation** (flood-threshold → route degraded) — considered
  and deferred in ADR-0028.

## Commits (all on `master`, pushed)

| | Commit | What |
|---|---|---|
| A | `3f147fc` | `fix(buffer)` — coerce numeric boxing in `BinaryWriterFormat` |
| B | `78646e4` | `fix(reload)` — cascade route rebind when a source restarts |
| C | `3d8a2c2` | `feat(buffer)` — quarantine-and-continue (ADR-0028) |

## Reference

- ADR-0028 — quarantine-and-continue policy (locked this session)
- `src/ElpisEdgeConnect.Core/Buffer/BinaryWriterFormat.cs` — coercion
- `src/ElpisEdgeConnect.Host/RuntimeReloadCoordinator.cs` —
  `ComputeSourceRestartRouteRebindActions`
- `src/ElpisEdgeConnect.Core/Buffer/SqliteBuffer.cs` — `TrySerializeOrQuarantine`
