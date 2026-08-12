# Session handoff — 2026-07-19 — Sparkplug B K2 complete, PR open

## Status: K2 done, awaiting merge

**PR `feat/sparkplug-b-k2-wire` → `master`** is **OPEN**. **Merge is pending —
the owner's call.**

K2 delivers the **stateless, broker-free wire layer** of the Sparkplug B sink:
the pinned schema + vendored protobuf types, the value/quality/timestamp
mappers, the identity/alias/sequence domain types, the NBIRTH/NDATA/NDEATH
payload factories, and the byte-level golden conformance gate. **Core stays
untouched — no change to `ElpisEdgeConnect.Core` or any existing project.** It
builds on the merged K1.1–K1.3 Core replay contracts.

Scope is the frozen master plan v2.3 §8 K2 line ("Sparkplug wire + payload
factory + mappers + 3.1.1 profile, golden tests"), executed to the frozen K2
plan `2026-07-19-sparkplug-b-k2-wire-payload-plan-v3.md`. The session actor is
**K3**, validation/license/DI is **K4**, the Studio wizard is **K5** — none of
that is in this PR.

### Slices 1–5 — all complete and externally approved

| Slice | Landed | Review |
|-------|--------|--------|
| 1 | Pinned `sparkplug_b.proto` (Tahu `46f25e79`, SHA-256 gated) + vendored `Google.Protobuf` types + provenance record + regeneration `-Verify` gate | r1 (byte-level drift gate, pinned generated hash, arch rejection) |
| 2 | Independent proto2 wire decoder (no generated types, no Google.Protobuf parse) + cross-check harness | r1 (field-number bound `(1<<29)-1`, fixed32 + illegal wire-type coverage) |
| 3 | `CanonicalToSparkplugTypeMap` + value/quality/timestamp mapper + `SparkplugErrors` + **ADR-0035 Rule 5 amendment** | r1 (null metrics never carry `QualityReason`; validated model made assembly-internal + immutable) |
| 4 | Topic factory + identity/alias/sequence domain types + NBIRTH/NDATA/NDEATH factories | r1 (NBIRTH alias-baseline exact set match; typed null-key rejection; encounter-order policy) |
| 5 | Wire-conformance golden gate + conformance note | r1 (precise rejection wording; cardinality-aware NBIRTH profile; ordered quality-property pairing) |

Every slice landed a feature commit + a folded review-r1 commit. All five
slices externally approved; slice 5 approval = K2 complete.

### Final verification (HEAD `664f7ae`)
- **SparkplugB.Tests 167** · **Core.Tests 1251** · **Host.Tests 225** ·
  **Management.Tests 1149** — all green (full unfiltered runs).
- Solution **0 warnings / 0 errors** on the new projects
  (`TreatWarningsAsErrors`); the only solution-wide warnings are pre-existing
  CS1998s in the untouched legacy `src/ElpisEdgeConnect` project.
- `tools/sparkplug-proto/regenerate.ps1 -Verify` byte-identical
  (generated SHA-256 `84E844E7…5529C7`).
- Only new package: `Google.Protobuf` 3.35.1. No Tahu runtime dependency.

### What shipped (all under `src/ElpisEdgeConnect.Sinks.SparkplugB`)
- `Protos/sparkplug_b.proto` (pinned, EPL-2.0 header intact) +
  `Protobuf/SparkplugB.g.cs` (vendored, internal) +
  `tools/sparkplug-proto/regenerate.ps1`.
- `SparkplugBProtocol` (`ProtocolName = "sparkplug-b"`, `spBv1.0`),
  `SparkplugErrors` (`SPARKPLUG.*` catalog).
- `Mapping/`: `SparkplugDataType` (pinned, cross-checked to the proto enum),
  `CanonicalToSparkplugTypeMap`, `SparkplugTimestamp`, `SparkplugQuality`,
  `SparkplugMetricValueModel` (internal, immutable) + `…Mapper` (internal).
- `Identity/`: `SparkplugEdgeNodeIdentity`, `SparkplugAliasKey` (no RouteId),
  `SparkplugSequenceNumber`, `SparkplugBirthDeathSequence` (both 0..255).
- `Topics/SparkplugTopicFactory` (incl. exact NCMD subscribe topic).
- `Payloads/`: `SparkplugMetricSample`, `SparkplugPayloadProjection`
  (internal), `SparkplugPayloadEncoder` (**the public encode surface**).
- Docs: `docs/compliance/sparkplug-b-proto-provenance.md`,
  `docs/compliance/sparkplug-b-wire-conformance.md`,
  ADR-0035 Rule 5 amendment (Unknown mapping, `QualityReason` contract,
  numeric + timestamp rules).

### Plan trail (durable evidence, on the branch under `docs/sessions/`)
`…-k2-wire-payload-plan-v1.md` → `-v2.md` (review folded) → `-v3.md`
(reality-checked, **frozen**, owner GO 2026-07-19). Per-slice review evidence
lives in the commit chain and PR description.

## K3 — the Sparkplug session actor (next milestone)

K2 is the pure wire layer; **K3 implements `SparkplugSessionActor`** — the
stateful MQTT-3.1.1 session that consumes the Core replay context + lifecycle
(K1.3) and drives the K2 factories: CONNECT/reconnect, `seq`/`bdSeq` management
+ wrap, alias assignment/persistence (the Edge-Node identity SQLite store from
K0 WS5), the mandatory `Node Control/Rebirth` NCMD handshake, and the
`IReplayAwareSinkAdapter` implementation.

**K3 MUST carry these named follow-ups** (deferred intentionally, not gaps):

1. **Material-schema / generation-changing rebirth** (from K1.3) — needs an
   authoritative new-generation manifest seed Core lacks today;
   `AdvanceGenerationAsync` stays off the K1.3 route capability until this
   lands. A post-K3 slice.
2. **Coordinated replay-sink hot replacement** (from K1.3) — the full
   coordinator ↔ driver dance. K1.3 restricts an in-place replay-sink change to
   a fail-closed reject; K3 builds the real coordinated hot-replace.
3. **Production `ISinkReplayCapabilityClassifier` registration** (from K1.3) —
   the incoming-side classifier is an inert optional seam in K1.3; K4 registers
   the real classifier alongside the Sparkplug sink DI.
4. **K3 epoch-gating acceptance tests** (from plan v2.3 §8 K3 carry-forward) —
   the actor gates lifecycle inputs on BOTH `ReplaySessionId` and
   `ReplayEpochId`; cover stale-session-same-epoch, non-increasing rebirth
   epoch, and promotion-only-after-successful-NBIRTH against the real actor.

### K2 wire-layer seams K3 consumes
- **Payload factories** take `seq`/`bdSeq`/alias values + publication instant as
  inputs (K2 is pure) — K3 owns all counter state and the alias table.
- **`bdSeq` alias** is a separate `EncodeNBirth` parameter (bdSeq has no
  `SparkplugAliasKey`); K3 reserves a non-zero alias for it, unique vs. the app
  map. Alias 0 is reserved; app aliases begin at 1.
- **NBIRTH alias baseline** requires exact set match between app metrics and the
  app alias map — K3 must announce every alias it will later use in NDATA.
- **NDEATH** bytes are frozen and identical for the MQTT Will and the
  intentional-disconnect publish; the `bdSeq` value must equal the paired
  NBIRTH's.
- **NDATA** is alias-only, chronologically ordered by wire timestamp, encounter
  order as the final tiebreak.

## Post-merge housekeeping
- The repo-root `k2-slice-*.diff` review artifacts were removed after this
  handoff was written (superseded; durable evidence is the plan trail, this
  note, the PR, the commit chain, and the golden suite). They were never
  committed.
