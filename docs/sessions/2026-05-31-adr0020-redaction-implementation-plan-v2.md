# ADR-0020 redaction engine — implementation plan v2

**Status:** v2 — **review-approved, implementation-ready.** Two ChatGPT review passes folded in (pass 1 reshaped v1→v2; pass 2 approved v2 and requested four pre-implementation edits, now applied — see §0b). No v3 required. Ready to start M-A on operator go.
**Date:** 2026-05-31
**Implements:** `docs/decisions/0020-diagnostic-bundle-redaction-spec.md` (Accepted, as amended by Amendment 1)
**Branch:** `feat/bundle-redaction-spec`
**Supersedes:** v1 (`91c9beb`/`30bbbae`). Read this; v1 retained for trail only.

---

## 0. What the review pass changed (v1 → v2)

The review pass reacted to the pre-launch / no-customers fact by withdrawing the migration-safety caution and optimising for architectural cleanliness. Six deltas:

| # | v1 | v2 (this plan) |
|---|---|---|
| 1 | Backup migration in M-C | **Backup migration moves into M-A** — gives a real consumer + real integration coverage immediately, not synthetic fixtures |
| 2 | O-1: backup keeps a per-consumer tier policy | **Full unification — no consumer-specific semantics.** Tiers are a property of the *field/rule*, identical for backup and bundle (see §1a) |
| 3 | Q-4: rename `SecretRedactor` later / optional | **Rename `SecretRedactor` → `ConfigRedactionEngine` immediately.** No shims, no aliases, no legacy mode |
| 4 | Q-1: rules-as-source-of-truth a strong recommendation | **Near-mandatory.** Adapter Rules → Factory → Engine → Drift Guard, single declaration; factory consumes the rules' key inventory |
| 5 | Q-2/O-4: snapshot test for drift | **Upgraded:** factory-metadata declaration → generated rules-coverage test; eliminate duplication, not just assert it |
| 6 | — | Spend architectural capital now; these restructures get expensive once customers arrive |

## 0a. Resolved open questions

- **Q-1 → resolved: rules are the single source of truth** (delta 4).
- **Q-2 → resolved: declaration-driven coverage, not just a snapshot** (delta 5).
- **Q-4 → resolved: rename now** (delta 3).
- **Q-5 → resolved (pass 2): sibling metadata shape** (see §0b.1 + C-2).
- **Q-3 → resolved (pass 2): phased detector** — deterministic ships in R-1 v1; entropy is Phase 2 and does not block R-1's first release (see §0b.2 + C-5).

## 0b. Plan review pass 2 (2026-05-31) — approved, four edits applied

Pass 2 approved every structural decision in v2 and requested four edits before implementation. All four are applied in this revision:

1. **Q-5 resolved now — MASK uses sibling metadata.** The masked value stays a string (`"password": "***"`) and a sibling key carries the metadata (`"password__redacted": { "masked": true, "originalByteLength": 14 }`). Rationale: existing consumers still read `"password"` as a string; manifest readers can ignore the metadata; less surprising JSON shape; easier future evolution. (C-2, and amends ADR-0020 R-3/Rule-4 representation.)
2. **Q-3 tightened — split deterministic from heuristic detection.** R-1 v1 ships **deterministic** detectors only (PEM headers, JWT format, PKCS markers, SSH key markers) — near-zero false positives, near-zero tuning. **Entropy/token-likelihood scoring is Phase 2** and explicitly does **not** gate R-1's first release. (C-5; amends ADR-0020 A1.4 #1 / R-1.)
3. **Redaction determinism invariant added** as a locked engine property (new §1c).
4. **Layering tightened — adapter rules are metadata-only** (§5 + §1c note): a `*BundleRedactionRules` declaration must not reference any Management type.

---

## 1. Scope boundary (unchanged from v1)

Builds the **reusable redaction substrate** ADR-0020 mandates and rewires backup onto it. Does **not** build the "Generate Diagnostic Bundle" ZIP/preview workflow (downstream deliverable).

> **UI prerequisite (operator standing preference):** the manifest preview (Rule 3) is operator-facing, so the downstream bundle-generation deliverable **must produce a static HTML mockup for operator sign-off before any Studio wiring** (round-3 mockup per `2026-05-31-diagnostic-strategy-handoff.md`). This substrate has no UI — no mockup needed here; flag carried forward.

## 1a. The unification rule (replaces v1's consumer-tier-policy idea)

**Tier is a property of the field, decided once, applied identically by every consumer.** There is no "backup mode" and no "bundle mode."

