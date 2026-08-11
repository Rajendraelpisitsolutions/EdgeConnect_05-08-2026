# Sparkplug B Wire Conformance — EdgeConnect Node-Only Profile (K2)

**Created:** 2026-07-19 (K2 Slice 5)
**Governing decisions:** ADR-0035 (as amended 2026-07-19), ADR-0036; frozen K2
plan `docs/sessions/2026-07-19-sparkplug-b-k2-wire-payload-plan-v3.md`.

## Specification pin (exact strings — three version tokens, never conflated)

| Item | Value |
|---|---|
| **Normative specification** | `Eclipse Sparkplug Specification Version 3.0.0` (November 2022) |
| **Sparkplug namespace** | `spBv1.0` — a topic-namespace token, **not** the specification revision |
| **Payload schema** | the exact pinned `sparkplug_b.proto` at Eclipse Tahu commit `46f25e79f34234e6145d11108660dfd9133ae50d` (see `sparkplug-b-proto-provenance.md`) |
| **Initial MQTT transport profile** | `MQTT 3.1.1` |

## What this profile is — and is not

This is the **EdgeConnect node-only Sparkplug 3.0 profile**: Edge-Node
NBIRTH/NDATA/NDEATH data publishing plus the mandatory receive-only
`Node Control/Rebirth` NCMD (ADR-0035 Rule 3). Device-level messages, Primary
Host STATE, other NCMDs, DCMD writes, and MQTT 5.0 are out of the v1 profile.

The **"K2 wire-conformance golden suite for the EdgeConnect node-only
Sparkplug 3.0 profile"** (`ElpisEdgeConnect.Sinks.SparkplugB.Tests`, notably
`SparkplugWireConformanceGoldenTests` and the slice-2/3/4 suites) proves
conformance **to this pinned subset and its wire invariants** through an
independent proto2 decoder. It is **not** the official Sparkplug Technology
Compatibility Kit; nothing here claims certified Sparkplug compatibility.
Certification-track work (mock host, real Ignition + MQTT Engine interop,
unconditional broker CI) is a **K6 release gate** (ADR-0035 Open item 4).

## Frozen profile policies (normative for this implementation)

- **Metric identity:** source-qualified name
  `{SourceInstanceId}/{DeviceId}/{TagPath}`; alias key is Edge-Node-scoped
  (no RouteId). Alias `0` is reserved; assigned aliases begin at 1; aliases
  are unique across the Edge Node including the `bdSeq` alias.
- **Alias baseline:** NBIRTH application metrics and the application alias
  map are an **exact set match** — every alias later used by an alias-only
  NDATA is physically announced (name + datatype) in the active NBIRTH.
  `bdSeq` carries name + alias in NBIRTH; `Node Control/Rebirth` is name-only
  (`[tck-id-operational-behavior-data-commands-rebirth-name-aliases]`).
- **Quality:** Good → omit the `Quality` property (never emit 192); Bad → `0`;
  Stale → `500`; Uncertain/Unknown → `0` plus the **controlled**
  `QualityReason` values `"quality uncertain"` / `"quality unknown"`.
  `QualityReason` is an **EdgeConnect-defined** property, present only for
  those two lossy mappings, never on null handling, and must not be read as
  detailed fault causality. Source free-text reasons are never published.
- **Numerics:** signed Int32/Int64 are bit-preserving two's-complement in the
  unsigned `int_value`/`long_value` arms; consumers interpret per the
  Sparkplug datatype.
- **Timestamps:** payload timestamp = UTC publication instant; metric
  timestamp = UTC acquisition instant; both are whole Unix milliseconds,
  floored, with pre-epoch instants rejected. `DateTime` metric values follow
  the same rules.
- **Ordering:** NBIRTH = `bdSeq`, `Node Control/Rebirth`, then application
  metrics by ordinal metric name. NDATA = chronological by wire timestamp
  (spec MUST for non-historical values, applied to historical batches as
  profile policy), ordinal metric name as tiebreak, and **caller encounter
  order as the final dimension** for equal key + equal wire millisecond
  (possible after truncation).
- **NDEATH:** one frozen shape for the MQTT Will payload and the
  intentional-disconnect publish — no payload timestamp, no `seq`, exactly
  one metric `bdSeq` (Int64, value matching the paired NBIRTH, no alias, no
  metric timestamp).
- **Sequence domains:** `seq` and `bdSeq` are constructible only within
  0–255; counter ownership and wrap live in the K3 session actor.
- **Rejection discipline:** semantic Sparkplug mapping, identity, alias-table,
  datatype, value, quality, and timestamp rejections use typed `SPARKPLUG.*`
  errors raised at the validated-model stage, before serialization. Ordinary
  CLR API-contract violations — such as a null collection element where the
  project API convention requires `ArgumentNullException` — may use standard
  argument exceptions. Nothing is silently dropped or coerced (ADR-0033
  discipline).
