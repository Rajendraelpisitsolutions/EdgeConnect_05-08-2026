# ADR-0020 redaction engine — implementation plan v1

**Status:** v1 — DRAFT plan, pre-review. Awaiting a ChatGPT review pass before code, per the plan-trail cadence.
**Date:** 2026-05-31
**Implements:** `docs/decisions/0020-diagnostic-bundle-redaction-spec.md` (Accepted, as amended by Amendment 1)
**Branch:** `feat/bundle-redaction-spec`

---

## 0. Scope boundary (what this plan does and does NOT build)

ADR-0020 is the **redaction spec**, not the bundle feature. This plan builds the **reusable redaction substrate** the spec mandates and rewires the one existing consumer (backup) onto it. It does **not** build the "Generate Diagnostic Bundle" ZIP/preview workflow — that is the downstream deliverable (#4 in the diagnostic-strategy roadmap) that consumes this substrate, planned separately once this lands.

**In scope:** the `[BundleTier]` attribute, the unified 4-tier engine, per-protocol redaction rules, the three classification mechanisms, the secret-shape detector (R-1), the CI drift guard (R-2), and `redactionEngineVersion` (R-3). Plus migrating backup onto the unified engine (O-1 unification).

**Out of scope (next deliverable):** bundle ZIP assembly, the operator-visible manifest *preview UI* (Rule 3), the `BUNDLE.GENERATED` audit event (Rule 5), `bundle-info.json` emission (R-3 *field* is specified here; the file that carries it ships with bundle generation).

> **UI prerequisite (operator standing preference):** the manifest preview (Rule 3) is an operator-facing surface, so when the bundle-generation deliverable starts it **must produce a static HTML mockup for operator sign-off first**, before any UI is wired into the Studio. This matches the round-3 mockup already scoped in `2026-05-31-diagnostic-strategy-handoff.md` ("Bundle generation manifest preview (ADR-0020)"). The substrate in *this* plan has no UI, so no mockup is required here — the flag is carried forward so it isn't missed downstream.

---

## 1. Component breakdown

### C-1 — `[BundleTier]` attribute + enum (Core)
- **Where:** `src/ElpisEdgeConnect.Core/Configuration/BundleTier.cs` (enum `Include`/`Mask`/`Strip`) + `BundleTierAttribute.cs`.
- **Why Core:** the attribute is protocol-*agnostic* (carries no protocol knowledge — just a tier), and protocol config records already reference Core. Placing the attribute in Core does **not** violate CLAUDE.md §9 #1; placing a protocol *name list* in Core would. The attribute is the generic vocabulary; the protocol-specific *usage* lives in each adapter module.
- `EXCLUDE` is **not** an attribute value — it is file-level (engine/consumer decides which files enter the archive), so the enum is three values.

### C-2 — Unified 4-tier engine (Management)
- **Where:** refactor `src/ElpisEdgeConnect.Management/Backup/SecretRedactor.cs` → a tier-aware redactor. Likely rename to a neutral `ConfigRedactionEngine` (it serves both backup and bundle now), keeping `SecretRedactor` as a thin shim or removing it (pre-launch, no compat — see O-1).
- **Outcomes per the Amendment A1.1 table:** INCLUDE (verbatim), MASK (`***` + sibling byte-length annotation), STRIP (omit key), EXCLUDE (file-level, handled by the consumer's file selection).
- The engine takes a **resolved name→tier map** (produced by the mechanisms in C-4) and walks raw JSON (`JsonNode`) applying it. Keeps the existing JSONPath-ish provenance output (manifest needs it).

### C-3 — Per-protocol redaction rules (beside each adapter)
- **Where:** one `*BundleRedactionRules.cs` per adapter module: `Sinks.Mqtt`, `Sinks.OpcUaServer`, `Sources.{Focas2, OpcUaClient, ModbusTcp, MTConnect, BrotherHttp, S7, EthernetIp}`.
- Each declares, for its Connection/Extras block:
  - **Known keys → explicit tier** (the mechanism-2 / fail-closed zone): every JSON key its `From*Instance` factory reads, mapped to a tier. Secret keys → MASK/STRIP; benign keys (endpoint, host, port) → INCLUDE.
  - **Unknown-key policy** (the mechanism-3 / fail-open zone): defaults to INCLUDE, with the protocol's own extra strip/mask names beyond the shared baseline.
- **Composition:** a Management-side registry composes all protocol rules + the shared baseline (`BackupSecretPatterns`, kept as the cross-protocol set). *Management coordinates; protocols declare.*

### C-4 — The three classification mechanisms (Management engine)
- **M1 typed attribute:** reflection over typed config records (Core `GatewaySettings`/`SourceInstanceConfig` typed fields, etc.) → name→tier. Fail-closed: unattributed typed field → STRIP.
- **M2 derived map (opaque-with-counterpart):** from each protocol's known-keys declaration (C-3) → JSON-key→tier for that protocol's Connection block. Fail-closed for keys the factory reads.
- **M3 allowlist (opaque-no-counterpart):** baseline + per-protocol extra names (C-3 unknown-key zone). Fail-open: default INCLUDE.
- Engine applies M1→M2→M3 by locating each byte's world; first match wins.

### C-5 — Secret-shape detector (R-1, Management engine)
- **Where:** `src/ElpisEdgeConnect.Management/Backup/SecretShapeDetector.cs`.
- Runs over **World-2b** values (fail-open zone) only. Detectors: PEM block markers (`-----BEGIN`), JWT structure (`xxx.yyy.zzz` base64url), common private-key markers, high-entropy token threshold.
- **Emits warnings, never auto-strips** (per A1.4 #1). Output feeds the future preview; for now it is surfaced via the engine's result object + asserted in tests.

### C-6 — CI drift guard (R-2)
- **Where:** `tests/ElpisEdgeConnect.Management.Tests/RedactionDriftGuardTests.cs` (snapshot test) — chosen over a source generator for v1 (lower machinery; revisit if it proves flaky). **O-4 said build-time/CI preferred; a CI-failing test satisfies that.**
- Asserts: (a) every JSON key each `From*Instance` factory reads is covered by that protocol's known-keys declaration; (b) every secret-typed field resolves to MASK or STRIP, never INCLUDE.
- **Open implementation question (see §3 Q-2):** how the test enumerates "keys the factory reads" without re-parsing C# — candidate: each protocol's rules declaration *is* the single source the factory and the test both consult.

### C-7 — `redactionEngineVersion` (R-3)
- Integer constant on the engine, surfaced in `BackupManifest` now (backup shares the engine) and reserved for `bundle-info.json` when bundle generation lands. **Amends Rule 4** and the `BackupManifest` shape.

### C-8 — Backup migration onto the unified engine (O-1 unification)
- Rewire `BackupBuilder` to call the unified engine.
- Backup's restore-round-trip requirement (invalid placeholder forces secret re-entry) becomes a **backup-consumer tier policy**: its secret fields use MASK-with-invalid-sentinel rather than STRIP, so restore tooling still detects and prompts. Bundle policy may prefer STRIP.
- Update `SecretRedactorTests` + `BackupBuilderTests`.

---

## 2. Sequencing (milestones)

1. **M-A:** C-1 attribute + C-2 engine skeleton (tier-aware, M1 only) + migrate backup (C-8) onto it. Smallest end-to-end slice with a real consumer + green tests.
2. **M-B:** C-3 per-protocol rules for the two adapters with typed secrets first (Mqtt, OpcUaClient), then the rest. C-4 M2+M3 wiring.
3. **M-C:** C-5 secret-shape detector (R-1).
4. **M-D:** C-6 drift guard (R-2) + C-7 `redactionEngineVersion` (R-3). Close the R-gates.

Each milestone: 0 warnings, deterministic tests, ≥80% coverage on new Management code (CLAUDE.md §5).

---

## 3. Open implementation questions for the review pass (do not resolve unilaterally)

- **Q-1.** Is the per-protocol rules declaration (C-3) the *single source of truth* that both the runtime engine and the drift guard consume — and does the `From*Instance` factory also read its key list from it (so factory and redaction can't drift by construction)? Or do we keep the factory hand-written and only *test* alignment? The first is stronger but a bigger refactor of shipped factories.
- **Q-2.** R-2 as a reflection-based snapshot test vs a Roslyn source generator. v1 leans snapshot test; is that acceptable given O-4's "build-time preferred," since a failing CI test is build-time-equivalent in our gate?
- **Q-3.** High-entropy threshold for C-5: what Shannon-entropy cutoff + min length avoids drowning operators in false-positive warnings on legitimately-random-looking IDs (e.g. GUIDs, ULIDs)? Needs a tuned default.
- **Q-4.** Do we rename `SecretRedactor` → `ConfigRedactionEngine` (cleaner, pre-launch so free to) or keep the name to minimise churn? Naming-only, but sets the public surface.
- **Q-5.** MASK byte-length annotation shape: sibling `"<field>__redacted": {masked, originalByteLength}` vs inline replacement object. Affects manifest readers.

---

## 4. Test strategy

- **Engine unit tests:** each tier outcome; nested opaque paths (`connection.credentials.password`); fail-closed vs fail-open per world; provenance paths.
- **Per-protocol rules tests:** every known secret key for each adapter resolves MASK/STRIP; benign keys INCLUDE.
- **Drift guard (R-2):** is itself the cross-cutting safety test.
- **Detector tests (R-1):** PEM/JWT/private-key/high-entropy positives + GUID/ULID/normal-string negatives.
- **Backup regression (C-8):** restore round-trip still detects redacted secrets and prompts.

---

## 5. Risks

- **Refactoring shipped factories (Q-1)** is the largest risk surface — it touches 9 adapter modules. Mitigate by doing Mqtt+OpcUaClient first as the pattern, reviewing, then fanning out.
- **Detector false-positive fatigue (Q-3)** could make R-1 noise rather than signal. Tune against real config samples before declaring R-1 done.
- **Engine in Management, attribute in Core, rules in adapters** spans three layers — keep the dependency direction strictly Core ← adapters ← Management (Management composes; nothing in Core or adapters references Management).

---

**Next step:** route this v1 through a ChatGPT review pass (cadence), produce v2, then start M-A. No code until the plan is reviewed.
