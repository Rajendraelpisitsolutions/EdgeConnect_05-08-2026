# Sparkplug B — K3 Session Actor Plan v1 External Review

**Date:** 2026-07-19  
**Review target:** `2026-07-19-sparkplug-b-k3-session-actor-plan-v1.md`  
**Review posture:** architecture and plan review only; no implementation started  
**Verdict:** **CONDITIONAL GO to v2; NO-GO to freeze or implementation**

## 0. Executive verdict

The K3 boundary is directionally correct and is substantially better scoped than a
naive “Sparkplug-flavoured MQTT sink.” The plan correctly leaves replay phase,
watermarks, batch splitting, cursor advancement, epoch minting, and coherent
snapshot capture in Core; it puts MQTT/Sparkplug session state, `seq`, `bdSeq`,
aliases, births/deaths, and NCMD handling in the Sparkplug assembly. The K4/K5/K6
carry-forward is also honest.

I would not freeze v1 yet. Four architectural points must be locked in v2:

1. **A suspect transport session must recover through Core's existing operational
   rebirth path, never through an actor-internal reconnect that Core cannot see.**
2. **The concurrency mechanism must be selected, not left as “lock or mailbox,” and
   stale MQTT callbacks must be generation-gated.**
3. **“First-observed metric” and “material schema mutation” must be separated.** The
   former is already supported by K1.3's same-generation rebirth; the latter remains
   deferred and must fail closed in K3.
4. **Alias allocation must be a batch-atomic, durable, gateway-scoped store operation,**
   and the store's path/lifetime ownership must be compatible with K4's later DI
   composition.

No new Core API appears necessary for those four decisions. One cutover failure case
must be pinned and reality-checked against the existing driver: when the final
non-historical update encounters a suspect send, the actor must queue a current-epoch
rebirth before returning from `CompleteCatchUpAsync`; Core then reaches its next
control-plane barrier and rebirths before any Live DATA. Initial-birth and rebirth-
NBIRTH failures remain fatal and must not promote the candidate epoch.

---

## 1. What v1 already gets right

### 1.1 Correct ownership boundary

The plan correctly treats Core as the authority for replay session identity, epoch,
phase, H/C boundaries, ordering, and cursor acknowledgement. The Sparkplug side reacts
to those inputs and owns only wire/session concerns. Keep this boundary unchanged.

### 1.2 Correct delivery claim

`DeliveryCapabilities(SupportsStoreAndForward=true,
AcknowledgementBoundary=LocalTransport)` is the honest capability. QoS 0 success is a
local MQTTnet transport completion, not broker receipt. Observable or uncertain send
failure invalidates the current Sparkplug session.

### 1.3 Correct identity durability direction

A gateway-level SQLite store, keyed by normalized broker endpoint + group + Edge Node,
is the right production shape. `bdSeq` reserve-and-commit before CONNECT, skip rather
than reuse after a crash, and corruption fail-closed are all correct.

### 1.4 Correct milestone split

The following remain out of K3:

- K4: production registration, cross-object route validation, identity/cardinality,
  delivery-boundary integration, license/catalogue composition.
- K5: operator wizard and edit routing.
- K6: external broker CI and independent Ignition/MQTT Engine interoperability.
- Post-K3: material generation-changing schema rebirth, coordinated hot replacement,
  clustered ownership/lease, and device-level Sparkplug messages.

### 1.5 Correct epoch promotion rule

The authoritative actor epoch changes only after a successful NBIRTH. A failed
initial birth or rebirth must not install the candidate epoch, baseline, alias
manifest, or next DATA sequence as authoritative.

---

## 2. Verdicts on v1 §5 open decisions

