# Slice 0 commit 3.1 — attestation proof-matrix + deadline-inputs lock (v2)

**Date:** 2026-06-26
**Supersedes:** `2026-06-26-slice-0-commit-3.1-proof-matrix-v1.md` (+ its review pass).
**Status:** v2 — addresses review P1–P8 and both open questions. **Still no 3.1 code** until v2 is
accepted and the FOCAS2 field measurement (the one blocking input) is recorded.

This is the lock between inert 3.0 (commit `4baa5cd`) and the behaviour-changing atomic supervisor
cutover (3.1). It pins, per retiring **generation**, the single deadline it is observed against, the
**composite** proof that admits a replacement, the source-id permit that gates resourceful init, and the
shared state vocabulary — so the deadline comes from **verified per-adapter proof inputs**, not a guess.

Read with `2026-06-26-slice-0-commit-3-cutover-plan-v3.md` and the 3.0 record
`2026-06-26-slice-0-commit-3-complete-diff.md`.

---

## A. 3.1 lock decisions (behaviour-changing gates)

1. **One absolute monotonic deadline *per retiring generation*** (not global — see §B).
2. **Admission is composite** (not bare `IsFullyProven` — see §C).
3. **Source-id lifecycle permit before resourceful init** (F1 — see §D).
4. **Replacement requires full proven quiescence**; denied replacement is **operator-visible immediately**
   via `SourceLifecycleBlockReason` through management/health.
5. **Expected-key retirement cannot touch a successor** — retirement acts only on its own generation key.
6. **No mixed-generation snapshots** — a snapshot never exposes one generation's current with another's
   runtime/health.
7. **Route cascade removed, exactly one route reader** — the stable-slot intake (commit 2) makes M.P2.4
   route-rebind obsolete; 3.1 deletes it and leaves a single reader.
8. **No global lock held across teardown I/O** — unrelated source lifecycle ops proceed while one source
   quiesces.
9. **Leak-harness multi-hour run is mandatory at 3.1** (no longer deferred — supervisor wiring is live).

---

## B. Per-retirement deadline (P1)

The deadline is computed **for the retiring generation**, from *its* adapter + config — never a
process-wide maximum (that would make every source wait for the slowest configured protocol).

```
deadline(gen) = now_monotonic
              + min( HOST_CAP,
                     MARGIN + max( supervisor_pump_budget,
                                   max over applicable surfaces s of
                                       surface_budget(s, gen.adapter, gen.config) ) )
```

- **`max`, never a sum** of pump + adapter waits — pump and adapter surfaces are observed *concurrently*
  against the one deadline.
- **`HOST_CAP`** is a single safety ceiling (fallback default), explicitly a cap — not the normal value.
- **`MARGIN`** rationale per §F.
- Monotonic clock source: `Stopwatch.GetTimestamp` / `TimeProvider` monotonic — not wall-clock (immune to
  NTP steps). Confirm the injected `TimeProvider` exposes a monotonic reading for the supervisor.
- Deadline expiry does **not** resolve the adapter operation — it records
  `QuiescenceUnprovenAtDeadline` (§G) while the operation is retained for late proof.

---

## C. Composite admission proof (P2)

Replacement admission keys off `Host.Generation.GenerationRetirementCompletion` (the commit-2 scaffold,
currently unused) — **not** `AdapterQuiescenceAttestation.IsFullyProven` alone. Aggregate `Evidence`:
`Active` if any component Active → `Unproven` if any applicable component Unproven → else `Proven`.

A replacement is admitted **iff all of**:

```
1. supervisor pump component  = Proven                 (SetPump — supervisor-owned evidence)
2. adapter retirement operation Completion resolved    (not pending)
3. attestation matches its snapshot:
     every surface declared Applicable in the snapshot is Proven, AND
     every surface declared NotApplicable is NotApplicable in the attestation
     (cross-check — guards against an applicable surface accidentally reported NotApplicable)
4. GenerationRetirementCompletion.Evidence = Proven    (pump + adapterStop + callbackDrain components)
5. no unresolved source-id barrier remains for this source id (§D)
```

**Surface → component mapping (3.1 wiring task to lock):**

