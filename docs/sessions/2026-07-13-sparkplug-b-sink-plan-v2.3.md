# Sparkplug B Sink — Plan (v2.3) — Post-Review Corrected Implementation Plan

**Date:** 2026-07-13
**Author:** Session with Sudhakar
**Status:** **GO — design ACCEPTED; K0 ACCEPTED; K1 gate RELEASED (2026-07-13).**
Spike evidence accepted and all round-1/2/3 corrections folded in; docs PR #178 merged.
K0 corrective pass reviewed and **accepted** (`…-sparkplug-b-k0-findings-v1.md`, 17
green tests). **Next: merge #179 → close #177 (unmerged) → delete the spike branch →
cut `feat/sparkplug-b-k1` from `master`.** Production hardening carried to K1/K3/K4
(K0 findings "Carry-forwards"). PR #177 stays spike evidence, not merged.
**Supersedes:** plan v2.2 (as the implementation plan). **ADRs:** 0035, 0036 (both
amended in lockstep with this doc).

> **Review disposition:** WS1/WS2/WS7 spike proofs are **accepted**. The problems were
> in the *synthesis*, not the tests. Six blocking corrections (§1–§6) + non-blocking
> (§7) are applied to the design/ADRs. The prototype code is unchanged (it is
> evidence, not production).

## 0.1 Round-2 review corrections (2026-07-13) — normative detail is in the ADRs
Applied after the second re-review; refine (do not reverse) §1–§7:
- **ADR-0036 Rule 6 rewritten** — the seam is an explicit **replay-session lifecycle**
  (`BeginReplaySession` / `PublishAsync(context)` / `CompleteCatchUp` / `EndSession`
  for graceful stop+config-replace / `RebirthRequested` reverse signal), **invoked
  even for an empty route** (NBIRTH with no DATA). `IMetricManifestProvider` **removed**
  — manifest = read-only projection of the persisted snapshot.
- **ADR-0036 Rule 7 reworded** — the actor owns Sparkplug session/protocol transitions
  only; **Core owns replay phase, `H`/`C`, splitting, cursor/`AckAsync`, barrier**. The
  actor's `Replaying`/`CatchingUp` states mirror Core's commands, not authority.
- **Snapshot atomicity has a concrete owner (§3, ADR-0036 Rule 5)** — one component
  owns the SQLite transaction (`SqliteRouteStore` append+upsert+boundary+coherent-read,
  or a composite buffer capability). **Sparkplug routes require
  `BufferMode.StoreAndForward`; `None`/`InMemory` are rejected at validation.**
- **NCMD Rebirth reverse handshake (§2, ADR-0036 Rule 4)** — on rebirth the actor
  signals Core (`RebirthRequested`); Core pauses the route path, captures a **fresh
  coherent snapshot**, and calls same-session rebirth (retain `bdSeq`, reset `seq`,
  NBIRTH, resume); cursor ownership unchanged. No reusing the stale birth snapshot.
- **Two distinct identity keys (§5)** — **snapshot persistence key** `RouteId + Source
  + Device + TagPath`; **Sparkplug alias key** `Source + Device + TagPath` (**no
  `RouteId`** → alias stability across route recreation). Both ADRs now say so.
- **Full-state final-update (§7 / ADR-0036 Rule 2)** — "changed since birth" compares
  value + null-state + datatype + quality + quality-reason + acquisition timestamp
  (not `Value` alone); static-metadata change = schema change → rebirth.
- **Delivery boundary is an ordered enum** `None=0 < LocalTransport=1 < Broker=2 <
  Application=3`; reject unknown before `required > available` (§4).
- **Client-ID uniqueness (§5 / WS3+WS8)** — validate the **MQTT Client ID unique per
  broker** across active Sparkplug *and* ordinary MQTT destinations, not only the
  `broker+group+edge_node` descriptor (two Edge Nodes sharing a Client ID evict each
  other).

**Round-3 additions (2026-07-13, ADR-0036 Rules 4/5):**
- **Birth-generation baseline** — every successful NBIRTH starts a new birth generation
  and **replaces** the baseline `CompleteCatchUpAsync` compares against; compare final
  state vs the **latest** NBIRTH, not the initial (a rebirth `10→20→10` must still land
  `10`). `RebirthRequested` is **async / queue-based / non-reentrant**. Test the
  changes-then-returns case.
- **Snapshot-manifest generation** — a **material route-schema change** (source, filter,
  tag mapping, published naming, transform output schema) starts a **new snapshot
  generation**; birth uses current-generation rows only, so a removed metric is not
  re-announced. Non-schema changes don't reset it; the Edge-Node alias store is separate
  and retains reservations. Silent upstream disappearance can't be inferred in node-only
  v1 (operator/config action required).

---

## 1. Layer ownership — split/ack/cursor stay in Core (corrects v2.2 §2)