| Decision | Review verdict | Required v2 wording |
|---|---|---|
| **5.1 Transport reconnect vs. Core rebirth** | **Approve option (a), tightened.** | A transport-suspect event queues `RequestRebirthAsync` for the current Core session/epoch. For a failed DATA send, the request must be accepted before `PublishAsync` returns a zero-accepted non-success result. `RebirthAsync` then performs a new MQTT CONNECT and reserves a new `bdSeq`. Never reconnect behind Core. |
| **5.2 Protocol substates vs. `AdapterState`** | **Approve internal substates; revise base mapping.** | `StartAsync` completion means the adapter-local runtime is started, so the coarse base state should be Running/ready even before a replay session exists. Sparkplug substates remain internal diagnostics. Active-session outage/rebirth maps through health/degraded reporting; do not make `AdapterState` the replay-phase authority. |
| **5.3 Coordinated hot replacement** | **Approve deferral.** | Keep K1.3's fail-closed rejection. Track this as a named **post-K4 coordinator/Core follow-up**, not casually as K3.x: it crosses Host reconciliation, route-driver ownership, and configuration apply semantics. |
| **5.4 Identity-store rooting/lifetime** | **Approve one gateway-wide store, with ownership correction.** | K3 exposes an injected store abstraction and SQLite implementation that takes an absolute path. K4 later resolves the gateway data root and registers one singleton. An adapter may idempotently initialize/use the shared store but must not own or dispose the gateway singleton. |
| **5.5 Manifest/naming validation split** | **Approve.** | K3 performs the final pre-CONNECT/pre-NBIRTH validation of the actual manifest and fails closed. K4 performs earlier config-time and cross-destination validation. Neither substitutes for the other. |
| **5.6 Lifecycle division** | **Approve with a more exact bracket.** | `Initialize`: normalize/validate immutable local config, no network I/O. `Start`: start actor-local resources and ensure store readiness, no CONNECT and no `bdSeq`. `Begin`: validate/resolve manifest, reserve `bdSeq`, CONNECT, SUBSCRIBE, NBIRTH. `End`: one graceful death attempt only for a valid born session, then disconnect. `Stop`: local-resource shutdown only, never a second death. |

### 2.1 The two operational rebirth branches

`RebirthAsync` must not infer transport action solely from the public
`RebirthReason`. The actor needs an internal latched recovery requirement:

- **Healthy-transport operational rebirth** — host NCMD or first-observed metric:
  reuse the MQTT connection, retain `bdSeq`, reset `seq`, emit NBIRTH from the fresh
  Core snapshot, install the candidate epoch only on success.
- **Transport-suspect operational rebirth** — disconnect, observable send error, or
  uncertain send completion: abandon/close the old client, reserve a new `bdSeq`,
  construct a new Will, CONNECT, SUBSCRIBE, emit NBIRTH, then install the candidate
  epoch only on success.

`RebirthReason.Other` is sufficient as a protocol-neutral diagnostic category; the
new-CONNECT decision is an actor-owned latch. When a host command and transport loss
coalesce, **transport-suspect wins** and the rebirth uses a new CONNECT/`bdSeq`.

---

## 3. Blocking finding B1 — reconnect and failure semantics are not yet executable

### 3.1 Required ordering for a DATA send failure

For every Replay, CatchUp, or Live `PublishAsync` call:

1. Validate session, epoch, phase, manifest membership, and current transport token.
2. Build one NDATA using the current `seq` without advancing it yet.
3. Attempt the local QoS-0 MQTTnet publish.
4. On local success, advance `seq` modulo 256 and return full success.
5. On observable or uncertain failure:
   - latch `TransportSuspect` / `RequiresNewConnect`;
   - capture the current authoritative Core session + epoch + host;
   - ensure the rebirth request is accepted before returning;
   - return non-success with `AcceptedCount=0` and no cursor-advancing claim.

Core's existing rebirth-before-retry ordering then captures a fresh snapshot, calls
`RebirthAsync`, and retries the same unacknowledged subrange under the newer epoch.
The actor must not cache and replay the failed Core batch itself.

### 3.2 Asynchronous disconnect while idle

An MQTT disconnect callback may arrive when Core is not inside a sink call. The
callback must:

