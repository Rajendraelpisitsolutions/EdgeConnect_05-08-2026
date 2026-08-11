# Sparkplug B — K3 Session Actor Plan (v2)

**Date:** 2026-07-19
**Author:** Session with Sudhakar
**Status:** DRAFT v2 — external review folded (plan-trail cadence:
v1 → review → **v2 (this)** → reality-check → v3 frozen → implement). Implementation
remains **PAUSED** until v3 freeze.
**Supersedes:** `2026-07-19-sparkplug-b-k3-session-actor-plan-v1.md`.
**Review folded:** `2026-07-19-sparkplug-b-k3-session-actor-plan-v1-chatgpt-review.md`
(verdict: *CONDITIONAL GO to v2; NO-GO to freeze*). All four blocking findings
(B1–B4), all six §5 open-decision verdicts, the transition matrix (review §8), the
acceptance matrix (review §10), and the §11 editing checklist are incorporated below.
**Governing docs:** frozen master plan `2026-07-13-sparkplug-b-sink-plan-v2.3.md`
§8 (K3 line + K3 carry-forward), **ADR-0036** (the runtime spec — K3's home),
**ADR-0035** (scope), K2 handoff `2026-07-19-sparkplug-b-k2-handoff.md`.
**Baseline:** `master` @ `808fae7` (K2 merged; wire layer + Core replay contracts
all present).

---

## 0. What changed from v1 (fold summary)

| Review item | v1 said | v2 locks |
|---|---|---|
| **B1** transport recovery | "session-suspect reconnect-as-new-session" in the actor | **Recovery routes through Core's operational rebirth**; the actor never reconnects behind Core. Two internal `RebirthAsync` branches (healthy / transport-suspect). Explicit `CompleteCatchUpAsync`-suspect rule. |
| **B2** concurrency | "a single publish/transition lock or a mailbox" | **One selected discipline**: a single async serialization gate in the actor + a mandatory **connection-generation token** + atomic control latches; callbacks never publish or mutate protocol counters. |
| **B3** schema | "first-observed metric → controlled rebirth" *and* "no material-schema rebirth" conflated | **Split**: first-observed = **same-generation `SchemaChange` operational rebirth (supported in K3)**; material mutation of an announced metric = **fail-closed, deferred**. Dynamic-only final-update comparator with a monotonic `dirtySinceBirth` set. |
| **B4** identity store | actor owns `data/sparkplug/identity-state.db` | **Injected `ISparkplugIdentityStateStore`** taking an absolute path; **K4 owns rooting + singleton lifetime**; batch-atomic alias resolution; explicit DB constraints. |
| **Component naming** | "`SparkplugSinkAdapter` *is* the actor" | **Facade + sole-actor + transport + store** split (§2). |
| **Slice order** | reconnect in slice 3, rebirth in slice 6 | **Reordered** (§9) so transport-suspect recovery lands *after* the rebirth seam. |
| **6 open decisions** | proposed | **Locked** (§3). |

**No new Core API is required** for any of the four decisions (confirmed against the
code — §10). The reality-check surfaced one posture-question — transport outage during
replay faults terminally because `Failed` has no auto-restart — now **resolved** (§10.2):
K3 ships a bounded in-`RebirthAsync` reconnect (Core-clean), and the
Degraded-instead-of-fault fix is a **named post-K3 Core follow-up** (§13), surfaced not
buried.

---

## 1. Scope — what K3 is

Per master plan v2.3 §8: *`K3  SparkplugSessionActor (consumes Core context+lifecycle)
+ bdSeq store + aliases + Rebirth NCMD`*.

K3 is the **stateful MQTT-3.1.1 session actor** — the concrete
`IReplayAwareSinkAdapter` for Sparkplug B. It consumes the K1.3 Core replay lifecycle
and drives the K2 wire factories. Concretely, K3 delivers:

### 1.1 Component split (B2 §4.4 — the governing shape)

```text
SparkplugSinkAdapter        thin IReplayAwareSinkAdapter façade (the Core contract surface)
SparkplugSessionActor       sole owner of ALL mutable protocol/session state
SparkplugMqttTransport      CONNECT / SUBSCRIBE / PUBLISH / DISCONNECT only; NO reconnect policy
ISparkplugIdentityStateStore  gateway-scoped persisted identity service (bdSeq + aliases)
  └ SqliteSparkplugIdentityStateStore   the SQLite implementation (absolute path injected)
```

- The **facade** implements `IReplayAwareSinkAdapter` + `ISinkAdapter` and forwards
  every call into the actor's serialization gate. It holds no protocol state.
- The **actor** owns `seq`, current `bdSeq`, the announced manifest + alias map, the
  birth baseline + `dirtySinceBirth` set, the authoritative `(ReplaySessionId,
  ReplayEpochId)` pair, the protocol substate, and the transport-generation counter.
- The **transport** is a thin MQTTnet wrapper with **automatic reconnect disabled**
  (B2 §4.1). Reconnect is *only* ever expressed as a Core-driven `RebirthAsync`.
- The **store** is injected; the actor uses it but does **not** own or dispose the
  gateway singleton (B4 §6.1). In K3 tests the store is constructed with an absolute
  temp path; K4 registers the shared singleton.

### 1.2 The birth-then-replay sequence (ADR-0036 Rule 2)

`BeginReplaySessionAsync`: validate/resolve the manifest → reserve+commit `bdSeq`
(before CONNECT) → build NDEATH Will → **CONNECT(clean)** → **SUBSCRIBE exact NCMD** →
**NBIRTH** (`seq=0`, current snapshot, `Node Control/Rebirth=false`) → Core drives
historical replay (`is_historical=true`) → catch-up final update → Live. Core begins
replay only after Begin succeeds.

### 1.3 `seq`/`bdSeq` lifecycle (ADR-0036 Rule 3; review §7.1)

- `seq` = single Edge-Node modulo-256 counter; NBIRTH is `seq=0`, the next successful
  NDATA is `seq=1`; **reset to 0 at each (re)birth**; **NDEATH carries no `seq`**.
- `seq` is **encoded with the current counter and advanced only after local MQTTnet
  publish success**. No `seq` is consumed by a validation failure, unknown-manifest
  (first-observed) detection, stale input, cancellation-before-send, or a publish
  rejected before MQTTnet is entered.
- `bdSeq` increments on **every new CONNECT**; the same value appears in that session's
  Will-NDEATH and NBIRTH; retained across a **healthy-transport** same-session rebirth;
  a **new** value is reserved for a **transport-suspect** rebirth's new CONNECT.

### 1.4 Snapshot→wire mapping

Translate Core's `ReplaySessionStartState` snapshot (birth/rebirth) and
`CanonicalDataPoint` batches into K2 `SparkplugMetricSample`s + the alias map:
source-qualified metric naming `{SourceInstanceId}/{DeviceId?}/{TagPath}` (ADR-0036
Rule 5), duplicate/reserved-name validation (`bdSeq` / `Node Control/Rebirth`
collisions), and the manifest = the persisted observed set (never-observed → absent
from NBIRTH). The actor announces, in NBIRTH, **exactly** the alias set it will use in
NDATA (the K2 encoder enforces the exact set-match — the actor feeds it a coherent
pair from one batch-atomic alias resolution, §5).

### 1.5 The catch-up final update (dynamic-state only — B3 §5.3)

At `CompleteCatchUpAsync`, emit one non-historical latest-value update per metric in
the union of (a) the monotonic `dirtySinceBirth` set and (b) any final-snapshot-vs-
baseline difference — value + null-state + datatype + quality + quality-reason +
acquisition timestamp, compared in **wire-normalized** form, **not** value alone and
**not** raw CLR equality (§5.4). Static schema differences are **not** emitted here —
they are the schema classifier's job (§5.2).

### 1.6 The Rebirth NCMD handler (ADR-0036 Rule 4; review §7.3)

Subscribe the **exact** `spBv1.0/{group}/NCMD/{edge_node}` at QoS 1 before birth; on a
valid `Node Control/Rebirth=true` capture the transport generation **and** the
authoritative Core session/epoch at receipt time, then queue
`IReplaySessionHost.RequestRebirthAsync(..., HostCommand)`; ignore every other NCMD
case with a diagnostic + no side effect; coalesce repeats; let an in-flight DATA
decision finish but start no new DATA send once the control latch is observed.

### 1.7 QoS-0 session-suspect semantics (ADR-0036 Rule 1)

BIRTH/DATA at QoS 0, retain=false. `PublishResult.Success` = "completed at the local
MQTTnet transport boundary with no observable error", **never** broker receipt. On any
observable **or uncertain** send error the actor latches the transport **suspect** and
recovers through Core (§4), never by resuming a suspect session.

### 1.8 Advertised capability + health

`SparkplugSinkConfiguration` + `ValidateConfigAsync` + advertised
`DeliveryCapabilities(SupportsStoreAndForward=true,
AcknowledgementBoundary=LocalTransport)` + `CheckHealthAsync` 3-way health snapshot
(§8).

### 1.9 Epoch/session gating (ADR-0036; review §7.2)

The actor records the epoch of its **most recent successful** birth as authoritative
and gates every lifecycle input on **both** `ReplaySessionId` and `ReplayEpochId`
(§7). A failed NBIRTH must **not** promote the candidate epoch, baseline, alias
manifest, or next `seq`.

---

## 2. Scope — what K3 is NOT

- **No route-validation integration, no license gating, no DI registration triad, no
  module-catalog entry** — **K4**. Includes the production
  `ISinkReplayCapabilityClassifier` registration and resolving the gateway data root +
  registering the identity-store singleton.
- **No Studio wizard / `AddSparkplugDestination.razor` / edit routing** — **K5**
  (mockup-first, ADR-0035 Rule 7).
- **First-observed metric is IN SCOPE** as a same-generation `SchemaChange`
  operational rebirth (§5.1). **Material schema mutation** of an already-announced
  metric (datatype/name/identity/alias/static-property change) is **OUT** — K3
  detects it and **fails closed** (§5.2). The generation-changing
  `AdvanceGenerationAsync` path stays a **post-K3** slice.
- **No device-level DBIRTH/DDATA/DDEATH** — ADR-0036 Rule 8.
- **No clustered/standby lease** — single-owner, single-node identity store in v1.
- **No coordinated replay-sink hot replacement** — a named **post-K4 cross-layer
  follow-up** (crosses Host reconciliation, route-driver ownership, and config-apply
  semantics), *not* a casual "K3.x". Keep K1.3's fail-closed reject.
- **No Core changes.** No Core API/behavior change is expected; a discovered Core
  semantic gap **stops the slice and is surfaced explicitly** (§10.2), never worked
  around inside an actor reconnect path.
- **No external broker in tests** — see the test-environment wording (§9 note).

---

## 3. Locked decisions (v1 §5 open decisions → resolved)

| # | Decision | **v2 verdict (locked)** |
|---|---|---|
| 3.1 | Transport reconnect vs. Core rebirth | **Core rebirth (option a), tightened.** A transport-suspect event queues `RequestRebirthAsync` for the current session/epoch; a failed DATA send has that request accepted **before** `PublishAsync` returns zero-accepted non-success; `RebirthAsync` then does a new CONNECT + new `bdSeq`. Never reconnect behind Core. (§4) |
| 3.2 | Protocol substates vs. base `AdapterState` | **Internal substates; revised base mapping.** `StartAsync` completion ⇒ base state **Running/ready** even before a session exists. Substates are internal diagnostics; active-session outage/rebirth surfaces through **health/Degraded**, not `AdapterState`. (§6, §8) |
| 3.3 | Coordinated hot replacement | **Deferred.** Keep K1.3's fail-closed reject; track as a named **post-K4** cross-layer follow-up. |
| 3.4 | Identity-store rooting/lifetime | **One gateway-wide injected store.** K3 exposes `ISparkplugIdentityStateStore` + a SQLite impl taking an **absolute path**; K4 resolves the gateway data root and registers the singleton. The actor idempotently initializes/uses it but never owns/disposes it. (§5) |
| 3.5 | Manifest/naming validation split | **Approved.** K3 does the **final pre-CONNECT/pre-NBIRTH** validation of the actual manifest and fails closed; K4 does earlier config-time + cross-destination validation. Neither substitutes for the other. |
| 3.6 | `Initialize`/`Start`/`Begin` division | **Approved, exact bracket** (§6). |

---

## 4. B1 — transport recovery through Core (the reconnect + failure model)

**Principle:** a suspect MQTT/Sparkplug transport session recovers **only** through
Core's existing operational-rebirth path. The actor never mints a new `bdSeq`/NBIRTH
outside Core's epoch/snapshot/cursor authority.

### 4.1 The two `RebirthAsync` branches (review §2.1)

`RebirthAsync` must **not** infer transport action from the public `RebirthReason`
alone. The actor holds an internal latched **`RequiresNewConnect`** flag:

| Branch | When | Transport action | `bdSeq` | `seq` |
|---|---|---|---|---|
| **Healthy-transport rebirth** | host NCMD or first-observed metric, connection healthy | **reuse** the MQTT connection | **retain** | reset; NBIRTH `0` |
| **Transport-suspect rebirth** | disconnect / observable send error / uncertain completion latched | **abandon/close** old client → new Will → **new CONNECT** → SUBSCRIBE | **reserve new** | reset; NBIRTH `0` |

`RebirthReason.Other` is a sufficient protocol-neutral diagnostic for transport
recovery; the new-CONNECT decision is the **actor-owned latch**. When a host command
and transport loss **coalesce, transport-suspect wins** (new CONNECT + new `bdSeq`).
Epoch/baseline/alias-manifest promote **only** on a successful NBIRTH in either branch.

### 4.2 Ordering for a DATA send failure (review §3.1)

For every Replay/CatchUp/Live `PublishAsync`:

1. Validate session, epoch, phase, manifest membership, and current transport token.
2. Build one NDATA using the current `seq` **without advancing it yet**.
3. Attempt the local QoS-0 MQTTnet publish.
4. On local success: advance `seq` modulo 256, return **full success**.
5. On observable/uncertain failure:
   - latch `TransportSuspect` / `RequiresNewConnect`;
   - capture the current authoritative Core session + epoch + host;
   - **ensure the rebirth request is accepted before returning**;
   - return non-success with `AcceptedCount=0`, no cursor-advancing claim.

Core's existing rebirth-before-retry ordering (the `ReplayRouteDriver` A2/C3 lock)
then captures a fresh snapshot, calls `RebirthAsync` (suspect branch), and retries the
**same unacknowledged subrange** under the newer epoch. The actor must **not** cache
and replay the failed Core batch itself.

### 4.3 Asynchronous disconnect while idle (review §3.2)

A disconnect callback may arrive when Core is not inside a sink call. The callback:

- validates its immutable transport-generation token (ignore if stale);
- atomically latches the transport **suspect**;
- captures the current authoritative session/epoch/host;
- queues the **non-reentrant** rebirth request;
- returns **without reconnecting or publishing**.

The K1.3 rebirth wake (`ReplayRouteDriver.WaitForWorkAsync`, the combined buffer-OR-
rebirth-OR-cancellation wait) pulls an idle Live driver out of its wait; the next
`RebirthAsync` performs the new CONNECT.

### 4.4 The `CompleteCatchUpAsync` special rule (review §3.3) — reality-checked ✓

`CompleteCatchUpAsync` returns no `PublishResult`, so throwing on a final-update send
failure would merely fault the route. The **no-Core-change** recovery:

1. latch transport suspect;
2. await acceptance of a **current-session/current-epoch** rebirth request;
3. do **not** claim the final update was emitted;
4. **return** from `CompleteCatchUpAsync`;
5. Core processes the pending rebirth **before any subsequent Live DATA**.

**Reality-check result (CONFIRMED — no Core gap):** verified against
`src/ElpisEdgeConnect.Core/Routing/ReplayRouteDriver.cs`. `CompleteCatchUpAsync` runs
in its own branch (lines 155–162) that sets `live = true; continue;` and does **not**
fall through to dequeue Live DATA. The `continue` re-enters the loop top (lines
124–132), where the control-plane barrier `host.TryTakePending(...)` runs **before**
any Live batch is dequeued. Because `RequestRebirthAsync` returns once the request is
*queued* (`IReplaySessionHost` contract) and the actor awaits it before returning, the
request is already pending when the driver loops — so the rebirth is processed before a
single Live DATA is emitted. The review's required ordering is satisfied by the
existing driver with **zero Core edits**. (Confirms review §12 sign-off condition 3.)

### 4.5 Failures that remain fatal (review §3.4)

- `BeginReplaySessionAsync`: CONNECT/SUBSCRIBE/NBIRTH failure **throws**; no
  authoritative birth exists; no epoch promoted.
- `RebirthAsync`: CONNECT/SUBSCRIBE/NBIRTH failure **throws**; candidate epoch **not**
  promoted; a transport with an observable/uncertain NBIRTH failure is abandoned as
  suspect. Per the driver, this **faults the route** (`ProcessRebirthAsync` propagates,
  `ReplayRouteDriver.cs:334`). See the surfaced posture-question in §10.2.
- `EndSessionAsync`: death/disconnect failure is diagnostic/best-effort; **no rebirth**
  during shutdown; Core's bounded cleanup proceeds (`EmitEndSessionAsync`,
  `ReplayRouteDriver.cs:292`).

