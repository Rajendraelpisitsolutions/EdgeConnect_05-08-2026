# Slice 0 — Commit 3: atomic supervisor cutover — Plan v2

**Date:** 2026-06-26
**Status:** v2 — folds in the **adapter clean-stop quiescence matrix** (§7.1, recon done) and the
refinements it implies. Design candidate for review (you + ChatGPT) **before any code**. v1 retained
as historical draft. **First behavior-changing commit**; per B8 it lands as **one atomic commit**.
**Builds on:** commits 1–2. **Shared with** the runtime-reconfigure workstream (delivers its stable
ingress).

---

## 1. Goal & the one-commit rule

Replace the supervisor's per-generation channel ownership with the stable `SourceSlot`, wiring —
**together, atomically** — stable ingress, the generation-scoped write + health/counter fences, the
revoke-before-cancel retirement order, and default-deny replacement admission. After this commit a
source restart swaps the *generation* while the *intake stays put*, so a bound route never goes stale
(the structural M1 fix goes live). Atomic because wiring stable ingress *before* the fences would let
a timed-out old pump write into a successor's channel — a regression vs `master`.

---

## 2. Current vs target structure (`SourceSupervisor`)

**Today:** `_supervised: ConcurrentDictionary<string, SupervisedSource>`;
`SupervisedSource { Registration, Channel, Intake, PumpTask, Cts }` — channel + intake **recreated per
`AddAsync`** (the M1 hazard); `StopInternal` cancels + completes-writer + bounded pump await
(**abandon on timeout, no fence** — the C5 finding) + `StopAsync` + `DisposeAsync`.

**Target:** `_slots: ConcurrentDictionary<string, SupervisedSlot>` where
`SupervisedSlot { SourceSlot Slot; SupervisedGenerationRuntime? Current }`. The **`SourceSlot` is
stable** (channel + `Intake` + gate across generations); `SupervisedGenerationRuntime { SourceRegistration
Registration; ISourceAdapter Adapter; SourceGeneration Generation; CancellationTokenSource Cts; Task
PumpTask }` is **per generation** (rebuilt on restart). The pump writes via `Generation.Writer.WriteAsync`
(lease-fenced). `GetIntake` returns the **stable** `slot.Slot.Intake`.

---

## 3. Lifecycle mapping (supervisor op → slot ops)

| Supervisor op | New flow |
|---|---|
| **Add / initial Start** | get-or-create `SourceSlot` → `PrepareGeneration` → build adapter + `InitializeAsync` **while unauthorized** → `TryActivate` (atomic current+authorize) → `StartAsync` → launch pump. Init failure → `AbandonPrepared`; start failure → `RetireCurrent` + dispose. Id consumed either way; zero authorized on failure. |
| **Restart** (same id) | same slot: retire current (§4) + prove quiescence + **default-deny check** (§5) → `PrepareGeneration` → Init → `TryActivate` → Start → pump. **Slot + intake persist → routes do not rebind.** |
| **Remove** (permanent) | retire current (§4) + prove quiescence → `CompleteIntakeForPermanentRemoval` (terminal; completes channel) → drop slot (allocator tombstone survives). |
| **Stop** (shutdown) | retire current (§4) + bounded quiesce; slot not marked terminal. |

`_lifecycleGate` still serializes slot mutations; the slot gate is the finer publish boundary.

---

## 4. Retire-before-cancel + quiescence wiring (C5 fix)

Teardown for a generation:
1. `slot.RetireCurrent(reason)` — gate `TryRetire` (revoke authority, linearization point) **then**
   `Writer.Detach()`.
2. Cancel the per-generation CTS.
3. Await the pump task bounded → `RetirementCompletion.SetPump(Proven)` on completion, `Unproven` on
   timeout.
4. `Adapter.StopAsync` + `DisposeAsync` bounded → `SetAdapterStop(Proven)` on completion, `Unproven`
   on timeout/throw.
5. Evaluate `Evidence`. Not `Proven` → `RetiredGenerationRegistry.Quarantine` (→ `MarkOrphaned` past an
   orphan budget); the abandoned pump/adapter is observed, never awaited forever.

The pump, on `IntakeWriteOutcome.RejectedRetired`/`ChannelClosed`, exits cleanly — no race to write a
successor's channel.

