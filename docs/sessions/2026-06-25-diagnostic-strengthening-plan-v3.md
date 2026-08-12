# Diagnostic strengthening + blocking-I/O containment — Plan v3 (implementation-lock)

**Date:** 2026-06-25
**Status:** **v3 — implementation-lock design.** Passed reality-check + design review. Supersedes v2
for implementation. **Slice 0 (shared source-generation foundation) must land before either this
workstream or the runtime-reconfigure workstream adds behavior-changing recovery/reconfigure logic.**
**Inputs folded in:** reality-check `2026-06-25-diagnostic-strengthening-reality-check.md` (C1–C6) and
its review (R1–R8 + locked defaults). v1/v2 retained as historical trail.
**Trigger:** FOCAS2 silent-stall incident + confirmed Modbus TCP equivalent — a shared blocking-I/O
detection/containment gap.

---

## 0. What v3 locks (delta from v2)

v2's spine stands (detection-first; shared platform gap). v3 hardens it with the reality-check
corrections and the review's race-safety amendments:

- Generation model is defined precisely (R1) and the hard invariant is **one *publish-authorized*
  generation per source slot**, not one physically-live generation.
- Every side effect is fenced by an **immutable generation lease** (R2), including the *existing*
  teardown path (reality-check C5).
- Bundles read **cached snapshot-provider interfaces only** — never raw supervisor/adapter handles
  (R3); the C1 diagnostics/I-O boundary is preserved, not "fixed."
- **Dual-clock**: monotonic for decisions, UTC for display (R4); monotonic is net-new (C4).
- Host-observed progress is **separated from adapter-reported transport success** (R5).
- Generation reset is **two-tiered**: per-generation state resets, per-source lifetime/history
  survives (R6, C6).
- Explicit **liveness health DTO fields** — no overloading `LastErrorCode` (R7, C2/footer finding).
- A dedicated **source-liveness event journal** — not the route-state recorder (R8, C2).
- **Alerting (push/paging) is a first-class v1 requirement** (user decision), built on stable reason
  codes.
- **ModSim is the deterministic Modbus reproduction harness**; 1–2 CNCs are a gated FOCAS canary
  (user decision).

---

## 1. Generation model (R1) — precise vocabulary + invariant

A **source slot** is the stable identity (instance id) + its supervisor-owned ingress. A
**generation** is one adapter instance + worker/connection bound to that slot, identified by a
monotonic `GenerationId`.

| State | Meaning |
|---|---|
| **Current / publish-authorized** | The sole generation permitted to affect routes, counters, or current health for the slot. |
| **Retired** | Publish authority revoked **atomically**. May still be physically executing. |
| **Quarantined** | Retired + isolated from further calls/side effects while cleanup is attempted. |
| **Orphaned** | Quarantined work still physically executing past the cleanup deadline. |

**Hard invariant:** *zero or one publish-authorized generation per source slot.* Physically-executing
retired generations are permitted **only** under an explicit, bounded orphan/resource policy. A
**replacement generation may start only** when (a) the adapter's proven capability and (b) the
resource/orphan budget say overlap is safe; otherwise escalate to controlled process restart or
operator action (never spin unbounded replacements).

---

## 2. Generation lease + side-effect fencing (R2)

- The fence is an **immutable lease token** created at generation construction and **captured by
  every execution path** of that generation — not a mutable `GenerationId` field on `SupervisedSource`
  (a mutable field lets an old callback read the new id and appear current).
- **Every side effect is fenced**, not just point publication:
  route/channel publication; subscription callbacks/notifications; source-observation + point
  counters; current-generation errors + health transitions; reconnect callbacks; any adapter-owned
  side channel.
- Preferred mechanism: a **generation-scoped ingress / channel-writer wrapper** handed to the
  generation, so an adapter physically cannot publish past the gate. Health/counter sinks are
  likewise lease-checked.
- **Late completion/fault** from a retired generation may be recorded in *generation history* but
  **must never overwrite current state**.
- **Teardown ordering (C5 fix):** close the publish gate **before** cancellation + bounded wait. If
  cleanup exceeds its deadline, **quarantine** the task, retain it for observation, and **count it
  against the orphan budget**. This corrects the existing `StopInternal` abandon path, which today
  waits-and-abandons with no fence.

---

## 3. Liveness / progress model (R5, R6) — signal ownership + reason codes