---

## 5. B4 + B3 store — identity store, aliases, and the schema taxonomy

### 5.1 First-observed metric (SUPPORTED in K3 — same-generation growth; review §5.1)

A metric first observed after the current birth is already in Core's same-generation
persisted observed set. Before encoding DATA, the actor:

1. detects the canonical key is **not** in its current announced manifest;
2. allocates **no** `seq`, publishes nothing, returns no acceptance;
3. awaits acceptance of `RequestRebirthAsync(..., SchemaChange)` for the current
   session/epoch;
4. Core captures a fresh populated snapshot at a new H and calls **healthy-transport**
   `RebirthAsync` under a newer epoch;
5. the actor allocates any missing stable aliases (batch-atomic, §5.4), emits the new
   NBIRTH, and Core retries the unacknowledged triggering subrange.

This is **same-generation operational manifest growth**, distinct from the deferred
material-schema-generation feature.

### 5.2 Material schema mutation (DEFERRED — fail-closed in K3; review §5.2)

K3 cannot safely re-announce a changed static schema for an **already-announced**
metric under the fixed Core generation. The **pinned material static-field set** (v2
freeze list) is:

- canonical identity **or** source-qualified published name change;
- datatype change;
- alias reassignment / alias-key inconsistency;
- unit change (when unit is part of the announced NBIRTH);
- any other NBIRTH **static** property that is part of the announced manifest.

