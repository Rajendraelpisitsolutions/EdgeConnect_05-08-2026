# Sparkplug B Plan v1 — Conformance Review (ChatGPT), archived

**Date:** 2026-07-13
**Role in trail:** the **review** step between plan v1 and v2 (per
`feedback_planning_cadence`). External conformance review of
`2026-07-13-sparkplug-b-sink-plan-v1.md` + ADR-0035/0036 against the Sparkplug B
3.0 spec. Archived here for provenance; dispositions are folded into
`…-sparkplug-b-sink-plan-v2.md` and the amended ADRs.

## Executive verdict (as received)
- Product/assembly architecture is **sound** — separate coexisting sink, reuse
  transport, build new payload/session layer. **Approved.**
- **Do not approve for implementation as written.** Plan → v2; ADR-0035 amend;
  **ADR-0036 replace** — its central QoS-1/PUBACK guarantee is incompatible with
  conformant Sparkplug.
- Root cause: the docs tried to hold three incompatible things at once —
  (1) strict conformance, (2) broker-acked at-least-once, (3) no Core/buffer change.

## Approve / Reject table (as received)
| Area | Verdict |
|---|---|
| Separate sibling sink | Approve |
| MQTT/EREMOS coexistence | Approve |
| Primary Host/STATE deferred | Approve (single-broker v1) |
| Google.Protobuf/generated schema | Approve w/ provenance + presence safeguards |
| Birth → historical replay → live | Approve after correcting semantics |
| QoS 1/PUBACK retry design | **Reject** |
| First-seen birth catalogue | **Reject as initial-startup strategy** |
| "No Core/buffer changes" | Unproven, probably false without optional extension |
| Device DBIRTH/DDATA/DDEATH | Blocked on a real lifecycle signal |
| Current test plan | Good start, insufficient as a release gate |

## The 12 required changes (headers + essence)
1. **Replace QoS 1/PUBACK.** BIRTH/DATA/DDEATH = QoS 0 (no PUBACK); NDEATH Will =
   QoS 1, no seq. `Success` = local transport send only, not broker ack. Resolve
   the locked-#12 conflict: standard QoS 0 (recommended) / non-standard QoS 1 /
   proprietary host-ack. Remove "AtLeastOnce citizen", "fully acked", "true
   wire-ack", guaranteed host dedupe.
2. **Mandatory receive-only `Node Control/Rebirth` NCMD** — exact-topic QoS-1
   subscribe before birth; `Rebirth=false` (no alias) in NBIRTH; rebirth on request
   retaining bdSeq; ignore other NCMD; coalesce; pause DATA during birth.
3. **Rename to birth-then-historical-replay.** Need 3 fields: `is_historical=true`,
   `metric.timestamp`=acquisition, `payload.timestamp`=publication. Drop "all hosts
   record/dedupe" claim. Explicit catch-up policy at the replay boundary.
4. **Replace first-seen with manifest + latest-value snapshot before birth** (birth
   must carry all metrics + current values / is_null). C/E naming → pre-implementation.
5. **Reconsider "no Core changes":** add optional protocol-neutral
   `IReplayAwareSinkAdapter` (Phase/HighWaterMark) + manifest/snapshot/lifecycle
   providers; SinkPublisher supplies context only to sinks that need it.
6. **Resolve device lifecycle before DBIRTH/DDATA/DDEATH** (DDEATH was missing).
   Either add a real lifecycle signal or narrow v1 to node-level.
7. **`seq`/`bdSeq`:** single Edge-Node modulo-256 seq; NDEATH excludes seq, DDEATH
   includes it; NBIRTH start 0 is EdgeConnect policy. **Persist bdSeq**, reserve
   before CONNECT (key broker+group+edge_node); single ownership in clustered.
8. **One single-owner Edge Node session actor** owning seq/bdSeq/sends/aliases/
   transitions; identity = broker+group+edge_node; reject duplicate active dest.
9. **Global aliases** (unique across whole node), one persisted allocator, no alias
   for Rebirth; deterministic name = relative TagPath; reject case-only collisions.
10. **Connection/shutdown profile:** Clean Session (3.1.1) / Clean Start + Session
    Expiry 0 (5.0); graceful NDEATH before DISCONNECT; Test-Connection must NOT use
    production Client ID/Will. Pin one MQTT version for v1 (3.1.1 lower-risk).
11. **Protobuf/licensing wording:** "no Tahu runtime dep" not "no EPL surface"; the
    .proto is EPL-2.0; pin commit/hash/toolchain; proto2 presence; golden byte tests
    (incl. Tahu seq=0 omission, issue #260); no legal conclusion in the ADR.
12. **Null/quality/timestamp policy in the type map** — is_null, Quality 0/192/500,
    uncertain policy, precision/overflow, non-UTC, empty-bytes-vs-null, datatype
    change = schema change.

## Decisions to lock (recommended answers, as received)
QoS 0 standard · birth→replay→catch-up→live · is_historical + dual timestamps ·
optional publisher context · manifest not first-seen · snapshot before CONNECT ·
receive-only Rebirth only · Primary Host deferred+rejected · MQTT 3.1.1 · device =
SourceInstanceId · name = relative TagPath + override · global persisted aliases ·
device lifecycle provider required for device scope · persist bdSeq · one actor per
broker/group/edge · pinned proto2 schema w/ hash.

## EdgeConnect dispositions (2026-07-13)
- **Accepted wholesale:** changes 2,3,5,7,8,9,10,11,12 — folded into ADR-0035 (v2
  amend) / ADR-0036 (v2 replace).
- **User decisions (this session):** delivery = **standard QoS 0** (relax broker-ack
  #12 for this sink); device scope = **node-level-only v1** (change 6 → narrow
  fallback); MQTT = **3.1.1 only**.
- **Verified in code:** no source→sink device-lifecycle signal exists today (sinks
  sit behind the per-route buffer) → confirms node-only v1 and the optional Core
  extension (change 5/6). Engine already has `ReplayCoordinator`/`RetryStateMachine`
  → spike whether replay phase is surfaceable before adding interface surface.

## Test / release gates (as received, adopted)
Protocol-kernel golden wire tests (topic/QoS/retain, field presence incl. zeros,
seq wrap, NDEATH-no-seq, is_historical, null values, Quality); state-machine +
failure injection (disconnect at every phase, QoS-0 send failure, seq 255→0, crash
around bdSeq, Rebirth during each phase, schema change, graceful stop/license
disable/removal, cold start w/ backlog, live-during-drain, duplicate identity, ACL
denial); **broker integration unconditional in ≥1 CI/release job**; independent
decoder + real Ignition/MQTT Engine interop before release.

## Implementation order (as received, adopted)
v2 plan+ADRs → spike (manifest/snapshot, replay-context, QoS-0 completion, dup
identity, crash-safe bdSeq) → protocol kernel + wire tests → single-owner session
actor → broker+failure tests → static wizard → wire Studio+licensing → Ignition
interop + release gates.

## Cited by the reviewer
Sparkplug B 3.0 spec; Tahu `sparkplug_b.proto`; Tahu issue #260 (seq=0 omission).