- validate its immutable transport-generation token;
- atomically latch the transport as suspect;
- capture the current authoritative session/epoch/host snapshot;
- queue the non-reentrant rebirth request;
- return without reconnecting or publishing.

The K1.3 rebirth wake pulls an idle Live driver out of its wait. The next
`RebirthAsync` performs the new CONNECT.

### 3.3 `CompleteCatchUpAsync` failure needs an explicit special rule

`CompleteCatchUpAsync` has no `PublishResult`, so throwing on a final-update send
failure simply faults the route; it does not enter the normal publish retry path.
The no-Core-change recovery that composes with K1.3 is:

1. latch transport suspect;
2. await acceptance of a current-session/current-epoch rebirth request;
3. do **not** claim the final update was emitted internally;
4. return from `CompleteCatchUpAsync` so Core reaches its next control-plane barrier;
5. Core processes the pending rebirth before any subsequent Live DATA.

This exact ordering needs a reality-check against the current driver loop and a
composition test. If the driver can publish Live DATA in the same iteration after
`CompleteCatchUpAsync` without re-entering its barrier, that is a genuine Core gap and
must be surfaced before v3. Do not silently reconnect or silently enter Live as a
fallback.

### 3.4 Failures that remain fatal

- `BeginReplaySessionAsync`: CONNECT/SUBSCRIBE/NBIRTH failure throws; no
  authoritative birth exists and no epoch is promoted.
- `RebirthAsync`: CONNECT/SUBSCRIBE/NBIRTH failure throws; the candidate epoch is not
  promoted. Any transport on which NBIRTH had an observable/uncertain failure is
  abandoned as suspect.
- `EndSessionAsync`: death/disconnect failure is diagnostic/best-effort; no rebirth is
  requested during shutdown, and Core's bounded cleanup proceeds.

### 3.5 Slice-order consequence

Current slice 3 says “session-suspect reconnect-as-new-session,” while the Core
rebirth branch and `RebirthAsync` are not introduced until slice 6. That dependency is
backwards. Initial MQTT connection may be built before operational rebirth, but
transport-suspect recovery cannot be considered complete until the rebirth slice.

---

## 4. Blocking finding B2 — select one concurrency discipline

The phrase “a single publish/transition lock or a mailbox” is not implementation-
ready. K1.3 already serializes Core-driven sink lifecycle calls on one task; K3 still
must protect mutable session state from MQTT callbacks.

### 4.1 Recommended discipline

Use the governing component split:

```text
SparkplugSinkAdapter       thin IReplayAwareSinkAdapter façade
SparkplugSessionActor      sole mutable-state owner
SparkplugMqttTransport     CONNECT/SUBSCRIBE/PUBLISH/DISCONNECT only; no reconnect policy
SparkplugIdentityStateStore gateway-scoped persisted identity service
```

Inside the actor:

- one async serialization gate protects all lifecycle transitions, all MQTT sends,
  `seq`, current `bdSeq`, manifest/baseline installation, and authoritative
  session/epoch state;
- Core-driven adapter calls enter that gate in their existing serialized order;
- MQTT callbacks do **not** publish and do not mutate `seq`, `bdSeq`, aliases,
  manifest, or protocol phase;
- callbacks only validate an immutable connection-generation token, capture an
  immutable authoritative session/epoch snapshot, set atomic control latches, emit
  diagnostics, and queue the non-reentrant Core request;
- no actor gate is held while waiting for work that could re-enter the sink;
- the MQTT transport must have automatic reconnect disabled. Reconnect policy belongs
  only to the actor through Core's rebirth lifecycle.

### 4.2 Connection-generation token is mandatory

Each created MQTT client/CONNECT attempt gets a monotonically increasing in-memory
transport generation. Every disconnect and application-message callback carries that
generation. After a replacement client becomes current, callbacks from old clients
are ignored.

Without this token, a delayed old-client disconnect can poison the new session, and a
delayed old NCMD can request another rebirth under the wrong epoch.

