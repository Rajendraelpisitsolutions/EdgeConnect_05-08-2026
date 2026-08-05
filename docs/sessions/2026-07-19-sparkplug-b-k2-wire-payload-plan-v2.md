# Sparkplug B — K2 Wire + Payload Plan (v2)

**Date:** 2026-07-19
**Author:** Session with Sudhakar
**Status:** v2 — external review folded (all three blockers + amendments A–F +
open-decision verdicts). **Supersedes v1 as the implementation plan.** Next:
reality-check pass → v3 (freeze).
**Governing docs:** master plan `2026-07-13-sparkplug-b-sink-plan-v2.3.md` §8,
**ADR-0035** (Rules 2, 4, 5), **ADR-0036** (context; runtime rules are K3),
K1.3 handoff `2026-07-19-sparkplug-b-k1.3-handoff.md`, plan v1 + this review.
**Baseline:** `master` @ `5b9c2f6`.

> **Review disposition (v1 → v2):** K2 scope, architecture, and the five-slice
> structure are **approved unchanged**. Three blockers folded: (1) alias policy
> corrected — `Node Control/Rebirth` is the *only* name-only exception, `bdSeq`
> **does** get an alias; (2) signed-integer bit-preserving encoding frozen;
> (3) timestamp range semantics frozen (pre-epoch rejection, not truncation
> alone). Plus: constrained `seq`/`bdSeq` domain types, NDEATH field-presence
> profile, exact `QualityReason` property contract, deterministic metric
> ordering, validated-model pipeline, expanded golden gate, and
> project-profile (not certification) conformance wording.

---

## 1. Scope (approved, unchanged from v1)

K2 = the **stateless, broker-free wire layer** of the Sparkplug B sink, per
plan v2.3 §8: new assembly `ElpisEdgeConnect.Sinks.SparkplugB` (+ tests),
pinned Tahu `sparkplug_b.proto` + vendored generated types, first-party
`SparkplugPayloadEncoder`, `CanonicalToSparkplugTypeMap`, the locked
value/quality/null/timestamp mapper, pure NBIRTH/NDATA/NDEATH payload
factories, `SparkplugTopicFactory` (incl. exact NCMD subscribe topic),
`SparkplugErrors` catalog, and the byte-level golden suite via an independent
test-side proto2 decoder.

"3.1.1 profile" = the Sparkplug-on-MQTT-3.1.1 conformance constants baked into
the payload/topic layer. Transport itself is K3.

## 2. Non-scope (unchanged)

No session state machine, no counter *management*, no alias
*assignment/persistence*, no `IReplayAwareSinkAdapter` implementation (K3); no
validation/license/DI (K4); no wizard (K5); **no Core changes** (stop-and-
surface if a Core need appears); no broker in tests.

## 3. Frozen wire semantics (new in v2 — the review's normative core)

### 3.1 Alias policy (Blocker 1 — corrected)

Aliases are unsigned 64-bit, unique across the Edge Node. When aliases are in
use (they are, in this profile):

| Metric | NBIRTH | NDATA |
|---|---|---|
| Canonical application metric | Name + alias | **Alias only, no name** |
| `bdSeq` | Name + **alias** | Not normally published |
| `Node Control/Rebirth` | **Name only, no alias** (spec exception) | Not part of normal NDATA |

- **Alias `0` is reserved** (never assigned); assigned aliases begin at `1`.
- The alias map (`IReadOnlyDictionary<SparkplugAliasKey, ulong>`, caller-
  supplied; K3 owns assignment/persistence) **contains `bdSeq`** and
  **structurally excludes `Node Control/Rebirth`**.
- `SparkplugAliasKey` excludes `RouteId`; Edge-Node identity is carried by the
  owning map/factory input; metric-name normalization happens **before** key
  construction.

**Factory validation (typed errors, before any bytes):** every ordinary NBIRTH
metric has an alias; aliases unique across the full Edge-Node namespace;
`Node Control/Rebirth` has no alias; every NDATA metric resolves to an
established alias; NDATA carries no metric name; alias `0` rejected; duplicate
or missing aliases rejected.