**Finding:** WS1 proved the *publisher/worker* owns epoch, boundary split, and
independent sub-range ack (it owns the `BufferBatch` + cursor). v2.2 wrongly said that
logic migrates into `SparkplugSessionActor` — the actor cannot ack a buffer sub-range
without buffer/cursor access, which would leak route ownership into the sink or need
another Core contract.

**Correction — fixed split of responsibilities:**

| Core / RouteWorker (replay-aware route path) | Sparkplug sink actor |
|---|---|
| Capture `H` and `C`; classify + split buffer batches at the cutoff | MQTT connection / session |
| Own the route cursor and `AckAsync` (per-sub-range) | NBIRTH / NDATA / NDEATH construction |
| Supply `Replay / CatchUp / Live` context + lifecycle | `seq` / `bdSeq` |
| Own the finite route barrier (H→C→Live) | aliases, `Node Control/Rebirth` NCMD |
|  | `is_historical` flag + payload encoding |

The WS1 `ReplayAwareSinkPublisher` and WS7 `ReplayCutoverCoordinator` logic therefore
**stays Core-side** (it becomes the production replay-aware route path); the sink
consumes context + lifecycle callbacks and emits Sparkplug. **No buffer ack in the
sink actor.**

## 2. Explicit route↔sink replay-session lifecycle (new — corrects v2.2)

**Finding:** the `IReplayAwareSinkAdapter.PublishAsync(context)` seam is necessary but
**not sufficient** — a publish call is too late to model startup, and an **empty route
never publishes** yet the actor must still CONNECT + NBIRTH. ADR-0036 requires
manifest/snapshot load, `bdSeq` reservation, CONNECT, NCMD subscribe, and NBIRTH
**before** any DATA.

**Correction — an explicit optional lifecycle interface** (names indicative):
```
BeginReplaySessionAsync(ReplaySessionStart start, ct)   // snapshot(as-of-H), bdSeq, CONNECT, NCMD, NBIRTH — even with an empty buffer
PublishAsync(points, PublishContext context, ct)        // phase-tagged historical/live DATA
CompleteCatchUpAsync(ReplaySessionCutover cutover, ct)  // final non-historical update (vs the CURRENT birth generation), enter Live
EndSessionAsync(ReplaySessionEnd end, ct)               // graceful NDEATH-before-DISCONNECT on stop / config replacement
event RebirthRequested                                  // sink -> Core reverse signal (async, queue-based, NON-reentrant): request a fresh coherent snapshot
```
**Every successful NBIRTH starts a new birth generation and replaces the baseline**
`CompleteCatchUpAsync` compares against — a same-session rebirth that moves a metric
then reverts it must still land the correct value (see §0.1 / ADR-0036 Rule 4).
Must cover: cold start with backlog; **cold start with no backlog**; recovery after
failure; H/C capture; NBIRTH-before-DATA; final update + Live; shutdown / config
replacement. Define + prove this before promoting the interfaces to public.

## 3. Crash-atomic snapshot "as of H" (corrects v2.2 §7 restart wording)

**Finding:** a latest-wins store cannot answer "as of `H`" — a value updated past `H`
before `GetSnapshotAsync` overwrites the ≤`H` value; `RouteBufferSequence` alone
doesn't fix it.

**Correction (locked, ADR-0036 Rule 5):** the production `ILatestValueSnapshotProvider`
persists the latest-value table in the **same per-route store as the buffer**;
**buffer append + snapshot upsert commit atomically**, and `(H, snapshot)` is captured
in one transaction/lock. Tests (K1): crash after append-before-upsert; after
upsert-before-append; a metric updated after `H` before the read; restart +
rehydration; SQLite transaction rollback. (The in-memory prototype proved
*completeness*, not this atomicity.)

## 4. `DeliveryPolicy.RequiredAcknowledgementBoundary` (corrects the governance rep)

**Finding:** the governance amendment is correct in concept but unrepresentable — the
locked `DeliveryPolicy` has only `Mode (AtMostOnce | AtLeastOnce)`, and `AtLeastOnce`
is the only S&F-compatible mode, so the validator can't tell "AtLeastOnce needing
LocalTransport" from "AtLeastOnce needing Broker."

**Correction:** add a protocol-neutral **`RequiredAcknowledgementBoundary`**
(`None | LocalTransport | Broker | Application`) to `DeliveryPolicy` (additive; the
enum is unchanged). Sparkplug route = `Mode = AtLeastOnce, Required = LocalTransport`.
**Validation rejects when `route.Required > sink.AcknowledgementBoundary`.** CLAUDE.md
#12 + blueprint §19.7 reworded to the boundary-comparison. The wizard shows the
boundary qualifier, never a bare "AtLeastOnce".

## 5. One canonical identity everywhere (fixes the ADR-0035↔0036 contradiction)