| `GenerationRetirementCompletion` component | Source of truth |
|---|---|
| `Pump` | supervisor pump task evidence (NOT an adapter surface) |
| `AdapterStop` | adapter attestation `Worker` ⊕ `BackgroundWork` (both Proven ⇒ Proven; any Unproven ⇒ Unproven) |
| `CallbackDrain` | adapter attestation `CallbackDrain` |

The snapshot cross-check (step 3) is the explicit guard the review asked for: `IsFullyProven` is
necessary but not sufficient — it must agree with the snapshot's applicability so a mis-declared
`NotApplicable` cannot slip through as "not Unproven."

---

## D. F1 — source-id lifecycle permit before resourceful initialization (P3)

```
A source-id-scoped lifecycle permit is acquired BEFORE adapter construction / InitializeAsync can open
a socket, session, fwlib handle, or background worker. The permit survives slot removal and is
revalidated at TryActivate. While a prior generation's retirement is unresolved, the permit is withheld,
so no second resource is opened before replacement is denied.
```

- Capability discovery (`SourceRetirementCapability.TryGet`) and the permit check run on the
  **constructed-but-uninitialized** adapter — valid because construction is non-resourceful (3.0 F1).
- Required deterministic tests:
  - unresolved retirement → adapter factory / `InitializeAsync` is **not** invoked;
  - `Stop → Start` cannot bypass the barrier;
  - `remove → same-id re-add` cannot bypass the barrier;
  - late proof clears the barrier → a fresh attempt is admitted;
  - a denied attempt surfaces `SourceLifecycleBlockReason` immediately (no silent wait).

---

## E. OPC UA — durable-pending callback drain (P4) + pump sequencing (P5)

**Decision (P4): durable-pending by default.** The dispatcher's internal drain budget is **no longer the
normal terminal authority**. Host-deadline expiry records `QuiescenceUnprovenAtDeadline` while the
operation is retained; a late drain still resolves `Proven`. This requires a **3.1 adjustment to the 3.0
dispatcher seam**: `RetireAndDrainAsync` must distinguish "still draining / not yet drained" (→ pending)
from a true terminal condition.

Terminal `CallbackDrain = Unproven` is reserved for genuine adapter-terminal conditions only:
- dispatcher throws / faults;
- queue state lost or unaccountable;
- adapter explicitly determines drain can never be proven.

**NOT** acceptable: a short adapter-internal timeout resolving terminal while late proof is still possible.

**Pump sequencing (P5).** Because `Worker = NotApplicable` (the pump is supervisor-owned), the supervisor
must keep the pump available long enough to account for queued callback work. Locked order:

```
1. retire publish authority (generation fenced — no new publishes)
2. close callback ingress (dispatcher BeginRetiringIngress — OnNotification rejects + records)
3. initiate adapter background cleanup (coordinator detach + dispose)
4. drain OR explicitly account the dispatcher queue (drained items dispatched; any shed items recorded
   as retired-generation history — never silently lost)
5. THEN observe / cancel the pump per the drain model
6. resolve CallbackDrain Proven only after the queue is fully drained or fully accounted
```

If the pump is cancelled before step 4 completes, callback drain can never prove — so step 5 must follow
step 4. Any items the dispatcher drops at shutdown must be counted (received + dropped) into retired-gen
history, consistent with the 3.0 race-counted-as-dropped behaviour.

---

## F. Deadline inputs — verified values (P6)

Effective values from the 3.0 codebase. Each row needs the **enforcement** column confirmed (does the
adapter actually abort the in-flight op at this timeout?) before the deadline is locked.

