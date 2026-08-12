# Diagnostic strengthening — Reality-check pass (v2 → v3 gate)

**Date:** 2026-06-25
**Status:** Reality-check of `2026-06-25-diagnostic-strengthening-plan-v2.md` against the actual code.
Findings feed **v3** (the implementation-lock design). Not an implementation doc.
**Method:** code verification (file:line evidence) of v2's load-bearing assumptions. No code changed.

> Cadence: v1 → review → v2 → **reality-check** → v3. This is the reality-check; v3 incorporates it.

---

## 1. What v2 got right (confirmed against code — keep as-is in v3)

| v2 claim | Verdict | Evidence |
|---|---|---|
| The "All systems healthy" footer ignores source liveness | **Confirmed** | `StatusFooter.razor` polls `/api/v1/routes` every 3s; "healthy" = `_routesUp == _routesTotal && _degradedSinks == 0`; degradation is **sink-only**. A `Running` source emitting nothing renders green. |
| Per-source health has no progress clocks | **Confirmed** | `RuntimeDiagnosticsCollector` `SourceDiagnosticsState` / `SourceHealthSnapshot` store only `State, PointsObserved, LastPointAtUtc, LastError, LastErrorAtUtc`. `LastPointAtUtc` updates **only when pointsObserved > 0** → a successful empty poll updates nothing. No lastPollStarted/transportSuccess/inFlight/generation anywhere. |
| No timeout bounds the live poll | **Confirmed** | `SourceSupervisor.RunPollLoopAsync` — bare `await adapter.PollAsync(ct)` (`SourceSupervisor.cs:632`), only `ct` cancellation. |
| No source "generation" concept exists | **Confirmed** | `SupervisedSource` holds only Registration/Channel/Intake/PumpTask/Cts. |
| Subscription path has no cadence to compare | **Confirmed** | `RunSubscribeLoopAsync` (`:477-525`) drains an `IAsyncEnumerable`; a stream that stops yielding emits no event. `SubscriptionSilent` needs a separate heartbeat clock. |
| No independent periodic health evaluator exists | **Confirmed** | Only periodic `BackgroundService` is `SinkSessionPoller` (sinks, 5s). Source health is push-on-observation + on-demand snapshot only. L1's monitor is genuinely net-new. |
| Bundle is config-only | **Confirmed (root cause found)** | `BundleContext` exposes only `GatewayId, Configuration, ConfigManager, Layout` — **no runtime/health/supervisor handles**. A runtime contributor is impossible without extending `BundleContext`. |

**Net:** v2's core diagnosis — no progress clocks, no bounded poll, no periodic evaluator, config-only
bundle — is accurate. The detection-first sequencing stands.

---

## 2. Corrections that change v3 (reality differs from v2's assumptions)

### C1 — Invariant #1 ("diagnostics never call device I/O") currently **HOLDS**. Reframe from "fix" to "preserve."
No health/diagnostics/bundle path calls adapter device I/O. The collector is push-only
(`ISourceHealthSink` "never polls source adapters"); `CheckHealthAsync` has **zero callers** in Host
and in steady-state Management paths (the one call, `Focas2BrowseService.cs:246`, is the wizard
browse-probe on a fresh ephemeral adapter — not a running-source read). **v3 change:** drop the
"diagnostics may be hanging" framing; instead state the invariant holds today and the new periodic
monitor + bundle MUST keep reading cached snapshots only (never call into the live adapter).

### C2 — The flight recorder is **NOT host-injectable**. L1's "emit a flight-recorder event" needs new plumbing.
The route "recent events" log is a `BoundedEventLog<RouteStateChangedEvent>` fed **only** by the
routing engine's `OnRouteStateChanged` (`IRoutingEngineDiagnostics`); the event type is route-state-
shaped (`RouteEvents.cs` `From/To : RouteState`). The supervisor's only seam is `ISourceHealthSink`,
which writes *state fields*, not events. **v3 change:** L1 must add a **new source-liveness event
type + a new sink method** (or a new per-source bounded event log on the collector). It cannot reuse
the existing route-state recorder.

### C3 — The bundle is **fail-CLOSED today**, not fail-soft. L3's "fail-soft per contributor" reverses a locked rule.
`IBundleContributor.cs:12-14` + `BundleBuilder.cs:233-238`: "any contributor throwing fails the whole
bundle," contributors awaited sequentially under one outer `ct`, **no per-contributor deadline**.
**v3 change:** do NOT blanket-flip to fail-soft. Scope fail-soft to **runtime contributors only**
(config/history/audit stay fail-closed — a bundle without config is still meaningless), and add a
per-contributor deadline wrapper as net-new infra. Bonus: the `BundleCapability` enum **already
reserves `Diagnostics` and `FlightRecorder`** members (wired nowhere) — reuse them. Requires
extending `BundleContext` to carry the `RuntimeDiagnosticsCollector` + supervisor handles. The
ADR-0020 amendment is therefore *scoped* (runtime contributors), not a wholesale invariant flip.

