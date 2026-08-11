# Sparkplug B Sink — Plan (v2)

**Date:** 2026-07-13
**Author:** Session with Sudhakar
**Status:** Post-review, decisions locked — **spike next, not full build**
**Trail:** v1 (`…-sparkplug-b-sink-plan-v1.md`) → conformance review
(`…-sparkplug-b-plan-v1-chatgpt-review.md`) → **v2 (this)**.
**ADRs:** `docs/decisions/0035-sparkplug-b-northbound-standard.md` (amended),
`docs/decisions/0036-sparkplug-replay-then-rebirth.md` (**replaced** — QoS-1 premise
withdrawn).

> **What changed from v1:** the conformance review rejected the QoS-1/PUBACK delivery
> design (conformant Sparkplug is QoS 0, no PUBACK) and showed the "publish-only
> slice" understated scope. v1 is now a **real Sparkplug kernel**, scoped down to
> **Edge-Node-only** to stay honest and shippable. Sparkplug remains #1 in the
> post-roadmap sequence, but it is a **materially bigger** first item than v1
> implied — registered and accepted.

---

## 0. Locked decisions

| # | Decision | Choice | Source |
|---|----------|--------|--------|
| A | Delivery guarantee | **Standard QoS 0**; store-and-forward preserves data + retries observable errors, but **no broker-acked at-least-once** — a spec-forced carve-out from locked #12 **for this sink** (documented QoS-0 ambiguity window). | user 2026-07-13 |
| B | Protobuf | **`Google.Protobuf` + own encoder** from a pinned proto2 Tahu `.proto`; provenance/hash/toolchain pinned; golden byte tests; no legal conclusion in ADR. | user + review |
| C | Device scope | **Edge-Node-only v1** (NBIRTH/NDATA). DBIRTH/DDATA/**DDEATH** deferred until a source→sink lifecycle provider exists. | user 2026-07-13 |
| D | MQTT version | **3.1.1 only** (Clean Session=true); 5.0 later. | user 2026-07-13 |
| E | Command plane | **Receive-only `Node Control/Rebirth` NCMD only** (mandatory); all other NCMD/DCMD rejected. Primary Host/STATE deferred; populated `PrimaryHostId` rejected. | review |
| F | Replay | **Birth → historical replay (`is_historical`) → catch-up → live**; manifest + latest-value snapshot required before CONNECT. | review |
| G | Identity/naming | `group_id`=site, `edge_node_id`=gateway; metric name=deterministic relative `TagPath` (+override); **global persisted aliases**; one owner per `broker+group+edge_node`. Now **pre-implementation**, not open. | review |

Code-verified this session: **no source→sink device-lifecycle signal exists**
(sinks are behind the per-route buffer by design) → confirms C. Engine already has
`ReplayCoordinator`/`RetryStateMachine` → spike whether replay phase is surfaceable
(bears on the Core extension, §3).

---

## 1. Scope

**In (v1):** MQTT 3.1.1 transport with Clean Session + NDEATH Will; CONNECT →
SUBSCRIBE exact NCMD → NBIRTH(seq 0, snapshot values, `Node Control/Rebirth=false`)
→ historical replay (NDATA `is_historical`) → catch-up → live NDATA; single
Edge-Node `seq` (mod 256), persisted `bdSeq`; receive-only Rebirth NCMD; global
persisted alias table; datatype + null/quality/timestamp map; TLS + user/pass;
manifest + latest-value snapshot providers; single-owner session actor; QoS-0
delivery contract; license gate; wizard + edit routing; test kernel + broker + mock
host + interop gate.

**Out (deferred, each its own slice):** all Device-level behavior
(DBIRTH/DDATA/DDEATH) + lifecycle provider; Primary Host/STATE; general NCMD & DCMD
writes; MQTT 5.0; DataSet/Template metrics; mutual TLS. Forward-compat config fields
may exist but are **validation-rejected** outside the supported subset (ADR-0033
discipline).

---

## 2. Component split (v2 — from the review, adopted)

```
SparkplugBSinkAdapter            thin ISinkAdapter facade (+ ISessionTrackingSink)
SparkplugSessionActor            SOLE owner: seq alloc, bdSeq reserve/read, all
                                 sends, alias table, replay transitions, suspect→
                                 reconnect. State: Stopped→LoadingManifest→
                                 Connecting→SubscribingNCMD→Birthing→Replaying→
                                 CatchingUp→Live→Rebirthing→Stopping
SparkplugMqttTransport           CONNECT/SUBSCRIBE/PUBLISH/DISCONNECT only (3.1.1)
SparkplugPayloadFactory          BuildNBirth / BuildNData / BuildNDeath
                                 (message-specific — makes invalid payloads hard to
                                 construct; e.g. NDEATH-with-seq is unrepresentable)
SparkplugMetricManifest          node metrics: canonical id, name, datatype, alias,
                                 unit/props
SparkplugLatestValueSnapshot     current value/null, timestamp, quality per metric
SparkplugReplayCoordinator       replay epoch, high-water mark, catch-up transition
SparkplugIdentityStateStore      PERSISTED bdSeq + aliases (key broker+group+node)
CanonicalToSparkplugValueMapper  datatype + precision/overflow/null
CanonicalToSparkplugQualityMapper 0/192/500 + uncertain policy
SparkplugTopicBuilder            spBv1.0/{group}/{type}/{edge_node}
SparkplugConfigurationValidator  reject out-of-scope modes, dup identity, PrimaryHostId
```

Device-level types (`SparkplugDeviceManifest`, `IDeviceLifecycleProvider`,
`BuildDBirth/DData/DDeath`) are stubbed-absent until the device slice.

---

## 3. Core touch-point (the one honest exception to "no Core changes")

v1 needs replay-phase + birth inputs the current `ISinkAdapter` batch doesn't
carry. Keep the base interface intact; add **optional, protocol-neutral** seams:

```
IReplayAwareSinkAdapter : PublishAsync(points, PublishContext, ct)
  PublishContext { Phase = Replay|CatchUp|Live ; ReplayEpoch ; HighWaterMark }
IMetricManifestProvider / ILatestValueSnapshotProvider
IDeviceLifecycleProvider     // deferred with device scope
```
`SinkPublisher` supplies context only to sinks that declare the capability; existing
sinks unaffected; **no Sparkplug-specific buffer/cursor**. **Spike gates this:**
prove whether `ReplayCoordinator`/`RetryStateMachine` can already surface
`Phase`/`HighWaterMark` before adding the interface. (ADR-0036 Rule 6.)

---

## 4. Delivery contract (locked, honest)

`PublishResult.Success` = local MQTTnet send completed with no observable error —
**not** broker acceptance. Store-and-forward buffers offline and retries observable
failures; the **QoS-0 ambiguity window is real** (possible loss on optimistic
advance, possible duplication on retry-after-rebirth) and is documented in the
wizard's delivery notice. On any observable **or uncertain** send error → disconnect
→ new session (rebirth) before retry, preserving `seq` validity. No "AtLeastOnce",
"acked", or "guaranteed dedupe" language anywhere. (ADR-0036 Rule 1.)

---

## 5. Pre-implementation inputs still needed

1. **Manifest + latest-value snapshot source** — browse vs. persisted route manifest
   vs. a latest-value cache vs. a purpose-built snapshot service. The spike answers
   this; birth cannot be built without it.
2. **Naming/identity confirmation (G)** — defaults stand unless a concrete consumer
   (named Ignition/UNS deployment) dictates a scheme. Still the one open external
   input; non-fatal (defaults are overridable wizard fields) but pins persisted
   aliases/topics.

---

## 6. Test / release gates (adopted from review)

- **Protocol-kernel golden wire tests** (independent decoder): exact topic, QoS 0 /
  retain false, field presence incl. **NBIRTH seq=0 physically encoded**, seq wrap
  255→0, **NDEATH has no seq**, `is_historical` + dual timestamps, `is_null` metrics,
  Quality property.
- **State-machine + failure injection:** disconnect before/at every phase; QoS-0
  send failure (observable + uncertain); crash around `bdSeq` reserve/CONNECT;
  Rebirth NCMD during birth/replay/catch-up/live; schema change; graceful stop /
  license disable / destination removal / config replace; cold start with backlog +
  no persisted manifest; live points during drain; **two destinations, same
  Edge-Node descriptor**; broker ACL denial on NCMD/DATA.
- **Broker integration runs unconditionally in ≥1 CI/release job** (container broker)
  — a stateful protocol must not release with broker tests normally skipped. (Note:
  this intersects the repo's CI gap — there is no CI yet; ties to the deferred
  "stand up CI" task, `2026-07-13-post-roadmap-sequencing-decision.md`.)
- **Independent interop before release:** own mock-host subscriber **plus** an
  independent Sparkplug decoder **plus** a real **Ignition + MQTT Engine** pass;
  verify replayed values don't overwrite the host's current value, Rebirth behavior,
  alias resolution post-rebirth, and near-max-packet birth payloads.

---

## 7. Implementation order (adopted)

1. **v2 plan + amended ADRs** — done (this doc + 0035/0036).
2. **Technical spike** — manifest/snapshot availability, replay-context propagation
   (can `ReplayCoordinator` supply it?), MQTTnet QoS-0 completion semantics,
   duplicate-identity detection, crash-safe `bdSeq`. *Gate before committing the Core
   seam.*
3. **Protocol kernel** — payload factory, topic builder, mappers, persistent
   identity store, wire-level golden tests. No Studio.
4. **Single-owner session actor** — CONNECT, NCMD subscribe, birth, replay, live,
   rebirth, graceful NDEATH.
5. **Broker + failure tests** — before UI.
6. **Static wizard mockup** — topic preview, identity validation, naming strategy,
   explicit **QoS-0 delivery notice**; sign-off before wiring.
7. **Wire Studio + licensing.**
8. **Ignition/MQTT Engine interop + release gates.**

---

## 8. Open items / flags for the user

1. **Scope inflation acknowledged** — v1 roughly tripled vs. the original slice
   (session actor, manifest/snapshot, Rebirth NCMD, bdSeq persistence, connection
   profile, optional Core seam). Sparkplug stays #1 but confirm you're good with the
   larger first item before the spike.
2. **CLAUDE.md #12 footnote** — the QoS-0 carve-out means #12's "AtLeastOnce
   supported" is not absolute. **Proposed** footnote noting the Sparkplug exception;
   not edited unilaterally — your call.
3. **Naming/identity consumer** (§5.2) — any named Ignition/UNS deployment dictating
   the scheme? Else defaults ship.
4. **Next action** — recommend the **technical spike** (step 2) before any kernel
   code, since it validates the two riskiest unknowns (replay-context from the engine,
   and manifest/snapshot sourcing). Alternatively the **static wizard mockup** can
   proceed in parallel since its fields are now pinned (G).