| Adapter | Effective timeout (default) | Where applied | Enforced on a wedge? | Responsive proof | Max legit in-flight | Margin basis |
|---------|-----------------------------|---------------|----------------------|------------------|---------------------|--------------|
| Modbus TCP | request **1000 ms**, connect **2000 ms** | `ModbusTcpSourceConfiguration` → conn-mgr socket | ⚠ confirm socket `ReceiveTimeout` aborts a hung read | read < 1 s | 2 s (connect on retirement-time reconnect) | 1× request timeout |
| S7 | request **1000 ms**, connect **2000 ms** | `S7SourceConfiguration` → `S7ConnectionManager` | ⚠ confirm Sharp7 honours timeout on blocking read | read < 1 s | 2 s (connect) | 1× request timeout |
| FOCAS2 | **BLOCKED — field-measure** | fwlib handle/EW timeout (`cnc_*`) | ⚠ native call may ignore timeout (the incident) | TBD (measure) | TBD (measure) | TBD after measurement |
| OPC UA | drain budget (1000-batch channel ≈ **1.7 s** absorb) + coordinator dispose | `NotificationDispatcher` + coordinator | drain is durable-pending (§E) — budget is NOT terminal | queued drained < absorb time | ~2 s drain + ~0.1 s dispose | 1× absorb time |
| MTConnect | **10 s** (`HttpClient.Timeout`) | `MTConnectHttpClient.cs:39` `Timeout = FromSeconds(TimeoutSeconds)` — **verified applied** | .NET `HttpClient.Timeout` enforced (socket-wedge fallback → pending) | poll < 10 s | 10 s | 1× HttpClient.Timeout |
| Brother | **10 s** (`HttpClient.Timeout`) | `BrotherHttpHttpApi` ctor (immutable per-CNC) — **verified applied** | as MTConnect | poll < 10 s | 10 s | 1× HttpClient.Timeout |

**Blocking item:** FOCAS2 field measurement (fwlib/controller context + measured healthy max in-flight +
chosen margin) — the deadline lock cannot close without it. Modbus/S7 enforcement-on-wedge and OPC UA
drain-rate are **confirm** items (bench), not blockers, but must be recorded before coding.

---

## G. State-transition vocabulary (P7)

One vocabulary, applied to every adapter row and to `remove → same-id re-add`:

| State | Meaning | Source-id barrier |
|-------|---------|-------------------|
| `AwaitingQuiescence` | operation pending, before deadline | held — no replacement |
| `QuiescenceUnprovenAtDeadline` | host stopped waiting; operation **retained** | held — late proof can still clear it |
| `QuiescenceTerminallyUnproven` | adapter Completion resolved terminal Unproven | held until controlled process restart / explicit operator action |
| `Proven` (incl. **Proven-late**) | all applicable surfaces Proven (possibly after the deadline) | **cleared** — a fresh activation attempt is admitted |

`remove → same-id re-add`: the barrier is keyed by source id, survives slot removal, and a same-id re-add
is admitted only when the prior generation reached `Proven`/`Proven-late`.

---

## H. Per-adapter proof matrix

(Surfaces · retirement op · idle-stop · responsive · max-legit · wedged · late-proof · terminal-unproven
· deadline input. Durable-pending applied uniformly per §E.)

### Modbus TCP / S7 — blocking-socket wire-idle (Worker)
- Op: `{Modbus,S7}Retirement.Begin(lock-free Disconnect, WaitForWireIdleAsync)`.
- Idle → Worker Proven (`RETIRE_WIRE_IDLE`). Responsive: read returns after close → Proven.
- Max-legit: connect 2 s / read 1 s. Wedged: read never returns → **pending**. Late: read returns → Proven-late.
- Terminal: close-init throw → `RETIRE_CLOSE_FAILED`; worker-exit fault → `RETIRE_FAULT`.
- Deadline input: §F (1 s read / 2 s connect; ⚠ enforcement).
- Tests: idle→proven · responsive→proven · wedged→pending · late→proven · close-failed→terminal · worker-fault→terminal.

### FOCAS2 — dedicated fwlib-thread true-exit (Worker) — incident surface
- Op: `Focas2Retirement.Begin(enqueue affine Disconnect + complete queue, WaitForThreadExitAsync)`.
- Idle/responsive → thread exits → Proven (`RETIRE_THREAD_EXITED`). Max-legit: longest fwlib call (measure).
- Wedged: native call hangs → thread never exits → **pending** (now *detected*). Late: returns → Proven-late.
- Terminal: cleanup-init throw OR affine-cleanup fault → `RETIRE_CLEANUP_FAILED`.
- Deadline input: **BLOCKED — field-measure**.
- Tests: idle→proven · responsive→proven · wedged→pending · late→proven · no-work-after-shutdown · cleanup-init-throw→terminal · cleanup-fault→terminal · idempotent.

