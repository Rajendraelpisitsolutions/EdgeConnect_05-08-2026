# ADR-0020 "Generate Diagnostic Bundle" — implementation plan v2

**Status:** v2 — **review-approved (two passes), implementation-ready.** Pass 1 restructured around the contributor model + folded the Q-1…Q-6 resolutions. Pass 2 approved v2 and requested four pre-G1 edits, now applied (§0b): Q-7 resolved (composer owns grouping), Q-8 resolved (inventory shape), contributor capability metadata, and a locked fail-closed contributor-failure policy. Ready for G1 on operator go.
**Date:** 2026-05-31
**Implements:** `docs/decisions/0020-diagnostic-bundle-redaction-spec.md` Rules 3/4/5.
**Builds on:** the complete redaction substrate on master (`4557753`). **Supersedes:** v1 (Q-1 resolution retained).

---

## 0. Framing — a support artifact, NOT a backup extension

The bundle *reuses* the backup's redaction + archive machinery, but it is a different product. A backup answers one question ("what is configured?"). A diagnostic bundle is a **support/explainability artifact** that grows to answer five:

> **what is configured · what is running · what is unhealthy · what changed · what happened recently**

The architecture must **not** assume `bundle == backup archive`, even while v1 scope is constrained. The contributor model (§1) is what enforces that.

## 0a. Review pass (2026-05-31) — changes from v1

- **Added the `IBundleContributor` abstraction** (§1) — the headline change; the bundle is *composed* from contributors, not assembled by a monolithic builder.
- **v1 scope +route-inventory summary** (§2) — a tiny topology count, high support value, near-zero risk.
- **Q-2 locked: shared `ArchiveComposer`** (§3), not `BundleBuilder extends BackupBuilder`.
- **Q-3 locked + codified rule:** *preview artifacts are never used as generation inputs* (§4).
- **Q-4 modified: same audit stream**, evolving `ConfigurationAuditLog` → `GatewayAuditLog` (§6).
- **Q-5 locked: full dry-run** (compute real redacted bytes + manifest + checksums + size; discard) — no estimate (§4).
- **Q-6 modified: BOTH** `[BundleTier]` for structured snapshot fields **and** free-text masking (+ `SecretShapeDetector`) for message/fault fields (§7).
- **Static HTML mockup gate expanded** — must answer "what leaves / why excluded / what masked" (§8).

## 0b. Review pass 2 (2026-05-31) — four pre-G1 edits applied

1. **Q-7 resolved — the `ArchiveComposer` owns manifest grouping.** Contributors contribute *content only*; they do not know about INCLUDE/MASK/STRIP/EXCLUDE. Flow: contributor → writer records redaction results → composer builds the grouped manifest. (§1, §3.)
2. **Q-8 resolved — `route-inventory.json` shape** (§2): counts + enabled/disabled counts + ids.
3. **Contributor capability metadata** added (§1) so `bundle-info.json` can list exactly which contributors produced a bundle (§5).
4. **Fail-closed contributor-failure policy locked** (§1): any contributor failure fails the whole bundle generation — no silent partial bundles. Partial-bundle semantics deferred unless a strong need appears.

Also locked: the §7 diagnostic split (structured → `[BundleTier]`, free-text → masking + detector) is the **permanent ADR-0020 extension rule** for every future diagnostic surface.

---

## 1. The contributor model (the core architecture)

A bundle is composed from independent contributors. Adding a future surface (diagnostics, Flight Recorder, capability matrix, Route Timeline) is a *new contributor*, never a `BundleBuilder` edit.

```csharp
public interface IBundleContributor
{
    /// Stable id recorded in bundle-info.json's contributor list (e.g. "config",
    /// "audit", "route-inventory"). NOT a tier — contributors never know about
    /// INCLUDE/MASK/STRIP/EXCLUDE (Q-7).
    string Name { get; }

    /// Coarse capability metadata so bundle-info.json can describe what a bundle
    /// contains, and so a future loader can reason about it (e.g. Config / Audit
    /// / Inventory / Diagnostics / FlightRecorder).
    BundleCapability Capability { get; }

    /// Supply this contributor's content through the writer. The writer applies
    /// redaction, checksums, and manifest accumulation — the contributor only
    /// decides WHAT goes in and supplies raw content.
    Task ContributeAsync(IBundleWriter writer, BundleContext context, CancellationToken ct);
}
```

