# Slice 0 — Source-generation foundation — Implementation plan v2

**Date:** 2026-06-25
**Status:** v2 — folds in the v1 review (B1–B8) **and the focused gate/cutover review (G1–G5)**.
**Go: commit this plan and begin commit 1**, presenting the gate API + deterministic tests as a focused
diff before finalizing that code commit. No v3 plan needed. **Supersedes v1 for implementation;**
v1 retained as historical draft.
**Contract:** `2026-06-25-source-generation-foundation-slice-0-spec.md`. **Shared with** the
runtime-reconfigure workstream (one primitive, joint review).
**Scope unchanged:** lifecycle correctness only — generation identity, unforgeable lease, gate-
linearized commit fencing, stable ingress, retire-before-cancel, quiescence-gated replacement,
orphan accounting. **No** poll timeout, recovery, liveness verdicts, bundle, or UI.

---

## 0. Approved architecture (unchanged from v1)

The slot/generation split stands and is confirmed: `SourceSlot` owns the **stable** route-facing
ingress; `SourceGeneration` owns one adapter execution lifetime; stop/reconfigure retires a generation
without replacing the intake; retirement revokes authority before cancellation; late work is fenced
from data/health/error/counter commits; Slice 0 stays lifecycle-only. Keep the stable-ingress
regression, deterministic barrier-based race tests, full cross-project gate, and standalone delivery.

The rest of this doc locks the concurrency and lifecycle details v1 left implicit.

---

## 1. Gate is the linearization boundary (B1) — no check-then-act

Authorization is **never** a readable property used to guard a write. The slot gate owns a synchronous
commit so validation and side effect share one linearization point.

**`SourceSlotGate` API (Host) — leases are one-shot, gate-owned capabilities (G1):**
- Leases are **gate-minted** via an internal constructor carrying an **opaque gate capability** — not
  forgeable from a `GenerationKey`, and bound to the minting gate.
- **Lease state machine:** `Issued -> Authorized -> Retired`. **No** transition out of `Retired`;
  **no** lease may be authorized twice.
- `TryAuthorize(lease)` **rejects** (structured outcome, below): a lease minted by **another gate**;
  a copied/synthetic key lacking the gate capability; a lease **already Authorized or Retired**; a key
  violating the slot/runtime identity or the **monotonic allocator high-water mark**.
- `TryRetire(expectedLease, reason)` retires **only** the expected generation — a late `Stop` for N
  cannot retire successor N+1; **idempotent** on an already-retired expected lease.
- **Structured results, not bare bools:** `GenerationAuthorizationOutcome` /
  `GenerationRetirementOutcome` distinguish `Ok | WrongGate | NotCurrent | AlreadyRetired |
  AuthorizationConflict | AllocatorOverflow` so tests, history, and logs stay precise. **Generation-id
  overflow fails closed** (no wrap).
- **`TryCommit` (G2) — one gate critical section; NOT an `Action`+captured-result.** Validation + the
  synchronous side effect occur in one gate scope, implemented as either (1) an internal,
  result-bearing, allocation-conscious commit (static delegate + state; no per-call closure) or
  (2) an internal **stack-confined authorization scope that cannot cross an `await`**. Locked rules:
  Host-internal only (adapters never receive the gate); **no `await`, blocking I/O, logging, notifier,
  or external callback under the gate**; **no gate transition re-entered** from a commit body; a
  commit-body exception **releases the lock without altering authorization**; the result
  **distinguishes "lease rejected" from "authorized but `TryWrite` returned false."** Capture a
  throughput/allocation baseline before commit 3 (every accepted point traverses this path).
- `IsPublishAuthorized` is **diagnostics-only** and must never gate a write/mutation.
- The slot's `CurrentGeneration` reference and the gate authorization flip **atomically under one slot
  synchronization boundary** (the activation transaction, §6).

---

## 2. Bounded-channel write algorithm + cutover (B2)

The gate is **not** held across an `await`. The stable channel is created with **`SingleWriter = false`**
**and `AllowSynchronousContinuations = false`** (G5 — otherwise a writer could execute reader
continuation work while the gate is held). Publish authority is enforced by the **gate**, not channel
options (successive generations, permitted future overlap, pending old writes, and concurrent
subscription callbacks make single-writer an unsafe lifetime promise).

**`GenerationScopedIntakeWriter.WriteAsync(point)` — distinct outcomes (G5); gate never held across `await`:**
1. `await channel.Writer.WaitToWriteAsync(generationCt)`:
   - **false** → stable channel **permanently closed** (terminal).
   - cancellation **after retirement** → **retired/rejected**, *not* a current-generation failure.
