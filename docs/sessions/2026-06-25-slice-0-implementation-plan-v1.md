# Slice 0 — Source-generation foundation — Implementation plan v1

**Date:** 2026-06-25
**Status:** v1 — implementation plan for the locked Slice 0 contract
(`2026-06-25-source-generation-foundation-slice-0-spec.md`). Draft for review (you + ChatGPT) before
coding. Shared dependency: runtime-reconfigure workstream must consume this, not reimplement it.
**Scope:** lifecycle correctness only — generation identity, immutable lease, commit-point fencing,
stable ingress, retirement ordering, orphan accounting, replacement admission. **No** poll timeout,
recovery, liveness verdicts, bundle, or UI (those are Slices A/B/C).

---

## 1. The central refactor (why this is more than "add a field")

**Today** the route-facing channel + intake are owned **per generation** and recreated on every
`AddAsync` (`SourceSupervisor.RegisterInternal` builds a fresh bounded channel; `SupervisedSource`
holds `{Registration, Channel, Intake, PumpTask, Cts}`). A restart/reconfigure therefore creates a
**new intake**, which is exactly the M1 orphan hazard (a route bound to the old reader freezes) and
the reconfigure plan's core problem.

**Slice 0 splits that into two lifetimes:**

| Concept | Lifetime | Owns |
|---|---|---|
| **SourceSlot** (stable) | lifetime of the configured source instance | the **stable** channel + `ISourceIntake` the route binds to, the **slot gate** (publish-authorization), per-source lifetime counters, and a reference to the current generation |
| **SourceGeneration** (swappable) | one adapter instance | the adapter `Registration`, `PumpTask`, `Cts`, the **immutable lease**, per-generation state; writes into the slot channel through a **generation-scoped writer** |

A restart/reconfigure swaps the **generation**; the **slot (and its intake) persists**, so routes
never rebind. This is the structural M1 fix *and* the reconfigure plan's stable-ingress endpoint —
delivered once, here.

> This means `RemoveAsync` (drop the slot) completes the channel, but a generation **retire/replace**
> does **not** — a behaviour split that does not exist today and is the heart of the change.

---

## 2. Code-grounding (verified insertion points)

- `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` — `_supervised` map, `SupervisedSource`,
  `RegisterInternal` (channel creation), `StartInternal`/`StopInternal`, `RunPollLoopAsync`/
  `RunSubscribeLoopAsync`, `Add/Remove/Restart/Start/Stop` under `_lifecycleGate`, `GetIntake`.
- `src/ElpisEdgeConnect.Core/Routing/ISourceIntake.cs` — the route-facing contract (stable slot will
  implement it).
- `src/ElpisEdgeConnect.Core/Diagnostics/ISourceHealthSink.cs` + `RuntimeDiagnosticsCollector.cs` —
  `RecordSourceState` / `RecordSourceObservation` (the counter/health commit points to fence);
  `EnsureSource` reset-on-id-reuse (replace with two-tier policy).
- Route binding: routes capture `SourceSupervisor.GetIntake(id).Reader` at build time — must keep
  returning the **stable** slot intake.

---

## 3. New types

### Core (`ElpisEdgeConnect.Core` — protocol-agnostic, reusable by both workstreams)
- `RuntimeInstanceId` — unique id minted once at gateway-process startup (registered as a singleton);
  guarantees generation keys are unambiguous across reboots.
- `GenerationId` — `ulong`, monotonic per slot.
- `GenerationKey` — `readonly record struct (RuntimeInstanceId, string SourceSlotId, ulong GenerationId)`;
  written to every snapshot/event.
- `GenerationLease` — **immutable**: holds its `GenerationKey` + a reference to the owning slot gate.
  Exposes only `Key` and `IsPublishAuthorized` (delegates to the gate). No mutable `CurrentGenerationId`.
- `SourceSlotGate` — the synchronization boundary. Atomic `Authorize(lease)`, `Retire(reason)`,
  `IsCurrent(lease)`. Holds the single publish-authorized lease (or none). Concurrent
  retire/authorize resolve to zero-or-one authorized (spec test 9).
- `GenerationLifecycleState` enum — `Authorized | Retired | Quarantined | Orphaned`.
- `GenerationSnapshot` + `SourceLifetimeSnapshot` — the cached, generation-keyed read models
  (§7 accounting); bounded history of retired generations.
