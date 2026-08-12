# 2026-05-31 — ADR-0020 redaction: M-A → M-C complete (handoff)

**Status:** The full ADR-0020 redaction substrate (M-A, M-B, M-C) is implemented, tested, and **merged to master**. All R-gates met. The only remaining ADR-0020 deliverable is the downstream **"Generate Diagnostic Bundle"** workflow that consumes the substrate.
**Master HEAD at handoff:** `d7c6c75`.

---

## The arc in one paragraph

ADR-0020 (Diagnostic Bundle redaction) started as a contradictory spec; this session amended it, then built the whole thing. **Amendment 1** reconciled the per-field `[BundleTier]` idea with the reality that secrets live in opaque `JsonElement` connection blocks, landing a three-mechanism model (M1 typed attributes / M2 per-protocol rules / M3 baseline) with a fail-direction split, unified on one engine (pre-launch, no compat). Then: **M-A** unified the redactor (`ConfigRedactionEngine`, 4-tier MASK/STRIP, determinism, `redactionEngineVersion`); **M-B** made it schema-aware (reflection-derived World boundary, `[BundleTier]` on all Core records, per-protocol `*BundleRedactionRules` for all 9 adapters, wired into DI, BackupBuilder switched to the live path); **M-C** added the secret-shape detector (R-1: deterministic + entropy, warn-only).

---

## Cold-start pointers (read in this order)

1. `docs/decisions/0020-diagnostic-bundle-redaction-spec.md` — **Accepted, incl. Amendment 1** (the binding decision; the rule table, fail-direction, R-gates).
2. `docs/sessions/2026-05-31-adr0020-redaction-implementation-plan-v2.md` — substrate plan (M-A/M-B sequencing).
3. `docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md` — M-B detail incl. **§3a the locked adapter pattern** (constants + KnownKeys==All + rule table for ExtraNameOverrides).
4. Code anchors:
   - Engine: `src/ElpisEdgeConnect.Management/Backup/ConfigRedactionEngine.cs` (schema-aware `Redact(json, schema, registry)` is the live path).
   - World boundary: `…/Backup/ConfigSchemaModel.cs` (opaque seam = `JsonElement?`/`[JsonExtensionData]`).
   - Registry: `…/Backup/BundleRedactionRulesRegistry.cs`; baseline: `BackupSecretPatterns.cs`.
   - Detector: `…/Backup/SecretShapeDetector.cs` (Phase 1 deterministic + Phase 2 entropy).
   - Attribute: `src/ElpisEdgeConnect.Core/Configuration/BundleTier.cs` + `BundleTierAttribute.cs` + `IBundleRedactionRules.cs`.
   - Per-protocol rules: `{adapter}/*ConnectionKeys.cs` + `*BundleRedactionRules.cs` (all 9 adapters).
   - DI wiring: `src/ElpisEdgeConnect.Host/Adapters/BundleRedactionRulesRegistration.cs` (unconditional) → called from `CompositionRoot.cs`.
   - Live consumer: `…/Backup/BackupBuilder.cs` (uses schema-aware path; stamps `redactionEngineVersion`; surfaces redaction + shape warnings into the manifest).

## Drift guards / safety tests (do not delete)
- `RedactionDriftGuardTests` — every application typed property has a `[BundleTier]` (R-2).
- Per-adapter `*BundleRedactionRulesTests` — `KnownKeys == *ConnectionKeys.All` (key coverage).
- `BundleRedactionRulesRegistrationTests` (Host.Tests) — all 8 rules wired + unique ProtocolNames.
- `BackupBuilderTests` — incl. the multi-protocol full-graph integration test + warn-only detector test.

## Decisions locked this session (don't relitigate)
- Three-mechanism redaction; fail-closed for typed/derived, fail-open for opaque-unknown (World 2b).
- Universal tiers, one ruleset for backup + bundle (no per-consumer semantics). Re-enterable secrets → MASK, never-ship material → STRIP.
- World boundary reflection-derived (`JsonElement?`/`[JsonExtensionData]`).
- Per-protocol rules via shared key constants; `KnownKeys == All` drift guard ("can't parse a key without a tier").
- `ExtraNameOverrides` only for factory-unread secret-shaped keys, each with a justifying comment (OPC UA `certificatePassword` is the lone instance).
- Redaction rules registered **unconditionally** (not license-gated) — the redactor must redact any protocol's config.
- Detector **warns, never strips**; runs on the redacted tree; Phase 1 deterministic + Phase 2 conservative entropy.
- `IBundleRedactionRules` lives in Core (adapter metadata, not redaction logic).

## R-gate status
- **R-1** secret-shape detector: ✅ (Phase 1 + Phase 2). **R-2** drift guard: ✅. **R-3** `redactionEngineVersion`: ✅.

## What is NOT done (next deliverable)
The **"Generate Diagnostic Bundle"** feature (ADR-0020 Rules 3/4/5) — see `2026-05-31-adr0020-bundle-generation-plan-v1.md`:
- Bundle ZIP assembly (reuses the redaction substrate + manifest machinery).
- Operator-visible **manifest preview** (Rule 3) — **gets a static HTML mockup for sign-off first** (operator standing preference).
- `bundle-info.json` with `bundleSpecVersion` + `redactionEngineVersion` (Rule 4).
- `BUNDLE.GENERATED` audit event (Rule 5).
- Bundle content beyond config/audit/history (capability matrix, health snapshot, flight recorder) depends on other ADRs and is scoped in the plan.

## Branch / build state
- Everything merged to **master** (`d7c6c75`); solution builds 0 errors. Feature branches (`feat/redaction-mb-b1`, `…-b5`, `…-mc-detector`, `…-mc-phase2`) are merged and can be pruned.
- **Studio note:** a running Studio locks the build output DLLs — stop it before rebuilding. `dotnet build-server shutdown` clears lingering build servers.
- Tests: 836 Management; per-adapter suites green; full solution green.
