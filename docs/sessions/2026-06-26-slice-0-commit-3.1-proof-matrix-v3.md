# Slice 0 commit 3.1 — attestation proof-matrix + deadline-inputs lock (v3)

**Date:** 2026-06-26
**Supersedes:** v2 (+ its review). **Status: BLOCKED pending the FOCAS2 field measurement**
(`2026-06-26-focas2-field-measurement-procedure.md`). All §A–§I semantics are locked; the deadline
constants cannot be finalized — and **no 3.1 code may begin** — until §F item 1 is recorded.

Folds review D1–D6. This is the implementation lock for the behaviour-changing atomic supervisor
cutover once unblocked. Read with `2026-06-26-slice-0-commit-3-cutover-plan-v3.md` and the 3.0 record
`2026-06-26-slice-0-commit-3-complete-diff.md`.

---

## A. 3.1 lock decisions
1. One absolute monotonic deadline **per retiring generation** (§B).
2. Admission is **composite** (§C) — never bare `IsFullyProven`.
3. **Source-id permit precedes adapter factory construction** (§D, D4).
4. Replacement requires full proven quiescence; denied replacement is **operator-visible immediately**.
5. Expected-key retirement cannot touch a successor.
6. No mixed-generation snapshots.
7. Route cascade removed → exactly one route reader.
8. No global lock held across teardown I/O.
9. Leak-harness multi-hour run mandatory at 3.1.

---

## B. Per-retirement deadline — no silent clamp (D1)

```
candidate(gen) = MARGIN + max( supervisor_pump_budget,
                               max over applicable surfaces s of surface_budget(s, gen.adapter, gen.config) )

if candidate > HOST_CAP:
    → the deadline lock is BLOCKED for that adapter/config. Resolve by explicit operator decision:
        (a) raise HOST_CAP with a recorded risk decision, OR
        (b) disable automatic replacement for that source/config, OR
        (c) require controlled process restart for replacement.
    NEVER silently use HOST_CAP (it would misclassify a verified-healthy stop as wedged).
else:
    deadline(gen) = now_monotonic + candidate
```

- `max`, never a **sum** of pump + adapter waits (observed concurrently against one deadline).
- `HOST_CAP` is a **safety ceiling**, not permission to shorten a verified healthy duration.
- Monotonic clock: a monotonic `TimeProvider`/`Stopwatch.GetTimestamp` reading — not wall-clock
  (NTP-step immune). [code-design lock, §F].
- Deadline expiry does **not** resolve or mutate the adapter operation (see §G/D2).

---

## C. Composite admission proof (D3 tests included)

Admission keys off `Host.Generation.GenerationRetirementCompletion` (`Evidence`:
Active→Unproven→Proven), **plus** the supervisor pump and the snapshot cross-check. A replacement is
admitted **iff all**:

```
1. supervisor pump component  = Proven
2. adapter retirement Completion resolved (not pending)
3. snapshot ⇄ attestation cross-check:
     every surface Applicable in snapshot is Proven in attestation, AND
     every surface NotApplicable in snapshot is NotApplicable in attestation
4. GenerationRetirementCompletion.Evidence = Proven
5. no unresolved source-id barrier for this source id (§D)
```

Surface → component mapping (first 3.1 wiring step): `Pump` ← supervisor pump evidence;
`AdapterStop` ← attestation `Worker` ⊕ `BackgroundWork`; `CallbackDrain` ← attestation `CallbackDrain`.

**Deterministic evaluator tests (D3) — added to the cutover test list:**
- applicable surface reported `NotApplicable` in attestation → **rejected** (not admitted);
- snapshot `CallbackDrainApplicable=true` but attestation `CallbackDrain=NotApplicable` → **not Proven**;
- adapter Completion pending + pump Proven → **not admitted**;
- adapter Proven + pump pending → **not admitted**;
- any terminal-unproven surface → **not admitted**;
- late Proven → clears source-id barrier and admits a fresh activation.

---

## D. F1 — source-id permit precedes adapter factory construction (D4)

Reconciled order (the permit gates **construction**, not just `InitializeAsync`):

```
1. acquire source-id permit / barrier check;
2. invoke the adapter factory to CONSTRUCT the adapter under that permit;
3. discover ISourceRetirement capability on the constructed-but-uninitialized adapter, before InitializeAsync;
4. deny unsupported adapters BEFORE initialization opens any resource (RetirementCapabilityUnsupported);
5. revalidate / consume the permit at TryActivate.
```