2. Reacquire the gate, `TryCommit` a synchronous `channel.Writer.TryWrite(point)`:
   - **gate rejection** (lease retired / not current) → **rejected-late**.
   - authorized but `TryWrite` **false** (capacity race) → **retry from step 1** (await again —
     **never a synchronous spin**).
   - `TryWrite` **true** → **committed**; may drain after retirement.

**Detach() (G3):** the linearization point for losing authority is the **successful
`TryRetire(expectedLease)`**, *not* `Detach()`. `Detach()` is an **idempotent atomic exchange** that
clears the inner writer reference (release-promptness); it **must not** revoke authority itself, and
**must not complete** the stable slot channel.

**Retirement sequence (G3):** (1) `TryRetire(expectedLease)` succeeds and revokes publish authority →
(2) idempotent `Detach()` clears the writer reference → (3) cancel the generation/retirement token →
(4) begin bounded quiescence observation. A writer that already passed `WaitToWriteAsync` still enters
the gate and is rejected after step 1; a writer still waiting is released by cancellation/channel
completion.

**Locked cutover rule (review §3.2):**
> A point **successfully enqueued** to the stable ingress before retirement remains valid and may
> drain afterward. Work still **waiting** in a pending write, or **arriving after** retirement, is
> rejected.

(The commit point is the successful `TryWrite`. This rule does **not** require generation envelopes /
a filtering reader — if a future requirement demands "drop already-enqueued retired items at
dequeue," that needs envelopes + a multiplexing reader and is out of Slice 0 scope.)

---

## 3. Full-generation quiescence, not `PumpTask` (B3)

`PumpTask.IsCompleted` is **not** proof the native/socket worker stopped — the FOCAS class is the
counter-example. Replacement admission keys off a composite contract:

**`GenerationRetirementCompletion`** aggregates:
- supervisor pump completion;
- adapter `StopAsync`/`DisposeAsync` completion;
- callback/work-drain completion where applicable.

It yields `QuiescenceEvidence = Proven | Unproven | Active`. **Default: replacement is denied unless
quiescence is `Proven`.** No adapter gains overlap by default; an adapter capability may later prove
safe overlap (out of Slice 0). `RetiredTaskObserver` observes the `GenerationRetirementCompletion`
(not the raw `PumpTask`); a late completion **decrements the active-orphan count** but **does not
erase** the cumulative lifetime orphan event.

---

## 4. Generation identity across permanent remove/re-add (B4)

The key `(RuntimeInstanceId, SourceSlotId, GenerationId)` collides if a slot is removed and re-added in
the same process with the counter restarting at 1. **Fix:** a **runtime-lifetime generation allocator
(tombstone) per source id** that survives live-slot removal, so the next generation id for a re-added
id never reuses a prior key. Add the `remove → re-add same id → next generation id` test.

**Locked lifecycle contract:**
- `Stop`, `Restart`, same-id reconfigure → **retain** the slot + intake.
- **Permanent deletion** → retire the generation, **complete the intake**, remove the live slot (the
  tombstone/allocator survives).
- A true **re-add after permanent deletion** creates a **new intake**; any surviving route must be
  **rebuilt**, not left bound to the completed reader.
- "Per-source lifetime counters survive" = **across generation replacement within the stable slot**,
  not across permanent deletion (unless a product requirement says otherwise).

---

## 5. Two commit paths: generation vs supervisor-administrative (B5)

Not every state update is generation-originated. After revoking a lease, the supervisor must still
record authoritative slot state (`Stopping`, `Stopped`, removal, replacement-denied).

- **Generation-scoped sink** — adapter observations/errors/counters commit **only** through the lease
  gate (`TryCommit`). Adapters receive **only** this.
- **Slot-administrative sink** — used **only** by `SourceSupervisor` for lifecycle transitions +
  terminal history. Adapters never get this path.
- A **fixed lock order** between the slot gate and `RuntimeDiagnosticsCollector` (gate → collector).
  Validating under the collector's lock alone does **not** linearize against gate retirement.

---

## 6. Activation & rollback (B6)

**Locked sequence:** construct unauthorized → `InitializeAsync` (contract: no data acquisition) →
**`SourceSlot.ActivateGeneration(...)`** (the *single* activation transaction, G4) → `StartAsync`
(subscription callbacks may now fire, authorized) → launch pump. Chosen because it (a) never authorizes
a generation whose `Initialize` failed and (b) covers adapters that start callbacks during `StartAsync`
(authority is in place first).

**`ActivateGeneration` is one atomic slot transaction (G4)** installing, together: the
current-generation reference/key; a reset of per-generation current diagnostics; the generation-scoped
sinks/writer; the lease authorization; and the externally-visible immutable slot snapshot. It is the
**sole** activation path. **No observer may see a new-current generation paired with the old
generation's authorization or current-health state.** A temporary **zero-authorized** state during
replacement is valid; a **mixed** state is not.

**Startup edge case (G4):** data successfully committed by an authorized callback during `StartAsync`
**remains valid even if `StartAsync` subsequently fails**, unless the product later chooses a stricter
staging rule.

**Failure behavior (locked):**
- Generation ids are **consumed even when startup fails** (allocator advances).
- A failed `Initialize`/`Start` leaves **zero publish-authorized generations** (`TryRetire` rollback).
- Partial adapter resources are stopped/disposed and recorded in **generation history**.
- No failed generation may remain current merely because authorization happened first.

---

## 7. Core/Host boundary & policy inputs (B7)

- **Core** (`ElpisEdgeConnect.Core`): identity/value types (`RuntimeInstanceId`, `GenerationId`,
  `GenerationKey`) and externally-consumed snapshot/result **DTOs** (`GenerationSnapshot`,
  `SourceLifetimeSnapshot`, `ReplacementAdmissionResult`, `GenerationReplacementContext`).
- **Host runtime** (`ElpisEdgeConnect.Host`): `SourceSlotGate`, `GenerationLease` impl, `SourceSlot`,
  `SourceGeneration`, generation-scoped + slot-administrative sinks, `RetiredTaskObserver`, and the
  replacement policy.
- **`IGenerationReplacementPolicy`** takes an **immutable `GenerationReplacementContext`** (pure
  snapshots + quiescence evidence + adapter capability + process/source budgets) — **not** live
  `SourceSlot`/`SourceGeneration` handles.
- Management/reconfigure code calls **one supervisor operation** and consumes a structured
  `ReplacementAdmissionResult`; it never reaches into the slot or gate directly.

---

## 8. Counter/state model (B5 + review §4) — two orthogonal fields, one owner each

Use **two orthogonal fields**, not one enum: **`AuthorityState` {Authorized, Retired}** and
**`RetirementState` {None, Quarantined, Orphaned, Completed}**. One authoritative owner per counter;
cached diagnostics **mirror** snapshots, never maintain duplicate truth.

| State / counter | Owner | Reset / survival |
|---|---|---|
| current lifecycle + current error | stable slot / supervisor (admin sink) | reset for a newly authorized generation |
| accepted points/polls | generation-scoped commit path | count only commits authorized at their linearization point; aggregate to slot lifetime |
| rejected late points/callbacks | retired-generation history path | recorded against originating generation; may aggregate to a separate lifetime rejected total |
| generation starts / retirements / start-failures | supervisor | survive generation replacement for slot lifetime |
| cleanup-timeout / quarantine / orphan **total** | retirement registry | cumulative for slot/process lifetime |
| **active** quarantined/orphaned work | retirement registry | increments on transition, decrements on proven completion |
| late completion/fault | generation history | never overwrites current slot state |

---

## 9. Revised commit series (B8) — no intermediate commit creates a worse runtime

v1's "stable ingress before fencing" (old commit 2) would let a timed-abandoned old task write into the
channel its successor uses — **a regression** vs today's orphaned channel. So commits 1–2 are
**behaviorally inactive** (new types unreachable), then a **single atomic supervisor cutover**:

1. **Identity + gate foundation (unused).** `RuntimeInstanceId`, runtime-lifetime generation
   allocator/tombstone, unforgeable gate-minted `GenerationLease`, `TryAuthorize` /
   `TryRetire(expectedLease)` / `TryCommit` synchronous primitive. Deterministic unit tests
   (gate concurrency, unforgeable lease, expected-lease retire, N can't retire N+1).
2. **Host scaffolding (unused).** `SourceSlot`/`SourceGeneration` model, `GenerationScopedIntakeWriter`
   (with the §2 algorithm), generation-scoped + admin sinks, `GenerationRetirementCompletion` /
   quiescence contract, `RetiredTaskObserver` types. Not yet wired into the live supervisor.
3. **Atomic supervisor cutover.** Switch `SourceSupervisor` to stable ingress **together with** all
   data/health/error/counter fences, **revoke-before-cancel** retirement ordering, and **default-deny**
   replacement admission — in one commit. After this, no old task can write into a successor's channel.
4. **Diagnostics / history / accounting.** Two-tier snapshots, active + cumulative orphan accounting,
   bounded history (generation-keyed).
5. **Reconfigure + permanent-remove integration.** Same supervisor primitive consumed by the
   reconfigure entrypoint; stable-ingress tests; delete/re-add semantics (§4); **full-suite gate**.

**Hard rule:** commits 1–2 must not change active supervisor behavior. The final subject (spec §12):
`runtime: add shared source-generation lease and publish fencing`.

---

## 10. Test plan (spec §11 + review §5; barriers/TCS, stress is supplemental)

Spec §11 tests 1–11 (v1 table) **plus** the review's additions:

- bounded channel full: old generation waiting on capacity; retirement occurs; later capacity does
  **not** admit its point;
- writer passes `WaitToWriteAsync`, then **retirement wins before `TryWrite`** → rejected;
- a **committed-before-retire** point **drains successfully** after a generation swap;
- a late `Stop` for generation N **cannot retire** generation N+1;
- a `GenerationLease` **cannot be forged** from a copied `GenerationKey`;
- start/initialize failure **rolls authorization back to zero** and records terminal history;
- supervisor records `Stopped` after lease retirement while an old generation **cannot overwrite** it;
- `PumpTask` completes but adapter quiescence **Unproven** → replacement **remains denied**;
- remove/re-add of the same source id in one process **does not reuse a generation key**;
- stable writer `Detach()` **releases its inner ingress reference**;
- exact interleavings use **barriers/TCS**; a high-iteration stress test is **supplemental**, not the
  sole proof.

Plus the **stable-ingress regression**: route intake survives a generation swap (the M1 property).

**Additional commit-1 gate tests (G1/G2):**
- a **retired lease cannot be re-authorized**;
- a lease minted by **gate A is rejected by gate B**;
- a lease can be **authorized only once**;
- **expected-lease retirement is idempotent** and cannot affect a successor (N can't retire N+1);
- **generation-allocator overflow fails closed**;
- a **commit-body exception releases the gate** without changing authority;
- **no mutation runs when authorization fails.**

---

## 11. Decisions resolved by the review (now locked)

1. Channel option: **`SingleWriter = false`**.
2. Ingress linearization: **successful stable-channel enqueue is the commit point**; pre-retirement
   committed data may drain after retirement.
3. Replacement default: **deny unless full generation quiescence is Proven** (not merely `PumpTask`).
4. Slot lifetime: **stop/restart/reconfigure retain** the slot; **permanent delete completes + removes**.
5. Generation id: a **runtime-lifetime allocator survives live-slot removal** (no same-process reuse).
6. Ownership: **one authoritative owner per counter**; cached diagnostics mirror snapshots.

---

## 12. Risks / remaining open (do not block commits 1–2)

- **Gate API final shape** — the only thing the review says still warrants a focused look; settle on
  the commit-1 diff (the `TryAuthorize`/`TryRetire`/`TryCommit` signatures + lock-order doc).
- **Atomic cutover (commit 3) size** — it is intentionally one commit; gate it on the full
  `Core.Tests` + `Host.Tests` + `Management.Tests` (filtered runs miss cross-cutting guards).
- **Reconfigure admission strictness** — the reconfigure workstream may want "old must terminate
  before replacement"; it must still use this gate/lease. Joint review **before commit 5**.

---

## 12a. Merge gates for the atomic cutover (commit 3) — do not block commits 1–2

1. **Built-in adapter quiescence matrix.** For every shipping adapter, identify what yields
   `QuiescenceEvidence.Proven` on an ordinary clean stop — otherwise default-deny replacement could turn
   a routine restart/reconfigure into a permanent refusal.
2. **Late-history path ownership.** Rejected-late counters + terminal history are updated **only** by
   trusted Host wrappers / the retirement registry — a retired adapter gets **no** unrestricted
   post-retirement mutation path.
3. **No notification under the gate.** Collector mutation may run gate → collector, but event dispatch,
   paging, UI notification, logging, and subscriber callbacks happen **after releasing both locks**.
4. **Performance regression check.** Establish point-ingress throughput/allocation baselines before the
   cutover; compare the fenced path under representative contention.
5. **Atomic-state barrier test.** A deterministic test proving snapshots never expose new-current
   identity with old-current authorization/health (G4).

## 13. Exit criteria

- All spec §11 + review §5 tests + the stable-ingress regression pass (barrier-based, deterministic).
- Full `Core.Tests` + `Host.Tests` + `Management.Tests` green; 0 warnings / 0 errors.
- Diff is lifecycle-correctness only — no liveness/recovery/UI behavior.
- No intermediate commit degrades runtime behavior vs `master` (B8).
- Both workstream owners approve before merge.

---

## 14. Ready-to-start assessment

Per the review: after B1–B8 are folded in (this doc), **commit 1 can begin immediately** with only a
focused review of the final gate API — no further broad architecture pass. Commits 1–2 are unused
scaffolding; the behavioral change is the single atomic cutover (commit 3).