- `IGenerationReplacementPolicy` — `AdmitReplacement(slot, retiredGen) -> {Allow | DenyActiveWork | Escalate}`.
  Conservative default impl: **deny while retired work physically active** (spec §8).

### Host (`ElpisEdgeConnect.Host`)
- `SourceSlot` — stable slot: `ISourceIntake` (stable), the slot channel, `SourceSlotGate`,
  lifetime counters, current `SourceGeneration`. Replaces the channel/intake ownership currently in
  `SupervisedSource`.
- `SourceGeneration` — `{Registration, PumpTask, Cts, GenerationLease, per-gen state}`.
- `GenerationScopedIntakeWriter` — wraps the slot channel writer; **at the commit point** validates
  the lease via the slot gate and only then writes; a retired lease's points are discarded (counted
  as rejected). This is the data-ingress fence (spec §4.1).
- `RetiredTaskObserver` — attaches a continuation to a retired pump task so its eventual
  completion/fault is observed and recorded in history **without** touching current state; flips
  Quarantined→Orphaned at the cleanup deadline and updates per-source/process orphan counts.

---

## 4. Supervisor refactor (behaviour-preserving where possible)

1. **Introduce `SourceSlot` owning the channel + stable intake.** `RegisterInternal` creates the slot
   (and its channel) once per instance id; `GetIntake` returns the slot's stable intake. **Decision to
   confirm in review:** set the slot channel `SingleWriter=false` (successive-generation writers),
   *or* keep `SingleWriter=true` and rely on the default no-overlap policy + gate serialization
   guaranteeing the channel only ever sees the current generation's writes (an orphan is fenced before
   it can call the underlying writer). v1 recommendation: keep `SingleWriter=true` under the default
   no-overlap policy, with a test pinning that a retired generation never reaches the channel writer.
2. **Generation construction (`StartInternal`).** Allocate the next `GenerationKey`, build the
   `GenerationLease`, construct the adapter + a `GenerationScopedIntakeWriter` bound to that lease,
   `Authorize` the generation on the slot gate **atomically**, then `InitializeAsync`/`StartAsync` and
   launch the pump writing through the scoped writer.
3. **Pump paths.** `RunPollLoopAsync` / `RunSubscribeLoopAsync` write via the generation-scoped writer
   (not the raw channel) and report observations/state through the **generation-keyed** sink (§5).
4. **Retirement ordering (`StopInternal` rework — spec §5).** On stop/reconfigure/recovery:
   (a) `gate.Retire(reason)` + **detach ingress** (scoped writer starts rejecting) **before**
   (b) `Cts.Cancel()` + writer-complete-or-keep (complete only on slot removal), then
   (c) bounded `WaitAsync(deadline)`; on completion record terminal outcome; on timeout hand the task
   to `RetiredTaskObserver` (quarantine→orphan + accounting). This corrects today's "cancel then
   abandon, no fence" path.
5. **Remove vs retire split.** `RemoveAsync` retires the current generation **and** drops the slot
   (completes the channel so routes see end-of-stream). `RestartAsync` retires the old generation and
   — subject to `IGenerationReplacementPolicy` — authorizes a new one on the **same** slot/intake.
   Replacement is **never** started merely because an await timed out (spec §5.10).
6. **Keep `_lifecycleGate`** serializing slot mutations; the slot gate is the finer-grained publish
   boundary.

---

## 5. Fencing the health/counter commit points (spec §4.2)

- Add **generation-keyed overloads** to `ISourceHealthSink` (e.g. `RecordSourceObservation(GenerationKey, …)`,
  `RecordSourceState(GenerationKey, …)`); the supervisor passes its lease key.
- `RuntimeDiagnosticsCollector` validates the key against the slot's current generation **under the
  same lock** as the state mutation; a non-current key may append a bounded terminal/history record
  but cannot mutate current source state or current-generation lifetime totals.
- Replace `EnsureSource`'s reset-on-id-reuse with the **two-tier** policy: per-generation current
  state resets; per-source lifetime totals + bounded history survive (spec §7).
- **Scope guard:** Slice 0 only *fences and generation-keys the existing* state/observation updates.
  It adds **no** new liveness reason codes, DTO fields, events, or UI — those are Slice A.

---