For NCMD, capture both the transport generation and the authoritative Core
session/epoch at receipt time. A command received under epoch N but processed after
epoch N+1 must remain an epoch-N request and be deterministically ignored/coalesced by
the host, not be rebound to the new epoch.

### 4.3 Control has priority over the next DATA send

A plain FIFO mailbox is insufficient unless control messages have priority: a queued
rebirth/disconnect behind multiple DATA commands would violate “finish the in-flight
publish decision, then pause further DATA.” The gate + atomic-latch design avoids
that ambiguity: every public send path checks control latches before allocating the
next `seq` or entering MQTTnet.

### 4.4 Facade/actor naming must be corrected

V1 alternates between “`SparkplugSinkAdapter` is the actor” and the governing design's
separate facade + actor. Select the existing split in v2. The facade implements the
Core interface; the actor is the sole state owner. This improves testability and keeps
transport callbacks from escaping into adapter lifecycle code.

---

## 5. Blocking finding B3 — separate schema growth from schema mutation

### 5.1 First-observed metric: supported in K3

A metric first observed after the current birth is already represented in Core's
same-generation persisted observed set. K3 should detect it before encoding DATA:

1. canonical key is not in the actor's current announced manifest;
2. do not allocate a DATA `seq`, do not publish, and return no acceptance;
3. await acceptance of `RequestRebirthAsync(..., SchemaChange)` for the current
   session/epoch;
4. Core captures a fresh, populated snapshot at a new H and calls same-session
   `RebirthAsync` under a newer epoch;
5. K3 allocates any missing stable aliases, emits the new NBIRTH, and Core retries the
   unacknowledged triggering subrange.

This is **same-generation operational manifest growth**, not the deferred material
schema-generation feature.

### 5.2 Existing metric's material schema mutation: deferred and fail-closed

K3 cannot safely apply a new datatype/name/identity/static schema to an already
announced metric under the fixed Core generation. At minimum, classify these as
material:

- canonical identity or source-qualified published name change;
- datatype change;
- alias reassignment or alias-key inconsistency;
- unit or other NBIRTH static-property change when that property is part of the
  announced manifest.

V2 must pin the complete static-field set. When K3 sees such a mutation, it must fail
closed with a typed diagnostic/route fault. It must not silently encode it as NDATA,
allocate a second alias, or pretend a same-generation rebirth solves the generation
contract. The generation-changing follow-up remains responsible for that path.

### 5.3 Final-update comparator is dynamic-state only

The final non-historical catch-up update handles dynamic host-visible state under an
unchanged manifest. Static schema differences are detected by the schema classifier,
not emitted as a final DATA update.

Use two pieces of actor state for the current successful birth:

- an immutable, wire-normalized birth baseline per canonical metric;
- a monotonic `dirtySinceBirth` set that is never cleared during that birth epoch.

For each Replay/CatchUp point, normalize the state and mark the metric dirty if it
differs from the current birth baseline. At cutover, union that dirty set with any
final-snapshot-vs-baseline difference and emit the final snapshot value for the union.
This preserves the required `10 → 20 → 10` case: the metric stays dirty and the final
non-historical `10` is emitted.

Compare the **wire-visible normalized form**, not raw CLR object equality:

- mapped Sparkplug datatype and value representation;
- `is_null` and value-arm presence;
- mapped quality and quality reason;
- acquisition timestamp at the exact encoded precision;
- byte arrays by contents;
- any numeric coercion/precision rule already locked in K2.

After every successful NBIRTH, replace both the baseline and dirty set atomically with
the new birth generation. A failed NBIRTH changes neither.

---

## 6. Blocking finding B4 — identity store and alias allocation need production invariants

### 6.1 Gateway-scoped service, not actor-owned file handling

Expose an injected `ISparkplugIdentityStateStore`-style abstraction in K3. The SQLite
implementation takes an absolute database path. K4 later resolves the gateway data
root and registers one shared instance for all Sparkplug destinations.

Do not hard-code `data/sparkplug/identity-state.db` inside the actor. Do not let each
adapter independently own/dispose a supposedly gateway-wide singleton.

