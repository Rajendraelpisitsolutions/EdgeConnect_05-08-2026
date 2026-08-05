# Slice 0 — Commit 3: atomic supervisor cutover — Plan v3

**Date:** 2026-06-26
**Status:** v3 — folds in the focused cutover review (C3-1…C3-8) **and the short final check (F1–F3 +
merge gates)**. **Implementation-locked.** Commit v1–v3 as the plan trail, then begin the inert 3.0
series. v1/v2 retained as historical trail. **First behavior-changing commit**; lands atomically (the
cutover, 3.1), preceded by an **inert adapter-quiescence-attestation precursor** (3.0, §0.1). Shared
with the runtime-reconfigure workstream.

---

## 0. What changed from v2 (the corrections)

v2's spine survives (stable `SourceSlot`, lease-fenced writer, revoke-before-cancel, two sink
authorities, atomic cutover). v2's **headline was overstated** and is withdrawn:

> ~~"the matrix confirms default-deny is safe out of the box / all six always Proven / no adapter
> hardening required."~~

Method completion is **not** quiescence proof (a `Thread.Join(10s)` or internal timeout can return
with the worker still alive). v3 replaces that with an **adapter-owned quiescence attestation** and
proof tests, and treats the v2 matrix as *reconnaissance only*. The eight corrections:

