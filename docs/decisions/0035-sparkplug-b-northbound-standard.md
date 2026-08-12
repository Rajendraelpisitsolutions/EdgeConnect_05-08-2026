# ADR-0035: Sparkplug B as a first-class northbound standard — scope, encoder, and coexistence

**Status:** Proposed (2026-07-13) — amended same day after a conformance review
(see plan trail `…-v1-chatgpt-review.md`): protobuf/licensing wording tightened
(Rule 2), mandatory Rebirth-NCMD carve-out added (Rule 3), device scope narrowed to
Edge-Node-only for v1 (Rule 3/4), type-map extended (Rule 5). Delivery/replay/
sequencing corrections live in the companion **ADR-0036 (v2)**.
**Amended 2026-07-19 (K2, owner GO):** Rule 5 extended with the frozen
`Unknown`-quality mapping, the controlled `QualityReason` wire contract, the
bit-preserving signed-integer encoding, and the timestamp range rules — see
"Rule 5 amendment" below and the frozen K2 plan
(`docs/sessions/2026-07-19-sparkplug-b-k2-wire-payload-plan-v3.md`).
**Date:** 2026-07-13
**Framing:** EdgeConnect positions as MQTT-first but today speaks only a custom
`eremos/{gatewayId}/…` topic grammar tailored to EREMOS V2 ingestion. Sparkplug B
is the de-facto industrial-MQTT standard and the price of entry for Ignition /
Inductive Automation shops and Unified Namespace (UNS) buyers. This ADR locks the
decisions that are painful to reverse: that Sparkplug B is a **new, coexisting**
sink (not a replacement, not a mode of the MQTT sink), how we encode the payloads
(own the protobuf, no Tahu runtime dependency), and exactly what the v1 slice supports. The
store-and-forward / sequence-number reconciliation is deep enough to have its own
record — **ADR-0036**. Plan trail: `docs/sessions/2026-07-13-sparkplug-b-sink-plan-v1.md`.

## Context

Sparkplug B (Eclipse Tahu specification) defines an MQTT topic namespace
`spBv1.0/{group_id}/{message_type}/{edge_node_id}/{device_id}` and protobuf-encoded
payloads with a stateful session model: birth certificates (NBIRTH/DBIRTH),
death certificates (NDEATH via MQTT Last-Will, DDEATH), data messages
(NDATA/DDATA), a per-edge-node wrapping sequence number, a birth/death sequence
(`bdSeq`), and per-metric alias tables. It is a materially different contract from
the current sink, which is stateless per-point.

Three facts shape this ADR:

1. **The existing MQTT sink is the EREMOS contract, and must not change.** EREMOS
   V2 ingests by topic on `eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}`
   (verified — `reference_eremos_v2_ingestion`). Sparkplug B serves a *different*
   audience (third-party / standards-driven consumers). The two are additive.

2. **Transport reuses cleanly; the payload + session layer is net-new.** The
   architecture map (this session) confirmed MQTTnet 4.3.7.1207, the
   connect/backoff/reconnect loop, health counters, the DI registration triad, and
   the wizard/edit-router scaffolding are all reusable. The Sparkplug session state
   machine (birth/death, alias table, seq/bdSeq) has no analog in the current sink.