**Finding:** ADR-0035 Rule 4 said metric name = relative `TagPath` (justified by
device separation that node-only v1 doesn't have); ADR-0036 said source-qualified.
WS2 prototype keyed by `TagName`.

**Correction:** ADR-0035 Rule 4 now matches ADR-0036 — **source-qualified name**
`{SourceInstanceId}/{DeviceId?}/{TagPath}`, and **two distinct canonical-`TagPath`
keys** (never the overridable display name): the **snapshot persistence key** =
`RouteId + SourceInstanceId + DeviceId + TagPath` (per-route partition), and the
**Sparkplug alias key** = `SourceInstanceId + DeviceId + TagPath` — **Edge-Node-scoped,
no `RouteId`**, so aliases stay stable when a route is recreated or moved. (Prototype's `TagName` key = spike shortcut,
corrected at promotion.)

## 6. Never-observed metric policy (locks v2.2 decision #1's loose end)

**Finding:** "birth as `is_null` **or** delay" is two policies, and `is_null` needs a
known identity/name/datatype/alias that Core has no manifest for.

**Correction (locked, ADR-0036 Rule 5):** the **persisted observed set is the initial
manifest**. A genuinely-never-observed, otherwise-unknown metric is **absent from
NBIRTH**; its first observation is a schema change → controlled rebirth. `is_null=true`
is used **only** for a metric already in the manifest whose current value is
explicitly null. v1 adds **no** new browse/catalogue system.

## 7. Non-blocking corrections (applied)

- **ADR-0035 licensing wording** → "No Tahu **runtime** dependency; obligations via
  OSS-compliance review" (dropped "no EPL exposure / ship-safe"). **Done.**
- **Value-map locked** (ADR-0035 Rule 5): Good = **omit** Quality; Uncertain =
  **`Quality=0` + `QualityReason`**. **Done.**
- **K0/K1 parity:** SqliteBuffer replay-boundary parity is a **K1** deliverable (part
  of Core promotion), **removed from K0**. See §8.
- **Snapshot-feed performance gate (new):** the persisted post-transform upsert is
  **opt-in per route**, **batched**, and **benchmarked** against the store-and-forward
  targets; a route with no snapshot consumer pays **effectively zero** cost. A K1 gate.
- **Stale statuses fixed:** WS1 → Complete; ADR-0035/0036 open items closed; WS2
  as-of-H claim corrected; v2.2 marked superseded.

## 8. Revised build phases

```
K0  gating inputs: chipped tracks WS4 (QoS-0), WS5 (bdSeq), WS3+WS8 (identity/cardinality)
K1  Core: promote seams public; the replay-aware ROUTE PATH (split/ack/cursor + finite
    barrier stays Core, §1); the lifecycle handshake (§2); persisted atomic snapshot
    provider (§3) + perf gate (§7); DeliveryPolicy.RequiredAcknowledgementBoundary (§4);
    SqliteBuffer replay-boundary parity; canonical-TagPath identity (§5)
K2  Sparkplug wire + payload factory + mappers (value/quality locked) + 3.1.1 profile   (golden tests)
K3  SparkplugSessionActor (consumes Core context+lifecycle) + bdSeq store + aliases + Rebirth NCMD
K4  route validation (boundary + identity + one-route cardinality) + license
K5  wizard (mockup-first; boundary-qualified delivery notice) + edit routing
K6  test + release gates (state-machine/failure injection, broker-in-CI, Ignition interop)
```

### K3 carry-forward — epoch gating in the actor acceptance tests
_(recorded from the PR #180 round-3 re-review; contract landed in K1.1, `ReplayEpochId`.)_

The `SparkplugSessionActor` must gate incoming lifecycle inputs on **both**
`ReplaySessionId` **and** `ReplayEpochId`, and a same-session rebirth candidate epoch
must be **strictly newer** than the current successful epoch. The K1.1 fake sink proves
stale-epoch handling but its helper compares only the epoch. **K3 production actor
acceptance tests must additionally cover:**
- a stale input carrying the **same numeric epoch but a different/stale session** (session
  must be compared, not just the epoch number);
- a **non-increasing rebirth epoch** (candidate epoch ≤ current successful epoch must be
  rejected, not promoted);
- promotion only after a **successful** NBIRTH (a failed rebirth leaves the prior epoch
  authoritative — already asserted at the contract layer, re-assert against the real actor).

## 9. Go/No-Go

**Conditional GO.** The hard unknowns are proven; the six corrections are design/doc
fixes that reshape the Core promotion (K1), not the spike results. Execute K0, then
promote the corrected seams through a **fresh production PR**. **PR #177 stays spike
evidence — do not merge as production.**

## 10. What changed vs v2.2
Ownership (§1) · lifecycle handshake (§2) · snapshot atomicity (§3) · delivery field
(§4) · identity unification (§5) · never-observed policy (§6) · perf gate + K0/K1
parity + value-map lock + licensing wording + stale-status (§7). ADR-0035 and ADR-0036
amended to match.