**Constructor-non-resourceful invariant is locked, not assumed:** a registration-level contract /
test asserts every source adapter factory constructs without opening a socket/session/handle/worker. A
future resourceful constructor must **fail review**, not silently bypass F1.

Required tests: unresolved retirement → factory/`InitializeAsync` not invoked · `Stop→Start` cannot
bypass · `remove→same-id re-add` cannot bypass · late proof clears barrier → fresh attempt admitted ·
denied attempt surfaces `SourceLifecycleBlockReason` immediately · every adapter factory passes the
non-resourceful-constructor contract.

---

## E. OPC UA — durable-pending drain + explicit retired-drain pump mode (D5)

**Durable-pending (locked):** the dispatcher's internal drain budget is **not** the normal terminal
authority. Host-deadline expiry → `QuiescenceUnprovenAtDeadline` (retained); late drain → `Proven`.
Terminal `CallbackDrain = Unproven` only for genuine adapter-terminal conditions (dispatcher fault,
lost/unaccountable queue state, adapter explicitly determines drain can never be proven). Requires a 3.1
adjustment to the 3.0 `RetireAndDrainAsync` seam so "still draining" (pending) is distinct from terminal.

**Retired-drain pump mode (D5) — locked behaviour:**
```
retire publish authority (generation fenced)
close callback ingress (dispatcher rejects + records)
KEEP the retired subscribe pump alive in drain/account mode
  → the pump MUST NOT exit on the first RejectedRetired write; it is still needed to consume queued work
consume queued notifications until dispatcher drain/accounting completes
record accepted-but-retired items into retired-generation history (never silently lost)
only THEN cancel/complete the pump — OR, if the host deadline expired first, RETAIN the pump/adapter
  runtime as orphaned so late proof can still occur
```
Cancelling the pump at the deadline would make durable-pending drain impossible unless the dispatcher
itself can account/drop all accepted work with stable retired-generation history.

**OPC UA drain-mode acceptance tests (added to cutover list):** pump kept alive after publish-authority
retired drains queued work → Proven · pump does not exit on first RejectedRetired · host deadline before
drain → pump/adapter retained (orphaned), `QuiescenceUnprovenAtDeadline`, late drain → Proven · shed
items recorded into retired-gen history, not lost · dispatcher fault → terminal CallbackDrain Unproven.

---

## F. Deadline inputs — pre-cutover lock classification (D6)

| Adapter | Effective value | Applied where | Class |
|---------|-----------------|---------------|-------|
| Modbus TCP | request 1000 ms / connect 2000 ms | conn-mgr socket | **bench-blocker**: confirm socket timeout aborts a hung read |
| S7 | request 1000 ms / connect 2000 ms | `S7ConnectionManager` | **bench-blocker**: confirm Sharp7 honours timeout on blocking read |
| FOCAS2 | **BLOCKED — field-measure** (handle alloc 10 s ≠ data-call bound; `cnc_setdtimeout` NOT set) | — | **external blocker** (§ procedure) |
| OPC UA | drain-absorb ~1.7 s (1000-batch channel) + dispose | dispatcher + coordinator | **bench-blocker**: verify worst-case drain rate |
| MTConnect | **10 s** `HttpClient.Timeout` (verified applied) | `MTConnectHttpClient.cs:39` | recorded; confirm socket-wedge fallback |
| Brother | **10 s** `HttpClient.Timeout` (verified applied) | `BrotherHttpHttpApi` ctor | recorded; confirm socket-wedge fallback |

**Classification (D6):**
- **External blocker (gates v3/deadline lock):** FOCAS2 field measurement.
- **Bench blockers (before 3.1 code uses final deadlines):** Modbus/S7 timeout enforcement; OPC UA drain rate.
- **Code-design blockers (before cutover diff finalization):** monotonic `TimeProvider`; `HOST_CAP` & `MARGIN` constants.

The value used per adapter is **max verified healthy in-flight + margin**, never the nominal configured timeout.

---

## G. State vocabulary — deadline-unproven is NOT terminal (D2)

