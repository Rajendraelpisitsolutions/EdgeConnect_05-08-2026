# ADR-0020 "Generate Diagnostic Bundle" — implementation plan v1

**Status:** v1 — DRAFT, **Q-1 resolved (codebase scan)**; ready for the ChatGPT review pass before code (plan-trail cadence).
**Date:** 2026-05-31
**Implements:** `docs/decisions/0020-diagnostic-bundle-redaction-spec.md` Rules 3 (preview), 4 (bundle-info), 5 (audit event) — the downstream feature the redaction substrate (M-A→M-C) was built for.
**Builds on:** the complete redaction substrate on master (`d7c6c75`): `ConfigRedactionEngine` (schema-aware), per-protocol rules, `SecretShapeDetector`, `BackupBuilder` + `BackupManifest`.

---

## 0. What this is — and what it reuses

A "Diagnostic Bundle" is a **support artifact** an operator generates and emails to support. It is the off-gateway extension of the explainability surfaces (P7). Mechanically it is **very close to the existing config backup** — same redaction engine, same manifest/checksum machinery — plus three things backup doesn't have:

1. an **operator-visible preview** of what will ship, grouped by tier, confirmed **before** the ZIP is written (Rule 3);
2. a self-describing **`bundle-info.json`** (Rule 4);
3. a **`BUNDLE.GENERATED` audit event** (Rule 5);
4. (over time) **more content** than backup — diagnostic surfaces, not just config.

**Reuse is the design.** Backup already proves the redaction + manifest + checksum + audit-log streaming path. The bundle should share that core, not fork it.

## 1. v1 bundle contents (scope) — ship what exists, version the rest

ADR-0020 Rule 3's example lists gateway identity, config schema, route topology/state, capability matrix (ADR-0019), Flight Recorder events (ADR-0021), health metrics snapshot. **Q-1 resolved (2026-05-31, codebase scan):**

| Surface | Exists today? | Notes |
|---|---|---|
| Gateway identity | ✅ | `IGatewayIdentity` |
| Redacted `gateway.json` + history + audit | ✅ | fully covered by the redaction substrate (this is the backup payload) |
| Health/diagnostic snapshots | ✅ **but new redaction surface** | `IDiagnosticsService`: `GetAllRouteSnapshots()` (three-way source/pipeline/sink), route-state / sink / backpressure event ring buffers; plus `GatewayStartupEventStore`, `ConfigurationFaultRegistry`, `IReloadOutcomeRegistry` |
| Capability matrix (ADR-0019) | ❌ not implemented | no `*Capability*` type exists |
| Flight Recorder (ADR-0021) | ❌ not implemented | the bounded event logs above are flight-recorder-*like* but not the ADR-0021 surface |
| Route Timeline (ADR-0026) | ❌ not implemented | — |

**The load-bearing finding:** the diagnostic snapshots are a **redaction surface the config substrate does NOT cover.** They are typed records (`RouteHealthSnapshot`, sink/backpressure events, config faults), not part of `gateway.json`, and per ADR-0020 Rule 1 their **fault/error free-text may contain device data → MASK**. Shipping them un-redacted would reintroduce the privacy footgun the whole ADR exists to prevent.

**Revised v1 scope (recommended — safe):**
- **v1 INCLUDE:** gateway identity; redacted `gateway.json`; config history; audit log. *(= the backup payload, already fully redaction-covered.)* Plus the bundle workflow itself: preview → confirm → generate, `bundle-info.json`, `BUNDLE.GENERATED` audit event.
- **v1 EXCLUDE (Rule 1):** license file; OS event logs; STRIP-tiered material.
- **v1.1 fast-follow (high support value):** redacted **diagnostic snapshots** — gated on first designing their tiering (attribute the snapshot records with `[BundleTier]` and/or a free-text MASK pass), reusing the same engine. This is where the bundle earns its value over a backup.
- **Later `bundleSpecVersion`s:** Flight Recorder (ADR-0021), capability matrix (ADR-0019) when those ship.

> **Decision for the review pass:** is the diagnostic-snapshot redaction worth pulling into v1 (more value, more work + a new redaction design), or is v1 = "backup payload + the bundle workflow" with diagnostics as the immediate v1.1? Recommendation: v1 ships the *workflow* safely on the already-covered backup payload; v1.1 adds diagnostics once their tiering is designed. (Q-1 is now a scoping decision, not an unknown.)

## 2. Factoring — shared archive core, two consumers

Proposed: extract the archive-composition core that `BackupBuilder` already embodies (redact JSON files, stream audit log, compute SHA-256, emit a manifest) into a shared component; `BackupBuilder` and a new `BundleBuilder` both use it. `BundleBuilder` adds the health snapshot, `bundle-info.json`, and the audit event, and is driven by the **preview→confirm** flow.

> **Open question Q-2:** extract a shared `ArchiveComposer`, or have `BundleBuilder` reuse `BackupBuilder` directly + extend? Recommend the shared core to avoid drift between two redaction call sites.