### Signal ownership (R5 — do not conflate)
| Signal | Owner | Source of truth |
|---|---|---|
| Schedule-due, poll-started, poll-completed, poll-timed-out, generation id | **Supervisor** | Authoritative; supervisor controls the loop. |
| Transport success/failure, active operation name | **Adapter instrumentation / structured poll outcome** | The supervisor **must not** infer transport success merely because `PollAsync` returned (an adapter may swallow partial errors or return empty). |
| Data accepted / emitted | **Generation-scoped ingress** | Lease-fenced; only the current generation counts. |

### Reason codes (cached current state per source; carry generation id)
| Reason | Rule | Meaning |
|---|---|---|
| `SchedulerOverdue` | No poll started by due + scheduler slack | Supervisor/scheduler progress failure |
| `PollOverdue` | Current poll exceeds **soft** operation budget | Slow or possibly stuck call |
| `PollTimedOut` | Current poll exceeds **hard** operation budget | Containment decision required |
| `TransportStale` | Polls complete/retry but no **successful device exchange** within policy | Device/network/protocol down |
| `DataSilent` | Transport healthy but no data emitted within **configured** cadence | Idle machine / filter / data-side — **config-driven, informational by default** |
| `SubscriptionSilent` | **Only** when the protocol exposes a real heartbeat/keepalive/callback-progress signal that has stalled | Subscription pump stalled |
| `RecoveryLoop` / `RecoveryExhausted` | Recovery rate / budget exceeded | Persistent defect |

**`DataSilent` (R5/R7 + user "config-based"):** disabled/informational unless an expected production
cadence is configured per source. Scheduler/poll/transport liveness run **independently** of data
production and never depend on it. A subscription that simply stops yielding, with no supported
heartbeat, is **not** `SubscriptionSilent` — it falls under `DataSilent` policy.

### Two-tiered generation reset (R6, C6)
- **Per-generation (reset on new generation):** in-flight operation, start/completion clocks,
  consecutive failures, active liveness reasons, recovery phase.
- **Per-source lifetime (survives):** total points/polls/timeouts/recoveries, retired/orphan counts,
  prior transition events, last-known generation outcome.
- New generation = clean *current-health* snapshot; the retired generation's terminal reason stays in
  bounded history. Today's accidental reset on instance-id reuse (`RuntimeDiagnosticsCollector`
  `EnsureSource`) is replaced by this explicit policy. All snapshots/events carry the generation id.

---

## 4. Dual-clock model (R4, C4)

- **Monotonic** time for: deadlines, elapsed durations, overdue decisions, debounce/hysteresis.
- **UTC** retained separately for operator display + cross-system correlation.
- **Never** serialize raw monotonic ticks as wall-clock, and never compare monotonic across process
  restarts. A snapshot exposes `StartedAtUtc` + a pre-computed `Elapsed`; the live evaluator keeps the
  monotonic timestamp internally.
- Monotonic is net-new (today everything, incl. FOCAS2 poll pacing, uses `DateTime.UtcNow`).
  **FOCAS2 wall-clock poll pacing is a separately-tracked fix**, not folded into this work.
- **Tests:** forward and backward wall-clock jumps must not change timeout/overdue/debounce decisions.

---

## 5. Health DTO + reason precedence (R7)

- Extend the route/source DTOs (`RouteSummaryDto` / `RouteSourceSummaryDto`) with **explicit liveness
  fields**: `healthSeverity`, stable `reasonCode`, `reasonSinceUtc`, `elapsed`. The footer
  (`StatusFooter.razor`) and route/global aggregation consume **these**, not `LastErrorCode`
  (liveness is not necessarily an adapter error).
- **Deterministic reason precedence + transition hysteresis** so simultaneous conditions render
  stably. `DataSilent` must **not** mask stronger progress failures (`PollTimedOut`,
  `SchedulerOverdue`).
- **Global health may never show "All systems healthy" while any source has a current
  degraded/unhealthy liveness reason** (acceptance test).

---

## 6. Source-liveness event journal (R8, C2)

- New bounded **`SourceLivenessChangedEvent`** journal (NOT the route-state recorder, which is
  route-engine-emitted and route-state-shaped). Fields: source id, generation id, prior/current
  reason + severity, transition time, elapsed evidence, recovery correlation id.
- Emit **on transitions only**, with debounce/hysteresis. Route/global health derives from cached
  *current* state; the journal is **history + forensic evidence** (and a bundle contributor).
- Requires a new event type + a new `ISourceHealthSink` method (the supervisor's only seam today
  writes state fields, not events).

---

## 7. Diagnostics boundary + snapshot providers (R3, C1)

- **C1 invariant holds today and must be preserved:** no health/diagnostics/bundle path calls adapter
  device I/O. Do **not** put raw `SourceSupervisor`, adapter instances, or control handles into
  `BundleContext`.