**Quiescence deadline (locked by the matrix, §7.1):** the bounded waits in steps 3–4 must allow each
adapter's *clean-stop* teardown to finish, so a healthy reconfigure is never mis-classified `Unproven`.
The longest clean-stop bound is **FOCAS2's 10 s thread-join** (`Focas2Thread.cs:131`). v2 sets the
per-generation quiescence deadline to **12 s** (FOCAS 10 s + margin), tunable. A clean stop completes
well inside this for all six adapters; only a wedged device exceeds it.

---

## 5. Default-deny replacement admission — confirmed safe (§7.1)

On **Restart**, a new generation is activated only if the old generation's `Evidence == Proven` (no
adapter opts into overlap yet). Otherwise the supervisor returns a structured **escalation** (reconcile
fault via `IConfigurationFaultRegistry`) and leaves the slot with **zero** authorized generations —
never two.

**The matrix (§7.1) confirms this is safe out of the box:** on a *clean* reconfigure (adapter idle,
device responsive) all six adapters quiesce within the 12 s deadline → replacement admitted seamlessly.
Quiescence only fails to prove when a **device call is wedged mid-flight** (FOCAS native P/Invoke / S7
Sharp7 read / Modbus FluentModbus read — synchronous, uncancellable) — exactly the case where starting
a second generation would be wrong. There, the restart is **denied + escalated** (operator / Slice C
recovery), not silently double-run.

**Intended behaviour change (flag):** today a Restart abandons a hung `StopAsync` after a timeout and
starts the new generation anyway (risking two live generations); after this commit a non-quiescent old
generation **withholds** the new one. Seamless for healthy sources; safe-by-refusal for wedged ones.

---

## 6. The two sink paths (B5) — introduced together in this commit

- **Generation-scoped sink (fenced):** per-point observations + per-generation errors reach
  `RuntimeDiagnosticsCollector` **only through `gate.TryCommit(lease, …)`** — a retired generation's
  late observation is dropped at the supervisor boundary (collector needs no generation-keying until
  commit 4). The `TryCommit` body does **only** the forward — no logging/events/notification under the
  gate (§7.4).
- **Slot-administrative sink (supervisor-only):** lifecycle transitions (`Running`/`Stopping`/
  `Stopped`/faulted) go straight to the collector via a supervisor-owned path adapters never receive.

---

## 7. Merge gates (v2 §12a)

### 7.1 Built-in adapter clean-stop quiescence matrix — **DONE** (recon 2026-06-26)

| Adapter | Teardown nature | Blocking call (wedge risk) | Clean-stop verdict |
|---|---|---|---|
| **MTConnect** | async HTTP; `StopAsync` is a state transition, `Dispose` closes `HttpClient` | none (HttpClient honours CT) | **Always Proven** (<100 ms) |
| **Brother HTTP** | async HTTP; `DisposeAsync` is a no-op (HttpClientFactory-owned) | none (HttpClient honours CT) | **Always Proven** (<1 ms) |
| **OPC UA Client** | async; dispatcher drain + `subscription.Delete` + `session.CloseAsync(ct)`; 5 s dispose safety net | none (UA stack async-native) | **Always Proven** (~1–2 s) |
| **FOCAS2** | `Disconnect`/`FreeLibHandle` on the dedicated thread; `Focas2Thread.DisposeAsync` `Join(10 s)` | native G342 P/Invoke on the dedicated thread (uncancellable) | **Proven on clean stop**; `Unproven` only if a native call is wedged (join waits to 10 s) |
| **Siemens S7** | `Disconnect` + dispose; `DisposeAsync` 5 s timeout | Sharp7 synchronous `ReadArea` wrapped in `Task.Run` (uncancellable) | **Proven on clean stop**; `Unproven` only if a read is wedged on a slow/dead PLC |
| **Modbus TCP** | `Disconnect` (socket close) + dispose | FluentModbus synchronous read wrapped in `Task.Run`; blocks up to `RequestTimeoutMs` | **Proven on clean stop**; `Unproven` only if a read is wedged on a slow/dead slave |

**Conclusion:** on a normal/clean reconfigure **all six quiesce within the 12 s deadline** → default-deny
admits the replacement. The three polling adapters (FOCAS2/S7/Modbus) fail to quiesce **only** in the
wedge case (an uncancellable device call mid-flight) → correctly denied. No adapter is slow on a *clean*
stop. **No per-adapter `StopAsync` hardening is required for commit 3**; the FOCAS2 hang-watchdog that
turns a wedge into auto-recovery remains Slice C.

> Per-adapter clean-stop quiescence tests (one each, using fakes/demo clients) are part of §9.