On any such mutation K3 **fails closed** with a typed diagnostic/route fault
(`SPARKPLUG.*`). It must **not** silently encode NDATA, allocate a second alias, or
pretend a same-generation rebirth solves the generation contract.

### 5.3 Final-update comparator = dynamic-state only (review §5.3)

Two pieces of per-birth actor state:

- an **immutable, wire-normalized birth baseline** per canonical metric;
- a **monotonic `dirtySinceBirth` set**, never cleared during that birth epoch.

For each Replay/CatchUp point: normalize and mark the metric dirty if it differs from
the birth baseline. At cutover: union the dirty set with any final-snapshot-vs-baseline
difference and emit the final snapshot value for the union. This preserves
`10 → 20 → 10` (stays dirty; final non-historical `10` emitted).

### 5.4 Wire-normalized comparison (not raw CLR; review §5.3)

Compare the wire-visible normalized form: mapped Sparkplug datatype + value arm;
`is_null` + value-arm presence; mapped quality + quality reason; acquisition timestamp
at the exact encoded precision; byte arrays by contents; any numeric coercion/precision
rule already locked in K2. After every successful NBIRTH, **atomically** replace both
baseline and dirty set; a failed NBIRTH changes neither.

### 5.5 Durable `bdSeq` reservation (B4 §6.2)

