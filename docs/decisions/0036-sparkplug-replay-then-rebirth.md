# ADR-0036: Sparkplug delivery, birth-then-historical-replay, and Edge Node session ownership

**Status:** Proposed (2026-07-13) — **v2, supersedes the withdrawn QoS-1/PUBACK
premise.** The first draft of this ADR (same day) proposed advancing the buffer
cursor only after every message received a broker PUBACK at QoS 1. A conformance
review established that is **impossible in conformant Sparkplug**: NBIRTH/DBIRTH/
NDATA/DDATA/DDEATH are **QoS 0** (no PUBACK); only the NDEATH Will is QoS 1. That
premise is withdrawn. This ADR replaces it.
**Date:** 2026-07-13
**Framing:** Sparkplug's ordered session model appears to conflict with EdgeConnect's
LOCKED store-and-forward (#8), never-cut-data (#7), replay ordering (#11), and
AtLeastOnce (#12). The original design tried to hold three things at once —
(1) strict Sparkplug conformance, (2) broker-acknowledged at-least-once,
(3) zero Core/buffer changes. **All three are not simultaneously achievable.** This
ADR records which gave, and locks the delivery, replay, sequencing, catalogue, and
ownership rules that make a *real* Sparkplug v1 rather than a Sparkplug-shaped MQTT
publisher. Companion: **ADR-0035** (scope/encoder/coexistence). Plan trail:
`docs/sessions/2026-07-13-sparkplug-b-sink-plan-v1.md` →
`…-v1-chatgpt-review.md` → `…-v2.md`.

## Context

Confirmed against the code and the Sparkplug B 3.0 specification:

- **QoS 0, no PUBACK.** Conformant Sparkplug publishes BIRTH/DATA/DDEATH at QoS 0,
  retain=false; only the NDEATH Will is QoS 1, retain=false, and **carries no
  `seq`**. There is therefore no broker acknowledgment to gate a cursor on.
- **`CanonicalDataPoint.SequenceNumber` is the wrong seq** (source-scoped,
  non-wrapping int64) — Sparkplug `seq` is a per-Edge-Node modulo-256 counter reset
  at each (re)birth. Unrelated; never reuse it.
- **The retry layer re-publishes.** `SinkPublisher` holds the cursor and re-sends a
  batch on `Success == false`. The MQTT PerTag "idempotent latest-wins topic" trick
  that made this safe is **invalid** here (a rewound `seq` in a live session makes a
  host demand a rebirth).
- **Sinks are decoupled from sources by the per-route buffer** (verified:
  `SourceHealthSnapshot` is pushed on the *source* side; the sink sees only drained
  points). No source-device lifecycle reaches a sink today — so device-level
  DBIRTH/DDATA/**DDEATH** would require a new Core path (deferred, Rule 8).
- **Birth needs the full metric set + current values before it is sent** — a
  first-seen catalogue cannot construct the initial NBIRTH (circular: DATA is
  illegal before birth announces the metric).

The rejected alternative — **live-only Sparkplug** (drop backlog, let EREMOS carry
history) — violates locked #7/#8 and stays rejected. The **QoS-1 non-standard mode**
was also rejected for v1: we ship conformant QoS 0 (user decision, 2026-07-13).

## Decision

### Rule 1 — QoS 0 delivery; honest best-effort, not broker-acked AtLeastOnce
BIRTH and DATA publish at **QoS 0, retain=false**; the **NDEATH Will is QoS 1,
retain=false, no `seq`**. `PublishResult.Success` is defined narrowly as *"every
generated MQTT publish completed at the local MQTTnet transport boundary with no
observable connection/send error"* — it does **NOT** mean the broker accepted,
persisted, or delivered the messages.

**Approved carve-out from locked #12 (user decision, 2026-07-13):** the Sparkplug
sink does **not** claim broker-acknowledged at-least-once. Store-and-forward still
buffers while offline and retries observable failures, but a QoS-0 ambiguity window
is unavoidable: an optimistic local-send-then-cursor-advance can still lose a point
if the broker never got it, and a local error after broker receipt can duplicate a
point on retry. This is documented, not hidden. On any observable **or uncertain**
send error, the sink **disconnects and establishes a new session before retrying**
(Rule 2/7), which preserves `seq` validity. #12 remains the platform default for
other sinks; this is a per-sink, spec-forced exception, recorded here.

Removed from the record (were false): "correct AtLeastOnce citizen", "fully
acknowledged batch", "true wire-ack", and any claim that hosts are *guaranteed* to
dedupe or historize replayed values.

**This is a typed capability, not just prose.** The sink exposes delivery
capabilities so the route validator — not merely the wizard — enforces the limit
(the delivery mode lives at the route, so a wizard warning alone is insufficient):
```
DeliveryCapabilities(
    SupportsStoreAndForward = true,
    AcknowledgementBoundary = LocalTransport)   // ordered: None=0 < LocalTransport=1 < Broker=2 < Application=3
// SupportsBrokerAcknowledgedAtLeastOnce is a COMPUTED property (AcknowledgementBoundary >= Broker),
// NOT a separately-stored flag — the ordered boundary is the single authoritative capability (no drift).
```
The current `DeliveryPolicy` (`AtMostOnce | AtLeastOnce`) **cannot express** this
distinction — `AtLeastOnce` is the only S&F-compatible mode, and Sparkplug needs S&F.
So `DeliveryPolicy` gains a protocol-neutral **`RequiredAcknowledgementBoundary`**
(`None | LocalTransport | Broker | Application`, additive; the locked enum is
unchanged). A Sparkplug route is `Mode = AtLeastOnce, RequiredAcknowledgementBoundary
= LocalTransport`. The boundary is a **totally-ordered enum with explicit numeric
values: `None=0 < LocalTransport=1 < Broker=2 < Application=3`**. **Route validation
MUST reject when `route.RequiredAcknowledgementBoundary > sink.AcknowledgementBoundary`**
(a typed error, not a warning) — a requirement exceeding what the sink can offer, not
"the AtLeastOnce+Sparkplug pairing" generally — and MUST reject an unknown/unparseable
boundary value **before** applying the comparison. The UI presents the boundary
qualifier, never a bare "AtLeastOnce".

### Rule 2 — Birth-then-historical-replay (corrected order + fields)
The sequence is **birth first, then replay** (the old "replay-then-rebirth" title
was backwards):
```
Load manifest + latest-value snapshot   (Rule 5 — before CONNECT)
Reserve+persist bdSeq                    (Rule 3)
Build NDEATH Will (bdSeq)
CONNECT (clean) → CONNACK
SUBSCRIBE exact NCMD topic → SUBACK      (Rule 4)
NBIRTH (seq=0, current-snapshot values, includes Node Control/Rebirth=false)
[device DBIRTHs — deferred, Rule 8]
Historical replay: NDATA with is_historical=true
Catch-up transition
Live
```
**Three distinct timestamp/flag fields** (the earlier draft conflated them):
- `metric.is_historical = true` on every replayed value;
- `metric.timestamp` = original UTC **acquisition** time (`DeviceTimestamp`);
- `payload.timestamp` = current UTC **publication** time.

Without `is_historical`, replaying old values moves the host's current value
backward. **Catch-up policy (locked choice):** at the replay boundary, mark any
records that arrived during replay as historical and emit a **final non-historical
latest-value update** per metric whose **host-visible state changed since birth**, so
the host's current value never steps through the backlog. **"Changed" compares full
host-visible state — value, null-state, datatype, quality + quality-reason, and
acquisition timestamp — not value alone** (a metric that went Good→Bad at the same
numeric value still needs the update). A change to a metric's **static metadata**
(name/datatype/unit/alias) is a **schema change → controlled rebirth**, not a final
update. (WS7's prototype compared `Value` only — a spike simplification; production
compares full state.) Live waits for drain (#11). We transport the historical indication and
timestamps; what a given host does with them is an **interop requirement for that
host** (ADR-0035 interop gate), not a protocol guarantee.

### Rule 3 — `seq` and `bdSeq` (corrected)
`seq` is a **single Edge-Node modulo-256 counter**; **every** NBIRTH/NDATA (and
DBIRTH/DDATA/DDEATH once device scope lands) participates; **NDEATH does not carry
`seq`.** NBIRTH starts at `0` as an EdgeConnect policy (the spec permits 0–255).

`bdSeq` **increments on every new MQTT CONNECT**, and the same value appears in that
session's CONNECT-Will NDEATH and its NBIRTH; a **same-session rebirth retains** the
current `bdSeq`. `bdSeq` **must be persisted and the next value reserved+written
*before* constructing CONNECT** (key at least `broker + group_id + edge_node_id`);
a skipped value after a crash is safe, reuse is not. In clustered/standby
deployments the identity must be protected by single ownership / a lease (Rule 7).
(Reverses the earlier "no bdSeq persistence needed" conclusion.)

### Rule 4 — Mandatory receive-only Rebirth NCMD
Publish-only does **not** exempt the one mandatory command. The Edge Node:
1. SUBSCRIBEs its **exact** `spBv1.0/{group_id}/NCMD/{edge_node_id}` topic at QoS 1
   (not a wildcard), and completes the SUBSCRIBE **before** birth;
2. includes `Node Control/Rebirth` = Boolean `false` in NBIRTH, **with no alias**;
3. on `Node Control/Rebirth = true`, **signals Core via `RebirthRequested` (Rule 6)**
   to obtain a **fresh coherent snapshot** — the original birth snapshot may be stale
   (replay in progress, post-`H` points unpublished, or quality/null changed).
   Core pauses the replay-aware route path, captures the current coherent snapshot,
   and calls a same-session rebirth on the sink, which **retains `bdSeq`, resets
   `seq = 0`, re-emits NBIRTH, and resumes**; **buffer cursor ownership is unchanged
   (Core-side)**;
4. **ignores** any other NCMD metric with a diagnostic event and **no side effect**;
5. **coalesces** repeated rebirth requests while a rebirth is underway;
6. **pauses DATA** while the new birth sequence is emitted.
This is a control-plane requirement, not a general command/write gateway. ACL
guidance: only trusted hosts may publish NCMD.

**Birth generation baseline (locked).** Every **successfully emitted NBIRTH starts a
new birth generation** and **replaces the baseline** `CompleteCatchUpAsync` (Rule 2)
compares against to compute the final non-historical update. Core must: (1) capture
the rebirth snapshot + its cutoff; (2) replace the current baseline **after NBIRTH
succeeds**; (3) compare final state against the **latest** successful NBIRTH, not the
initial one; (4) **test the changes-then-returns case** — initial birth `10` → rebirth
`20` → value returns to `10` before catch-up completes must still land `10`, not leave
the host at `20`. `RebirthRequested` (Rule 6) is **async, queue-based, and
non-reentrant**: the actor must not synchronously block on Core while Core is awaiting
an actor publish.

### Rule 5 — Metric manifest + latest-value snapshot before birth; global aliases
Before CONNECT the sink needs two inputs (a buffer scan alone is **insufficient** —
it need not contain each metric's *current* value):
- **Manifest** (per node metric): canonical identity, Sparkplug metric name,
  datatype, alias, optional unit/static properties.
- **Latest-value snapshot** (per metric): current value or `is_null=true`, capture
  timestamp, current quality.

Metric **name** must be **source-qualified and globally unique within the Edge
Node**, because in node-only mode (Rule 8) every metric shares one NBIRTH namespace
— a bare relative `TagPath` collides when two sources expose the same path (e.g.
`Line1/Temperature`), and global aliases do **not** repair a duplicate *name* in the
birth catalogue. Default:
```
{SourceInstanceId}/{DeviceId?}/{relative TagPath}
```
with an explicit override for a required customer convention. **Duplicate published
names inside the Edge Node fail validation before CONNECT.** Reject or warn on names
differing only by case. The **reserved control names** `bdSeq` and
`Node Control/Rebirth` are validated so canonical telemetry can never collide with
them.

**Two distinct keys — do not conflate (corrected):**
- **Sparkplug alias key** (scoped to the Edge Node's persisted alias state):
  `SourceInstanceId + DeviceId + canonical TagPath`. It **does NOT include `RouteId`**
  — recreating or moving a route must not renumber aliases when the Edge Node and its
  canonical metrics are unchanged (alias stability). Aliases are **globally unique per
  Edge Node**, from one persisted allocator, keyed by canonical identity (not the
  overridable display name); `Node Control/Rebirth` gets no alias; fail startup on a
  duplicate.
- **Snapshot persistence key** (per-route partition, Rule 5 snapshot store):
  `RouteId + SourceInstanceId + DeviceId + canonical TagPath`. `RouteId` partitions
  the per-route latest-value table; it does **not** flow into the alias key. A metric first seen **after** birth is a
**schema change** → controlled rebirth, never an unannounced alias. A datatype change
for an existing metric is likewise a schema change, not an ordinary DATA update.

**Never-observed metrics (locked policy).** The **persisted observed set is the
initial manifest** (Core has no complete protocol-neutral configured catalogue — WS2).
A genuinely-never-observed, otherwise-unknown metric is **absent from NBIRTH**; its
first observation is a schema change → controlled rebirth. `is_null=true` is used
**only** for a metric already in the manifest whose current canonical value is
explicitly null. v1 does **not** introduce a new browse/catalogue system to birth
never-read metrics.

**Snapshot consistency (locked).** Birth must read a **coherent point captured
atomically with `H`** — a latest-wins store alone cannot answer "as of `H`" (a value
updated past `H` before the read would overwrite the ≤`H` value). The production
`ILatestValueSnapshotProvider` persists the latest-value table in the **same per-route
store as the buffer**, and the **buffer append + snapshot upsert commit atomically**,
so `(H, snapshot)` is captured in one transaction/lock. **One component must own that
transaction** — a provider and the buffer as *separate* capabilities cannot guarantee
atomicity. K1 picks a concrete shape (plan v2.3 §3): a `SqliteRouteStore` that owns
`append canonical batch` + `upsert latest-value rows` + `capture replay boundary` +
`read coherent (boundary, snapshot)`, **or** an optional composite buffer capability
performing them under one transaction. **A Sparkplug B v1 route therefore requires
`BufferMode.StoreAndForward` (SqliteBuffer); `None` and `InMemory` are rejected at
route validation** — an unbuffered/in-memory route cannot satisfy persisted restart
recovery or same-store transactions. (The in-memory `IReplayBoundaryProvider` remains
valid spike evidence.) Crash cases (append-before-upsert, upsert-before-append,
update-after-`H`-before-read, restart rehydration, rollback) are tested (plan v2.3 §3).

**Snapshot manifest generation (locked).** Because the persisted observed set *is* the
manifest, a removed metric must not linger and be re-announced forever. A **material
route-schema change** — source, filter, tag mapping, published naming, or transform
output schema — **starts a new snapshot generation**; birth uses **only
current-generation rows**; prior-generation rows are retired/ignored. The **Edge-Node
alias store is separate and retains old alias reservations** (stability). Non-schema
changes (retry count, buffer size) do **not** reset the generation. A metric that
**silently disappears upstream with no config/schema signal cannot be inferred as
removed in node-only v1** — operator/config action is required (documented limitation).

### Rule 6 — Optional Core seams + an explicit replay-session lifecycle
The earlier absolute "no Core/buffer/`SinkPublisher` changes" is **withdrawn**. Base
`ISinkAdapter`/`IMessageBuffer` stay intact; Core adds **optional** capabilities and
**owns buffer phase/`H`/`C`/split/cursor/barrier** (Rule 7). A per-point publish seam
is **not sufficient**: an **empty route publishes no DATA yet must still CONNECT +
NBIRTH**, and startup/shutdown/rebirth are lifecycle events, not publish calls. The
seam is therefore an explicit **replay-session lifecycle** (names indicative),
driven by the Core replay-aware route path and **invoked even when no DATA batch
exists**:
```
BeginReplaySessionAsync(ReplaySessionStart, ct)   // snapshot(as-of-H)+boundary; CONNECT; NCMD; NBIRTH — even for an EMPTY buffer
PublishAsync(points, PublishContext{Phase,Epoch,Cutoff,BatchFirst,BatchLast}, ct)
CompleteCatchUpAsync(ReplaySessionCutover, ct)    // final non-historical update; enter Live
EndSessionAsync(ReplaySessionEnd, ct)             // graceful NDEATH-before-DISCONNECT on stop / config replacement
event RebirthRequested                            // sink → Core reverse signal (Rule 4): request a fresh coherent snapshot
```
Supporting seams: **`IReplayBoundaryProvider`** (atomic cursor+cutoff; WS1) and
**`ILatestValueSnapshotProvider`** (persisted, atomic; Rule 5). **`IMetricManifestProvider`
is REMOVED** — the manifest is a **read-only projection of the persisted observed
snapshot** (Rule 5), not a separate provider; v1 adds no catalogue system.
`IDeviceLifecycleProvider` stays deferred (Rule 8). Existing sinks (no capability) are
unaffected. **No Sparkplug-specific buffer or cursor** — Core owns the cursor and
`AckAsync`. (WS1 resolved: `ReplayCoordinator.IsDraining` does not cover cold-start
replay; the boundary provider supplies the signal.)

### Rule 7 — One single-owner session actor per Edge Node identity
**The actor owns Sparkplug protocol/session transitions and serializes all MQTT
publications**: `seq` allocation, `bdSeq` reserve/read, all birth/death sends, the
alias table, `Node Control/Rebirth` handling, and "session is suspect → reconnect".
**Core (RouteWorker / replay-aware path) owns buffer replay phases, `H`/`C`
boundaries, batch splitting, cursor advancement (`AckAsync`), and the route's
Replay→CatchUp→Live barrier** (Rule 6; plan v2.3 §1). The actor **reacts** to the
lifecycle/context Core supplies; it does **not** derive or advance buffer phase or the
cursor. It may keep internal `Replaying`/`CatchingUp` states, but these **mirror
Core's commands** — they are not the authority. Protocol-side state model:
`Stopped → LoadingSession → Connecting → SubscribingNCMD → Birthing →
(Core-driven Replaying/CatchingUp) → Live → Rebirthing → Stopping`. Effective identity
is `broker + group_id + edge_node_id`; **v1 rejects a second active destination that
resolves to the same descriptor** (plus MQTT Client-ID uniqueness per broker — plan
v2.3 §5 / WS3+WS8).

**Route cardinality — v1 is one route per Edge Node.** A single actor fixes `seq`
concurrency but does **not** by itself create a coherent node-wide birth snapshot or
replay barrier when several per-route buffers target one node (each route can be
independently replaying / catching-up / live with its own high-water mark and its
own slice of the manifest). v1 therefore constrains **one Sparkplug destination
(= one Edge Node) to exactly one active route**; reuse of the descriptor by another
route or destination is rejected at validation. Multiple-routes-per-node (union
manifest/snapshot, readiness barrier, node-wide replay coordination) is a later,
explicit capability — never an accidental behavior discovered in testing.

### Rule 8 — v1 is Edge-Node scope only; device lifecycle deferred
v1 publishes **NBIRTH/NDATA only**. DBIRTH/DDATA/**DDEATH** are deferred until a real
source→sink device-lifecycle provider (`IDeviceLifecycleProvider`) exists — "no new
data" cannot distinguish device-offline from idle/disabled/poor-quality. We do
**not** claim device-level conformance while omitting DDEATH.

## Consequences

**Positive:**
- Conformant QoS-0 Sparkplug; store-and-forward preserves data offline without
  faking a delivery guarantee the transport can't give.
- Birth is built from a real manifest + current snapshot; replay uses `is_historical`
  + correct dual timestamps, so a host's live value doesn't walk backward.
- `seq`/`bdSeq` are spec-correct (NDEATH excluded; bdSeq persisted); one actor makes
  ordering and lifecycle race-free.
- Core stays almost untouched — only an *optional, protocol-neutral* context
  extension, gated by capability; no Sparkplug logic leaks into the buffer.

**Negative / costs:**
- QoS-0 ambiguity window is real (possible loss on optimistic advance, possible
  duplication on retry-after-rebirth); consumers must dedupe by timestamp. Honest,
  documented, not a guarantee.
- The manifest + latest-value snapshot are new prerequisites with real sourcing work
  (browse/config/latest-value cache); C/E naming decisions become pre-implementation.
- Single-owner actor + persisted bdSeq/aliases are non-trivial; clustered/standby
  needs a lease.
- Node-only v1 means no device modeling yet — a real but honest limitation.

**Forbidden patterns:**
- Gating the cursor on a QoS-1 PUBACK or claiming broker-acked at-least-once for
  this sink (Rule 1).
- Putting `seq` in NDEATH; deriving `seq`/`bdSeq` from canonical fields; failing to
  persist `bdSeq` (Rule 3).
- Emitting DATA for a metric the current birth didn't announce; per-device (non-
  global) alias allocation; silent datatype/schema change (Rule 5).
- Building the initial birth from first-seen buffer points instead of a manifest +
  snapshot (Rule 5).
- A wildcard NCMD subscription, or acting on any NCMD other than Rebirth (Rule 4).
- Claiming device-level conformance / emitting DDATA without DDEATH + a lifecycle
  signal (Rule 8).
- Two owners for one `broker+group+edge_node` identity (Rule 7).
- Dropping buffered backlog to keep the session "clean" (the rejected live-only
  option).
- A Studio "Test Connection" that uses the production MQTT Client ID or registers the
  production NDEATH Will / NBIRTH / NCMD subscription — it could evict or disturb the
  live Edge Node session. Test Connection uses a unique temporary Client ID and does
  connect / authenticate / TLS-check / disconnect only, with no Sparkplug identity.

## Open / Pending
1. **Replay phase from the engine — RESOLVED (WS1).** `ReplayCoordinator.IsDraining`
   does NOT cover cold-start replay; the optional `IReplayBoundaryProvider` supplies
   an atomic `(cursor, CutoffExclusive)` and the phase is derived publisher-side.
   **Boundary split + cursor ownership + `AckAsync` stay in Core/RouteWorker** (they
   own the buffer cursor) — the sink actor never independently acks a sub-range (plan
   v2.3 §1). An explicit route↔sink **replay-session lifecycle** (begin-session →
   context publish → complete-catch-up) is required so NBIRTH occurs even for an
   **empty** route and always precedes DATA (plan v2.3 §2).
2. **Latest-value snapshot source — RESOLVED (WS2).** New protocol-neutral
   `ILatestValueSnapshotProvider`, **persisted, atomic with the buffer** (see Rule 5
   snapshot-consistency). Not `IRouteTap`, not a buffer scan, not source re-read.
3. **Host timestamp/historian conformance** — verify replay-with-`is_historical`
   against the target host(s) in the ADR-0035 interop gate.
4. **CLAUDE.md #12 amendment — DONE (2026-07-13, user-approved).** CLAUDE.md §3 #12
   and `ARCHITECTURE_BLUEPRINT.md` §19.7 + Appendix A now record the
   acknowledgment-boundary rule (broker-acked AtLeastOnce requires a Broker/App ack
   boundary; local-transport-only destinations like Sparkplug B v1 QoS 0 are
   S&F-durable but not broker-acked). No longer open.

## Reference
- **ADR-0035** — Sparkplug scope/encoder/coexistence (amended in lockstep).
- Plan trail: v1 → `…-v1-chatgpt-review.md` (the conformance review) → v2.
- CLAUDE.md §3 **#7/#8/#9/#10/#11/#12** (the locked decisions this reconciles).
- `ISinkAdapter` / `PublishResult` / `SinkPublisher` / `ReplayCoordinator` /
  `SourceHealthSnapshot` (contracts and the source/sink isolation boundary).
- Sparkplug B 3.0 specification (QoS/retain, seq/bdSeq, NDEATH-no-seq, Rebirth NCMD,
  is_historical, Clean-Session, birth manifest); Tahu `sparkplug_b.proto` (proto2)
  and issue #260 (zero-valued `seq` omission — golden-byte tests, ADR-0035 Rule 2).