- **Q-7 — grouping is the composer's job, not the contributor's.** `IBundleWriter` (the contributor-facing surface of the shared `ArchiveComposer`, §3) exposes only content verbs: `AddRedactedJson(name, rawJson)`, `AddVerbatim(name, bytes)`, `AddExcludedNote(name, reason)`. The **composer** derives the INCLUDE/MASK/STRIP/EXCLUDE manifest grouping from the engine's redaction result; contributors stay tier-agnostic.
- `BundleContext` carries the gateway identity, the `ConfigRedactionEngine` + `BundleRedactionRulesRegistry`, the data-root layout, and `IDiagnosticsService` — so contributors pull what they need without bespoke wiring.
- `BundleBuilder` enumerates the registered contributors and runs each through one `ArchiveComposer`. **Both preview and generate run the same contributor set** (§4). `bundle-info.json` lists `Name` + `Capability` of every contributor that ran.
- **Fail-closed contributor policy (LOCKED).** Any contributor throwing fails the entire bundle generation — **no silent partial bundles.** A support bundle must be trustworthy: an operator must never receive an artifact that is quietly missing a surface. (Backup's best-effort per-file warnings are a *backup* affordance; the bundle is stricter.) Partial-bundle semantics may be added later only with an explicit, operator-visible "partial" marker — not by default.

**v1 contributors:** `GatewayIdentityContributor`, `ConfigContributor`, `HistoryContributor`, `AuditContributor`, `RouteInventoryContributor`.
**v1.1+ contributors (drop-in):** `DiagnosticsContributor`, then `FlightRecorderContributor` (ADR-0021), `CapabilityMatrixContributor` (ADR-0019), `RouteTimelineContributor` (ADR-0026).

---

## 2. v1 scope (Q-1 resolved + review modification)

**v1 contributors / content:**
- Gateway identity.
- Redacted `gateway.json` (substrate).
- Config history (redacted).
- Audit log.
- **Route-inventory summary** — a small `route-inventory.json` (Q-8 resolved): counts + enabled/disabled counts + ids. Support almost always wants enabled/disabled at a glance.

  ```json
  { "sources": 14, "sinks": 7, "routes": 22,
    "enabledRoutes": 19, "disabledRoutes": 3,
    "routeIds": ["..."] }
  ```

  Metadata already in config; near-zero risk.

**v1 EXCLUDE (Rule 1):** license file; OS event logs; STRIP-tiered material.
**v1.1 (high support value):** `DiagnosticsContributor` — health snapshots, fault registry, reload outcomes, route-state history — gated on the §7 redaction design.
**Later `bundleSpecVersion`s:** Flight Recorder, capability matrix, Route Timeline as their ADRs ship.

Confirmed not implemented today: capability matrix (ADR-0019), Flight Recorder (ADR-0021), Route Timeline (ADR-0026).

---

## 3. Shared `ArchiveComposer` (Q-2 locked)

Extract the archive mechanics `BackupBuilder` already embodies — redact JSON via the engine, stream + SHA-256 per entry, accumulate the manifest (tier groups, redaction paths, detector warnings, checksums) — into a shared `ArchiveComposer`. `BackupBuilder` and `BundleBuilder` both use it; **`BundleBuilder` does not extend `BackupBuilder`** (bundle diverges far faster than backup). Backup may later be re-expressed as a fixed contributor set over the composer, but that refactor is optional and not on v1's critical path.

---

## 4. Preview → confirm → generate (Q-3 + Q-5 locked)

Two-step, both running the **same contributor set** over the composer:

1. **Preview = full dry-run.** Run every contributor, produce the real redacted bytes + manifest + checksums + **real total size**, then **discard the bytes** and return the manifest for display. No estimate — once you're doing the redaction anyway, real numbers cost nothing extra and never surprise the operator.
2. **Confirm + generate.** Re-run the contributors, stream the ZIP, fire the audit event (§6).

**Locked rule:** *preview artifacts are never used as generation inputs.* Generate recomputes from scratch (stateless — no cache, no session lifetime, no invalidation). The determinism invariant guarantees the preview the operator approved and the generated bundle are byte-identical; a test asserts `previewManifest == generatedManifest`.

---

## 5. `bundle-info.json` (Rule 4)

Top-level: `bundleSpecVersion` (int, =1), `gatewayId`, `bundleGeneratedAtUtc`, `generatorVersion`, **`redactionEngineVersion`** (R-3), `redactionSummary` (counts included/masked/stripped/excluded), `bundlerInvokedBy`, and **`contributors`** — the `Name` + `Capability` of every contributor that ran (e.g. `["config","history","audit","route-inventory"]`), so a support engineer sees exactly what the bundle contains. The bundle-level provenance header, distinct from `manifest.json` (per-file paths + checksums).

## 6. Audit — same stream, evolve to `GatewayAuditLog` (Q-4 modified)

`BUNDLE.GENERATED` goes in the **same audit stream** as config changes, not a sibling log. The operator question is "what happened on this gateway?", not "what config changed?". Direction: evolve `ConfigurationAuditLog` → `GatewayAuditLog` so `CONFIG.CHANGED`, `BUNDLE.GENERATED`, and future `ROUTE.CREATED` etc. coexist. The event records operator identity, timestamp, manifest summary hash, optional reason.

> **Implementation note:** check the current `ConfigurationAuditLog` contract — if it's hard-typed to config actions, the v1 step is the minimal generalization to accept a `BUNDLE.GENERATED` action in the same chain (not a parallel log). Full rename to `GatewayAuditLog` can be incremental.

## 7. Diagnostic-snapshot redaction (Q-6 modified — design now, build in v1.1)

Snapshots carry **two kinds of data**, and BOTH paths are needed (attributes alone will not suffice):

- **Structured fields** (`RouteName`, `State`, `Timestamp`, counts) → `[BundleTier]` on the snapshot records, reusing M1/the engine.
- **Human free-text** (`ErrorMessage`, `FaultReason`, operator messages) → **free-text masking** + the `SecretShapeDetector`, because device data / PII can appear there (Rule 1 MASK). This path is unavoidable.

v1's factoring must leave room for both. The `DiagnosticsContributor` (v1.1) serialises snapshots to JSON and writes them through `IBundleWriter` exactly like config — so the structured-field tiers flow through the same engine; the free-text MASK pass is the v1.1 addition.

**This split is the permanent ADR-0020 extension rule** for every future diagnostic surface (Flight Recorder, Route Timeline, capability matrix): structured fields tier via `[BundleTier]`; human/free-text fields go through free-text masking + the secret-shape detector. New surfaces conform to this rule rather than inventing per-surface redaction.

## 8. Static HTML mockup gate (expanded — operator preference)

The preview is operator-facing, so it gets a **static HTML mockup signed off BEFORE any management API or Studio wiring.** The mockup must let the operator answer three questions before Generate is clickable:

1. **What will leave the gateway?** (the INCLUDE list + the route-inventory summary)
2. **Why was something excluded?** (the EXCLUDE list with reasons — license file, etc.)
3. **What was masked?** (the MASK list + the secret-shape detector warnings: "3 values look secret-shaped — review")

If the mockup can't make those three legible, it isn't done.

---

## 8a. G3 mockup sign-off — locked UX→behaviour requirements (2026-05-31)

The preview mockup (`docs/sessions/2026-05-30-ux-mockups/6-bundle-preview.html`) was
approved for layout/UX with five revisions, now applied. These become binding behaviour
for **G4/G5**, not just visuals:

1. **Secret-shape warnings gate Generate.** When the detector flags ≥1 value, the operator
   must explicitly acknowledge before the generate endpoint will run. No warnings → no gate.
2. **The warning copy states the value is included verbatim** — the detector flags, never
   masks/strips/changes it. (Cancel → fix config → regenerate, or acknowledge.)
3. **Optional "reason for generating" field** — free text, flows into the `BUNDLE.GENERATED`
   audit entry (alongside operator + manifest hash). The generate API accepts it.
4. **Safe-sharing note** shown before Generate.
5. **Explicit contributor list** (name + capability + what each provides), not just chips.

API implication (G4): `PreviewAsync`/preview endpoint surfaces the secret-shape warning count;
the generate endpoint takes `{ reason?, acknowledgedSecretShapeWarnings: bool }` and refuses
to generate when warnings exist and acknowledgement is false.

## 9. Sequencing

1. **G1 — backend core, no UI:** `ArchiveComposer` + `IBundleContributor`/`IBundleWriter`/`BundleContext` + the v1 contributors + `BundleBuilder` + `bundle-info.json` + the dry-run preview (real manifest, discarded bytes). Headless tests incl. `previewManifest == generatedManifest`.
2. **G2 — audit event:** `BUNDLE.GENERATED` in the shared stream (§6).
3. **G3 — static HTML mockup** of the 3-question preview → **operator sign-off gate.**
4. **G4 — management API:** preview + generate endpoints.
5. **G5 — Studio:** Blazor preview component wired to the API; download.

(v1.1, separate: `DiagnosticsContributor` + the §7 free-text MASK pass.)

## 10. Open questions — resolved (review pass 2)

- **Q-7 → RESOLVED: the `ArchiveComposer` derives manifest grouping; contributors are tier-agnostic** (§1, §3).
- **Q-8 → RESOLVED: `route-inventory.json` = counts + enabled/disabled counts + ids** (§2).

No open questions remain blocking G1.

## 11. Risks

- **Preview/generate divergence** — mitigated by determinism + the `previewManifest == generatedManifest` test.
- **Contributor scope creep** — the model makes adding surfaces cheap; keep v1 to its five contributors regardless.
- **Diagnostic free-text leak** — the §7 free-text path is mandatory for v1.1; do not ship `DiagnosticsContributor` without it.
- **UI before mockup** — gated at G3.

---

**Next step:** plan is review-approved. On operator go → start **G1** (backend core + contributor model, no UI). No UI before the G3 mockup sign-off.