## 3. The preview → confirm → generate flow (Rule 3)

Backup is one-click. The bundle is **two-step**:

1. **Preview (dry-run):** compute the full manifest — every file that would be included, grouped by tier (INCLUDE / MASK / STRIP / EXCLUDE), the redaction paths, the detector warnings, and a size estimate — **without writing the ZIP**. Returned by a management API endpoint.
2. **Confirm + generate:** the operator confirms; the ZIP is written using the *same* computed manifest, and the `BUNDLE.GENERATED` audit event fires.

The determinism invariant makes this safe: the preview's manifest and the generated ZIP's manifest are computed from the same inputs and are byte-identical.

> **Open question Q-3:** does the generate step re-run redaction, or reuse the preview's already-redacted bytes (cached server-side per operator session)? Recommend recompute (stateless, simpler) — determinism guarantees equivalence; the preview's role is display, not a cache.

## 4. `bundle-info.json` (Rule 4)

Top-level in the ZIP: `bundleSpecVersion` (int, starts at 1), `gatewayId`, `bundleGeneratedAtUtc` (ISO 8601), `generatorVersion` (build), **`redactionEngineVersion`** (R-3, from `ConfigRedactionEngine.EngineVersion`), `redactionSummary` (counts included/masked/stripped/excluded), `bundlerInvokedBy` (operator session id). Distinct from the existing `manifest.json` (which lists per-file redaction paths + checksums) — `bundle-info.json` is the bundle-level provenance header.

## 5. The manifest preview UI — STATIC HTML MOCKUP FIRST (Rule 3 + operator preference)

The preview is an **operator-facing Studio surface**, so per the standing preference it gets a **static HTML mockup for sign-off BEFORE any Blazor/Studio wiring**. The mockup shows: the four tier groups with counts and example rows, the detector warnings panel ("3 values look secret-shaped — review"), the size estimate, and the Confirm/Cancel actions. This matches the round-3 mockup already scoped in `2026-05-31-diagnostic-strategy-handoff.md` ("Bundle generation manifest preview (ADR-0020)").

**Sequence:** static HTML mockup → operator sign-off → management API (preview + generate endpoints) → Blazor preview component wired to the API. No UI is wired into Studio before the mockup is approved.

## 6. `BUNDLE.GENERATED` audit event (Rule 5)

On generate, write an audit entry: operator identity, timestamp, manifest summary hash, optional operator-supplied reason. So a customer can later answer "did we ever ship a bundle from this gateway?" from the audit chain.

> **Open question Q-4:** does the existing audit chain (`ConfigurationAuditLog`) accept non-configuration events, or is it config-change-only? If config-only, decide whether `BUNDLE.GENERATED` extends it or lives in a sibling audit surface. Needs a look at the audit chain's contract.

## 7. Sequencing

1. **G1 — backend, no UI:** shared archive core (Q-2) + `BundleBuilder` (config+audit+history+identity+health) + `bundle-info.json` + the preview (dry-run manifest) computation. Unit + integration tested headless.
2. **G2 — audit event:** `BUNDLE.GENERATED` (Q-4).
3. **G3 — UI mockup:** static HTML manifest-preview mockup → **operator sign-off gate**.
4. **G4 — management API:** preview + generate endpoints.
5. **G5 — Studio:** Blazor preview component wired to the API; download.

## 8. Open questions for the review pass

- **Q-1 RESOLVED (facts) → now a scoping decision (§1).** Capability matrix + Flight Recorder + Route Timeline are NOT implemented. Health/diagnostic snapshots ARE queryable but are a new redaction surface (fault free-text → MASK). Recommendation: v1 = backup payload + bundle workflow; diagnostics are v1.1 after their tiering is designed. **The review pass should ratify or override this scoping.**
- **Q-2** shared archive core vs extend BackupBuilder.
- **Q-3** generate recomputes vs reuses preview bytes.
- **Q-4** audit chain accepts `BUNDLE.GENERATED` or needs a sibling.
- **Q-5** size-estimate accuracy for the preview (compute real redacted bytes, or estimate) — affects whether the preview does a full dry-run redaction.
- **Q-6 (new, from Q-1):** the diagnostic-snapshot redaction design for v1.1 — attribute the snapshot records with `[BundleTier]` (reuses M1/the engine) vs a dedicated free-text MASK pass. Worth sketching now so v1's factoring doesn't preclude it.

## 9. Risks

- **Preview/generate divergence** — mitigated by the determinism invariant; a test must assert preview manifest == generated manifest.
- **Scope creep into diagnostic surfaces** — keep v1 to existing-and-queryable surfaces; version the rest.
- **UI before mockup** — explicitly gated (G3 sign-off before G4/G5).

---

**Next step:** route this v1 through a ChatGPT review pass; produce v2; then start G1 (backend, no UI). No UI work before the G3 mockup is signed off.