For normalized identity `(BrokerEndpoint, GroupId, EdgeNodeId)`:

- `BEGIN IMMEDIATE` (serialized) → read + validate the previous value → checked
  next-value arithmetic (K2 `SparkplugBirthDeathSequence` wrap policy) → **durably
  commit before returning** → *then* construct CONNECT options/Will;
- any attempted new CONNECT **consumes** its reserved value even if CONNECT later fails
  (skip-committed-unused after restart, never reuse);
- unknown schema version / malformed row / negative / out-of-range / commit failure
  **prevents CONNECT and fails closed**; **never silently reset to 0**;
- pin SQLite durability settings explicitly so "commit before CONNECT" is a durability
  claim, not just in-process visibility.

### 5.6 Batch-atomic alias resolution (B4 §6.3)

Validate the complete proposed birth manifest first, then in **one transaction**:

1. load + validate existing mappings;
2. preserve every existing canonical-key → alias mapping;
3. sort missing canonical keys by a deterministic canonical comparer;
4. allocate all missing aliases (K2 alias value type, checked arithmetic; alias 0
   reserved, app aliases begin at 1; `bdSeq` gets a non-zero alias unique vs. the app
   map; `Node Control/Rebirth` gets none);
5. enforce unique `(node_identity, canonical_key)` **and** `(node_identity, alias)`;
6. commit the whole set or none;
7. return one **immutable** alias map used by both NBIRTH and subsequent NDATA.