### 6.2 Durable `bdSeq` reservation

For normalized identity `(BrokerEndpoint, GroupId, EdgeNodeId)`:

- begin a serialized `BEGIN IMMEDIATE` transaction;
- read and validate the previous value;
- compute the next value with checked arithmetic;
- persist and commit durably before returning it;
- only then construct the CONNECT options/Will;
- any attempted new CONNECT consumes its reserved value, even if CONNECT later fails;
- unknown schema version, malformed row, negative/out-of-range value, or commit
  failure prevents CONNECT and fails closed;
- never silently reset to zero.

Pin SQLite durability settings explicitly so “commit before CONNECT” is a durability
claim, not merely an in-process visibility claim.

### 6.3 Batch-atomic alias resolution

Before allocation, validate the complete proposed birth manifest. Then resolve all
aliases for the node in one transaction:

1. load and validate existing mappings;
2. preserve every existing canonical-key → alias mapping;
3. sort missing canonical keys by a deterministic canonical comparer;
4. allocate all missing aliases using the K2 alias value type and checked arithmetic;
5. enforce unique constraints on `(node_identity, canonical_key)` and
   `(node_identity, alias)`;
6. commit the whole set or none of it;
7. return one immutable alias map used by both NBIRTH and subsequent NDATA.

V1 should not automatically delete or recycle aliases when metrics disappear from a
particular route/snapshot. Persisted mappings survive route recreation and temporary
absence. Compaction/reclamation requires a separately governed policy.

### 6.4 Identity and comparison details to pin

- Use Core's normalized `BrokerEndpoint` representation rather than an ad-hoc
  concatenated endpoint string.
- Alias canonical key remains `SourceInstanceId + optional DeviceId + canonical
  TagPath`; it excludes `RouteId` and display-name override.
- Pin the exact string comparer/collation for canonical keys and published-name
  duplicate checks, including a test for case-only pairs. Do not inherit SQLite or
  dictionary defaults accidentally.
- The reserved control metrics have no telemetry alias allocation.
- Schema creation/migration must be versioned; an unknown future version fails
  closed.
- Concurrent calls from different node identities and separate store connections must
  remain unique and monotonic.

---

## 7. Required correctness clarifications before v3 freeze

### 7.1 `seq` commit point

- NBIRTH is encoded with `seq=0`.
- The next successful NDATA uses `seq=1`.
- For each NDATA attempt, encode using the current counter and advance only after
  local MQTTnet success.
- On observable/uncertain failure, do not reuse the old Sparkplug session; the next
  successful birth resets the counter to zero.
- No `seq` is consumed by validation failure, unknown-manifest detection, stale input,
  or a publish rejected before MQTTnet is entered.
- NDEATH has no `seq`.

### 7.2 Session/epoch input policy

V2 must define behavior per surface, not merely “reject/ignore” globally:

- stale MQTT callback from an old transport generation: ignore + diagnostic counter;
- `PublishAsync`/`CompleteCatchUpAsync` carrying a different session or epoch:
  fail closed as a lifecycle invariant violation rather than returning a retryable
  publish failure forever;
- `RebirthAsync` with a different session, non-increasing epoch, or unexpected
  candidate ordering: fail closed/throw;
- initial Begin may install a session only after successful NBIRTH;
- actor-authoritative session/epoch pair changes atomically with baseline/manifest.

Required actor tests include same numeric epoch under a different session and a
non-increasing rebirth epoch.

### 7.3 NCMD parse/behavior matrix

Pin all cases:

- exact current NCMD topic + current transport generation only;
- valid Boolean `Node Control/Rebirth=true`: request once;
- false, null, wrong datatype, malformed protobuf, missing metric: no side effect +
  diagnostic;
- unknown metrics: no side effect for those metrics + diagnostic;
- a payload containing valid Rebirth plus unknown extras: recommended behavior is
  request once and diagnose the extras;
- duplicates while a request is pending/rebirth active: coalesce;
- an in-flight DATA publish may finish, but no next DATA send starts after the control
  latch is observed;
