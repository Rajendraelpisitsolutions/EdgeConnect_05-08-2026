# Sparkplug B — K2 Wire + Payload Plan (v3) — Reality-Checked Freeze Candidate

**Date:** 2026-07-19
**Author:** Session with Sudhakar
**Status:** **v3 FROZEN — owner GO 2026-07-19.** Reality-check pass complete.
**v2 §1–§8 stand unchanged except as amended below.** §4 tradeoff resolved by
owner decision (controlled wire values only). Implementation branch:
`feat/sparkplug-b-k2-wire`.
**Incorporates by reference:** plan v2 (`…-k2-wire-payload-plan-v2.md`) — this doc
records only verification results, resolutions of v2 §9, and deltas.
**Baseline:** `master` @ `5b9c2f6`.

---

## 1. Reality-check results — reviewer spec claims (v2 §9.1)

Verified against the **official spec source at tag `v3.0.0`**
(`eclipse-sparkplug/sparkplug`, AsciiDoc chapters) and the Tahu
`sparkplug_b.proto`. **All review claims confirmed**, with exact requirement IDs:

| Claim (v2) | Verdict | Normative source |
|---|---|---|
| `Node Control/Rebirth` MUST NOT have an alias in NBIRTH | **Confirmed** | `[tck-id-operational-behavior-data-commands-rebirth-name-aliases]` |
| NBIRTH MUST include `Node Control/Rebirth`, Boolean, value `false` | **Confirmed** | `[…rebirth-name]`, `[…rebirth-datatype]`, `[…rebirth-value]` |
| No alias exception for `bdSeq` → general name+alias NBIRTH rule applies (Blocker 1 table) | **Confirmed** | payloads ch. 6 alias rules; bdSeq reqs impose name/datatype/value only |
| NBIRTH/NDATA: name+alias at birth; **alias-only, name MUST be excluded** in NDATA; alias unique across the Edge Node | **Confirmed** | payloads ch. 6 |
| `seq` 0–255 inclusive, +1 wrap after 255; NDEATH MUST NOT include `seq` | **Confirmed** | payloads ch. 6 |
| `bdSeq`: name `bdSeq`, datatype **INT64**, wrap ≤255, NBIRTH value == Will value | **Confirmed** | `[tck-id-message-flow-edge-node-birth-publish-will-message-payload-bdSeq]`, `[…nbirth-payload-bdSeq]` |
| NDEATH timestamp is **MAY** (profile omits it — v2 §3.6 stands) | **Confirmed** | payloads ch. 6 |
| Chronological ordering required | **Confirmed** (see §3.1 nuance) | `[tck-id-operational-behavior-data-publish-nbirth-order]` |
| Quality property: Int32 (PropertyValue type 3); value MUST be one of **0, 192, 500** | **Confirmed** | `[tck-id-payloads-propertyset-quality-value-type]`, `[…-value-value]` |

**Proto verified** (fetched from Tahu; slice 1 pins the exact commit + SHA-256):
proto2; SPDX `EPL-2.0` header present. `Payload`: `timestamp=1 uint64`,
`metrics=2`, `seq=3 uint64`, `uuid=4`, `body=5`. `Metric`: `name=1`, `alias=2
uint64`, `timestamp=3 uint64`, `datatype=4 uint32`, `is_historical=5`,
`is_transient=6`, `is_null=7`, `metadata=8`, `properties=9`; value oneof
`int_value=10 uint32`, `long_value=11 uint64`, `float_value=12`,
`double_value=13`, `boolean_value=14`, `string_value=15`, `bytes_value=16`,
`dataset_value=17`, `template_value=18`, `extension_value=19`. Blocker 2's
bit-preserving rule is exactly right for arms 10/11. `PropertySet` = parallel
`keys`/`values` repeated lists → **our projection fully controls property order**
(v2 §9.4 resolved: deterministic ordering is achievable and required).

## 2. Reality-check results — Core contracts (v2 §9.2) — no Core change needed

- `ReplaySessionStart` → `ReplaySessionStartState { ReplayBoundary, LatestValueSnapshot }`;
  `ReplaySessionCutover` carries the same snapshot shape; `PublishContext` carries
  phase/epoch/H/C/batch-range. All factory inputs exist on `master`.