### C4 — Monotonic time is **unmet everywhere today**, including FOCAS2 poll pacing.
Supervisor + collector use `DateTime.UtcNow`; FOCAS2 inter-poll pacing itself uses wall-clock
(`Focas2SourceAdapter.cs:332/339`). Only `Stopwatch` uses are reload timing + browse-probe latency.
**v3 change:** invariant #8 (monotonic for timeout/progress decisions) is net-new — introduce a
monotonic clock for the new progress snapshot + bounded-wait. Flag FOCAS2's wall-clock pacing as a
**related, separate fix** (an NTP step today can distort poll pacing); don't silently fold it in.

### C5 — Generation fencing **also applies to existing teardown**, not just new recovery. Scope ↑.
`SourceSupervisor.StopInternal` (`:552-570`) **already** does the "WaitAsync(10s) + abandon the pump
task" dance — *without any generation fence*. So the late-result-leak risk v2 raises for L4 is
**already live on the stop path** today. **v3 change:** the generation fence must cover both the new
recovery path AND existing teardown/abandon — it's a retroactive correctness fix, not only forward-
looking.

### C6 — `EnsureSource` **resets source state on instance-id reuse** — generation counters need a deliberate policy.
`RuntimeDiagnosticsCollector.cs:704-717`: reusing an instance id after reconfigure resets counters to
a fresh `Created` state. **v3 change:** define whether progress/consecutive-failure/recovery counters
survive or deliberately reset across generations, and make it explicit (not an accident of id reuse).

---

## 3. The convergence finding (cross-workstream — important for sequencing)

**Two separate plans now both require the same "source generation" primitive:**
- This diagnostic plan: generation fencing for bounded-wait + recovery (v2 L4/L5, and C5 above).
- The runtime-reconfigure plan (`2026-06-23-runtime-reconfigure-systemic-plan-v2.md` §5): stable
  ingress + generation tokens + at-most-one-live-generation, so a reconfigure can retire a generation
  and reject its late results.

These are the **same concept** (a monotonic generation id per source, with publish-fencing and
at-most-one-live invariant). **v3 recommendation:** build **one** shared source-generation primitive
in the supervisor, consumed by both workstreams — do not invent two. This is a coordination point
with Sony (who owns the reconfigure plan). Whichever plan implements the generation primitive first
should land it as a standalone, shared piece; the other consumes it. Surface this in both v3s.

---

## 4. Insertion-point map (for v3 implementation slices)

| Concern | Where it lands (verified) |
|---|---|
| Liveness reason in the footer | `StatusFooter.razor` (client-side) + the `/api/v1/routes` DTO (`RouteSummaryDto` / `RouteSourceSummaryDto` — already carries `LastErrorCode`, currently ignored by the footer) |
| Progress snapshot + reason codes | `RuntimeDiagnosticsCollector` (`SourceDiagnosticsState` / `SourceHealthSnapshot`) — extend with the new clocks; `ISourceHealthSink` — new record methods |
| Recording poll start/complete/transport/in-flight | `SourceSupervisor.RunPollLoopAsync` / `RunSubscribeLoopAsync` (push the new clocks alongside the existing `RecordSourceObservation`) |
| Bounded wait + generation fence | `SourceSupervisor.RunPollLoopAsync` (live) + `StopInternal` (teardown, C5); new generation field on `SupervisedSource` |
| Periodic monitor | new host `BackgroundService` (mirror `SinkSessionPoller`'s shape/cadence), reads collector snapshot only |
| Liveness events | new event type + `ISourceHealthSink`/collector log (C2) |
| Runtime bundle | extend `BundleContext`; new `Runtime`/reuse-`Diagnostics` contributor; per-contributor deadline wrapper (C3) |

---

## 5. Open items still needing the user (carried from v2 — none block v3 drafting)

1. Reproduction appetite: re-point 1–2 CNCs at EdgeConnect to capture the wedge live, vs wait for next
   production occurrence to auto-capture.
2. Alert delivery for v1: Studio-only vs push/paging.
3. `DataSilent` default cadence per machine (or defer to per-source config with a default).
4. fwlib version + CNC model/connection-limit details (only for the FOCAS recovery proof, Slice D).

---

## 6. v3 readiness

v2's spine survives the reality-check. v3 must fold in C1–C6 (esp. the flight-recorder plumbing C2,
the scoped bundle fail-soft C3, monotonic-time net-new C4, and the teardown-fence scope C5), and
adopt the **shared generation primitive** with the reconfigure plan (§3). With those, Slice A
(detection only) is implementable with no behavior change and closes the silent-outage blind spot.