- no general NCMD/DCMD write handling is introduced.

### 7.4 Graceful end

For a current, authoritative, connected, successfully born session:

1. suppress further control requests/reconnects;
2. attempt explicit NDEATH using the current `bdSeq` and no `seq`;
3. cleanly disconnect;
4. mark the protocol session stopped;
5. make repeated End/Stop/Dispose calls idempotent.

No explicit death is attempted for a Begin that never succeeded. After an already
observed disconnect/suspect session, cleanup closes resources but must not invent a
new connection merely to send death. `StopAsync` and `DisposeAsync` never emit a
second death.

### 7.5 Health mapping

Keep protocol substate internal. Suggested mapping through the existing health shape:

- **Healthy:** adapter runtime/store are ready and either no Core session has begun
  yet or the active session is Live.
- **Degraded:** active session is connecting, subscribing, birthing, replaying,
  catching up, rebirthing, disconnected/suspect, or waiting for Core rebirth, while
  the actor loop remains operational.
- **Unhealthy/Faulted:** store corruption/schema incompatibility, actor-loop failure,
  illegal lifecycle transition, or unrecoverable initialization/configuration error.

Include current protocol state, authoritative session/epoch, transport generation,
last successful birth, `bdSeq` (safe numeric diagnostic), last error code/time, and
pending-rebirth flags. Do not expose credentials.

### 7.6 Base adapter methods

The context-free base `ISinkAdapter.PublishAsync(points, ct)` must fail closed because
Core's replay path must never call it. `UpdateCurrentValuesAsync` should likewise be
explicitly unsupported for this push sink. Test both so accidental routing cannot
silently bypass Sparkplug lifecycle context.

### 7.7 Test-environment wording

Replace “no broker in unit tests” with:

> K3 uses no external broker process or environment dependency. Pure actor/store
> tests use injected fakes; transport integration tests use an in-process MQTTnet
> server. External broker CI and independent host interoperability remain K6.

### 7.8 Project-change wording

Replace “No change to `ElpisEdgeConnect.Core` or any existing project” with:

> No Core API/behavior change is expected. Changes are limited to the SparkplugB
> assembly/tests plus necessary solution/project/test-infrastructure metadata. A
> discovered Core semantic gap stops the slice and is surfaced explicitly.

---

## 8. Required transition matrix for v2

| Trigger | Core replay session | Core epoch | MQTT connection | `bdSeq` | Wire `seq` | Cursor consequence |
|---|---|---|---|---|---|---|
| **Initial Begin** | New/current session | Initial candidate; authoritative only after NBIRTH | New CONNECT | Reserve new before CONNECT | NBIRTH `0`, next DATA `1` | Core begins replay only after Begin succeeds |
| **Host Rebirth NCMD, transport healthy** | Same | Strictly newer candidate | Reuse | Retain | Reset; NBIRTH `0` | Core keeps cursor, captures fresh H/snapshot, re-drives |
| **First-observed metric, transport healthy** | Same | Strictly newer candidate | Reuse | Retain | Reset; NBIRTH `0` | Triggering subrange remains unacknowledged and is retried |
| **Transport error/disconnect** | Same Core session; new MQTT session | Strictly newer candidate | Replace with new CONNECT | Reserve new | Reset; NBIRTH `0` | Failed subrange has zero acceptance; Core rebirths before retry |
| **Host command + transport loss coalesced** | Same | One newer candidate | Replace | Reserve new | Reset; NBIRTH `0` | One rebirth; transport-suspect branch wins |
| **Final-update send becomes suspect** | Same | Current request, then newer candidate | Replace during rebirth | Reserve new | Reset at replacement birth | Queue request before cutover returns; barrier must run before Live DATA |
| **Rebirth NBIRTH fails** | Old session identity remains recorded | Candidate not promoted | Attempted transport is abandoned/suspect | Reserved new value is never reused | No further DATA | Route faults; no cursor advancement by actor |
| **Graceful End** | Ends | Unchanged | Current valid connection only | Current | NDEATH has no `seq` | No reconnect/rebirth; Core cleanup continues |