## 6. Test plan (maps 1:1 to spec §11; deterministic, no sleeps)

A `FakeGenerationSource` test adapter whose pump can: emit after retirement, complete/fault late, and
block past a deadline — driven by `TaskCompletionSource` + injected `TimeProvider`/monotonic shim.

| Spec test | Test |
|---|---|
| 1 late point after retirement rejected | scoped writer drops post-retire point; channel never receives it |
| 2 callback past `IsCurrent` at commit rejected | force the race: pass IsCurrent, retire, then commit → rejected under gate lock |
| 3 late fault can't replace current health | retired gen faults after a new gen is current → history only |
| 4 ingress detached before cancellation | assert order: retire/detach precedes `Cts.Cancel` |
| 5 over-deadline task counted once as orphaned | one orphan increment, idempotent |
| 6 orphan late completion → history only | current state unchanged |
| 7 id reuse increments gen, preserves lifetime | gen id ++ ; lifetime totals retained |
| 8 keys unambiguous across restart | new `RuntimeInstanceId` → no collision |
| 9 concurrent retire/authorize → 0/1 authorized | stress loop, invariant holds |
| 10 replacement denied while work active unless capability+policy | default policy denies; opt-in allows |
| 11 stop + reconfigure use the same primitive | both paths drive one gate; no parallel impl |

Plus regression: existing `SourceSupervisor`/routing/`Host.Tests` still green (behaviour-preserving),
and **route intake survives a generation swap** (new positive test — the M1/stable-ingress property).

---

## 7. Commit series (standalone, no recovery/liveness/UI — spec §12)

1. **Core primitives** — `RuntimeInstanceId`, `GenerationKey`, `GenerationLease`, `SourceSlotGate`,
   state/accounting records, `IGenerationReplacementPolicy` + default. Unit-tested in isolation
   (gate concurrency = spec test 9).
2. **Supervisor slot/generation split** — introduce `SourceSlot` (stable channel+intake) +
   `SourceGeneration`; `GetIntake` returns the stable intake. Behaviour-preserving; routing/Host tests
   green; add the stable-ingress-survives-swap test.
3. **Commit-point fencing** — `GenerationScopedIntakeWriter` + generation-keyed health sink + collector
   validation + two-tier reset (spec tests 1–3, 7).
4. **Retirement ordering + orphan accounting** — reworked `StopInternal`, `RetiredTaskObserver`,
   per-source/process counts (spec tests 4–6).
5. **Replacement admission** — `RestartAsync`/reconfigure entrypoint consult `IGenerationReplacementPolicy`;
   conservative default; escalation result (spec tests 10–11).

Suggested final subject (spec §12): `runtime: add shared source-generation lease and publish fencing`.

---

## 8. Risks & decisions for review

- **Channel `SingleWriter` (§4.1)** — confirm true-under-default-policy vs false. v1 leans true + a
  pinning test; flag for review.
- **Stable-ingress behaviour change is load-bearing for the reconfigure plan** — its replacement-
  admission rule may be stricter ("old must terminate before replacement"); it must still use *this*
  lease/gate. Joint review required before commit 5.
- **Two-tier reset semantics** — agree exactly which totals survive (spec §7) so Slice A's liveness
  counters build on a stable base.
- **Behaviour preservation** — commit 2 must not change externally observable supervisor behaviour
  except the intended stable-intake property; gate it on the **full** `Management.Tests` + `Host.Tests`
  + `Core.Tests` (filtered runs miss cross-cutting guards).
- **No scope creep** — resist adding timeouts/recovery here; the supervisor poll watchdog is Slice C.

---

## 9. Exit criteria

- All 11 spec acceptance tests + the stable-ingress regression pass.
- Full `Core.Tests` + `Host.Tests` + `Management.Tests` green; 0 warnings / 0 errors.
- No new liveness/recovery/UI behaviour introduced (diff is lifecycle-correctness only).
- Both workstream owners (diagnostic-strengthening + runtime-reconfigure DRI) approve before merge.

---

## 10. Open items (do not block starting commits 1–2)

- Confirm the `SingleWriter` decision (§8) before commit 3.
- Confirm replacement-admission strictness jointly with the reconfigure workstream before commit 5.
- DRI assignment (the maintainer of `SourceSupervisor`) — owns this series end-to-end.