3. **No clean-license Sparkplug .NET library.** Eclipse Tahu's library is EPL-2.0.
   Dependency licensing has bitten this project before (OPC UA needed the
   Foundation RCL — `project_opc_foundation_membership`; the MELSEC wire was
   hand-rolled to avoid HslCommunication's commercial terms — ADR-0033). We apply
   the same posture here.

## Decision

### Rule 1 — Sparkplug B is a separate, coexisting sink assembly

Sparkplug B ships as a new assembly `ElpisEdgeConnect.Sinks.SparkplugB` with
`ProtocolName = "sparkplug-b"`. It is **not** a replacement for
`ElpisEdgeConnect.Sinks.Mqtt` and **not** a `MqttPublishMode` flag on the MQTT
sink. Both sinks may run on the same gateway on different routes; the MQTT sink
remains the EREMOS path unchanged. This mirrors the deliberate choice to keep
`MqttPublishMode` Batch/PerTag as two explicit code paths rather than one branchy
method — Sparkplug's stateful session is different enough to warrant its own home.

### Rule 2 — No Tahu runtime dependency; generate from a pinned, reviewed schema

EdgeConnect has **no Eclipse Tahu runtime package dependency**. It generates
`Google.Protobuf` C# types from a **pinned, reviewed copy of the official Sparkplug
`sparkplug_b.proto`** (proto2), wrapped by a first-party `SparkplugPayloadEncoder`.
The `.proto` itself is EPL-2.0; therefore this ADR makes **no legal conclusion** that
generated code carries no EPL obligation — schema provenance, notices, source
inclusion, generated artifacts, and SBOM treatment go through the project's
open-source-compliance process (cf. `project_opc_foundation_membership`, ADR-0033).

Pin and record: Tahu **commit**, schema **SHA-256**, `protoc`/`Grpc.Tools` version,
and `Google.Protobuf` runtime version. Use the generic proto2 schema and **preserve
optional-field presence** — there is a documented Tahu C# bug where a zero-valued
`seq`/timestamp was omitted, producing a non-compliant NBIRTH at `seq=0`. Ship
**byte-level golden tests** (independent decoder) proving: NBIRTH `seq=0` is
physically encoded; required metric timestamps present; NDEATH has no `seq`;
`is_historical` encoded when true; null metrics carry `is_null=true` with no value
arm. Same-generated-class round-trips are necessary but **not sufficient**
(common-mode error risk).

### Rule 3 — v1 is Edge-Node data-publishing plus the mandatory Rebirth NCMD

v1 is **data-publishing-only except for the mandatory, receive-only `Node
Control/Rebirth` NCMD** (a conformance requirement — a conforming Edge Node MUST
subscribe its exact NCMD topic, announce `Node Control/Rebirth=false` in NBIRTH, and
rebirth on request; see ADR-0036 Rule 4). v1 implements **NBIRTH / NDATA** at the
Edge-Node level, the global alias table, `seq`/`bdSeq` per ADR-0036, `is_historical`
replay, the datatype/quality/null/timestamp map (Rule 5), TLS + username/password
auth, MQTT **3.1.1** only, and one namespace-identity convention (Rule 4).

**Deferred (each its own later slice):** **all Device-level behavior**
(DBIRTH/DDATA/**DDEATH**) until a real source→sink device-lifecycle signal exists
(ADR-0036 Rule 8); Primary Host `STATE/{host_id}` awareness; all NCMD metrics other
than Rebirth; all DCMD writes; MQTT 5.0. Forward-compatible config fields may exist
but anything outside the supported subset is **accepted in config and rejected at
validation** with a typed error — never silently ignored (MELSEC "Slice 1"
discipline, ADR-0033 Rule 2). A populated `PrimaryHostId` is **rejected**, not
accepted-and-ignored.

### Rule 4 — Namespace identity, deterministic and single-owner (now pre-implementation)

The Sparkplug namespace maps onto EdgeConnect identity as:

| Sparkplug field | Default | Overridable |
|---|---|---|
| `group_id` | site / customer id | yes (wizard field) |
| `edge_node_id` | gateway id | yes |
| `device_id` | *(deferred — node-only v1, Rule 3)* | n/a in v1 |

Metric name is **source-qualified and globally unique within the Edge Node** —
`{SourceInstanceId}/{DeviceId?}/{relative TagPath}` (matches ADR-0036 Rule 5). A bare
relative `TagPath` is **insufficient**: in node-only v1 every metric shares one
NBIRTH namespace (there are no device topics to separate them — the earlier
device-separation justification was wrong and is withdrawn), so two sources exposing
the same path would collide. An explicit override supports a required customer
convention. The immutable keys use **canonical identity, never the overridable display
name**, and are **two distinct keys** (ADR-0036 Rule 5): the **snapshot persistence
key** = `RouteId + SourceInstanceId + DeviceId + canonical TagPath` (per-route
partition); the **Sparkplug alias key** = `SourceInstanceId + DeviceId + canonical
TagPath` — **Edge-Node-scoped, no `RouteId`**, so aliases stay stable when a route is
recreated or moved.

**Single-owner identity:** the effective Edge Node identity is
`broker + group_id + edge_node_id`; v1 **rejects a second active destination that
resolves to the same descriptor** (ADR-0036 Rule 7). These identity + naming
decisions are **pre-implementation, not "open/non-blocking"** — they fix persisted
aliases, topic stability, catalogue keys, and wizard fields. **Open input:** a
specific downstream consumer (e.g. a named Ignition deployment) may dictate a
scheme we must match; if so it pins these before code, else the defaults ship as
overridable wizard fields.

### Rule 5 — Value mapping covers datatype AND null / quality / timestamp / precision

`CanonicalValueType` (11 types, no unsigned) maps to Sparkplug datatypes (~20) via a
single pinned table (`CanonicalToSparkplugTypeMap`), with collapse rules documented
at the mapping site (`Integer`/`Long` → `Int32`/`Int64`; `ByteArray` → `Bytes`;
`Array`/`Object` have no scalar equivalent → **rejected at the encoder**, never
silently dropped). But the datatype number alone is insufficient — v1 also pins:

| Canonical state | Sparkplug encoding |
|---|---|
| Null | `is_null=true`, value field omitted |
| Good quality | **omit** the `Quality` property (locked — implies GOOD; do not also encode 192) |
| Bad quality | `Quality=0` |
| Stale | `Quality=500` |
| Uncertain | **`Quality=0` (BAD) + `QualityReason`** (locked — Sparkplug has no Uncertain code; conservatively flag as not-good) |
| Replayed value | `is_historical=true` (independent of quality) |
| Value timestamp | original UTC **acquisition** time |
| Payload timestamp | current UTC **publication** time |

`Quality`, when present, is a signed Int32 property. Also document: Decimal→Double
precision loss, integer overflow handling, invalid/non-UTC timestamps, empty
byte-array vs null, and that a **datatype change after birth is a schema change**
(controlled rebirth, ADR-0036 Rule 5), not an ordinary DATA update. The map is the
single source of truth, unit-tested against the `.proto` enum and byte-level golden
tests (Rule 2).

#### Rule 5 amendment (2026-07-19, K2 — owner GO; implemented in `Sinks.SparkplugB/Mapping`)

- **`Unknown` quality (canonical `DataQuality.Unknown`)** maps to **`Quality=0`
  (BAD) + `QualityReason="quality unknown"`**. This is an **EdgeConnect semantic
  mapping, not a fourth Sparkplug quality code** — when present, `Quality` is
  always one of the spec's `0`/`500` (we never emit `192`; Good omits the
  property).
- **`QualityReason` wire contract (frozen):** an **EdgeConnect-defined**
  property (not Sparkplug-standard); key exactly `QualityReason`; String type,
  UTF-8; present **only** for `Uncertain` (`"quality uncertain"`) and `Unknown`
  (`"quality unknown"`); omitted for Good/Bad/Stale and null handling; never an
  empty string; deterministic property ordering. Only these **controlled wire
  values** are published — source free-text reasons (protocol errors, exception
  text, device diagnostics) stay in EdgeConnect diagnostics and are never
  exposed here. Consumers must not interpret it as detailed fault causality.
  Any later source-reason passthrough uses a **separate property** and requires
  an ADR amendment covering naming, sanitization, length limits, and
  compatibility.
- **Signed integer encoding (frozen):** Tahu stores values in unsigned protobuf
  arms (`uint32 int_value`, `uint64 long_value`); canonical `Integer`/`Long`
  are written **bit-preserving** (unchecked two's-complement reinterpretation).
  Consumers interpret per the Sparkplug **datatype**, not the protobuf CLR
  type. Checked casts are forbidden at the mapping site. Canonical unsigned
  types do not exist in v1; if introduced they are rejected until explicitly
  mapped.
- **Timestamp range rules (frozen):** normalize to UTC → **reject pre-epoch**
  instants with a typed error (an unsigned cast would fabricate a future time)
  → floor to whole Unix milliseconds (sub-millisecond precision discarded) →
  reject upper-range overflow rather than wrap. Applies to payload/metric
  timestamps **and** `DateTime` metric values. (The canonical model has no
  Decimal type, so the earlier Decimal→Double note is moot in v1.)

### Rule 6 — License-gated like every other adapter

Add `LicenseModuleKeys.SinkSparkplugB = "sink-sparkplug-b"` (add, never rename —
LOCKED once issued). Registration follows the standard three-layer host triad
(ResolveInputs → Construct → Build, `ReplaceSinkRegistrationEnumerable`), gated by
`ILicenseManager.IsModuleEnabled`, wired with one line in
`EdgeConnectComposition.cs`. Tier placement in `docs/licensing/module-catalog.md`
is set before ship (proposed: Premium, alongside `sink-opc-ua-server`).

### Rule 7 — UI follows the locked wizard contract, mockup-first

The destination tile (`DestinationProtocolPickerModel`), wizard
(`SparkplugSinkWizardModel` + `AddSparkplugDestination.razor`), and
`SinkEditRouter` wiring follow ADR-0015 (wizard contract) and ADR-0008
(destinations-not-sinks language). A **static HTML mockup is signed off before any
Studio wiring** (`feedback_static_html_ui_review`). The tile is **Pending** until
the wizard ships, then **Available** — "done" means the tile is operator-Available,
per CLAUDE.md §8.

## Consequences

**Positive:**
- Closes the biggest MQTT-first credibility gap at low cost; unlocks
  Ignition/UNS conversations without touching the EREMOS path.
- No Eclipse Tahu **runtime** dependency; schema provenance and generated-code
  obligations are handled through the project's open-source-compliance review (no
  legal conclusion is asserted here — Rule 2).
- The supported surface is honest: unsupported modes/types fail loudly at config
  or encode time, never silently misbehave (ADR-0033 discipline carried forward).
- Coexistence means a single gateway can feed EREMOS and a third-party UNS
  simultaneously — a genuine multi-consumer story.

**Negative / costs:**
- We maintain a hand-owned protobuf encoder and a datatype table that must track
  the Tahu spec (mitigated: generated from the official `.proto`; encoder
  unit-tested by round-trip decode).
- The stateful session machine (birth/death, alias, seq/bdSeq) is real new
  complexity with no reuse from the MQTT sink (see ADR-0036 for the hardest part).
- "Configurable but validation-rejected" forward-compat fields can surprise an
  operator (mitigated by clear typed messages).

**Forbidden patterns:**
- Adding an EPL-2.0 (or other copyleft/commercial) Sparkplug library dependency to
  ship the payloads (Rule 2).
- Implementing Sparkplug B as a mode of `MqttSinkAdapter` or changing the EREMOS
  topic contract (Rule 1).
- Silently dropping an unmappable datatype, or emitting a value without its
  null/quality/timestamp treatment (Rule 5).
- Shipping the tile as Available before the wizard exists (Rule 7 / CLAUDE.md §8).
- Pulling Primary Host / STATE, general NCMD, DCMD writes, Device-level behavior, or
  MQTT 5.0 into the v1 slice (Rule 3) — **except** the mandatory receive-only
  `Node Control/Rebirth` NCMD, which is required and in-scope.
- Accepting a populated `PrimaryHostId` and ignoring it instead of rejecting it
  (Rule 3).

## Open / Pending

1. **Namespace + metric-naming — RESOLVED (2026-07-13).** Source-qualified naming
   locked (Rule 4, matching ADR-0036 Rule 5); immutable identity/persistence key =
   canonical `TagPath`. No named downstream consumer overrides it; defaults ship as
   overridable wizard fields.
2. **License tier** — proposed Premium; confirm in `module-catalog.md` before ship.
3. **Manifest + latest-value snapshot source — RESOLVED (WS2).** No complete
   Core-level configured manifest exists (tag defs are protocol-opaque); the
   **persisted** latest-value snapshot doubles as the observed manifest. The snapshot
   must be captured **crash-atomically with the buffer append**, and birth reads a
   coherent point captured atomically with `H` (see ADR-0036 Rule 5 and plan v2.3
   §3). A genuinely-never-observed metric is **absent from NBIRTH** until first
   observation (a schema change → controlled rebirth); `is_null` is used only for a
   **known** manifest metric whose current value is explicitly null.
4. **Interop proof is a release gate, not a coding gate** — a Sparkplug mock-host
   subscriber (own harness) plus an **independent decoder** and a **real Ignition +
   MQTT Engine** pass; broker integration must run **unconditionally in at least one
   CI/release job**, not only when Mosquitto happens to be present.

## Reference

- **ADR-0036** — Sparkplug delivery, birth-then-historical-replay, and Edge Node
  session ownership (QoS-0 delivery × store-and-forward × ordered seq). *(File slug
  retains the legacy `replay-then-rebirth` name; the decision content is v2.)*
- Plan trail: `docs/sessions/2026-07-13-sparkplug-b-sink-plan-v1.md`.
- **ADR-0008** destinations-not-sinks UI language; **ADR-0015** wizard contract;
  **ADR-0020** diagnostic-bundle redaction (connection-keys registration);
  **ADR-0033** MELSEC hand-rolled / slice-scope (third-party-library posture and
  narrow-first discipline this ADR reuses).
- CLAUDE.md §3 (locked decisions), §8 ("done" = operator-Available),
  §9 (anti-patterns).
- `reference_eremos_v2_ingestion` (why the MQTT sink contract must not change).
- Eclipse Tahu Sparkplug B specification and `sparkplug_b.proto`.