The phrase “new session” must distinguish **new MQTT/Sparkplug transport session**
from **new Core `ReplaySessionId`**. Operational rebirth keeps the Core session id and
advances only the replay epoch.

---

## 9. Recommended seven-slice decomposition for v2

| Slice | Content | Exit evidence |
|---|---|---|
| **1 — façade, config, actor skeleton** | `SparkplugSinkConfiguration`; local validation; thin adapter + sole actor + transport interface; internal protocol states; one serialization discipline; base overloads fail closed; LocalTransport capability | lifecycle/state-transition tests; capability test; illegal call tests; no MQTT |
| **2 — gateway identity store** | versioned SQLite schema; durable `bdSeq`; batch-atomic alias allocator; injected absolute path; checked arithmetic; corruption/concurrency/no-reuse | K0 crash matrix against production store; reopen/concurrent-instance tests; alias atomicity and stability |
| **3 — pure birth-plan/mapping** | canonical identity/name mapping; manifest validation; reserved/duplicate detection; alias resolution; wire-normalized birth baseline + dirty comparator; material-schema classifier | empty/populated birth plans; case/comparer tests; byte/timestamp/quality comparator; `10→20→10`; material mutation fails closed |
| **4 — MQTT transport + initial Begin** | clean MQTT 3.1.1 CONNECT; QoS-1 Will; exact NCMD SUBSCRIBE; NBIRTH; connection-generation token; initial epoch/baseline promotion | in-process server ordering `CONNECT→SUBSCRIBE→NBIRTH`; Will contents; failed CONNECT/SUBSCRIBE/NBIRTH do not promote |
| **5 — Replay/CatchUp/Live DATA** | context/session/epoch/phase gating; historical flag; QoS-0 local-boundary result; `seq` commit point; final update; first-observed rebirth-before-retry signal | strict full/zero acceptance; stale session/epoch; no seq on unknown metric; final-update matrix; cutover suspect composition reality-check |
| **6 — operational rebirth + end** | NCMD parse/coalesce; healthy-transport same-session rebirth; transport-suspect new CONNECT/`bdSeq`; async idle disconnect; stale callback suppression; graceful End/Stop idempotence | host NCMD retains `bdSeq`; transport failure changes it; old callbacks ignored; no DATA during rebirth; NDEATH before disconnect |
| **7 — health, diagnostics, failure sweep** | 3-way health; counters; redaction; every-phase disconnect/failure; full actor trace; regression gates | acceptance matrix green; deterministic synchronization only; full unfiltered regressions |

The transport slice intentionally proves only initial connection. “Reconnect” exits in
slice 6, after the operational rebirth seam exists.

---

## 10. Acceptance tests to add to the K3 gate

### 10.1 Identity/store

- empty store starts `bdSeq` at the locked initial value and increments;
- commit succeeds then simulated crash-before-CONNECT skips the value on reopen;
- rollback/commit failure never returns a value and prevents CONNECT;
- malformed/negative/overflow/unknown-schema rows fail closed;
- 200 concurrent reservations are unique and monotonic;
- different node identities are independent;
- aliases persist across actor/route recreation;
- missing aliases for a multi-metric manifest allocate all-or-none in deterministic
  order;
- duplicate alias/canonical-key corruption fails closed;
- absent metrics do not cause alias reuse.

### 10.2 Birth/epoch/sequence

- empty route still CONNECTs, SUBSCRIBEs, and NBIRTHs;
- NBIRTH physically carries `seq=0`; NDEATH carries no `seq`;
- successful birth promotes session/epoch/baseline atomically;
- failed initial NBIRTH promotes nothing;
- failed rebirth NBIRTH keeps the previous authoritative epoch;
- stale session with the same numeric epoch is rejected;
- stale epoch in the current session is rejected;
- non-increasing rebirth epoch is rejected;
- DATA `seq` advances only after local success and wraps under the K2 value policy.