### OPC UA Client — supervisor pump + callback/background surfaces
- Surfaces: Worker **NotApplicable**; CallbackDrain; BackgroundWork.
- Op: `OpcUaRetirement.Begin(closeIngressFlag, unwireSubscriptions, coordinator detach+dispose, drain)` with **durable-pending** drain (§E) and the §E pump sequencing.
- Idle → drained-empty + coordinator disposed → `RETIRE_PROVEN`. Responsive: queued drained by pump → Proven.
- Max-legit: drain-absorb (~2 s) + dispose. Wedged: pump gone / not drained → **pending** (NOT terminal at budget). Late: drains → Proven-late.
- Terminal (genuine only): dispatcher fault / lost-queue / unaccountable → `CALLBACK_UNDRAINED`; coordinator dispose fault → `BACKGROUND_FAULT`; ingress-flag throw → `RETIRE_FAULT`; non-concrete dispatcher on initialized adapter → fail closed.
- Deadline input: §F drain-absorb + dispose (⚠ confirm drain rate).
- Tests: idle→proven · queued+pump→proven · pump-removed-before-drain→pending(not-terminal) · host-deadline→UnprovenAtDeadline+retained · late-drain→proven · dispatcher-fault→terminal · coordinator-fault→BackgroundWorkFault · non-concrete→fail-closed · shed-items→recorded-not-lost.

### MTConnect / Brother — supervisor-driven pull, in-flight poll (Worker)
- Op: `PullAdapterRetirement.Begin(PollQuiescenceGate.BeginQuiescingAsync)`.
- Idle → gate drains immediately → Proven (`RETIRE_POLL_IDLE`). Responsive: poll completes → ExitPoll → Proven.
- Max-legit: `HttpClient.Timeout` = 10 s (verified applied). Wedged: poll never returns → **pending**. Late: returns → Proven-late.
- Terminal: none from the gate (durable-pending only).
- Deadline input: §F 10 s (verified). Brother: same, layered over the inner single-flight guard (independent lock — no race).
- Tests: idle→proven · responsive→proven · wedged→pending · late→proven · refuse-new-poll-after-retiring · ExitPoll-releases-on-throw · (Brother) single-flight-vs-gate non-interference.

---

## I. Non-adapter supervisor-cutover acceptance tests (P8)

- expected-key retirement cannot touch a successor generation;
- snapshots never expose mixed current-generation / runtime / health;
- **every** activation path honours the unresolved-retirement barrier (no back door);
- route cascade removed → exactly **one** route reader remains;
- denied replacement is visible through management/health **immediately**;
- unrelated source lifecycle ops proceed while one source is quiescing (no global teardown lock);
- no global lock held across teardown I/O;
- **leak-harness multi-hour run** passes with supervisor wiring live.

---

## J. v2 exit criteria (review checklist)

| # | Criterion | v2 status |
|---|-----------|-----------|
| 1 | per-adapter verified deadline values or explicit "blocked pending measurement" | §F — Modbus/S7/OPC UA/MTConnect/Brother recorded; **FOCAS2 blocked** |
| 2 | per-retirement deadline formula | §B ✓ |
| 3 | composite proof evaluator (pump + attestation + snapshot applicability) | §C ✓ |
| 4 | source-id lifecycle permit before resourceful init | §D ✓ |
| 5 | OPC UA durable-pending decision + pump sequencing | §E ✓ |
| 6 | transition semantics (pending / deadline-unproven / terminal-unproven / proven-late) | §G ✓ |
| 7 | non-adapter supervisor cutover tests | §I ✓ |

## K. Open items before 3.1 coding
1. **FOCAS2 field measurement** (blocking) — record measured healthy max in-flight, fwlib/controller
   context, chosen margin. The deadline lock cannot close without this.
2. **Confirm** (bench, non-blocking but required pre-coding): Modbus/S7 socket-timeout enforcement on a
   hung read; OPC UA worst-case drain rate vs. the 1000-batch channel.
3. **Confirm** the monotonic clock source on the supervisor's `TimeProvider` and the `HOST_CAP` + `MARGIN`
   constants.
4. Lock the **surface → `GenerationRetirementCompletion` component** mapping (§C) as the first 3.1 wiring
   step.

After v2 is accepted and item 1 is recorded, 3.1 implementation begins on the implement → focused diff →
finalize cadence.
