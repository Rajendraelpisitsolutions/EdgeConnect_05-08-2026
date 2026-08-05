# 2026-05-31 — ADR-0020 redaction: session handoff

**Status:** M-A closed and shipped; M-B fully planned and review-approved; B1 is the next code step.
**Branch:** `feat/bundle-redaction-spec` (merged to master at session close — see §Branch state).

---

## What this session did

Took ADR-0020 (Diagnostic Bundle redaction) from a contradictory spec to shipped substrate:

1. **Found the spec couldn't be implemented as written** — the per-field `[BundleTier]` mechanism can't reach secrets in opaque `JsonElement` connection blocks, and a name-based `SecretRedactor` already shipped in `Management/Backup/`.
2. **Wrote ADR-0020 Amendment 1** (three-mechanism split, fail-direction, universal tiers) → review pass → operator steer (pre-launch: unify, no compat) → **operator-ratified (Accepted).**
3. **Substrate plan v1→v2** (two review passes) → **M-A shipped** (`38be433`): tier-aware `ConfigRedactionEngine`, `[BundleTier]` in Core, name→tier baseline, backup unified, `redactionEngineVersion`, determinism invariant. 782 Management tests green.
4. **M-B plan v1→v2** (one review pass, approved). Schema-aware redaction: reflection-derived World boundary, M1/M2/M3 pipeline, per-protocol `IBundleRedactionRules`.

## Cold-start pointers (read in this order)

- `docs/decisions/0020-diagnostic-bundle-redaction-spec.md` — **Accepted**, incl. Amendment 1 (the binding decision).
- `docs/sessions/2026-05-31-adr0020-redaction-implementation-plan-v2.md` — substrate plan; **M-A marked DONE** (`38be433`).
- `docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md` — **M-B, implementation-ready. Start here for the next code step (B1).**
- M-A code: `src/ElpisEdgeConnect.Management/Backup/ConfigRedactionEngine.cs`, `BackupSecretPatterns.cs`; `src/ElpisEdgeConnect.Core/Configuration/BundleTier*.cs`.

## Next code step — B1

`IBundleRedactionRules` (Core) + registry (Management) + schema-model reflection + engine schema-aware overload + `SchemaModel_Dump_IsStable` snapshot test. **Baseline-only classification first** so the intricate boundary logic is pinned before any tier rides on it. Then B2 (`[BundleTier]` on Core records) → B3 (Mqtt + OpcUaClient, **review checkpoint**) → B4 (fan out to 7) → B5 (drift guard + integration).

## Decisions locked this session (don't relitigate)

- Universal tiers — one ruleset, no per-consumer semantics; re-enterable secrets → MASK, key material → STRIP (ADR §1a).
- World boundary is **reflection-derived** (opaque seam = `JsonElement?`/`[JsonExtensionData]`); 3 seams today.
- Per-protocol rules via **shared key constants + CI drift guard** — not full factory inversion.
- `IBundleRedactionRules` lives in **Core** (adapter metadata, not redaction logic).
- M1 attribute placement **moved M-A → M-B** (attributes land with their runtime consumer).
- Secret-shape detector: **deterministic (PEM/JWT/PKCS/SSH) ships R-1 v1; entropy is Phase 2** (→ M-C).

## Open / deferred

- M-C: secret-shape detector (R-1, deterministic first) + entropy Phase 2 (Q-3 tuning).
- M-D: finalise R-2 drift guard + close R-gates.
- Pre-launch posture + static-HTML-UI-first preference recorded in memory.

## Branch state

All session work (ADR amendment, both plans, M-A code, this handoff) lands on **master** via the `feat/bundle-redaction-spec` merge at session close. Resume from master.