### 7.2 Late-history path ownership
Rejected-late counters / terminal history written only by the trusted Host generation-scoped sink /
`RetiredGenerationRegistry`; adapters get no post-retirement mutation path.

### 7.3 (was 7.4) No notification under the gate
Collector mutation may run gate→collector; logging, events, paging, subscriber callbacks happen **after**
releasing both locks.

### 7.4 Performance baseline
Point-ingress throughput + allocation before vs after the fenced per-point path, under representative
contention.

### 7.5 Atomic-state barrier test
Deterministic test proving a snapshot never exposes a new-current key paired with old-current
authority/health.

---

## 8. Stable-intake behaviour change & cross-workstream interaction

- **`GetIntake` is now stable across restarts** — routes bind once and survive generation swaps (M1
  fixed structurally).
- **M.P2.4 route-rebind cascade becomes redundant** (`RuntimeReloadCoordinator.ComputeSourceRestart
  RouteRebindActions`). Harmless if left (it rebuilds the route against the same stable reader), so
  **commit 3 leaves it**; removing it is a follow-up once the cutover is proven.
- **Runtime-reconfigure workstream (Sony):** this commit delivers the stable ingress + generation
  primitive its Layer A depends on. Its admission may be stricter ("old must terminate before
  replacement") but uses this same gate/slot. Joint confirmation before merge.

---

## 9. Test plan

- **Atomic-state barrier** (§7.5).
- **Stable-ingress end-to-end:** a route bound to a source survives a source restart and keeps
  receiving points on the same intake (live M1 proof, into the routing engine).
- **Default-deny:** clean restart proves quiescence + activates; a restart whose old generation can't
  prove quiescence is denied + escalates.
- **Retire-before-cancel:** authority revoked before CTS cancel; a late pump write is rejected, not
  enqueued.
- **Init/start failure rollback:** id consumed, zero authorized, partial adapter disposed.
- **Permanent remove:** channel completed, route sees end-of-stream, slot terminal.
- **Per-adapter clean-stop quiescence:** one test per adapter (matrix §7.1) via fakes/demo clients —
  asserts `Evidence == Proven` within the deadline on a clean stop.
- **Leak harness:** `tests/ElpisEdgeConnect.LeakHarness` — no thread/handle/socket growth across many
  restart cycles (teardown ordering changed).
- **Full gate:** `Core.Tests` + `Host.Tests` + **full** `Management.Tests` + `Integration.Tests`;
  0 warnings / 0 errors.

---

## 10. Risks & rollback

- **Touches the live supervisor + every adapter restart path.** Mitigation: one atomic commit gated by
  full suites + leak harness; the §7.1 matrix verified (no hardening needed).
- **Quiescence-deadline mis-tuning** could mis-deny a clean reconfigure. Mitigation: deadline ≥ the
  longest clean-stop bound (FOCAS 10 s); v2 uses 12 s; covered by the per-adapter clean-stop tests.
- **Default-deny escalation** changes wedged-source restart semantics (§5) — intended; needs a clear
  operator fault + next action on a denied restart.
- **Happy-path parity:** clean add/restart/remove must be externally indistinguishable from today
  except the stable-intake property; pin with `SourceSupervisorAddRemoveRestartTests` (adjusted only
  where intake-stability is the intended change).
- **Rollback:** commits 1–2 are inert; revert commit 3 alone to return to per-generation channels.

---

## 11. Open questions for review (Q1 resolved by §7.1)

1. ~~Do all six adapters prove clean-stop quiescence?~~ **Resolved (§7.1): yes, within a 12 s deadline;
   no `StopAsync` hardening needed.** Confirm the 12 s value (vs reusing the existing 10 s ceiling with
   FOCAS's join nested inside).
2. **Per-generation runtime home:** `SupervisedGenerationRuntime` wrapper (v2 §2) vs extending
   `SourceGeneration` to carry adapter/pump/cts. (v2: wrapper, keeping `SourceGeneration` lease-state-only.)
3. **M.P2.4 cascade:** leave for commit 3 (recommended), remove in a follow-up.
4. **Escalation surface:** reuse `IConfigurationFaultRegistry` + reconcile-fault for a denied
   replacement (v2: reuse).
5. **Subscription pump quiescence (OPC UA):** the pump is an `await foreach`; confirm `CallbackDrain`
   applicability + how its completion proves `SetPump(Proven)` (dispatcher `StopAsync` drain, per §7.1).