v1 does **not** delete/recycle aliases when metrics disappear — persisted mappings
survive route recreation and temporary absence; compaction is a separately governed
later policy.

### 5.7 Identity/comparison details to pin (B4 §6.4)

- use Core's normalized `BrokerEndpoint` representation, not an ad-hoc concatenated
  string;
- alias canonical key = `SourceInstanceId + optional DeviceId + canonical TagPath`;
  **excludes** `RouteId` and display-name override;
- pin the exact string comparer/collation for canonical keys and published-name
  duplicate checks, **including a case-only-pair test** — do not inherit SQLite or
  dictionary defaults accidentally;
- reserved control metrics get **no** telemetry alias;
- schema creation/migration is **versioned**; an unknown future version fails closed;
- concurrent calls from different node identities / separate connections stay unique +
  monotonic.

---

## 6. Lifecycle + state mapping (decisions 3.2 + 3.6)

| Method | Responsibility | Network / `bdSeq` | Base `AdapterState` |
|---|---|---|---|
| `InitializeAsync` | normalize + validate **immutable local config** | none | Initializing |
| `StartAsync` | start actor-local resources; **ensure store readiness** | **no CONNECT, no `bdSeq`** | → **Running/ready** (adapter runtime up; no session yet) |
| `BeginReplaySessionAsync` | validate/resolve manifest → reserve `bdSeq` → CONNECT → SUBSCRIBE → NBIRTH | new CONNECT + new `bdSeq` | Running (session Live once entered) |
| `RebirthAsync` | healthy or suspect branch (§4.1) | reuse or new CONNECT/`bdSeq` | Running (Degraded via health during rebirth) |
| `CompleteCatchUpAsync` | dynamic final update → Live (or suspect handshake §4.4) | reuse | Running |
| `EndSessionAsync` | **one** graceful NDEATH for a valid born session, then disconnect | current connection only | Running → stopping |
| `StopAsync` | local-resource shutdown only | **never a second death** | Stopped |

**`AdapterState` is not the replay-phase authority.** Protocol substates
(`Connecting/SubscribingNCMD/Birthing/Replaying/CatchingUp/Live/Rebirthing/Suspect/
Faulted`) are **internal diagnostics**; the coarse base state stays the `ISinkAdapter`
contract surface, and active-session outage/rebirth surfaces through **health/Degraded**
(§8).

---

## 7. Session/epoch input policy per surface (review §7.2)

- **Stale MQTT callback** from an old transport generation: **ignore + diagnostic
  counter** (async transport events are not contract violations).
- **`PublishAsync`/`CompleteCatchUpAsync`** carrying a different session or epoch:
  **fail closed** as a lifecycle-invariant violation (a Core-driven programmer/contract
  error), not a forever-retryable publish failure.
- **`RebirthAsync`** with a different session, non-increasing epoch, or unexpected
  candidate ordering: **fail closed / throw**.
- **Initial Begin** may install a session **only after** a successful NBIRTH.
- The **actor-authoritative `(session, epoch)`** pair changes **atomically** with the
  baseline/manifest/alias-map.

Required tests: same numeric epoch under a **different** session; a **non-increasing**
rebirth epoch (§11).

---

## 8. Health mapping + base-method fail-closed (review §7.5–§7.6)

**Health** (protocol substate stays internal):

- **Healthy:** adapter runtime + store ready **and** (no session begun yet **or** the
  active session is Live).
- **Degraded:** active session is connecting/subscribing/birthing/replaying/catching-
  up/rebirthing/disconnected-suspect/waiting-for-Core-rebirth, while the actor loop is
  operational.
- **Unhealthy/Faulted:** store corruption/schema incompatibility, actor-loop failure,
  illegal lifecycle transition, or unrecoverable init/config error.

Snapshot includes: current protocol state, authoritative session/epoch, transport
generation, last successful birth, `bdSeq` (safe numeric diagnostic), last error
code/time, pending-rebirth flags. **No credentials exposed.**

