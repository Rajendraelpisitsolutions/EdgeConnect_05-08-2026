# Slice 0 — Commit 3: atomic supervisor cutover — Plan v1

**Date:** 2026-06-26
**Status:** v1 — cutover design for review (you + ChatGPT) **before any code**. This is the **first
behavior-changing commit** (commits 1–2 were unused scaffolding). Per B8 it must land as **one atomic
commit** — no intermediate state worse than `master`.
**Builds on:** commit 1 (`SourceSlotGate`/lease/allocator), commit 2 (`SourceSlot`, scoped writer,
quiescence, registry). **Shared with** the runtime-reconfigure workstream — this commit delivers the
stable ingress that plan also needs.

---

## 1. Goal & the one-commit rule

Replace the supervisor's per-generation channel ownership with the stable `SourceSlot`, wiring —
**together, atomically** — stable ingress, the generation-scoped write + health/counter fences, the
revoke-before-cancel retirement order, and default-deny replacement admission. After this commit a
source restart swaps the *generation* while the *intake stays put*, so a bound route never goes stale
(the structural M1 fix goes live).

**Why atomic (B8):** wiring stable ingress *before* the fences would let a timed-out old pump write
into the same channel its successor uses — a regression vs today's orphaned-channel behaviour. So all
of it lands in one commit, gated by the §7 merge gates and the full test suites.

---

## 2. Current vs target structure (`SourceSupervisor`)

**Today** (`src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs`):
- `_supervised: ConcurrentDictionary<string, SupervisedSource>`.
- `SupervisedSource { Registration, Channel<CanonicalDataPoint>, Intake, PumpTask, Cts }` — the
  channel + intake are **recreated per `AddAsync`** (`RegisterInternal`), so a restart makes a new
  intake (the M1 hazard).
- `RunPollLoopAsync` / `RunSubscribeLoopAsync` write to `sup.Channel.Writer.WriteAsync`.
- `StopInternal` cancels the CTS, completes the writer, awaits the pump (bounded, **abandon on
  timeout, no fence** — the C5 finding), then `StopAsync` + `DisposeAsync`.

**Target:**
- `_slots: ConcurrentDictionary<string, SupervisedSlot>`.
- `SupervisedSlot { SourceSlot Slot; SupervisedGenerationRuntime? Current }` where the **`SourceSlot`
  is stable** (owns channel + `Intake` + gate across generations) and
  `SupervisedGenerationRuntime { SourceRegistration Registration; ISourceAdapter Adapter;
  SourceGeneration Generation; CancellationTokenSource Cts; Task PumpTask }` is **per generation**
  (rebuilt on restart).
- The pump writes via `Generation.Writer.WriteAsync` (the lease-fenced scoped writer).
- `GetIntake(id)` returns `slot.Slot.Intake` — **reference-stable across restarts**.

---

## 3. Lifecycle mapping (supervisor op → slot ops)

| Supervisor op | New flow |
|---|---|
| **Add / initial Start** | get-or-create `SourceSlot` → `PrepareGeneration` → build adapter (RegistrationFactory) + `InitializeAsync` **while unauthorized** → `TryActivate` (atomic current+authorize) → `StartAsync` → launch pump on the per-generation CTS. **Init/Start failure** → `AbandonPrepared` (init) or `RetireCurrent` + adapter dispose (start), id consumed, zero authorized. |
| **Restart** (same id) | on the **same slot**: retire current (§4) + prove quiescence + **default-deny check** (§5) → `PrepareGeneration` → Init → `TryActivate` → Start → pump. **Slot + intake persist → routes do not rebind.** |
| **Remove** (permanent) | retire current (§4) + prove quiescence → `CompleteIntakeForPermanentRemoval` (terminal; completes channel so the bound route sees end-of-stream) → drop slot from map (allocator tombstone survives). |
| **Stop** (shutdown) | retire current (§4) + bounded quiesce; do **not** mark slot terminal (clean shutdown, not removal). |

`_lifecycleGate` still serializes slot mutations; the slot gate is the finer publish boundary.

---

## 4. Retire-before-cancel + quiescence wiring (C5 fix)