### 3.2 Signed numeric encoding (Blocker 2 — frozen)

Tahu stores values in **unsigned** protobuf arms (`uint32 int_value`,
`uint64 long_value`) while Sparkplug datatypes `Int32`/`Int64` are signed. The
rule is **bit-preserving two's-complement reinterpretation** (unchecked):

- Canonical `Integer` (Int32) → `uint32 int_value` via bit-preserving cast.
- Canonical `Long` (Int64) → `uint64 long_value` via bit-preserving cast.
- The consumer interprets per the Sparkplug **datatype**, not the protobuf CLR
  type. Checked casts are forbidden here (they corrupt/reject negatives).
- Canonical **unsigned** types: none exist today; if introduced later they are
  **unsupported until explicitly added** — typed rejection, never silently
  squeezed into signed arms.

**Golden byte cases (minimum):** `-1`, `int.MinValue`, `int.MaxValue`,
`long.MinValue`, `long.MaxValue`.

### 3.3 Timestamp conversion (Blocker 3 — frozen)

Sparkplug timestamps are unsigned 64-bit Unix **milliseconds**. The conversion
pipeline:

1. Normalize to UTC per the frozen canonical UTC policy.
2. **Reject** timestamps before `1970-01-01T00:00:00Z` with a typed
   `SPARKPLUG.*` error (a cast would fabricate a huge future time).
3. Convert to whole Unix milliseconds.
4. Discard sub-millisecond precision (floor for post-epoch values).
5. **Reject** upper-range overflow rather than wrap.

Never document this as bare "truncate toward zero" — that wording is ambiguous
for negatives. **Golden cases:** exact epoch; sub-millisecond value; epoch+1 ms;
pre-epoch rejection; UTC/non-UTC behavior per the strict UTC contract.

### 3.4 Quality + `QualityReason` property contract (verdict 1 — accepted)

Locked table (ADR-0035 Rule 5) plus the accepted `Unknown` mapping:

| Canonical | Wire |
|---|---|
| Good | omit `Quality` entirely |
| Bad | `Quality=0` |
| Stale | `Quality=500` |
| Uncertain | `Quality=0` + `QualityReason="quality uncertain"` |
| **Unknown** | **`Quality=0` + `QualityReason="quality unknown"`** |
| Null value | `is_null=true`, no value arm (independent of quality) |
| Replayed | `is_historical=true` (independent of quality) |

- This is an **EdgeConnect semantic mapping, not a fourth Sparkplug quality
  code** — the ADR-0035 amendment (part of Slice 3) must say so. Sparkplug
  standardizes only `0`, `192`, `500`; we use `0` and `500` and never emit `192`
  (Good = omit).
- **`QualityReason` wire contract:** key exactly `QualityReason`; value type
  String; UTF-8; present only when a reason exists; **no empty-string
  property**; deterministic property ordering.
- **Stable reason strings** for the mapped states (`"quality uncertain"`,
  `"quality unknown"`) — arbitrary source text would make golden payloads
  nondeterministic. Richer source diagnostics come later as separate
  properties, not by loosening these strings.

### 3.5 Constrained sequence domain types (amendment A)

K3 manages the counters; K2 must be *unable* to encode invalid ones. Introduce
constrained value types — construction validates `0..255`, wrap-after-255 is
the caller's (K3's) concern:

```csharp
SparkplugSequenceNumber       // seq: 0..255; NBIRTH carries it, NDEATH never does
SparkplugBirthDeathSequence   // bdSeq: 0..255
```

Factories accept only these types — no raw `ulong` with deep-in-encoder checks.

### 3.6 NDEATH profile (amendment B — frozen field-presence table)

One NDEATH shape (the MQTT 3.1.1 Will payload); K3's intentional-disconnect
NDEATH reuses **the same frozen bytes** (any deviation must be explicitly
documented — avoid two NDEATH profiles):