### 10.3 Transport recovery

- observable publish failure requests rebirth before returning zero-accepted failure;
- uncertain send completion follows the same path;
- async disconnect while Live-idle requests rebirth and wakes Core;
- transport-suspect rebirth reserves a new `bdSeq` and new Will;
- host-command rebirth on a healthy connection retains `bdSeq`;
- simultaneous host command + disconnect produces one rebirth and a new CONNECT;
- delayed disconnect callback from the replaced client is ignored;
- delayed NCMD from an old transport generation is ignored;
- final-update send failure queues rebirth before cutover returns and no Live DATA is
  emitted first.

### 10.4 Manifest/schema/final update

- duplicate and reserved published names fail before alias allocation/CONNECT;
- comparer behavior for case-only names is explicit;
- first-observed metric produces no MQTT publish, no `seq`, and no acceptance before
  rebirth;
- first-observed metric is present in the new NBIRTH before its retried DATA;
- datatype/name/identity/unit/static-property mutation follows the pinned material
  policy and fails closed in K3;
- value, null-state, quality, quality-reason, acquisition time, and byte contents can
  independently dirty a metric;
- raw CLR differences that normalize to identical wire state do not cause a false
  final update;
- `10→20→10` emits final `10`;
- a successful same-session rebirth replaces the baseline; a failed one does not.

### 10.5 NCMD/end/health

- exact NCMD topic only; wildcard/other node ignored;
- valid true requests; false/null/wrong type/malformed/missing ignored with diagnostics;
- valid control plus unknown extras requests once and diagnoses extras;
- repeats coalesce while pending/in progress;
- an in-flight DATA decision may finish, but the next DATA waits for rebirth;
- End emits explicit death before clean disconnect exactly once;
- failed Begin emits no End death;
- already-disconnected/suspect End does not reconnect;
- Stop/Dispose emit no second death;
- health distinguishes ready-no-session, Live, rebirthing/suspect, and faulted store.

### 10.6 Determinism

No `Thread.Sleep`, timing races, external broker, or polling for assertions. Use
`TaskCompletionSource`, explicit transport hooks, bounded channels/events, injected
clock/time provider, and in-process server observations.

---

## 11. Exact v2 editing checklist

1. **§1.1:** restore the governing thin-adapter + sole-actor + transport split.
2. **§1.3:** state that the store is gateway-scoped/injected and K4 owns path rooting
   and singleton composition.
3. **§1.6:** define baseline + monotonic dirty set + wire-normalized comparator; move
   static schema differences to the schema classifier.
4. **§1.8 / §3.3 / §5.1:** lock Core-mediated transport recovery, two rebirth
   branches, request-before-not-full ordering, async idle disconnect, and the special
   `CompleteCatchUpAsync` failure rule.
5. **§2:** replace the broad “no material-schema rebirth” wording with the explicit
   first-observed-vs-material-mutation taxonomy.
6. **§3.1:** replace “lock or mailbox” with the selected serialization gate + atomic
   callback latches + connection-generation token.
7. **§4:** reorder slices to remove reconnect-before-rebirth and adopt the revised
   seven-slice table.
8. **§5:** convert all six open decisions into locked v2 decisions using §2 above.
9. **§6:** add the transition matrix and acceptance cases; correct the broker/project
   wording; add context-free base-method tests.
10. **§7:** track coordinated hot replacement as a named post-K4 cross-layer follow-up.
11. Add a **reality-check item** for the final-update failure composition with the
    current K1.3 phase loop. Any failure of that composition is the only likely Core
    gap exposed by this review.

---

## 12. Sign-off condition

K3 is ready for the v2 → reality-check pass when all four blocking findings are
resolved in the plan, the transition matrix is adopted, the cutover-failure ordering
is confirmed against the current `ReplayRouteDriver`, and the acceptance matrix is
attached to the slices. Implementation should remain paused until the resulting v3 is
frozen.