Reworked teardown for a generation (used by Restart/Remove/Stop):
1. `slot.RetireCurrent(reason)` — gate `TryRetire` (revoke authority, the linearization point) **then**
   `Writer.Detach()`. (Already in `SourceSlot`.)
2. Cancel the per-generation CTS.
3. Await the pump task bounded (existing 10s ceiling) → feed
   `Generation.RetirementCompletion.SetPump(Proven)` on completion, `Unproven` on timeout.
4. `Adapter.StopAsync` + `DisposeAsync` bounded → `SetAdapterStop(Proven)` on completion, `Unproven`
   on timeout/throw.
5. Evaluate `RetirementCompletion.Evidence`. If **not `Proven`** → `RetiredGenerationRegistry.Quarantine`
   (and, past an orphan budget, `MarkOrphaned`); the abandoned pump/adapter is observed, not awaited
   forever.

The pump itself, on `IntakeWriteOutcome.RejectedRetired` / `ChannelClosed` (the writer was retired/
detached), exits cleanly — it no longer races to write into a successor's channel.

---

## 5. Default-deny replacement admission

On **Restart**, a new generation is activated only if the old generation's `Evidence == Proven`
(no adapter yet opts into overlap). Otherwise the supervisor returns a structured **escalation**
(reconcile fault) and leaves the slot with **zero** publish-authorized generations — never two.

**Intended behaviour change (flag for review):** today a Restart tears down + brings up even if the
old `StopAsync` hangs (it's abandoned after 10s and the new generation starts — risking two live
generations). After this commit, a Restart whose old generation can't prove quiescence is **denied**
and escalates. For a *clean* reconfigure (adapter stops normally → Proven) this is seamless; for a
*wedged* source (e.g. FOCAS `StopAsync` blocked on a dead handle) the new generation is withheld and
the operator/escalation path takes over (the auto-recovery watchdog is Slice C). This is the
at-most-one-publish-authorized guarantee made real.

---

## 6. The two sink paths (B5) — introduced together in this commit

- **Generation-scoped sink (fenced):** per-point observations and per-generation errors are forwarded
  to `RuntimeDiagnosticsCollector` **only through `gate.TryCommit(lease, …)`** — a retired
  generation's late observation is dropped at the supervisor boundary (so the existing collector needs
  no generation-keying yet; that's commit 4). The `TryCommit` body does **only** the forward — **no
  logging, event dispatch, or notification under the gate** (§7.3).
- **Slot-administrative sink (supervisor-only):** lifecycle transitions (`Running`, `Stopping`,
  `Stopped`, faulted) go straight to the collector via a supervisor-owned path that adapters never
  receive.

This keeps "accepted current-generation data" and "supervisor lifecycle truth" on separate, correctly-
fenced paths.

---

## 7. Merge gates (v2 §12a) to clear in this commit

1. **Built-in adapter quiescence matrix (the key risk).** For each operator-available adapter —
   FOCAS2, S7, Modbus TCP, MTConnect, Brother HTTP, OPC UA Client — confirm what yields
   `QuiescenceEvidence.Proven` on an **ordinary clean stop**: (a) the pump task completes on CTS
   cancel, and (b) `StopAsync`/`DisposeAsync` complete within bound. Otherwise default-deny turns a
   routine restart into a permanent refusal. **To be filled by inspecting each adapter's `StopAsync`
   before/as part of this commit** (e.g. FOCAS2 `Disconnect`/`FreeLibHandle`, OPC UA subscribe-loop
   drain). Any adapter that can't prove clean-stop quiescence is documented as "restart escalates when
   wedged" — acceptable, but it must be deliberate, not surprising.
2. **Late-history path ownership:** rejected-late counters / terminal history are written only by the
   trusted Host generation-scoped sink / `RetiredGenerationRegistry`; adapters get no post-retirement
   mutation path.
3. **No notification under the gate:** collector mutation may run gate→collector, but logging, events,
   paging, and subscriber callbacks happen **after** releasing both locks.