- Extend the bundle through **narrow cached-snapshot interfaces**:
  `IRuntimeDiagnosticsSnapshotProvider`, `ISourceProgressSnapshotProvider`,
  `IRuntimeEventSnapshotProvider`. Runtime contributors **copy immutable/cached snapshots only**.
- **Per-contributor deadline** is containment-only and **must never trigger a call into a running
  adapter**. Config/history/audit contributors stay **fail-closed**; explicitly-classified **runtime**
  contributors may **fail-soft**, recording timeout/skip/truncation in the manifest. Reuse the
  already-reserved-but-unwired `BundleCapability.Diagnostics` slot.
- **Bundle must complete** even when every source call is blocked and when a runtime contributor
  ignores cancellation (its deadline fires; manifest records the skip; config/history/audit
  fail-closed behavior is untouched).
- **Tracked (Slice B):** a contributor whose deadline fires must **not** leave a running background
  task — repeated timed-out contributors must not accumulate orphaned work. Bound/observe the
  abandoned contributor task with the same orphan-accounting discipline as generation orphans (§2).

---

## 8. Alerting — push/paging (user decision: first-class in v1)

- Liveness-fault transitions emit through **stable reason codes** to: Studio status, the existing
  health/management endpoint, **and a push/paging seam**.
- Design a thin **`ILivenessAlertNotifier`** seam fed by the `SourceLivenessChangedEvent` journal
  (transition-driven, debounced). v1 ships at least one concrete delivery (leveraging the gateway's
  existing health-check port / watchdog config); the reason codes are provider-agnostic so additional
  integrations are pure consumers.
- Alerting is **observational** (P1) — it reads cached liveness state and never calls the adapter.
- Escalation reasons (`PollTimedOut`, `RecoveryExhausted`) carry an actionable operator message +
  next-action.
- **Tracked (pre-Slice-A):** the initial concrete paging transport must be **named before Slice A
  coding begins**. The seam is provider-agnostic, but v1 ships one named transport.

---

## 9. Reproduction & test harness (user decision)

- **Modbus — ModSim (primary, deterministic):** a Modbus slave simulator (ModSim / equivalent)
  configured to **accept a TCP connection and then not respond** reproduces the blocking-read stall
  **deterministically and off-production**. This is the canonical Modbus regression/repro for the
  blocking-I/O class — realizes v2 §7's "non-responsive Modbus test server" with a real tool.
- **FOCAS2 — 1–2 CNC canary (gated):** the user can provide 1–2 CNCs. Per the review's safety point,
  the canary runs **only after Slice A detection + Slice B forensics are live** (so a wedge is
  captured, not just hit), with a defined stop condition for orphan count / resource growth /
  production impact. Start with one CNC; expand to two only after controller connection limits are
  known.
- **Natural-occurrence auto-capture** remains the safety net for production sites that are not part of
  the canary.
- The deterministic FOCAS path uses a **shim around fwlib** so a chosen collector call blocks
  indefinitely (acceptance tests), independent of real hardware.

---

## 10. Implementation slices (locked order)

### Slice 0 — Shared source-generation foundation (lands FIRST; shared with reconfigure workstream)
One standalone primitive, **consumed by both this plan and the runtime-reconfigure plan** — not
reimplemented by either:
- stable source slot + monotonic generation id;
- **immutable generation lease**;
- atomic publish-authorization / retirement;
- generation-scoped ingress fence;
- late-task observation + bounded retired/orphan accounting;
- **no automatic recovery yet.**
This is the convergence contract (reality-check §3). It also retroactively fences the existing
teardown path (C5).

