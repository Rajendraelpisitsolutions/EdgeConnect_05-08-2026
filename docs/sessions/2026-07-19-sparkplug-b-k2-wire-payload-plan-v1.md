# Sparkplug B — K2 Wire + Payload Plan (v1)

**Date:** 2026-07-19
**Author:** Session with Sudhakar
**Status:** DRAFT v1 — awaiting external review pass (plan-trail cadence:
v1 → review → v2 → reality-check → v3).
**Governing docs:** master plan `2026-07-13-sparkplug-b-sink-plan-v2.3.md` §8 (K2
line), **ADR-0035** (Rules 2, 4, 5 are the K2 spec), **ADR-0036** (context only —
its runtime rules land in K3), K1.3 handoff `2026-07-19-sparkplug-b-k1.3-handoff.md`.
**Baseline:** `master` @ `5b9c2f6` (K1.3 merged; Core replay contracts public).

---

## 1. Scope — what K2 is

Per plan v2.3 §8, K2 is the **stateless wire layer** of the Sparkplug B sink:

> `K2  Sparkplug wire + payload factory + mappers (value/quality locked) + 3.1.1
> profile   (golden tests)`

Concretely:

1. **New assembly `ElpisEdgeConnect.Sinks.SparkplugB`** (+ test project
   `ElpisEdgeConnect.Sinks.SparkplugB.Tests`). References Core only (locked
   dependency direction). `ProtocolName = "sparkplug-b"` constant defined here
   (ADR-0035 Rule 1) even though DI/license registration is K4.
2. **Pinned `sparkplug_b.proto` + generated `Google.Protobuf` types** (ADR-0035
   Rule 2): pinned, reviewed copy of the official Tahu proto2 schema; record Tahu
   commit, schema SHA-256, `protoc`/`Grpc.Tools` version, `Google.Protobuf`
   runtime version. **No Tahu runtime dependency.** Optional-field **presence
   preserved** (the documented Tahu C# `seq=0`/timestamp omission bug is the
   canary).
3. **`SparkplugPayloadEncoder`** — first-party wrapper over the generated types;
   the only public encode surface.
4. **`CanonicalToSparkplugTypeMap`** — the single pinned datatype table
   (ADR-0035 Rule 5): `Integer→Int32`, `Long→Int64`, `Float→Float`,
   `Double→Double`, `Boolean→Boolean`, `String→String`, `DateTime→DateTime`,
   `ByteArray→Bytes`; **`Array`/`Object` rejected at the encoder** with a typed
   error, never dropped. Unit-tested against the `.proto` enum values.
5. **Value/quality/null/timestamp mapper** — the locked Rule 5 table:
   Null → `is_null=true`, no value arm; Good → **omit** `Quality`;
   Bad → `Quality=0`; Stale → `Quality=500`; Uncertain → `Quality=0` +
   `QualityReason`; replayed → `is_historical=true`; metric timestamp = UTC
   acquisition time; payload timestamp = UTC publication time. Documented at the
   mapping site: Decimal→Double precision loss, integer overflow, non-UTC
   timestamp normalization, empty byte-array vs null.
6. **Payload factories** (pure functions over Core contracts + inputs supplied
   by the future K3 actor):
   - **NBIRTH** — from a manifest/snapshot (`ReplaySessionStart` shape) + a
     caller-supplied alias map: `seq=0` physically encoded, `bdSeq` metric,
     `Node Control/Rebirth=false` metric (ADR-0035 Rule 3), full metric names +
     aliases + datatypes, per-metric acquisition timestamps.
   - **NDATA** — alias-only metrics, `is_historical` per `PublishContext` phase.
   - **NDEATH** — will payload: `bdSeq` only, **no `seq`**, no metrics beyond
     `bdSeq`.
7. **`SparkplugTopicFactory`** — `spBv1.0/{group}/{type}/{edge_node}` for
   NBIRTH/NDATA/NDEATH plus the exact **NCMD subscribe topic** string. Node-only
   v1: no device topics (ADR-0035 Rule 3). Metric naming =
   `{SourceInstanceId}/{DeviceId?}/{TagPath}` per Rule 4; alias key =
   Edge-Node-scoped (no RouteId) — K2 defines the **key types**
   (`SparkplugAliasKey`, `SparkplugEdgeNodeIdentity`), K3 owns their stores.
8. **Error catalog `SparkplugErrors.cs`** — `SPARKPLUG.*` codes
   (`MODULE.CATEGORY_SUBCATEGORY` convention) for every typed rejection
   (unmappable datatype, invalid identity part, etc.).
9. **Golden conformance tests** (the K2 exit gate — ADR-0035 Rule 2): byte-level
   assertions via an **independent test-side proto2 decoder** (hand-rolled
   minimal wire-format reader in the test project; NOT the generated classes —
   round-trip through the same generated code is necessary but insufficient).
   Must prove at minimum: NBIRTH `seq=0` **physically present** in bytes;
   required metric timestamps present; NDEATH carries **no** `seq`;
   `is_historical=true` encoded when set; `is_null=true` with **no value arm**;
   Good quality → no `Quality` property bytes; Uncertain → `Quality=0` +
   `QualityReason`; Stale → `Quality=500`; alias-only NDATA metrics carry no
   name.