| Field | Presence |
|---|---|
| payload timestamp | **omitted** (a precomputed Will timestamp is useless to hosts) |
| `seq` | **omitted** |
| metrics | exactly one: `bdSeq` |
| `bdSeq` metric name | present (`"bdSeq"`) |
| `bdSeq` alias | **omitted in NDEATH** |
| `bdSeq` metric timestamp | **omitted** |
| `bdSeq` datatype | `Int64` |
| `bdSeq` value | same value later used in the paired NBIRTH |

Golden tests prove the **absence of every non-required field**, not only `seq`.

### 3.7 Deterministic metric ordering (amendment E)

Dictionary inputs give no order; the factories pin one:

1. `bdSeq` first;
2. `Node Control/Rebirth` second (NBIRTH only);
3. application metrics ordered by canonical `SparkplugAliasKey` (stable,
   culture-invariant ordinal comparison);
4. repeated historical samples of one metric in **chronological order** (spec
   requirement for value sequences; also prevents golden-byte churn).

### 3.8 Internal pipeline — validate, then encode (amendment F)

The public encoder is not the first place invalid data is discovered:

```text
Canonical value
  → validated Sparkplug metric model   (typed SPARKPLUG.* rejections live here:
                                        Array/Object, timestamp range, alias
                                        failures, numeric incompatibility,
                                        empty-reason property, seq domain)
  → validated payload model            (ordering, field-presence profile)
  → protobuf projection
  → bytes                              (effectively infallible; resource
                                        failures only)
```

## 4. Generated code + provenance (verdicts 2 & 3 — approved with safeguards)

**Vendored generated C#** (no `Grpc.Tools` in product builds). Required
artifacts:

- pinned `sparkplug_b.proto` with **upstream copyright, SPDX identifier, and
  EPL-2.0 notice intact**;
- checked-in generated `.cs`, **never manually edited**, with a generated-file
  header recording the exact generation command;
- regeneration script (may use `protoc`/`Grpc.Tools` locally);
- **regeneration verification procedure that fails when regeneration produces
  a diff** (CI or an explicit pre-release job);
- provenance record `docs/compliance/sparkplug-b-proto-provenance.md`: Tahu
  commit, proto SHA-256, `protoc` version, generator/plugin version,
  `Google.Protobuf` runtime version, redistribution actions taken. The record
  **describes provenance; it does not claim legal approval** (ADR-0035 Rule 2
  posture unchanged).

## 5. Specification pin + conformance wording (verdict 6 + amendment D)

Exact strings for the conformance note and all docs/UI copy — three version
strings, never conflated:

- **Normative specification:** `Eclipse Sparkplug Specification Version 3.0.0`
  (November 2022).
- **Sparkplug namespace:** `spBv1.0` (a namespace token, *not* the spec
  revision).
- **Payload schema:** the exact pinned `sparkplug_b.proto` at the recorded
  Eclipse Tahu commit.
- **Initial MQTT transport profile:** `MQTT 3.1.1`.

**Suite naming:** "K2 wire-conformance golden suite for the EdgeConnect
node-only Sparkplug 3.0 profile." It proves conformance to **our pinned subset
and wire invariants** — it is *not* the official Sparkplug TCK and must never
be described as certified compatibility.

## 6. Golden gate — expanded (amendment C; supersedes v1 §1.9 minimum)

The independent test-side proto2 decoder must expose **field presence, wire
type, and repeated-field order** — not only decoded default values (this is
the central protection against proto2 default-value bugs) — and must **skip
unknown fields safely** (self-test included).

Byte-level proofs (v1 list retained, plus):

- NBIRTH `seq=0` physically present; NBIRTH + NDATA payload timestamps
  physically present; NDATA `seq=0` physically present **after wrap**;
- datatype fields present in NBIRTH; metric timestamps present on NBIRTH and
  NDATA;
- negative `Int32`/`Int64` cases (§3.2 list);
- `false` Boolean physically encoded (not absent); `0.0` Float/Double
  physically encoded;
- empty string vs null; zero-length byte array vs null;
- `is_null=true` with no value arm; `is_historical=true` when set;
- Good → no `Quality` bytes; Uncertain/Unknown → `Quality=0` + exact
  `QualityReason` string; Stale → `Quality=500`; no empty-string property;