- **MASK** — key retained, value → `***` + byte-length annotation. Used for secrets that a **restore round-trip must prompt for** (passwords, tokens, auth material). The key staying visible is exactly what lets backup-restore tooling detect "this was redacted, prompt the operator." The masked marker is invalid-for-direct-apply, so a restore that skips re-entry fails validation loudly.
- **STRIP** — key omitted entirely. Reserved for material that must **never ship in any artifact**, not even its key name: private-key bytes, certificate PEM bodies, license-file content.
- **INCLUDE** — verbatim. Benign connection data support needs (endpoint, host, port).
- **EXCLUDE** — file-level (consumer's file selection), not a field tier.

Restore-prompting therefore **falls out of MASK's representation**, not out of a backup-only code path. If a genuine backup-vs-bundle conflict surfaces during implementation (a field one consumer needs MASK and the other STRIP), **stop and surface it** rather than quietly add a per-consumer branch — that would reintroduce the drift we're deleting.

## 1c. Redaction determinism invariant (LOCKED)

> **For identical input JSON and an identical rule set, `ConfigRedactionEngine` must produce byte-for-byte identical redacted output and byte-for-byte identical provenance output.**

This is load-bearing and is locked before implementation, not discovered after:

- Manifest checksums (`BackupManifest.Checksums`), bundle hashes, support-side bundle comparisons, and future replay workflows all depend on it.
- It reinforces ADR-0020 Rule 4 (self-describing/reproducible) at the engine level — reproducibility is only real if the engine is deterministic.

**Forbidden in the redaction output path** (each breaks the invariant): non-deterministic dictionary/JSON iteration order, timestamps embedded in redacted output or provenance, random IDs/GUIDs, culture-dependent formatting, or any wall-clock/`Random`/`Guid.NewGuid()` read. Provenance paths emit in a stable document-order traversal. A determinism test (same input twice → identical bytes) is part of the engine's test suite.

> **Layering note (metadata-only rules):** a `*BundleRedactionRules` declaration is **pure metadata** — key→tier inventory + name lists. It must not reference any Management type (no `*BundleRedactionRules` → `ConfigRedactionEngine` dependency). Management composes the rules; the rules never reach back up. This keeps the dependency arrow Core ← adapters ← Management intact and prevents the eventual accidental coupling.

---

## 2. Component breakdown

### C-1 — `[BundleTier]` attribute + enum (Core)
`src/ElpisEdgeConnect.Core/Configuration/BundleTier.cs` (enum `Include`/`Mask`/`Strip`) + `BundleTierAttribute.cs`. Protocol-agnostic → allowed in Core. `EXCLUDE` is file-level, not an enum value.

### C-2 — `ConfigRedactionEngine` (Management) — renamed from `SecretRedactor`
- **Rename immediately** (delta 3): `SecretRedactor` → `ConfigRedactionEngine`, `SecretRedactorTests` → `ConfigRedactionEngineTests`. Delete the old name; no alias.
- Tier-aware: applies a resolved name→tier map over raw JSON (`JsonNode`), emitting INCLUDE/MASK/STRIP outcomes + JSONPath-ish provenance (manifest needs it).
- **Q-5 (resolved, pass 2):** MASK annotation is **sibling metadata** — the masked value stays a string (`"password": "***"`) and a sibling key carries `"password__redacted": { "masked": true, "originalByteLength": 14 }`. Existing string consumers are unaffected; metadata is ignorable.

### C-3 — Per-protocol redaction rules as **single source of truth** (beside each adapter)
- One rules declaration per adapter module (9): `Sinks.{Mqtt, OpcUaServer}`, `Sources.{Focas2, OpcUaClient, ModbusTcp, MTConnect, BrotherHttp, S7, EthernetIp}`.
- Each declares a **key→tier inventory** for its Connection/Extras block (every key the protocol understands + its tier) plus an unknown-key policy (fail-open INCLUDE + protocol-specific extra MASK/STRIP names beyond the shared baseline).
- **Delta 4 — the factory consumes this inventory.** `From*Instance` reads its key list from the rules declaration (or, where full inversion is too invasive, the rules declaration is the canonical list the factory is generated/checked against). The intent: **a new Connection key cannot be parsed by the factory without having a tier**, by construction.

### C-4 — Three classification mechanisms (engine)
M1 typed attribute (reflection over Core typed fields, fail-closed → STRIP) · M2 derived map (from C-3 inventory, fail-closed) · M3 allowlist (shared baseline + per-protocol extras, fail-open INCLUDE). Engine locates each byte's world, applies M1→M2→M3, first match wins.

### C-5 — Secret-shape detector (R-1, engine) — phased (delta: pass 2)
`SecretShapeDetector.cs`. Runs over World-2b (fail-open) values only. **Warns, never auto-strips** (A1.4 #1).
- **Phase 1 (ships in R-1 v1) — deterministic:** PEM headers (`-----BEGIN`), JWT structure (`xxx.yyy.zzz` base64url), PKCS markers, SSH key markers. Near-zero false positives, near-zero tuning. **This is the R-1 release gate.**
- **Phase 2 (fast-follow, does NOT block R-1 v1) — heuristic:** Shannon-entropy / token-likelihood scoring. This is where false-positive/tuning burden lives, so it is deliberately decoupled from the first release. **Q-3 (now scoped to Phase 2):** entropy cutoff + min length to avoid GUID/ULID false positives — tune against real config samples.
- **Honest note:** until Phase 2 lands, a high-entropy *unstructured* token under a benign name in World 2b is mitigated only by the static allowlist + preview (consistent with the fail-open residual risk already accepted in ADR-0020 A1.3).

### C-6 — Drift guard (R-2) — declaration-driven (delta 5)
- The per-protocol rules declaration (C-3) is the metadata source; a generated/parametrised coverage test asserts: (a) the factory's read-set == the declared key inventory (no key parsed without a tier); (b) every secret-typed field resolves MASK/STRIP, never INCLUDE.
- **Fails CI** (satisfies O-4 "build-time preferred"). Not a Roslyn source generator in v1 unless the parametrised test proves insufficient.

### C-7 — `redactionEngineVersion` (R-3)
Integer engine constant, stamped in `BackupManifest` now (backup shares the engine), reserved for `bundle-info.json` at bundle time. Amends Rule 4 + `BackupManifest`.

### C-8 — Backup onto the unified engine (now in M-A; full unification)
`BackupBuilder` calls `ConfigRedactionEngine` with the **same universal tiers** as the bundle will (no backup mode). Restore-prompting handled by MASK markers (§1a). Update `BackupBuilderTests` + the renamed engine tests.

---

## 3. Sequencing (revised)

1. **M-A — engine + rename + backup migration.** Rename to `ConfigRedactionEngine`; tier-aware engine (INCLUDE/MASK/STRIP + sibling metadata + determinism); rewire `BackupBuilder` onto it; universal MASK/STRIP tiers via the baseline; `redactionEngineVersion` stamped in the backup manifest (pulls a slice of C-7 forward since backup is the live consumer). **Exit:** backup export produces tiered output through the unified engine, green tests, real integration coverage. **DONE — commit `38be433`, 782 Management tests green.**
   > **M-A discovery (2026-05-31): M1 moved to M-B.** Implementation showed M1's fail-closed enforcement only becomes real once the engine can distinguish a World-1 typed path from a World-2b opaque path — and that boundary is M-B machinery. Backup's secrets are all World-2b (caught by the baseline), so placing `[BundleTier]` on Core records in M-A would wire ~15 records of attributes to nothing on the runtime path. Per operator call, `[BundleTier]` placement + the M1 reflection map move into M-B, landing with the per-protocol rules + derived map that actually consume them. Keeps every milestone's code on the runtime path; no speculative scaffolding.
2. **M-B — `[BundleTier]` on Core records + per-protocol rules as source of truth + M1/M2/M3 + World boundary.** Place the attributes (C-1 usage, ex-M-A) alongside the per-protocol rules. Mqtt + OpcUaClient first (typed secrets) to establish the rules→factory pattern; review; fan out to the other 7. Wire M1 (typed reflection, fail-closed), M2 (derived map, fail-closed), M3 (baseline allowlist, fail-open) and the World boundary that selects between them.
3. **M-C — secret-shape detector (R-1).** Ship **Phase 1 deterministic** detectors (PEM/JWT/PKCS/SSH) — this closes the R-1 gate. **Phase 2 entropy** scheduled as a fast-follow (Q-3 tuning), not blocking.
4. **M-D — drift guard (R-2) + finalise R-3.** Declaration-driven coverage test; close the R-gates.

Each milestone: 0 warnings, deterministic tests, ≥80% coverage on new Management code.

---

## 4. Test strategy
Engine unit tests (each tier; nested opaque paths; fail-closed vs fail-open per world; provenance) · per-protocol rules tests (every secret key MASK/STRIP; benign INCLUDE) · drift guard (the cross-cutting safety net) · detector tests (PEM/JWT/private-key/high-entropy positives; GUID/ULID/normal negatives) · backup regression (restore still detects MASK'd secrets and prompts).

---

## 5. Risks (revised)
- **Rules-as-source-of-truth refactor (C-3/delta 4)** touches 9 shipped `From*Instance` factories — now the largest surface, and deliberately accepted as architectural-capital spend. Mitigate: Mqtt + OpcUaClient first as the reviewed pattern, then fan out.
- **Detector false-positive fatigue (Q-3)** — tune before declaring R-1 done.
- **Layering** — strict dependency direction Core ← adapters ← Management. Management composes; nothing in Core or adapters references Management. **Adapter `*BundleRedactionRules` are metadata-only and must not reference any Management type** (§1c). The rename and rules-as-truth must not leak Management types downward.

---

**Next step:** plan is review-approved (two passes) and implementation-ready. On operator go → start **M-A**.
