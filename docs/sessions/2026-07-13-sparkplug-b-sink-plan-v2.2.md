# Sparkplug B Sink — Plan (v2.2) — Post-Spike Implementation Plan & Go/No-Go

> **⚠️ SUPERSEDED by plan v2.3** (`2026-07-13-sparkplug-b-sink-plan-v2.3.md`) after the
> PR #177 review. v2.3 corrects: ack/split/cursor ownership stays Core-side (not the
> sink actor); an explicit route↔sink replay-session lifecycle (NBIRTH even for an
> empty route); crash-atomic snapshot-as-of-H; `DeliveryPolicy.RequiredAcknowledgementBoundary`;
> unified canonical-`TagPath` identity; never-observed = absent-until-first-seen; plus
> a snapshot-feed performance gate. Read v2.3 as the implementation plan; v2.2 is kept
> for trail continuity. (The §7 decisions below remain valid.)

**Date:** 2026-07-13
**Author:** Session with Sudhakar
**Status:** **GO for the protocol kernel** — §7 design decisions CONFIRMED
(2026-07-13). K0 execution prerequisites remain: the three chipped tracks land +
SqliteBuffer replay-boundary parity. Implementation-ready synthesis of the spike.
**Trail:** v1 → review → v2 → 2nd review → v2.1 (spike charter) → spike
(WS1/WS2/WS7 findings) → **v2.2 (this)**.
**ADRs:** 0035 (amended), 0036 (v2). **Branch:** `feat/sparkplug-b-ws1-spike`
(7 commits, 15 spike tests + 983 Core.Tests green).

---

## 1. Go/No-Go

**GO.** The three hardest unknowns are resolved with executable evidence and **no
locked-contract change**:
- **Replay context (WS1):** the cold-start replay signal doesn't exist today; the
  optional `IReplayBoundaryProvider` supplies it atomically; the replay-aware sink
  seam + split-at-cutoff + independent-ack + retry-without-republish all work.
- **Birth inputs (WS2):** no complete current-value source exists; a protocol-neutral
  `ILatestValueSnapshotProvider` fed post-transform supplies birth values a buffer
  scan cannot (A/B test).
- **Cutover (WS7):** the two-watermark (H, C) birth→replay→catch-up→final-update→live
  algorithm terminates finitely under live load, never steps the host value backward,
  and survives rebirth mid-replay.

Residual risk is **bounded and named** (§6): three chipped tracks, the WS2 restart
decision, and SqliteBuffer parity. None re-opens a validated result.

---

## 2. What the spike validated (promote, don't redesign)

| Seam (spike, internal) | Role | v2.2 disposition |
|---|---|---|
| `IReplayBoundaryProvider` + `ReplayBoundary` | atomic cursor+cutoff capture on the buffer | **Core, promote public**; add SqliteBuffer impl |
| `ILatestValueSnapshotProvider` + records | current-value snapshot for birth | **Core, promote public**; add a **persisted** impl; feed from RouteWorker |
| `PublishContext` / `ReplayPhase` / `IReplayAwareSinkAdapter` | replay-aware sink seam | **Core, promote public** |
| `ReplayAwareSinkPublisher` (WS1) | split/phase/ack orchestration | **logic migrates into the Sparkplug session actor** (Sparkplug complexity in the sink — ADR-0035 R1); Core keeps only the neutral seams |
| `ReplayCutoverCoordinator` (WS7) | birth→replay→catch-up→live orchestration | **logic migrates into the Sparkplug session actor** |
| `InMemoryLatestValueSnapshotProvider` (WS2) | in-memory feed prototype | replaced by the persisted Core impl |