| # | Correction |
|---|---|
| C3-1 | Admission guards **every** activation path (not just `Restart`), at the sole activation primitive, with an unresolved-retirement barrier that survives permanent removal (tombstone). |
| C3-2 | Adapter quiescence is an **explicit attestation** (worker + callback-drain + reconnect), not inferred from `StopAsync`/`DisposeAsync` returning. Withdraw the certainty claims. |
| C3-3 | **Initiate adapter cleanup before** awaiting the pump (closing the transport is often what releases the pump); observe all components against **one** deadline. |
| C3-4 | **One absolute monotonic deadline** (not separate pump+adapter 12s waits). Budget covers the worst *verified healthy in-flight* case; lock values only after the 5-scenario proof tests. |
| C3-5 | **One atomic owner of "current"** (identity + runtime + authority + admin snapshot); expected-key retirement so N's late cleanup can't touch N+1. |
| C3-6 | **Remove the M.P2.4 route-rebind cascade in the cutover** (or prove strict single-reader); a stable channel is `SingleReader=true` — two readers split points. |
| C3-7 | Denied replacement is an **operator-visible lifecycle state in commit 3** with a stable reason + retry-on-late-quiescence; validate the fault path (don't reflexively reuse the config-fault registry). |
| C3-8 | **No gateway-wide lock held across teardown I/O** — global lock for membership only; per-slot async lifecycle mutex for prepare/activate/retire. |

### 0.1 Sequencing (keeps the cutover atomic AND the proofs honest)

1. **Commit 3.0 — inert adapter quiescence attestation.** Add the `AdapterRetirementResult` contract
   (§4.2) + per-adapter implementations + the 5-scenario proof tests (§7), **unwired**. Like commits
   1–2, changes nothing live.
2. **Commit 3.1 — the atomic cutover.** Wire stable ingress + fences + reordered retirement + one
   deadline + generalized admission + remove the M.P2.4 cascade, together.

---

## 1. Goal & the one-commit rule

Replace the supervisor's per-generation channel ownership with the stable `SourceSlot`, wiring —
together, atomically (commit 3.1) — stable ingress, the generation-scoped write + health/counter
fences, the (reordered) revoke-before-cancel retirement, one-deadline quiescence, and admission that
guards every activation path. A source restart then swaps the generation while the intake stays put
(M1 fixed). Atomic because partial wiring would let a timed-out old pump write a successor's channel.

---

## 2. Structure — one atomic "current", per-slot locking

- `_slots: ConcurrentDictionary<string, SupervisedSlot>`; the **global lock guards membership only**
  (C3-8).
- **`SourceSlot` owns the single current record** (C3-5): generation identity + runtime payload +
  authorization, installed/cleared under **one** synchronization boundary. v2's separate
  `SupervisedSlot.Current` pointer is dropped — no split-brain. To carry runtime, `SourceGeneration`
  (or the slot's current record) gains the **runtime payload** (`ISourceAdapter`, pump `Task`, CTS,
  `SourceRegistration`); commit 3.1 extends the commit-2 `SourceSlot`/`SourceGeneration` accordingly,
  and retirement becomes **expected-key**: `TryRetireCurrent(expectedGenerationKey, reason)` so a late
  stop for N can't retire N+1.
- **Per-slot async lifecycle mutex** for prepare/activate/retire on that slot. **No lock protecting
  other sources is held while awaiting pump/adapter/native/callback cleanup** (C3-8). Start-failure
  rollback atomically returns the slot to zero current/authorized while retaining the failed runtime in
  retirement history (C3-5).

---

## 3. Lifecycle mapping — admission precedes resourceful initialization (C3-1, F1)

Admission is enforced through a **source-id-scoped lifecycle permit acquired BEFORE adapter
construction / `InitializeAsync`** — because `InitializeAsync` may already open a socket/session or
allocate a native handle, so checking the barrier only at `TryActivate` is too late (a second physical
client could exist before a replacement is denied). Two-stage permit:

1. **Acquire** a source-id-scoped lifecycle permit (an operation epoch).
2. Under the permit, **check the unresolved-retirement barrier**; if blocked, **the adapter factory and
   `InitializeAsync` are never invoked** (fail closed).
3. Build + `InitializeAsync` the candidate (unauthorized).
4. **Revalidate + consume the permit atomically at `TryActivate`** — a stale permit (barrier/epoch
   changed) cannot activate. Released on initialize/start failure.

The **source-id coordinator survives `SourceSlot` removal** — it is process-lifetime, keyed by source
id (alongside the generation allocator / retirement registry) — so a mutex owned only by the old slot
cannot serialize remove → same-id re-add. The per-slot async mutex (§2/C3-8) serializes a *live* slot's
lifecycle; the source-id coordinator serializes across slot incarnations.

| Op | Flow (permit acquired before build/Init for all paths) |
|---|---|
| Add / initial Start | acquire permit → barrier check → `PrepareGeneration` + build + Init (unauthorized) → revalidate+consume at `TryActivate` → Start → pump. |
| Restart | same source id: retire current (§4) → acquire permit → barrier check → prepare+Init → `TryActivate` → Start → pump. Slot+intake persist. |
| Remove (permanent) | retire current (§4) → complete intake terminally → drop slot; **the source-id barrier persists** so a same-id re-add can't bypass it. |
| Stop (shutdown) | retire current (§4); slot not terminal. |

**Barrier rule (locked):** the route-facing intake may be terminally completed after authority is
revoked, but the **orphaned runtime remains owned by the retirement registry and blocks reactivation of
that source id until quiescence is proven** — F2's durable retirement operation can clear it later.

**Required tests:** an unresolved retirement prevents adapter-factory/`InitializeAsync` invocation;
`Stop→Start` and remove→same-id re-add share the same source-id coordinator; a stale permit cannot
activate after the barrier/epoch changes; late proof permits a fresh attempt without process restart.

---

## 4. Retirement: one adapter-owned operation, one deadline, durable (C3-3/C3-4, F2)

### 4.1 One adapter-owned retirement operation (resolves the stop/dispose ordering question)
The host does **not** author generic `StopAsync`+`DisposeAsync` sequencing. Each adapter exposes **one
retirement operation** whose completion stays observable:

```text
AdapterRetirementOperation { Task<AdapterQuiescenceAttestation> Completion;  structured DetailCode }
```

(equivalently a `RetireAsync` task). Semantics:
- calling it **promptly initiates the adapter-defined cleanup** (the adapter knows whether closing the
  transport is what releases its worker — C3-3);
- after the retirement linearization point (gate `TryRetire`, expected-key → **detach** outputs → cancel
  the generation token), the host observes `Completion` **together with the pump** against **one absolute
  monotonic deadline** (not separate per-component waits that could sum to 24s+);
- **deadline expiry → host evidence `UnprovenAtDeadline`, but the operation is RETAINED and observed**;
- if physical quiescence occurs later, `Completion` yields `Proven`, the source-id barrier clears, and a
  subsequent activation is permitted **without process restart**;
- **`Proven` requires BOTH**: every applicable execution surface terminated **AND** every mechanism that
  could create new adapter work disabled.

Deadline budget is derived from the verified adapter cleanup contract + a host cap + margin; **values
locked only after the §7 proof tests** — v3 does **not** hard-code 12s.

### 4.2 Attestation covers every execution surface (C3-2)
`AdapterQuiescenceAttestation` covers **every registered surface**, not just worker + callback-drain:
add a `BackgroundWork/Reconnect` component (timers, reconnect loops, dispatchers) — a fixed component or
a typed component collection. **Missing required components fail closed** (`Unproven`). `DetailCode` is
**stable/structured**, never free text. A method returning after an *internal* timeout (e.g. FOCAS
`Thread.Join(10s)`) is **not** `Proven`. An adapter **without** the attestation capability **fails
closed with the dedicated lifecycle-block reason** — never a method-completion fallback (merge gate 5).

### 4.3 Minimal generation-keyed history retained in commit 3
The retirement observer consumes late completion/fault and records minimal generation-keyed history now
(the late `Proven` transition is exactly this signal); full snapshots/accounting land in commit 4.
Registry transitions idempotent; active counts cannot underflow; every abandoned task's exception is
observed (§9).

---

## 5. Replacement admission — strict, dedicated contract, operator-visible (C3-7, F3)

- **Rationale (correct):** the gate enforces at most one *publish-authorized* generation; admission
  adds the **stricter physical-resource-overlap** policy.
- **Commit 3.1 policy (LOCKED): full proven quiescence.** No adapter overlap capability is enabled in
  this cutover; a future explicit capability can relax it in a separate change. This aligns the
  reconfigure workstream with the shared invariant without a premature capability branch.
- **Dedicated runtime lifecycle contract (locked NOW)** — distinct from configuration faults, adapter/
  device errors, and the later Slice A liveness reason fields:
  - `SourceLifecycleState` + stable `SourceLifecycleBlockReason`;
  - a structured `ReplacementAdmissionResult` (supervisor + management);
  - a cached admin snapshot **exposed immediately** to management/health — no green-but-silent source.
- **Retry policy:** retry **only on new evidence** (a late `Proven`) or an explicit operator lifecycle
  request — **no time-based auto-retry loop**, **no force-overlap override** in Slice 0.
- **Terminal:** if proof never arrives, the operator action is a **controlled gateway-process restart**.
- **Do NOT reuse `IConfigurationFaultRegistry`** — the dedicated lifecycle contract above replaces it
  (the config registry stays reserved for configuration intent).

---

## 6. Two sink paths + no-notification-under-gate (additional gate)

- **Generation-scoped sink (fenced):** observations/errors reach the collector only via
  `gate.TryCommit(lease, …)`; the body performs **only bounded in-memory mutation** — **audit
  `RuntimeDiagnosticsCollector` methods**; if any log, publish events, or invoke subscribers
  synchronously, split mutation from notification so notifications happen **after releasing both
  locks**.
- **Slot-administrative sink (supervisor-only):** lifecycle transitions, never adapter-accessible.

---

## 7. Adapter quiescence — reconnaissance + proof gate (C3-2, C3-4)

The v2 table is **reconnaissance** of teardown shape, **not** proof. Per adapter, commit 3.0 ships the
attestation (§4.2) + a proof matrix covering **five scenarios**: idle stop; stop during a *responsive*
in-flight op; stop near the *maximum legitimate* op duration; *deliberately wedged* op; callback/
reconnect activity (where applicable). A fake that returns quickly is **not** proof:

- **FOCAS2:** fwlib worker shim + assert the dedicated thread actually terminated (not just `Join`
  returned).
- **S7 / Modbus TCP:** injectable blocking transport that wedges a read; assert `Worker=Unproven` on
  wedge, `Proven` on responsive stop.
- **OPC UA Client:** real dispatcher/callback-drain seam; **`CallbackDrain` is applicable** and must be
  explicitly `Proven`/`Unproven` (resolves the v2 open question).
- **MTConnect / Brother HTTP:** async-native; `Worker=Proven` on cancellation; `CallbackDrain=NotApplicable`.

Reconnaissance still informs the budget (§4.1): the blocking adapters' worst *healthy* in-flight bound
is the device read timeout, which the deadline must cover.

---

## 8. Stable ingress, route cascade removal, Sony coordination

- **`GetIntake` stable across restarts** — routes bind once, survive swaps (M1 fixed).
- **Remove the M.P2.4 source-restart route cascade in the cutover (C3-6).** The stable channel is
  `SingleReader=true`; two overlapping route readers would split points. v3 **removes/disables**
  `RuntimeReloadCoordinator.ComputeSourceRestartRouteRebindActions` in commit 3.1, plus an **end-to-end
  barrier test** proving exactly one route reader is active at all times and route-local buffer/
  delivery state is preserved across a source restart. ("Rebuilds against the same reader" is not
  accepted as evidence.)
- **Runtime-reconfigure workstream (Sony):** commit 3.1 delivers the stable ingress + generation
  primitive its Layer A consumes; joint confirmation of the admission policy before merge.

---

## 9. Retired-runtime ownership (additional gate)

A quarantined/orphaned runtime **retains** its adapter, CTS, worker/pump tasks, and observation
continuations until proven completion or process exit. Every task exception is observed. Registry
transitions are idempotent; active counts never underflow.

---

## 9a. Merge gates for commit 3.1 (do not block starting 3.0)

1. **3.0 stays behaviorally inert.** The new retirement operations, completion signals, seams, and the
   source-id coordinator are **unreachable from the live supervisor**; existing `StopAsync`/`DisposeAsync`
   behaviour is unchanged until the atomic cutover.
2. **Ingress perf/allocation baseline restored.** Every accepted point traverses the generation gate —
   capture representative throughput + allocation before vs after 3.1.
3. **One current aggregate.** The slot owns a single **`SourceGenerationRuntime`** record (identity,
   adapter, CTS, writer, startup phase, pump completion, admin snapshot). Extending `SourceGeneration`
   is acceptable only if these can never be observed as **mixed generations**.
4. **Partial-lifecycle retirement.** Adapter retirement is idempotent and tested from **constructed,
   initialized, starting, running, and start-failed** phases.
5. **Unsupported adapter fails closed.** Any adapter lacking the attestation capability produces the
   dedicated `SourceLifecycleBlockReason` — it must NOT fall back to method-completion inference.

## 10. Test plan (superset of v2; from the review)

- All activation paths honour the unresolved-retirement barrier (Stop→Start, reconcile/Add,
  remove→same-id re-add cannot bypass).
- Late proof permits a later retry without process restart.
- Expected-key retirement cannot touch a successor.
- Activation / current-runtime / health snapshots never expose mixed generations (atomic barrier).
- Adapter stop is **initiated before** awaiting pump completion where the contract requires it.
- One absolute deadline is enforced (no 24s summation).
- Concrete FOCAS/S7/Modbus **wedge** tests produce `Unproven`, not false `Proven`.
- OPC UA callback drain is explicitly `Proven`/`Unproven`.
- Unrelated source lifecycle operations proceed while one source is quiescing (per-slot lock).
- Stable ingress + reconfigure: exactly one route reader, no point split (M.P2.4 removed).
- Denied replacement is immediately visible in management/health.
- Late task faults observed + retained in generation history.
- **Full Core, Host, Management, Integration, and leak-harness gates green; 0/0.**

---

## 11. Risks & rollback

- **Scope grew:** commit 3 now includes adapter attestation + proofs (3.0) and the live cutover (3.1).
  Mitigation: 3.0 is inert (revertible), 3.1 is the single behavior-changing commit gated by §10.
- **Quiescence budget mis-tuned** → mis-deny a healthy in-flight stop. Mitigation: budget from the
  5-scenario proofs, not a guessed 12s.
- **Default-deny escalation** changes wedged-source restart semantics → operator-visible state (§5).
- **Rollback:** revert 3.1 (then 3.0) to return to per-generation channels; commits 1–2 stay inert.

---

## 12. Resolved by the final check (F1–F3) + remaining inputs

**Resolved (folded into v3):**
- One **adapter-owned retirement operation** (not host-sequenced stop/dispose) — §4.1 (was Q1).
- **Dedicated `SourceLifecycle*` fault path** (not the config registry) — §5 (was Q3).
- **Full proven quiescence; no overlap capability** in 3.1 — §5 (was Q5; also reconciles the
  reconfigure-workstream strictness with the shared invariant).
- **One `SourceGenerationRuntime` aggregate** as the slot's current record — §2 / gate 3 (was Q2).
- **Admission precedes resourceful init** via the source-id permit — §3 (F1).

**Remaining inputs (locked during 3.0, not blocking the start):**
- Per-adapter deadline values + the host cap/margin formula — locked after the §7 5-scenario proofs.
- Per-adapter retirement-operation shape (sequential vs concurrent cleanup) — defined by each adapter
  in 3.0, **starting with one representative blocking adapter (Modbus or S7)** + its responsive/wedged
  tests, before replicating the pattern.