### Slice A — Detection without behavior change
Progress clocks (monotonic), independent periodic evaluator (new host `BackgroundService`, mirrors
`SinkSessionPoller`'s shape, reads cached snapshot only), reason codes, explicit health DTO fields,
source-liveness journal, route/global propagation, Studio/footer corrections, and the alert seam
(§8). **No** task detachment, adapter disposal, or replacement generation. Closes the 18-hour blind
spot with zero recovery races.

### Slice B — Forensics
Per-operation probes (FOCAS2 collectors + Modbus block/function first), cached runtime bundle
contributors via the snapshot-provider interfaces (§7) + process/runtime indicators (uptime,
thread-pool queue/worker counts, managed thread count, GC pressure, active recovery/orphan counts),
and proactive source-gap explanations driven by reason codes (extends ADR-0023 beyond Compare).

### Slice C onward — Containment + adapter-proven recovery
Bounded supervisor waiting (monotonic `WaitAsync`-style), generation retirement orchestration, backoff
+ retry/orphan budgets, terminal escalation, then **adapter-proven** recovery (FOCAS cross-thread
abort/free proof; Modbus socket-close-interrupt proof via ModSim) and the process-isolation decision
(ADR). Automatic recovery is enabled **per adapter only after its proof tests pass**.

---

## 11. Acceptance tests (gate before each slice; superset of v2 §7 + review §5)

- An old callback arriving after a new generation starts **cannot** publish, increment current
  counters, or replace current health/error state.
- Stop/reconfigure **retires the publish gate before** cancellation; a task abandoned past the
  cleanup deadline is observed and counted against the orphan budget.
- Forward **and** backward wall-clock jumps do not change timeout/overdue/debounce decisions.
- A completed **empty** poll updates poll progress **without** falsely claiming data emission or
  transport success.
- A subscription with no data **and no supported heartbeat** is **not** `SubscriptionSilent` (it's
  `DataSilent` policy).
- A runtime bundle completes when **all** source calls are blocked, and when one runtime contributor
  ignores cancellation — skipped runtime material recorded, config/history/audit still fail-closed.
- Reusing a source instance id creates a **new current generation** while preserving bounded
  source-lifetime history.
- Global health **cannot** display "All systems healthy" while any source has a current
  degraded/unhealthy liveness reason.
- (Modbus, ModSim) accept-but-never-respond: detection fires within the soft threshold; recovery (when
  enabled) does not consume one extra thread-pool worker per attempt; socket-close interrupt behavior
  is characterized before auto-recycle is enabled.
- (FOCAS2, fwlib shim) chosen collector blocks indefinitely: health transitions within the soft
  threshold without waiting for `PollAsync`; thread affinity / abort-free behavior / handle counts /
  late completion / shutdown behavior are characterized before auto-recycle is enabled.

---

## 12. ADR decisions

1. **Amend ADR-0027** — health has independent lifecycle, progress, transport, and data-cadence
   dimensions; route/global health propagates reason-coded liveness faults (explicit DTO fields).
2. **Amend ADR-0020** — add **runtime** contributors via cached snapshot-provider interfaces +
   per-contributor deadlines; **runtime** contributors may fail-soft (config/history/audit remain
   fail-closed). Scoped amendment, not a blanket invariant flip.
3. **Amend ADR-0023** — permit proactive source-gap explanations driven by liveness reason codes.
4. **New ADR — Source generation & blocking-operation containment** — generation states/lease,
   publish-authorization invariant, side-effect fencing, retry/orphan budgets, terminal escalation.
   **Shared with the runtime-reconfigure workstream** (single primitive).
5. **Potential follow-up ADR — Adapter process isolation** — if FOCAS/another library has no safe
   in-process abort (decided in Slice C+).

---

## 13. Locked defaults (open items)

| Item | Decision (v3) |
|---|---|
| Reproduction | **ModSim deterministic for Modbus** (off-production, primary). **1–2 CNC FOCAS canary** gated behind live Slice A+B instrumentation, stop-conditioned. Natural-occurrence auto-capture is the production safety net. |
| Alert delivery | **Push/paging is first-class in v1** (user need): Studio + health endpoint + `ILivenessAlertNotifier` seam on stable reason codes. |
| `DataSilent` | **Config-based**: disabled/informational unless an expected per-source cadence is configured. Never masks stronger progress failures; never gates scheduler/poll/transport liveness. |
| fwlib / controller details | Deferred to the Slice C FOCAS recovery-proof / trigger-capture slice. Not a v3 design blocker. |

---

## 14. Cross-workstream coordination (Sony)

**Slice 0's generation primitive is shared with the runtime-reconfigure plan**
(`2026-06-23-runtime-reconfigure-systemic-plan-v2.md` §5 — stable ingress + generation tokens +
**zero or one publish-authorized generation; physical overlap requires an explicit proven capability
and resource policy**). **Neither workstream may privately reimplement a generation mechanism.** Whoever
lands Slice 0 first ships it standalone; the other consumes it. This dependency is recorded in the
reality-check (§3, shared to `Sony_Development`) and must be agreed before either side adds
behavior-changing recovery (this plan, Slice C) or reconfigure (reconfigure plan, Layer B/C) logic.

---

## 15. Deferred / out of scope for v3

- FOCAS2 wall-clock poll-pacing fix (related; tracked separately, §4).
- Adapter-worker process-isolation model (decided in Slice C+ by ADR if no safe in-process abort).
- Per-environment **trigger** root cause (network idle-drop / device connection-limit) — captured via
  Slice B forensics + the §9 repro, then fixed per site; distinct from this platform gap.
- `1420311-source` never-connected investigation (separate).