**Placement rule (confirm — §7):** Core exposes the **neutral data seams** (boundary,
snapshot, publish-context) and the **snapshot feed**; the **Sparkplug-specific
orchestration** (cutover, seq/bdSeq, birth/death, aliases) lives in the
`Sinks.SparkplugB` session actor. This keeps Core protocol-agnostic (locked #1).

---

## 3. Locked design decisions (consolidated from the trail)

- **Separate coexisting assembly** `ElpisEdgeConnect.Sinks.SparkplugB`; MQTT/EREMOS
  path untouched (ADR-0035 R1).
- **`Google.Protobuf` + own encoder** from a pinned proto2 Tahu schema; no Tahu
  runtime dep; provenance/hash/toolchain pinned; **byte-level golden tests** incl.
  NBIRTH `seq=0` presence (Tahu #260) (ADR-0035 R2).
- **v1 scope:** Edge-Node-only (NBIRTH/NDATA); **mandatory receive-only Rebirth
  NCMD**; MQTT **3.1.1** (Clean Session); no Device/DBIRTH/DDATA/DDEATH, no Primary
  Host/STATE, no general NCMD/DCMD, no MQTT 5.0 (ADR-0035 R3).
- **QoS 0 delivery**; **not broker-acked AtLeastOnce** — typed `DeliveryCapabilities`
  + route validation rejects the pairing (ADR-0036 R1; CLAUDE.md #12 amended).
- **Birth-then-historical-replay**; `is_historical` + metric(acquisition)/payload
  (publication) timestamps; **two watermarks H,C**; host-safe final-update policy
  (ADR-0036 R2 / WS7).
- **`seq`** single Edge-Node mod-256; **NDEATH carries no seq**; **`bdSeq` persisted**,
  reserved before CONNECT, retained on same-session rebirth (ADR-0036 R3).
- **Source-qualified metric names** `{SourceInstanceId}/{DeviceId?}/{TagPath}`;
  **global persisted aliases**; reserved names validated; dup names rejected before
  CONNECT (ADR-0036 R5).
- **Single-owner session actor**; **one route per Edge Node**; identity
  `broker+group+edge_node` (ADR-0036 R7).

---

## 4. Core promotion & integration (Phase K1)

1. **Promote** the four Core seam types internal→public (§2), with XML docs
   (Core enforces CS1591). Add a short **ADR** confirming `IReplayBoundaryProvider`
   and `ILatestValueSnapshotProvider` as accepted optional Core capabilities (the
   locked `IMessageBuffer`/`ISinkAdapter` base contracts remain unchanged).
2. **Snapshot feed:** call the persisted `ILatestValueSnapshotProvider` from
   `RouteWorker`'s enqueue path (post-transform) — the values are what sinks receive.
3. **Buffer-assigned sequences:** the snapshot value must be tie-able to a buffer
   position (for birth as-of-H). `EnqueueAsync` currently returns void — add an
   **additive** way to learn the assigned sequence range (overload or small
   capability, same "don't amend the locked contract" discipline as WS1). *Approve
   the approach — §7.*
4. **SqliteBuffer parity:** implement `IReplayBoundaryProvider` on `SqliteBuffer`
   (capture cursor + append cutoff in one read transaction); parity tests vs the
   in-memory impl.

---

## 5. The Sparkplug kernel build (`Sinks.SparkplugB`)

- **K2 — wire + payload:** `SparkplugPayloadFactory` (message-specific builders so
  invalid payloads are unrepresentable — NDEATH-with-seq, DATA-with-name+alias,
  etc.), `SparkplugTopicBuilder` (`spBv1.0/{group}/{type}/{edge_node}`),
  `CanonicalToSparkplug{Value,Quality}Mapper` (datatype + null/quality/timestamp,
  ADR-0035 R5), the MQTT 3.1.1 connection profile (Clean Session, NDEATH Will with
  `bdSeq`, graceful NDEATH-before-DISCONNECT).
- **K3 — session actor:** `SparkplugSessionActor` (single owner per identity, state
  model from ADR-0036 R7) consuming the Core seams: capture H (boundary), birth from
  snapshot-as-of-H, replay/catch-up via the WS1 phase seam + WS7 two-watermark
  cutover, final-update, live; `SparkplugIdentityStateStore` (persisted bdSeq +
  global aliases); receive-only Rebirth NCMD (exact-topic QoS-1 subscribe before
  birth; `Node Control/Rebirth=false` no alias; coalesce; pause DATA during birth).
- **K4 — config/licensing:** `DeliveryCapabilities` + route validation; license key
  `sink-sparkplug-b`; config validator (identity, one-route cardinality, reject
  `PrimaryHostId`); DI registration triad + one line in `EdgeConnectComposition`.
- **K5 — Studio:** static wizard **mockup first** (fields locked in v2.1 §7) →
  `SparkplugSinkWizardModel` + `AddSparkplugDestination.razor` + `SinkEditRouter`
  wiring; Test-Connection uses a temp Client ID, no production Will/NBIRTH/NCMD.

---

## 6. Gating inputs before/within the kernel

1. **Chipped tracks (owners needed):** WS4 (MQTTnet QoS-0 semantics → K2 wording),
   WS5 (crash-safe bdSeq → K3 state store), WS3+WS8 (identity/cardinality → K4
   validator).
2. **WS2 restart-coverage decision (§7 Q1):** persist (recommended) vs seed vs
   delay — pins the persisted-snapshot impl (K1.2) and the never-observed-metric
   birth behavior.
3. **SqliteBuffer parity (K1.4):** named prerequisite, not optional.

---

## 7. Decisions — CONFIRMED 2026-07-13

1. **WS2 restart coverage → PERSIST.** The latest-value snapshot is persisted
   alongside the durable buffer; a genuinely-never-observed metric births as
   `is_null` (or delays CONNECT). Pins the K1.2 persisted-snapshot impl.
2. **Buffer sequence reporting → ADDITIVE.** Learn buffer-assigned sequences via an
   additive overload/capability; the locked `EnqueueAsync` is not amended.
3. **Placement → SPARKPLUG SINK ASSEMBLY.** Cutover/seq/birth orchestration lives in
   `Sinks.SparkplugB`; Core exposes only the neutral seams + snapshot feed. Core
   stays protocol-agnostic (locked #1).
4. **Naming/identity → DEFAULTS SHIP.** No named downstream consumer is dictating the
   scheme; source-qualified naming + `group=site / edge_node=gateway` ship as
   overridable wizard fields (ADR-0036 R5 / ADR-0035 R4).

With #1 confirmed, the §6 restart-decision gating input is resolved; the chipped
tracks and SqliteBuffer parity remain as K0 execution prerequisites.

---

## 8. Sequencing

```
K0  gating inputs: WS4, WS5, WS3+WS8 land; WS2 restart decision; SqliteBuffer parity
K1  promote Core seams public + snapshot feed + buffer-seq reporting
K2  Sparkplug wire + payload factory + mappers + connection profile   (golden tests)
K3  SparkplugSessionActor + identity state store + Rebirth NCMD
K4  DeliveryCapabilities/route validation + license + config validator
K5  wizard (mockup-first) + edit routing
K6  test + release gates (below)
```

---

## 9. Test / release gates (from v2.1 §4, carried)

- **Protocol golden/actor tests** (independent decoder): topic/QoS-0/retain, NBIRTH
  `seq=0` physically encoded, seq wrap, **NDEATH no seq**, `is_historical` + dual
  timestamps, `is_null`, Quality, Rebirth (bdSeq retained), no-DATA-during-rebirth,
  graceful NDEATH-before-DISCONNECT.
- **State-machine + failure injection** (from v2.1): disconnect at every phase,
  QoS-0 send failure, crash around bdSeq, schema change, cold start w/ backlog,
  live-during-drain, duplicate identity, ACL denial.
- **Broker integration unconditional in ≥1 CI job** (ties to the deferred "stand up
  CI" task — no CI exists yet).
- **Independent interop before release:** mock host + independent decoder + real
  **Ignition + MQTT Engine** (replayed values don't overwrite current value; alias
  resolution post-rebirth; near-max-packet births).

---

## 10. Recommendation

Approve **GO**. Assign the three chipped tracks, get the §7 decisions (especially
restart coverage), then execute K0→K6. The spike branch (`feat/sparkplug-b-ws1-spike`)
carries the validated seams; K1 promotes them and everything downstream is
conventional adapter work against a proven foundation.