Deadline expiry must **not** mutate any `GenerationRetirementCompletion` component to terminal `Unproven`.
The lifecycle state lives **beside** the completion (added if the commit-2 type can't represent it), and
the component states are never overloaded:

```
before deadline:  component Active        → lifecycle AwaitingQuiescence (pending, no replacement)
at host deadline: component STAYS Active  → lifecycle QuiescenceUnprovenAtDeadline (operation retained, late proof can clear)
adapter proves:   component Proven        → barrier clears (incl. Proven-late)
adapter terminal: component Unproven      → lifecycle QuiescenceTerminallyUnproven (barrier held until operator/process action)
```

| Lifecycle state | Component | Source-id barrier |
|---|---|---|
| `AwaitingQuiescence` | Active | held |
| `QuiescenceUnprovenAtDeadline` | **Active (unchanged)** | held; late proof can clear |
| `QuiescenceTerminallyUnproven` | Unproven | held until operator/process action |
| `Proven` / `Proven-late` | Proven | **cleared** |

Applies to every adapter row and to `remove → same-id re-add` (barrier keyed by source id, survives slot removal).

---

## H. Per-adapter proof matrix
(Unchanged from v2 §H except durable-pending OPC UA per §E and deadline inputs per §F. Surfaces ·
retirement op · idle · responsive · max-legit · wedged · late-proof · terminal · deadline input.)

- **Modbus/S7** (Worker, wire-idle): idle/responsive→Proven; wedged→**pending**; late→Proven-late; terminal = close-failed / fault. Input §F (bench-blocker).
- **FOCAS2** (Worker, true thread-exit): idle/responsive→Proven; wedged→**pending** (detected); late→Proven-late; terminal = cleanup-failed. Input **BLOCKED**.
- **OPC UA** (CallbackDrain + BackgroundWork; Worker NotApplicable): idle/responsive→Proven; wedged→**pending** (durable, §E); late→Proven-late; terminal only on genuine fault. Input §F (bench-blocker).
- **MTConnect/Brother** (Worker = in-flight poll): idle/responsive→Proven; wedged→**pending**; late→Proven-late; terminal = none (durable). Input = 10 s (verified).

---

## I. Non-adapter supervisor-cutover acceptance tests
- expected-key retirement cannot touch a successor;
- snapshots never expose mixed current-generation / runtime / health;
- every activation path honours the unresolved-retirement barrier (no back door);
- route cascade removed → exactly one route reader;
- denied replacement visible through management/health immediately;
- unrelated source lifecycle ops proceed while one source quiesces (no global teardown lock);
- no global lock across teardown I/O;
- **composite-proof evaluator tests** (§C, D3);
- **OPC UA retired-drain-mode tests** (§E, D5);
- **F1 permit / non-resourceful-constructor tests** (§D, D4);
- **leak-harness multi-hour run** passes with supervisor wiring live.

---

## J. v3 exit criteria
| # | Criterion | Status |
|---|-----------|--------|
| 1 | per-adapter verified deadline values OR explicit blocked | §F — **FOCAS2 BLOCKED**; Modbus/S7/OPC UA bench-blockers; MTConnect/Brother recorded |
| 2 | per-retirement deadline formula, no silent clamp | §B ✓ (D1) |
| 3 | composite evaluator + tests | §C ✓ (D3) |
| 4 | source-id permit before factory + non-resourceful-ctor contract | §D ✓ (D4) |
| 5 | OPC UA durable-pending + retired-drain pump mode | §E ✓ (D5) |
| 6 | deadline-unproven ≠ terminal-unproven | §G ✓ (D2) |
| 7 | deadline-input class (external / bench / code-design) | §F ✓ (D6) |
| 8 | non-adapter + new acceptance tests | §I ✓ |

## K. Blocking items before 3.1 coding
1. **FOCAS2 field measurement** (external blocker) — run `2026-06-26-focas2-field-measurement-procedure.md`,
   paste results into §F. **Until this is recorded, v3 stays BLOCKED and 3.1 code does not start.**
2. Bench: Modbus/S7 socket-timeout enforcement on a hung read; OPC UA worst-case drain rate.
3. Code-design: monotonic `TimeProvider` source; `HOST_CAP` / `MARGIN` constants (with the §B
   block-on-exceed rule, not a clamp).
4. Lock the surface → `GenerationRetirementCompletion` component mapping (§C) as the first wiring step.