4. **Performance baseline:** capture point-ingress throughput + allocation before vs after the fenced
   path (the writer's `WaitToWriteAsync`-outside / `TryCommit`-inside per point) under representative
   contention.
5. **Atomic-state barrier test:** a deterministic test proving a snapshot never exposes a new-current
   generation key paired with old-current authority/health.

---

## 8. Stable-intake behaviour change & cross-workstream interaction

- **`GetIntake` is now stable across restarts.** Routes bind once and survive generation swaps — M1
  fixed structurally.
- **M.P2.4 route-rebind cascade becomes redundant** (`RuntimeReloadCoordinator.ComputeSourceRestart
  RouteRebindActions`). With a stable intake, a source restart no longer orphans the route's reader,
  so the cascaded route restart is unnecessary. It is **harmless if left** (it rebuilds the route
  against the same stable reader), so **commit 3 leaves it in place**; removing it is a separate
  cleanup once the cutover is proven. Flag for review.
- **Runtime-reconfigure workstream (Sony):** this commit delivers the stable ingress + generation
  primitive that plan's Layer A depends on. Its replacement-admission may be stricter ("old must
  terminate before replacement") but uses this same gate/slot. Joint confirmation before merge.

---

## 9. Test plan

- **Atomic-state barrier** (§7.5): new-current never paired with old authority/health.
- **Stable-ingress end-to-end:** a route bound to a source survives a source restart and keeps
  receiving points on the same intake (the live M1 proof — extends the commit-2 unit test to the
  routing engine).
- **Default-deny:** a restart whose old generation can't prove quiescence is denied + escalates;
  a clean restart proves quiescence + activates.
- **Retire-before-cancel ordering:** authority revoked before the CTS is cancelled; a late pump write
  is rejected, not enqueued.
- **Init/start failure rollback:** id consumed, zero authorized, partial adapter disposed.
- **Permanent remove:** channel completed, route sees end-of-stream, slot terminal.
- **Per-adapter clean-stop quiescence:** one test per adapter (matrix §7.1), using fakes/demo clients.
- **Leak harness:** run `tests/ElpisEdgeConnect.LeakHarness` — no thread/handle/socket growth across
  many restart cycles (this commit changes teardown ordering).
- **Full gate:** `Core.Tests` + `Host.Tests` + **full** `Management.Tests` + `Integration.Tests`;
  0 warnings / 0 errors.

---

## 10. Risks & rollback

- **Biggest risk:** the cutover touches the live supervisor + every adapter's restart path. Mitigation:
  one atomic commit gated by the full suites + leak harness; the quiescence matrix (§7.1) verified
  per adapter first.
- **Default-deny escalation** changes restart semantics for wedged sources (§5) — intended, but must
  be reviewed and surfaced to operators (a denied restart needs a clear fault + next action).
- **Behaviour parity for the happy path:** a clean add/restart/remove must be externally
  indistinguishable from today except for the stable-intake property. Pin with the existing
  `SourceSupervisorAddRemoveRestartTests` (adjusted only where intake-stability is the intended change).
- **Rollback:** commits 1–2 are inert; if commit 3 regresses, revert it alone — the scaffolding
  remains and the supervisor returns to per-generation channels.

---

## 11. Open questions for review

1. **Quiescence matrix outcomes:** do all six adapters prove clean-stop quiescence today, or do any
   (FOCAS2 especially) need a small `StopAsync` hardening so a *normal* stop is `Proven`? (Inspect
   before coding; this gates whether default-deny is safe out of the box.)
2. **Where the per-generation runtime lives:** `SupervisedGenerationRuntime` as proposed (§2), or
   extend `SourceGeneration` to carry adapter/pump/cts? (v1 leans on a supervisor-side wrapper to keep
   `SourceGeneration` lease-state-only.)
3. **M.P2.4 cascade:** leave in place for commit 3 (recommended) and remove in a follow-up, or remove
   now? (v1: leave; remove later.)
4. **Escalation surface:** reuse the existing `IConfigurationFaultRegistry` + reconcile-fault path for
   a denied replacement, or a new signal? (v1: reuse.)
5. **Subscription adapters:** `CallbackDrain` applicability + how the subscribe loop proves pump
   quiescence (it's an `await foreach`, not a poll) — confirm the OPC UA path.