- `LatestMetricValue` rows carry metric key, `CanonicalValueType`, value,
  `IsNull`, UTC timestamp, `DataQuality`, `QualityReason`, `Unit`, static
  properties, buffer sequence — everything NBIRTH needs.
- **Bonus finding:** `LatestMetricValue.Create` **already rejects** `Array`,
  `Object`, and bare `Null` datatypes at Core, so NBIRTH inputs arrive
  pre-sanitized. The encoder-side `Array`/`Object` rejection (ADR-0035 Rule 5)
  therefore guards the **NDATA path** (live `CanonicalDataPoint`s) — both
  rejection sites stay, and slice 3 tests cover the NDATA one.

## 3. Profile choices pinned by the reality check (deltas to v2)

### 3.1 Ordering nuance (amends v2 §3.7.4)
The normative MUST (`…-nbirth-order`) covers **`is_historical=false`** values.
Our profile applies chronological ordering to historical replay values **as
well** (profile choice, consistent with spec intent and golden-byte stability);
the conformance note cites the tck-id for the non-historical case and marks the
historical case as profile policy.

### 3.2 NBIRTH `seq` nuance (amends v2 §6 wording, not behavior)
Spec 3.0.0 requires NBIRTH `seq` in **0–255 inclusive** — not necessarily 0
(`[tck-id-payloads-sequence-num-zero-nbirth]`, text prevails over the id's
name). Our profile still starts sessions at `seq=0`; the "NBIRTH `seq=0`
physically present" golden test stands — its point is the proto2
zero-default-omission canary (the Tahu bug class), not a spec mandate that
births always be zero.

### 3.3 Quality emission (resolves v2 §9.5)
When present, `Quality` MUST be 0/192/500 — we emit only `0` and `500` (Good =
omit; never 192). **`QualityReason` is emitted only for the two lossy
mappings** — Uncertain (`"quality uncertain"`) and Unknown
(`"quality unknown"`). Bad and Stale map losslessly to their codes and carry
**no** `QualityReason` in v1. (See §4 for the flagged tradeoff.)

### 3.4 Toolchain pins (resolves v2 §9.3)
- `Google.Protobuf` runtime **3.35.1** (current stable, 2026-06-11); `protoc`
  from the matching protobuf release — exact versions recorded in the
  provenance doc at generation time.
- New csproj mirrors `Sinks.Mqtt`: net8.0, `Nullable`, `ImplicitUsings`,
  `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`,
  `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`.
- **Vendored generated file:** relies on its auto-generated header +
  `GeneratedCodeAttribute` for analyzer/style exemption; if the 0-warnings gate
  still trips on it, scope a `NoWarn` to that file alone (never project-wide) —
  verified in slice 1.

## 4. `QualityReason` — FROZEN by owner decision (GO, 2026-07-19)

K2 v1 publishes **only these controlled wire values** in `QualityReason`:
`"quality uncertain"` (Uncertain) and `"quality unknown"` (Unknown). Source-
specific text (e.g. `"read timeout"`, protocol errors, exception messages,
device diagnostics) stays inside EdgeConnect diagnostics surfaces and is
**never exposed through `QualityReason`**.

Frozen constraints:

1. `QualityReason` is an **EdgeConnect-defined** property, not a
   Sparkplug-standard property.
2. Present **only** for `Uncertain` and `Unknown`.
3. Omitted for `Good`, `Bad`, `Stale`, and null handling unless the frozen
   mapping explicitly says otherwise.
4. Consumers must **not** interpret it as detailed fault causality (the
   conformance note and ADR amendment state this).
5. Any later source-reason passthrough uses a **separate property** and
   requires an **ADR amendment** covering naming, sanitization, length limits,
   and compatibility.

## 5. Freeze status

- v2 §1–§8 (scope, non-scope, frozen wire semantics, provenance safeguards,
  spec-pin strings, golden gate, slices, follow-ups) — **unchanged**.
- v2 §9 reality-check items — **all resolved** (§1–§3 above).
- Slice 1 first actions: pin the Tahu commit + SHA-256 (the proto verified here
  was fetched from `master`; the pin must reference an exact commit), generate +
  vendor, provenance doc, regeneration-verification script.
- **On GO:** cut `feat/sparkplug-b-k2-wire` from `master`, execute slices 1–5
  with per-slice external review, K1.3 rhythm.