**Base methods:** the context-free `ISinkAdapter.PublishAsync(points, ct)` **fails
closed** (Core's replay path must never call it); `UpdateCurrentValuesAsync` is
explicitly **unsupported** for this push sink. Both are tested so accidental routing
cannot bypass the Sparkplug lifecycle context.

---

## 9. Slices (reordered per review §9)

| Slice | Content | Exit evidence |
|---|---|---|
| **1 — façade, config, actor skeleton** | `SparkplugSinkConfiguration` + local `ValidateConfigAsync`; thin adapter + sole actor + transport interface; internal protocol states; **the one serialization discipline**; base overloads fail closed; advertised **LocalTransport** capability | lifecycle/state-transition tests; capability test; illegal-call (base-method) tests; **no MQTT** |
| **2 — gateway identity store** | versioned SQLite schema; durable `bdSeq`; batch-atomic alias allocator; **injected absolute path**; checked arithmetic; corruption/concurrency/no-reuse | K0 crash matrix against the **production** store; reopen/concurrent-instance tests; alias atomicity + stability |
| **3 — pure birth-plan / mapping** | canonical identity/name mapping; manifest validation; reserved/duplicate detection; alias resolution; **wire-normalized birth baseline + dirty comparator**; **material-schema classifier** | empty/populated birth plans; case/comparer tests; byte/timestamp/quality comparator; `10→20→10`; **material mutation fails closed** |
| **4 — MQTT transport + initial Begin** | clean 3.1.1 CONNECT; QoS-1 NDEATH Will; exact NCMD SUBSCRIBE; NBIRTH; **connection-generation token**; initial epoch/baseline promotion | in-process server ordering `CONNECT→SUBSCRIBE→NBIRTH`; Will contents; failed CONNECT/SUBSCRIBE/NBIRTH promote nothing |
| **5 — Replay/CatchUp/Live DATA** | context/session/epoch/phase gating; historical flag; QoS-0 local-boundary result; **`seq` commit point**; final update; first-observed rebirth-before-retry signal | strict full/zero acceptance; stale session/epoch; no `seq` on unknown metric; final-update matrix; **cutover-suspect composition test** |
| **6 — operational rebirth + end** | NCMD parse/coalesce; healthy-transport same-session rebirth; **transport-suspect new CONNECT/`bdSeq`**; async idle disconnect; **stale-callback suppression**; graceful End/Stop idempotence | host NCMD retains `bdSeq`; transport failure changes it; old callbacks ignored; no DATA during rebirth; NDEATH before disconnect |
| **7 — health, diagnostics, failure sweep** | 3-way health; counters; redaction; every-phase disconnect/failure; full actor trace; regression gates | the acceptance matrix (§11) green; deterministic synchronization only; full unfiltered regressions |

> The transport slice (4) intentionally proves **only initial connection**.
> "Reconnect" exits in **slice 6**, after the operational-rebirth seam exists —
> removing v1's reconnect-before-rebirth inversion.

**Test-environment wording (locked):** *K3 uses no external broker process or
environment dependency. Pure actor/store tests use injected fakes; transport
integration tests use an in-process MQTTnet server. External-broker CI and independent
host interoperability remain K6.*

**Project-change wording (locked):** *No Core API/behavior change is expected. Changes
are limited to the SparkplugB assembly/tests plus necessary
solution/project/test-infrastructure metadata. A discovered Core semantic gap stops the
slice and is surfaced explicitly.*

Working style unchanged: slice-per-commit, external review per slice, `v3.x`
amendments if reality diverges.

---

## 10. Reality-check status (v2 → v3 gate)

### 10.1 Confirmed against the code

- **Cutover-suspect ordering (review §3.3/§12.3): CONFIRMED, no Core gap** — see §4.4.
  `ReplayRouteDriver` runs its top-of-loop control barrier before any Live DATA after
  `CompleteCatchUpAsync` returns.
- **`RebirthReason` sufficiency: CONFIRMED** — `ProcessRebirthAsync`
  (`ReplayRouteDriver.cs:334`) never inspects the reason; it captures a fresh birth and
  calls `RebirthAsync` regardless, so `Other`/`SchemaChange`/`HostCommand` are purely
  diagnostic to Core. No new enum value needed.
- **Dual-branch `RebirthAsync` compatibility: CONFIRMED** — the driver only ever calls
  `RebirthAsync` for recovery (never re-calls `BeginReplaySessionAsync` mid-run) and
  retains the same `ReplaySessionId` while advancing the epoch
  (`ReplayRouteDriver.cs:347`). The actor takes its new-CONNECT/new-`bdSeq` branch
  internally off its suspect latch; Core is oblivious.

### 10.2 Transport outage during replay — RESOLVED (B + surfaced Core follow-up)

**The investigated fact (authoritative):** `Failed` is a **terminal** route state with
**no automatic restart** anywhere in Core or Host.

- `RouteStateTransitionValidator` (`RouteStateTransitionValidator.cs:95`): the only edge
  out of `Failed` is `Failed → Stopping`. There is no `Failed → Starting/Running`.
  Recovery needs full re-registration (`Stopped → Configured`), which v1 does not expose
  as an API.
- `RoutingEngine` faults a crashed worker via `TryTransitionTo(RouteState.Failed,
  reason)` and its own comment notes "starting from Failed throws."
- `SinkSupervisor.RestartAsync` (`SinkSupervisor.cs:302`) is **config-apply-driven** (the
  hot-reload coordinator when a destination's config changes), **not** a failure
  watchdog. Nothing auto-restarts a Failed route.
- The `Running ↔ Degraded ↔ Draining` recovery cycle (`RouteLifecycleManager`) is driven
  by the **legacy `SinkPublisher`** degraded/recovered notifications — the **non-replay**
  path. `ReplayRouteDriver` does **not** participate: a `Begin`/`RebirthAsync` failure
  throws → terminal `Failed`.

**The asymmetry:** the legacy MQTT sink rides out a transient broker outage in
**Degraded** (via `PublishWithRetryAsync` + `RetryStateMachine` + store-and-forward) and
auto-recovers to Running. The Sparkplug replay driver, as-is, faults **terminally** on a
transport-suspect `RebirthAsync` that can't reconnect — a broker blip permanently kills
the route until an operator re-applies config. Option A ("rely on supervision to
restart") is therefore **not viable** — no such supervision exists.

**Resolution (locked):**

1. **K3-local mitigation — Option B, no Core change.** The actor's **transport-suspect
   `RebirthAsync` branch** performs a **bounded reconnect-with-backoff** (bounded
   attempts/time; deterministic via the injected clock) before it gives up and throws.
   This is Core-driven (Core called `RebirthAsync`), not an autonomous callback
   reconnect, so it does **not** violate B1's "never reconnect behind Core." It rides out
   short broker blips without faulting the route. On exhausting the bound it throws →
   terminal `Failed` (the fatal-failure rule of §4.5 still holds for a *sustained*
   outage). The bound is a `SparkplugSinkConfiguration` value with a sane default.
2. **Surfaced Core follow-up (named).** The architecturally-correct behavior — the replay
   driver going **Degraded + backoff + store-and-forward** instead of terminally faulting
   on a sustained transport outage, matching the legacy sink — is a **Core change to
   `ReplayRouteDriver`/worker fault semantics**. It is **out of K3** (K3 is Core-clean)
   and recorded as a named follow-up (§13). This is the one genuine Core gap the review's
   condition #4 anticipated; it is surfaced here, not buried in the actor.

**Slice impact:** the bounded-reconnect bound lands in slice 1 config + slice 6
(transport-suspect rebirth); an acceptance test asserts a within-bound outage recovers
(no fault) and an over-bound outage faults deterministically (§11.2 transport recovery).

### 10.3 v3-freeze checklist (review §12)

1. ✅ Four blocking findings (B1–B4) folded into this plan.
2. ✅ Transition matrix adopted (§11).
3. ✅ Cutover-failure ordering confirmed against the current `ReplayRouteDriver` (§4.4).
4. ✅ Acceptance matrix attached to the slices (§11); carry per-slice into
   implementation.
5. ✅ **Open item §10.2 decided** — Option B (bounded in-`RebirthAsync` reconnect) +
   surfaced Core follow-up. Grounded in the investigated fact that `Failed` is terminal
   with no auto-restart.

All five v3-freeze conditions are now met in v2. The remaining gate is the external
reality-check pass over this v2 (§14).

---

## 11. Adopted matrices (review §8 + §10, verbatim)

### 11.1 Transition matrix

| Trigger | Core replay session | Core epoch | MQTT connection | `bdSeq` | Wire `seq` | Cursor consequence |
|---|---|---|---|---|---|---|
| **Initial Begin** | New/current session | Initial candidate; authoritative only after NBIRTH | New CONNECT | Reserve new before CONNECT | NBIRTH `0`, next DATA `1` | Core begins replay only after Begin succeeds |
| **Host Rebirth NCMD, transport healthy** | Same | Strictly newer candidate | Reuse | Retain | Reset; NBIRTH `0` | Core keeps cursor, captures fresh H/snapshot, re-drives |
| **First-observed metric, transport healthy** | Same | Strictly newer candidate | Reuse | Retain | Reset; NBIRTH `0` | Triggering subrange remains unacknowledged and is retried |
| **Transport error/disconnect** | Same Core session; new MQTT session | Strictly newer candidate | Replace with new CONNECT | Reserve new | Reset; NBIRTH `0` | Failed subrange has zero acceptance; Core rebirths before retry |
| **Host command + transport loss coalesced** | Same | One newer candidate | Replace | Reserve new | Reset; NBIRTH `0` | One rebirth; transport-suspect branch wins |
| **Final-update send becomes suspect** | Same | Current request, then newer candidate | Replace during rebirth | Reserve new | Reset at replacement birth | Queue request before cutover returns; barrier runs before Live DATA |
| **Rebirth NBIRTH fails** | Old session identity remains recorded | Candidate not promoted | Attempted transport abandoned/suspect | Reserved new value never reused | No further DATA | Route faults; no cursor advancement by actor |
| **Graceful End** | Ends | Unchanged | Current valid connection only | Current | NDEATH has no `seq` | No reconnect/rebirth; Core cleanup continues |

> "New session" distinguishes a **new MQTT/Sparkplug transport session** from a **new
> Core `ReplaySessionId`**. Operational rebirth keeps the Core session id and advances
> only the replay epoch.

### 11.2 Acceptance matrix (the K3 gate — carry per-slice)

**Identity/store:** empty store starts `bdSeq` at the locked initial value + increments;
commit-then-crash-before-CONNECT skips the value on reopen; rollback/commit failure
never returns a value + prevents CONNECT; malformed/negative/overflow/unknown-schema
rows fail closed; 200 concurrent reservations unique + monotonic; different node
identities independent; aliases persist across actor/route recreation; missing aliases
allocate **all-or-none** in deterministic order; duplicate alias/canonical-key
corruption fails closed; absent metrics cause no alias reuse.

**Birth/epoch/sequence:** empty route still CONNECTs/SUBSCRIBEs/NBIRTHs; NBIRTH
physically `seq=0`, NDEATH no `seq`; successful birth promotes session/epoch/baseline
atomically; failed initial NBIRTH promotes nothing; failed rebirth NBIRTH keeps the
previous epoch; stale session same numeric epoch rejected; stale epoch in current
session rejected; non-increasing rebirth epoch rejected; DATA `seq` advances only after
local success + wraps under the K2 policy.

**Transport recovery:** observable publish failure requests rebirth before returning
zero-accepted; uncertain completion same path; async disconnect while Live-idle requests
rebirth + wakes Core; transport-suspect rebirth reserves new `bdSeq` + new Will;
host-command rebirth on healthy connection retains `bdSeq`; simultaneous host command +
disconnect → one rebirth + new CONNECT; delayed disconnect from replaced client ignored;
delayed NCMD from old transport generation ignored; final-update send failure queues
rebirth before cutover returns + no Live DATA first.

**Manifest/schema/final update:** duplicate + reserved published names fail before alias
allocation/CONNECT; case-only comparer behavior explicit; first-observed metric →
no publish, no `seq`, no acceptance before rebirth; first-observed present in new NBIRTH
before its retried DATA; datatype/name/identity/unit/static-property mutation fails
closed; value/null-state/quality/quality-reason/acquisition-time/byte-contents each
independently dirty a metric; raw CLR diffs that normalize to identical wire state cause
no false final update; `10→20→10` emits final `10`; a successful same-session rebirth
replaces the baseline, a failed one does not.

**NCMD/end/health:** exact NCMD topic only (wildcard/other node ignored); valid `true`
requests; false/null/wrong-type/malformed/missing ignored + diagnostic; valid control +
unknown extras → request once + diagnose extras; repeats coalesce while pending/in
progress; an in-flight DATA decision may finish but the next DATA waits for rebirth; End
emits explicit death before clean disconnect exactly once; failed Begin emits no End
death; already-disconnected/suspect End does not reconnect; Stop/Dispose emit no second
death; health distinguishes ready-no-session / Live / rebirthing-suspect / faulted-store.

**Determinism:** no `Thread.Sleep`, timing races, external broker, or polling for
assertions — `TaskCompletionSource`, explicit transport hooks, bounded channels/events,
injected clock/time provider, in-process server observations.

---

## 12. Exit criteria (K3 gate)

- Solution builds **0 warnings / 0 errors** (warnings-as-errors on the new project).
- `SparkplugSinkAdapter` (façade) + `SparkplugSessionActor` implement the full state
  machine (birth → replay → catch-up → live → operational rebirth → transport-suspect
  recovery → graceful end) exercised by deterministic in-process-MQTTnet tests — **no
  `Thread.Sleep`, no external broker**.
- `bdSeq` crash-safety proven (the K0 WS5 matrix against the production store); aliases
  persist + stay stable across route recreation; batch-atomic all-or-none allocation.
- Epoch/session gating proven against the real actor (stale-session-same-epoch,
  non-increasing rebirth epoch, promotion-only-after-successful-NBIRTH).
- Advertised `DeliveryCapabilities` is `LocalTransport`; NDEATH carries no `seq`;
  transport-suspect recovery mints a new `bdSeq`; healthy rebirth retains it.
- No change to `ElpisEdgeConnect.Core` or any existing project; only MQTTnet + the K2
  SparkplugB project referenced.
- Full unfiltered regression green (Core + Host + Management + SparkplugB); the full
  Management.Tests project run before any PR.
- Plan trail updated; handoff written before sign-off.
- **Still NOT operator-shippable** — the tile is Available only after K5. K3 completion
  is a backend milestone (CLAUDE.md §8).

---

## 13. Carry-forward (so nothing is lost)

- **K4:** route validation (delivery boundary + identity/descriptor uniqueness +
  one-route-per-Edge-Node cardinality), license module `sink-sparkplug-b` + catalog
  tier, DI registration triad, production `ISinkReplayCapabilityClassifier`,
  **gateway-data-root resolution + identity-store singleton registration**.
- **K5:** wizard (mockup-first) + edit routing.
- **K6:** broker-in-CI + real Ignition/MQTT-Engine interop (ADR-0035 Open 4).
- **Post-K3:** material-schema generation-changing rebirth (`AdvanceGenerationAsync`).
- **Post-K4 cross-layer:** coordinated replay-sink hot replacement.
- **Core follow-up (surfaced 2026-07-19, §10.2):** give `ReplayRouteDriver`/worker a
  **Degraded + backoff + store-and-forward** path for a sustained transport outage on
  Begin/Rebirth, instead of terminal `Failed` — restoring parity with the legacy
  `SinkPublisher` outage behavior. A Core change, out of K3's Core-clean scope; K3 ships
  the bounded in-`RebirthAsync` reconnect (Option B) as the local mitigation.
- **Later:** clustered/standby lease; device-level DBIRTH/DDATA/DDEATH.

---

## 14. Open for the review pass (v2 → reality-check → v3)

All four blockers, six decisions, and the §10.2 outage posture are resolved. What
remains before freeze:

1. The external **reality-check pass** over this v2 (ChatGPT), per the plan-trail
   cadence — confirming the fold is faithful and surfacing anything missed.
2. Confirmation that the Core follow-up (§10.2 / §13) is acceptable as a *post-K3* item
   rather than a K3 blocker (owner already chose B + surfaced follow-up; noting it here
   for the review record).

Once the reality-check pass is clean, v2 → **v3 (frozen)** and implementation begins at
slice 1.