- `Node Control/Rebirth`: name present, Boolean datatype, `false` value, **no
  alias**; `bdSeq`: `Int64` datatype + value, alias in NBIRTH, no alias in
  NDEATH;
- NDEATH: absence of every non-required field per §3.6;
- alias-uniqueness rejection; missing/invalid-alias rejection; alias-`0`
  rejection; NDATA name-absence;
- payload- and metric-level **field-number correctness** against the pinned
  proto;
- timestamp range cases (§3.3 list).

## 7. Slices (structure approved; reviewer adjustments folded)

| Slice | Content | Review adjustment folded |
|---|---|---|
| 1 | Scaffolding; pinned proto + vendored generated types; provenance record | + regeneration-verification procedure; exact spec/proto pin strings (§5) |
| 2 | Independent proto2 decoder + harness | decoder exposes field **presence**, wire type, repeated-field **order**; unknown-field skip self-test |
| 3 | Type map + value/quality/timestamp mapper + `SparkplugErrors` + **ADR-0035 Rule 5 amendment** (Unknown mapping, QualityReason contract, signed-int + timestamp rules) | + §3.2 signed semantics; §3.3 range rules; §3.4 exact PropertySet contract |
| 4 | Topic factory; identity/alias key types; constrained seq types; NBIRTH/NDATA/NDEATH factories | **Blocker 1 alias policy**; §3.5 domain types; §3.6 NDEATH profile; §3.7 ordering; §3.8 pipeline |
| 5 | Full golden gate (§6) + conformance note | zero/false/negative/empty canaries; NDEATH absence checks; §5 wording |

Slice-per-commit, external review per slice, `v3.x` amendments if reality
diverges — same rhythm as K1.3.

## 8. Retained K1.3 follow-ups (unchanged; none land in K2)

1. Material-schema / generation-changing rebirth → post-K3 slice.
2. Coordinated replay-sink hot replacement → K3.
3. Production `ISinkReplayCapabilityClassifier` registration → K4.

## 9. Reality-check pass (v2 → v3) — what must be verified before freeze

1. **Fetch + pin the actual artifacts:** Tahu `sparkplug_b.proto` at a chosen
   commit; Sparkplug 3.0.0 spec PDF. Verify against the real text: the
   `Node Control/Rebirth` no-alias exception; **no** alias exception for
   `bdSeq`; `seq`/`bdSeq` `0..255` + wrap; NDEATH timestamp permission
   wording; chronological-ordering requirement; quality codes `0/192/500`;
   the exact `uint32 int_value`/`uint64 long_value` field numbers and the
   full `Metric`/`Payload`/`PropertySet` field-number table (§6 depends on it).
2. **Core contract shapes:** confirm `ReplaySessionStart`/`PublishContext`
   (as merged in K1.1–K1.3) carry what the factories need (snapshot rows,
   phase, epoch) with no Core change required.
3. **Versions:** current `Google.Protobuf` runtime, `protoc`, and the .NET 8 /
   warnings-as-errors settings mirrored from an existing sink project.
4. **PropertySet ordering determinism** in the generated proto2 types
   (repeated key/value lists — confirm the projection controls order).
5. Confirm the `QualityReason`-on-`Quality=500` (Stale) case: does Stale also
   carry a reason when the canonical point has one? (v2 position: yes if
   non-empty, per §3.4 presence rule — verify no golden-determinism conflict.)

## 10. Exit criteria (K2 gate — v1 list plus)

- Solution builds 0 warnings / 0 errors; no change to Core or any existing
  project; only `Google.Protobuf` runtime added.
- Provenance record complete; regeneration verification passes (no diff).
- Full §6 golden gate green via the independent decoder.
- Every rejection path carries a `SPARKPLUG.*` typed error surfaced at the
  validated-model stage (§3.8), with tests.
- ADR-0035 Rule 5 amendment merged (Unknown mapping + QualityReason contract +
  numeric/timestamp rules), worded as an EdgeConnect semantic mapping.
- Conformance note uses §5 pin strings and project-profile wording only.
- Plan trail updated; handoff note before sign-off.