"3.1.1 profile" in the K2 line = the Sparkplug-on-MQTT-3.1.1 **conformance
constants baked into the payload/topic layer** (topic grammar, NCMD topic,
Rebirth announce metric, NDEATH-as-will shape). The MQTT 3.1.1 **transport**
itself is K3 (session actor over MQTTnet).

## 2. Scope — what K2 is NOT (boundaries stay per v2.3 §8)

- **No session state machine** — no CONNECT/reconnect, no `seq`/`bdSeq`
  *management*, no alias *assignment/persistence*, no Rebirth NCMD *handling*,
  no `IReplayAwareSinkAdapter` implementation. All K3. K2's factories are pure
  and take `seq`/`bdSeq`/alias values as inputs.
- **No config validation / license registration / module catalog entry** — K4
  (K0 WS3+WS8 rules; `LicenseModuleKeys.SinkSparkplugB`).
- **No wizard / Studio surface** — K5 (mockup-first per Rule 7).
- **No Core changes.** K1 delivered the Core seams; if K2 discovers a missing
  Core need, that is a stop-and-surface event, not a quiet Core edit.
- **No broker in tests.** K2 tests are pure encode/decode — deterministic, no
  Mosquitto dependency.

## 3. Retained K1.3 follow-ups (recorded so they are not lost — none land in K2)

| # | Follow-up (K1.3 handoff) | Lands in |
|---|---|---|
| 1 | Material-schema / generation-changing rebirth (`AdvanceGenerationAsync` seed) | post-K3 slice (needs K3 actor + manifest generation) |
| 2 | Coordinated replay-sink hot replacement (coordinator ↔ driver dance) | K3 |
| 3 | Production `ISinkReplayCapabilityClassifier` registration | K4 (alongside sink DI registration) |

## 4. Proposed slices

| Slice | Content | Exit evidence |
|---|---|---|
| 1 | Project scaffolding; pinned proto + provenance record; generated types build 0/0 | provenance doc committed; solution builds |
| 2 | Independent test-side proto2 wire decoder + harness | decoder self-tests vs hand-computed byte vectors |
| 3 | `CanonicalToSparkplugTypeMap` + value/quality/null/timestamp mapper + `SparkplugErrors` | unit tests incl. every rejection path |
| 4 | Topic factory + identity/alias key types + NBIRTH/NDATA/NDEATH factories | factory unit tests over Core contract shapes |
| 5 | Byte-level golden conformance suite (§1.9 list) + `docs/` conformance note | all golden tests green; suite named so a violation fails |

Same working style as K1.3: slice-per-commit, external review per slice, plan
amendments (`v3.x`) if reality diverges.

## 5. Open decisions to resolve at the review pass (v1 → v2)

1. **`DataQuality.Unknown` mapping — GAP in the locked Rule 5 table.**
   `DataQuality` has `Unknown = 0` ("not yet determined"); ADR-0035 Rule 5 pins
   Good/Bad/Stale/Uncertain/Null only. Proposal: treat **Unknown like Uncertain**
   (`Quality=0` + reason `"quality unknown"`) — conservative, matches the
   "conservatively flag as not-good" rationale. Alternative: reject at encode.
   Whichever is chosen must be added to ADR-0035 Rule 5.
2. **Generated-code strategy:** (a) build-time generation via pinned
   `Grpc.Tools` (schema is the artifact; protoc runs in every build), or
   (b) vendor the generated `.cs` checked in (deterministic, no protoc in CI;
   regeneration is an explicit reviewed step). Proposal: **(b) vendored**, with
   the pinned proto + regeneration script beside it — stronger audit posture for
   the OSS-compliance review and no build-chain variance.
3. **Provenance/compliance record location:** proposal
   `docs/compliance/sparkplug-b-proto-provenance.md` (Tahu commit, SHA-256,
   tool versions, EPL-2.0 notice pointer) — feeds the OSS-compliance process
   without asserting a legal conclusion (Rule 2).
4. **Alias-map input shape for the NBIRTH factory:**
   `IReadOnlyDictionary<SparkplugAliasKey, ulong>` supplied by the caller (K3
   owns assignment). Confirm `ulong` alias width and the reserved-alias policy
   (aliases for `bdSeq`/`Node Control/Rebirth`? proposal: **well-known metrics
   are name-only, no alias**).
5. **`DateTime` encoding detail:** Sparkplug `DateTime` = ms-since-epoch UTC;
   confirm truncation policy for sub-millisecond canonical timestamps
   (proposal: truncate toward zero, documented at the map site).
6. **Sparkplug spec version pin:** golden tests written against Sparkplug
   **3.0.0** spec text (Tahu v1.0.x proto) — confirm the exact spec revision to
   cite in the conformance note, since "v3.0" spec vs "spBv1.0" namespace vs
   "MQTT 3.1.1" transport are three different version strings that must not be
   conflated in docs or UI copy.

## 6. Exit criteria (K2 gate)

- Solution builds **0 warnings / 0 errors** (warnings-as-errors on the new
  projects, same as Core).
- No change to `ElpisEdgeConnect.Core` or any existing project.
- No Tahu runtime dependency; only `Google.Protobuf` runtime added (+
  `Grpc.Tools` build-only iff decision §5.2(a)).
- Provenance record complete (commit, SHA-256, tool versions).
- Full golden suite green, including every §1.9 byte-level proof, via the
  independent decoder.
- All mapper rejection paths carry `SPARKPLUG.*` typed errors from the catalog.
- Plan-trail docs updated; handoff note written before sign-off.
