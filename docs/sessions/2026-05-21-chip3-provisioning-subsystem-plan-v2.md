# Chip 3 — Provisioning Subsystem (v2 plan, LOCKED after two ChatGPT review passes)

**Status:** v2 — LOCKED. Folds in 16 review items across two ChatGPT review rounds. Per round-2 ratification: "no additional review cycle needed before v2." Reality-check happens in the v3 pass.
**Date:** 2026-05-21
**Predecessor:** [v1 (open questions)](2026-05-21-chip3-provisioning-subsystem-plan.md)
**Predecessor (locked roadmap):** [Phase 2 wrap-up roadmap v2.3](2026-05-21-phase2-wrapup-roadmap-v2.3.md) §1.1-§1.3 → [v2](2026-05-21-phase2-wrapup-roadmap-v2.md) §3.4
**Predecessor (load-bearing dependency):** M.P2.4 Brother HTTP migration — [handoff](2026-05-21-mp24-handoff.md). `BrotherHttpSourceConfiguration.FromSourceInstance` shape is what the Brother template renders into.
**Estimated size:** 1.5–2 weeks (v1 estimate carries; the v2 additions tighten correctness without adding meaningful implementation surface).
**Test baseline:** 2263 passing across 12 projects post-M.P2.4. Target after Chip 3: ~2300 with ~35 new Pester + ~5 new C# integration tests.

---

## 0. What v2 changed from v1

Two ChatGPT review rounds. **16 items folded in across 2 rounds; 0 rejected. 1 v1 open question (Q-V1-H) RETRACTED — superseded by the cleaner MQTT-placeholder-injection answer (round-1 item #6).**

### Round 1 (11 items)

| # | Review item | Verdict | Where in v2 |
|---|---|---|---|
| 1 | `templateSchemaVersion` field in `_provisioning` | ✅ Agree | §3.3, §6 |
| 2 | Canonicalization spec (UTF-8/LF/sorted-keys) | ✅ Agree (highest-leverage) | §5.4.1 (NEW) |
| 3 | Acknowledge future `-AllowOverwrite` flag | ✅ Agree | §5.5.1 (NEW future-work) |
| 4 | `gatewayProvisioningId` field | ✅ Agree with nuance | §3.3, §6 |
| 5 | Route generation strategy — lock granularity | ✅ Agree | §5.1.1 (NEW) |
| 6 | MQTT sink placeholder injection | ✅ Agree — supersedes Q-V1-H | §5.1.2 (NEW) |
| 7 | "Schema validation augments, never replaces" invariant | ✅ Agree | §5.2.1 (NEW) |
| 8 | Evidenceability fields (`sourceTemplateChecksum`, `generatorInvocationId`) | 🔧 Alter — defer | §13 future-work |
| 9 | PowerShell portability note (separable rendering/validation logic) | ✅ Agree | §5.5.2 (NEW) |
| 10 | Template sprawl governance | 🔧 Alter — downscope to manifest | §5.1.3 (NEW) |
| ⭐ | "Deterministic provisioning guarantees" subsection | ✅ Strongly agree | §5.4.4 (NEW, umbrella) |

### Round 2 (5 items)

| # | Review item | Verdict | Where in v2 |
|---|---|---|---|
| 1 | Deterministic array ordering | ✅ Agree | §5.4.2 (NEW) |
| 2 | Timezone/locale immunity | ✅ Agree | §5.4.3 (NEW) |
| 3 | Audit-intent for future overwrite | ✅ Agree | §5.5.1 (extended) |
| 4 | Anti-templating-engine boundary lock | ✅ Strongly agree | §5.1.4 (NEW) |
| 5 | `_`-prefix reserved namespace formalization | ✅ Strongly agree | §3.3.1 (NEW) + ADR proposal §12 |

### Retractions

- **Q-V1-H** (sinks+routes overlay file approach): RETRACTED. ChatGPT round-1 item #6's MQTT placeholder injection is strictly better — broker host/port become `{{ mqttHost }}` / `{{ mqttPort }}` placeholders resolved at generation time from a per-gateway sidecar file (see §5.1.2). The template stays one file, no post-generation operator edits.

---

## 1. Goal

Deliver the **Provisioning Subsystem** — the canonical mechanism by which fleet-scale CNC source configurations enter the system. The first concrete shape is `tools/bulk-provision/`: per-protocol Golden-source templates, a per-gateway CSV + per-gateway sidecar config, a PowerShell generator CLI that stamps each output `gateway.json` with a `_provisioning` block (provenance + Configuration identity), and an end-to-end test that proves the round-trip from CSV through `FromSourceInstance(...)` to a working source instance configuration.

The subsystem provides a **deterministic provisioning guarantee** (§5.4.4): same template + same CSV + same sidecar + same generator + same template schema version → byte-identical output after canonicalization. This is the architectural backbone for the 100-CNC customer commissioning per deployment-readiness §3 Option A lock and §6 critical path.

---

## 2. Architectural framing

### What this IS

- A first-class **subsystem** of EdgeConnect — not a one-off script. Owns: template schema, validation pipeline, rendering pipeline, provenance + Configuration identity, generator CLI. Per v2.3 §1.2 canonical term **the Provisioning Subsystem**.
- A **Golden-source template** workflow. Templates are version-controlled and treated as the single source of truth; operators never hand-edit generated `gateway.json` files. Locked discipline in deployment-readiness §3.
- A **declarative substitution-only** rendering model. No embedded scripting, no conditionals, no transforms (§5.1.4 anti-templating-engine lock).
- A **schema-validated** boundary. Templates pass through the same canonical `GatewayConfiguration` schema used by `NJsonSchemaConfigurationValidator`. Provisioning-specific validation augments, never replaces or forks canonical validation (§5.2.1).
- A **deterministic** generator. Same inputs → byte-identical output after canonicalization (§5.4.4).
- A **fail-loud** generator. Invalid input → clear error, non-zero exit, no file written. Refuse-overwrite on hand-edited files.

### What this is NOT (locked scope caps)

- **NOT a Drift detection subsystem.** Drift detection deserves install-time data to drift against — we don't have that yet. **No drift comparison logic ships in v1, even as a stub.** Trigger to pause + surface if the v1 implementation drifts toward it.
- **NOT a runtime API surface.** Lives at `tools/bulk-provision/`. v1 must NOT introduce `src/ElpisEdgeConnect.Provisioning/` even as an empty project.
- **NOT the Modbus per-tag CSV importer.** Per-instance (one CSV row → one source instance), not per-tag.
- **NOT a Studio UI feature.** Operator surface is the PowerShell CLI. Studio handoff is the existing `ImportDraftDialog.razor`.
- **NOT a template-inheritance system.** Out for v1. Two separate template files for variants.
- **NOT a generic templating engine (NEW v2 lock per round-2 #4).** Placeholders are `{{ name }}` literal substitution ONLY. No conditionals (`{% if %}`), no loops (`{% for %}`), no helpers, no nested includes, no transforms, no expression language. If a template needs more than literal substitution, the right answer is a NEW template, not a smarter engine.
- **NOT a new shared abstraction** per v2.3 §1.1. Bounded code under `tools/bulk-provision/`, scoped to one operation.

### Locked invariants that anchor the work

From `CLAUDE.md` §3, `ARCHITECTURE_BLUEPRINT.md` Appendix A, and the v2 roadmap:

- **Canonical data model (Locked #2)** — templates produce `SourceInstanceConfig` JSON DTOs.
- **Schema-first config (B1)** — generated files validate against the canonical `GatewayConfiguration` schema.
- **Append-only catalog semantics** (v2.3 §1.2) — template tag paths are a subset of `BrotherTagMap` / `Focas2TagMap`; never invent new paths.
- **No legacy DTO leaks** (M.P2.4 §2 lock).
- **Audit-chain integrity** — generator output flows through Studio's draft → validate → apply round-trip; `_provisioning` block survives and is queryable post-apply.
- **Deterministic provisioning** (v2 NEW §5.4.4) — same inputs → byte-identical output after canonicalization. Becomes the testable invariant for the Pester E2E suite.

---

## 3. Locked inputs from the roadmap (do not relitigate)

These remain LOCKED from v2 roadmap + v2.2 + v2.3. v1 elaborated within these locks; v2 extends only where ChatGPT review pointed at gaps.

### 3.1 Architecture (v2 roadmap §3.4.1)

The five-component subsystem architecture is unchanged from v1 §3.1:

```
┌─────────────────────────────────────────────────┐
│              Provisioning Subsystem             │
│                                                  │
│  ┌──────────────┐    ┌──────────────────────┐  │
│  │ Template     │    │ Validation Pipeline   │  │
│  │ Schema       │───→│ (Canonical schema +   │  │
│  │              │    │  provisioning aug)    │  │
│  └──────────────┘    └──────────────────────┘  │
│         │                       │                │
│         ▼                       ▼                │
│  ┌──────────────────────────────────────────┐  │
│  │ Rendering Pipeline                        │  │
│  │ (declarative {{ name }} substitution)     │  │
│  │ template + CSV + sidecar → gateway.json   │  │
│  └──────────────────────────────────────────┘  │
│         │                                        │
│         ▼                                        │
│  ┌──────────────────────────────────────────┐  │
│  │ Provenance + Configuration Identity       │  │
│  │ (_provisioning block: 9 fields, locked)   │  │
│  └──────────────────────────────────────────┘  │
│         │                                        │
│         ▼                                        │
│  ┌──────────────────────────────────────────┐  │
│  │ Generator CLI (PowerShell shell only —    │  │
│  │ rendering/validation logic is .NET, so    │  │
│  │ future portability stays open)            │  │
│  └──────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

Note vs v1 diagram: the Rendering Pipeline now consumes **template + CSV + sidecar** (not just template + CSV), reflecting round-1 item #6 (MQTT placeholders → sidecar). The `_provisioning` block now has **9 fields** (was 7) — new `templateSchemaVersion` + `gatewayProvisioningId` per round-1 items #1 and #4. The Generator CLI block explicitly separates shell vs logic per round-1 item #9.

### 3.2 Locked decisions from v2 roadmap §3.4.2 (verdicts Q1-Q8)

Unchanged from v1 §3.2:

| Q | Lock |
|---|---|
| Q1 PowerShell vs Python | **PowerShell.** |
| Q2 CSV columns | **`make,instanceId,deviceId,deviceName,host,enabled`** — per-gateway CSV. |
| Q3 Provenance format | **`_provisioning` root block** (JSON object). |
| Q4 Schema validation | **NJsonSchema via PowerShell or shell-out.** v3 reality-check resolves wiring (Q-V1-A). |
| Q5 Modbus per-tag CSV | **Distinct tool, distinct README section.** |
| Q6 Edited-file detection | **Content hash** — SHA-256 of canonicalized JSON minus `_provisioning`. |
| Q7 Template inheritance | **Out for v1.** |
| Q8 Studio integration | **`ImportDraftDialog.razor`** — verified to exist. |

### 3.3 `_provisioning` block schema (v2 NEW — 9 fields, was 7)

Every generated `gateway.json` carries exactly this `_provisioning` root block:

```json
{
  "_provisioning": {
    "generatedBy": "bulk-provision",
    "generatorVersion": "1.0.0",
    "templateSchemaVersion": 1,
    "templateId": "fanuc-standard-v1",
    "fleetId": "100cnc-customer-A",
    "gatewayProvisioningId": "100cnc-customer-A-line1",
    "generatedAt": "2026-05-22T08:15:00Z",
    "configFingerprint": "sha256:<hash of file MINUS _provisioning block, after canonicalization>",
    "csvFingerprint": "sha256:<hash of input machines.csv>"
  },
  "Gateway": { ... },
  "Sources": [ ... ],
  "Sinks": [ ... ],
  "Routes": [ ... ]
}
```

**Field-by-field purpose:**

| Field | Type | Source | Purpose |
|---|---|---|---|
| `generatedBy` | string | constant `"bulk-provision"` | Distinguishes Provisioning-Subsystem output from hand-edited / Studio-wizard-authored configs. |
| `generatorVersion` | string | constant (semver) | The generator binary version. Bumps on generator behavior change. |
| **`templateSchemaVersion`** (NEW round-1 #1) | integer | constant (per generator) | Structural contract of the template system itself. Distinct from `templateId` (which template was applied) and `generatorVersion` (which tool ran). v1 locks at `1`. Bumps when placeholder syntax / block structure evolves. |
| `templateId` | string | template filename minus `.json` | Which Golden-source template was applied. |
| `fleetId` | string | CLI `-FleetId` | Identifies the customer / fleet. |
| **`gatewayProvisioningId`** (NEW round-1 #4) | string | CLI `-GatewayProvisioningId` or sidecar | Fleet-level provisioning identity for this specific gateway (e.g., `"100cnc-customer-A-line1"`). **Distinct from runtime `Gateway.GatewayId`** (UUID per Locked Decision #19, generated at first start). The provisioning id is human-readable, stable across re-provisioning; the runtime id is opaque, established on first boot. See §3.3.2 below for the strict identity-separation rule. |
| `generatedAt` | string (ISO 8601 UTC, with `Z` suffix) | generator capture time | Support / debugging timeline. The only time-varying field in the block. |
| `configFingerprint` | string (`"sha256:<hex>"`) | computed | SHA-256 of canonicalized file content MINUS the `_provisioning` block itself. |
| `csvFingerprint` | string (`"sha256:<hex>"`) | computed | SHA-256 of input CSV bytes (raw, pre-parse). |

**Configuration identity** (v2.3 §1.2 canonical term) is the subset `templateId + templateSchemaVersion + fleetId + gatewayProvisioningId + configFingerprint + csvFingerprint`. The remaining three (`generatedBy + generatorVersion + generatedAt`) are pure provenance.

#### 3.3.1 Reserved namespace rule for `_`-prefix root keys (NEW round-2 #5)

**Locked invariant:** root-level keys beginning with `_` are reserved for system metadata namespaces. The canonical `GatewayConfiguration` schema permissively accepts them; the parser preserves them on round-trip but does not interpret them in the data path. Future namespaces (`_diagnostics`, `_migration`, `_ai`, `_audit`) follow this same pattern.

Rationale: today `_provisioning` is one ad-hoc metadata block. Without the namespace rule, every future metadata block has to renegotiate its name + its schema-permissiveness story. Locking the convention now turns `_provisioning` from a one-off into the first instance of a generic mechanism.

**v3 reality-check Q-V1-B (extended):** verify that `NJsonSchemaConfigurationValidator` currently accepts unknown root properties (the canonical schema doesn't currently use `additionalProperties: false` at root, suggesting permissive — but confirm). If not permissive, schema update lands as a precondition to Chip 3 implementation, not as part of it.

**Proposed ADR** (see §12): the `_`-prefix rule deserves a small ADR — likely **ADR-0015** if it lands before M.2d.4's wizard contract, otherwise **ADR-0016**. ADR documents: convention scope (root-level only, not nested), parser semantics (preserve on round-trip, ignore in data path), schema semantics (permissive, additional sub-schema optional), forward compatibility (new namespaces never require parser changes).

#### 3.3.2 Identity separation rule (NEW round-1 #4 nuance)

**Locked invariant:** `_provisioning.gatewayProvisioningId` and `Gateway.GatewayId` are NEVER conflated. They serve different purposes:

| Identity | Purpose | Lifecycle | Format |
|---|---|---|---|
| `Gateway.GatewayId` | Runtime system identity. Used by the gateway service for licensing, audit chains, diagnostics correlation. | Established on first boot per Locked Decision #19. Persists in `FileSystemGatewayIdentity`. | Opaque UUID. |
| `_provisioning.gatewayProvisioningId` | Fleet-level provisioning identity. Used by operators, support, fleet rollout tracking, future drift analysis. | Established at generation time. Travels with the file. | Human-readable string. Convention: `{fleetId}-{location-or-line}`. |

The generator does NOT set or modify `Gateway.GatewayId` — that's the gateway's own concern at first boot. The generator only sets the provisioning identity. README + ADR-0015 document this separation explicitly so future tooling doesn't conflate them.

### 3.4 Deliverables — see §7 (extended)

### 3.5 Definition of Done — see §8 (extended)

---

## 4. v2.3 implementation-discipline locks (apply to this v2)

Carried verbatim from v2.3 §1.1-§1.3:

### 4.1 No-new-shared-abstractions discipline

Per v2.3 §1.1, during Chip 3 implementation:

- **Allowed:** local PowerShell helpers (private functions), file-scoped types under `tools/bulk-provision/`, Pester test fixtures.
- **Forbidden:** new shared framework abstractions, generic provisioning primitives reusable outside `tools/bulk-provision/`, anything in `src/ElpisEdgeConnect.Provisioning/`.

**Trigger to pause + surface:** if implementation tempts toward a shared "provisioning primitives" namespace, stop and report.

### 4.2 Terminology freeze

Per v2.3 §1.2, all artifacts produced by Chip 3 use these exact canonical terms:

- **Provisioning Subsystem** — not "bulk-provision tooling", "config generator."
- **Golden-source template** — not "master template", "reference template."
- **`_provisioning` block** — not "provenance header", "config metadata."
- **Configuration identity** — refers to `templateId + templateSchemaVersion + fleetId + gatewayProvisioningId + configFingerprint + csvFingerprint`. Not "config fingerprint" alone.
- **Drift detection** — reserved name; out of v1 scope. Never appears in code/comments/test names that ship in v1.

### 4.3 No promotion to platform contract surface

Per v2.3 §1.3: the `_provisioning` block schema lives in `docs/config-schemas/gateway-configuration.schema.json`; the workflow discipline lives in `tools/bulk-provision/README.md`; the Configuration-identity rationale lives in this plan trail (referenced from the README). v1 does NOT create `docs/contracts/`.

**Exception (NEW):** the `_`-prefix reserved-namespace rule (§3.3.1) DOES warrant an ADR. ADRs are a different surface from `docs/contracts/` — they're the canonical home for cross-milestone architectural decisions per CLAUDE.md §2 "Decision records" guidance. v2.3 §1.3 deferred only the `contracts/` folder; ADRs remain in scope.

---

## 5. Subsystem architecture in detail

### 5.1 Template Schema

Each Golden-source template is a `gateway.json`-shaped JSON file with **placeholder strings** in instance-specific fields. Static fields baked in; per-machine and per-gateway variables flow in from CSV + sidecar.

**Placeholder convention:** `{{ name }}` with mandatory single-space padding. Curly-brace delimiters chosen so a stray missing replacement breaks JSON validation (visually loud, a feature). The substitution engine is **literal text replacement only** — see §5.1.4.

**Per-protocol placeholder taxonomy:**

| Template | Per-row placeholders (from CSV) | Per-gateway placeholders (from sidecar/CLI) |
|---|---|---|
| `template-fanuc-v1.json` | `{{ instanceId }}`, `{{ deviceId }}`, `{{ deviceName }}`, `{{ host }}`, `{{ enabled }}` | `{{ gatewayId }}`, `{{ gatewayName }}`, `{{ gatewayProvisioningId }}`, `{{ fleetId }}`, `{{ site }}`, `{{ mqttHost }}`, `{{ mqttPort }}`, `{{ mqttQos }}`, `{{ mqttClientIdPrefix }}` |
| `template-brother-v1.json` | same | same |
| `template-modbus-v1.json` | `{{ instanceId }}`, `{{ deviceId }}`, `{{ deviceName }}`, `{{ host }}`, `{{ port }}`, `{{ enabled }}` (note: Modbus needs `port`) | same |

Per-row placeholders come from the CSV `machines.csv`. Per-gateway placeholders come from either:
1. CLI parameters (for the most common — `-GatewayId`, `-GatewayProvisioningId`, `-FleetId`), OR
2. A per-gateway sidecar JSON file (`-SidecarConfig <path>`) carrying the remainder.

**Static fields baked into each template:**

- Fanuc: `ProtocolName = "focas2"`, `Connection.port = 8193`, `Polling.IntervalMs = 3000`, `Connection.dataPoints = [...]` (~65 baseline paths from FOCAS2 catalog).
- Brother: `ProtocolName = "brother-http"`, `Polling.IntervalMs = 3000`, `Connection.dataPoints = [...]` (~75 paths from `BrotherTagMap`).
- Modbus: `ProtocolName = "modbus-tcp"`, `Polling.IntervalMs = 3000`, `Connection.dataPoints = []` (per-tag definitions come from the separate Modbus per-tag CSV importer per v2 §3.4.2 Q5).

#### 5.1.1 Route generation strategy — locked (NEW round-1 #5)

**Lock: one route per protocol-class per gateway.**

For the 100-CNC customer scenario (80 Fanuc + 20 Brother, ~25 sources/gateway across 4 gateways), each generated `gateway.json` contains exactly TWO routes per gateway that actually has both protocol classes (most cases — pure-Fanuc gateways get one route):

| Route | Source list | Sink list |
|---|---|---|
| `route-fanuc-{gatewayName}` | all sources with `ProtocolName == "focas2"` on this gateway | the gateway's MQTT sink |
| `route-brother-{gatewayName}` | all sources with `ProtocolName == "brother-http"` on this gateway | the gateway's MQTT sink |

Total fleet route count: 4-8 routes (4 gateways × 1-2 protocol classes each), not 100. This balance:

- Preserves **fault isolation** between protocol classes (a Brother adapter failure does not cause the Fanuc route's store-and-forward buffer to drain).
- Avoids **route explosion** (1 route per source = 25/gateway = 100/fleet = unmanageable in Studio's `/routes` page).
- Avoids **giant mixed-protocol routes** (one route for everything = no failure isolation, hard to debug).

If the customer's actual machine layout demands different routing (e.g., per-cell or per-line groupings), the operator either uses the same template + multiple CSVs (one per cell) and runs the generator multiple times, OR — if cross-cell routing is required — that's a future template + ADR conversation, not a v1 generator capability.

#### 5.1.2 MQTT sink — placeholder injection (NEW round-1 #6, supersedes Q-V1-H)

**Locked: MQTT broker connection details are placeholders resolved at generation time from a per-gateway sidecar config. NO hand-editing of generated files.**

Round-1 review item #6: the v1 plan's "operator edits `localhost:1883` later" approach was operationally weak — it created post-generation mutation, violated golden-source discipline, and required the refuse-overwrite check to gain "sink-only diffs allowed" nuance (which v1 surfaced as Q-V1-H).

**The clean answer:** treat broker details as placeholders just like deviceId. The generator consumes a sidecar file:

`tools/bulk-provision/samples/sidecar-100cnc-customer-A-line1.json`:

```json
{
  "gatewayId": "GW-CUSTOMER-A-LINE1",
  "gatewayName": "Customer A — Line 1",
  "gatewayProvisioningId": "100cnc-customer-A-line1",
  "fleetId": "100cnc-customer-A",
  "site": "Customer A — Plant 3",
  "mqttHost": "broker.customer-a.internal",
  "mqttPort": 1883,
  "mqttQos": 1,
  "mqttClientIdPrefix": "edgeconnect-gw-line1"
}
```

CLI consumes both:

```
.\generate.ps1 `
    -MachinesCsv .\samples\machines-100cnc-customer-A.csv `
    -SidecarConfig .\samples\sidecar-100cnc-customer-A-line1.json `
    -OutputFile .\out\gateway-customer-a-line1.json
```

Per-gateway CLI overrides remain available (`-MqttHost`, `-FleetId`, etc.) for ad-hoc generation; sidecar is the canonical operator workflow at 4-gateway scale.

**Refuse-overwrite check is now strict** — no sink-only diff carve-out needed. Hand-editing any field of a generated `gateway.json` makes the file fail re-generation. Operators wanting to change broker host edit the **sidecar**, not the output. The sidecar is operator-edited; the output is golden-source.

**Q-V1-H is RETRACTED** — the overlay-file approach is no longer needed. The architecture is one template, one CSV, one sidecar, one output. Cleaner.

#### 5.1.3 Template naming + lifecycle convention (NEW round-1 #10, downscoped)

**Lock: template naming convention.** `template-{protocol}-{variant}-v{N}.json`. For v1: `template-fanuc-v1.json`, `template-brother-v1.json`, `template-modbus-v1.json` (no variant suffix needed when only one variant exists).

**Lock: active-template manifest.** A single `tools/bulk-provision/templates/MANIFEST.md` lists current vs deprecated templates with one-line rationale + successor mapping. Maintained by hand at v1 scale (3 templates); not auto-generated.

**Lock: when `templateId` evolves** — append-only versioning. A new `template-fanuc-v2.json` ships alongside the existing `template-fanuc-v1.json`; the v1 file is marked `DEPRECATED` in MANIFEST.md but NOT deleted. Already-deployed fleets carrying `_provisioning.templateId: "fanuc-v1"` continue to round-trip cleanly through `ConfigurationManager.CreateDraftAsync` (the template is the spec for generation, not for parsing — the canonical schema handles parsing).

**Out of scope:** automated deprecation enforcement, automatic upgrade tooling, template-evolution migrators. If the customer accumulates ≥8 templates, that's a v2-amendment trigger for real lifecycle tooling. At 3 templates the manifest is sufficient.

#### 5.1.4 Anti-templating-engine boundary (NEW round-2 #4)

**Lock: declarative `{{ name }}` substitution ONLY. Forever.**

The substitution engine is a regex `{{\s*(\w+)\s*}}` → string replacement. That's it.

**Explicitly forbidden — not now, not ever in v1 templates:**

- `{% if %}` / `{% endif %}` (conditionals)
- `{% for %}` / `{% endfor %}` (loops)
- `{{ name | filter }}` (transforms)
- `{> include "other.json" %}` (includes / partials)
- Computed placeholders (`{{ make + "-" + index }}`)
- Helpers, macros, expression languages
- Nested templates

If a template needs different content per row/per gateway beyond literal substitution, the right answer is **a new template file**, not a smarter engine. Three templates → potentially 10 → potentially 50 is fine. Three templates → one Jinja-like engine with conditionals is the failure mode.

Rationale: provisioning systems organically drift into templating-DSL maintenance. Locking this boundary now is cheap; retrofitting it after someone adds "just one `{% if %}`" is impossible.

### 5.2 Validation Pipeline

Two-stage validation, unchanged from v1 in shape:

1. **CSV validation** — required columns present, no duplicate `instanceId`, `host` parses as IP or hostname, `make` is one of `{fanuc, brother, modbus}`, `enabled` is one of `{true, false}` (case-insensitive). PowerShell-native checks; clear per-row error messages with line numbers.
2. **JSON schema validation** — rendered `gateway.json` validates against `docs/config-schemas/gateway-configuration.schema.json` using NJsonSchema. v3 reality-check (Q-V1-A) resolves PS-interop-vs-shell-out.

#### 5.2.1 Schema validation augments, never replaces (NEW round-1 #7)

**Locked invariant:** provisioning-specific validation (CSV column checks, placeholder-balance checks, per-template structural sanity) **augments** canonical schema validation. It NEVER replaces, forks, parallels, or overrides `NJsonSchemaConfigurationValidator`.

What this means concretely:

- The CSV validator (PowerShell) is purely additive — it catches errors before rendering, but every rendered file still passes through `NJsonSchemaConfigurationValidator` before write.
- No shadow JSON-schema reimplementation in PowerShell. If the canonical schema rejects a file, the generator rejects the file — they cannot diverge.
- New provisioning-specific checks (e.g., "fleetId is non-empty", "templateSchemaVersion matches the generator's expected version") are wrapped around the canonical validation, never in lieu of it.
- The canonical schema is the source of truth. The generator does not fork from it.

This locks the bright line that prevents "shadow validation engine" from emerging during future provisioning work.

### 5.3 Rendering Pipeline

For each row of `machines.csv`:

1. Read the row.
2. Select the template per `row.make`.
3. Substitute placeholders (per-row from CSV + per-gateway from sidecar/CLI) → produce one `SourceInstanceConfig` JSON fragment.
4. Append to `Sources[]` array in the accumulating output.

After all rows: assemble final `gateway.json` candidate (`Gateway` + `Sources` + `Sinks` + `Routes`). Apply canonicalization (§5.4.1) → compute `configFingerprint` and `csvFingerprint` → stamp `_provisioning` block → validate (§5.2) → write file.

### 5.4 Determinism

Determinism is the load-bearing correctness invariant of the entire subsystem. It supports the audit guarantee, the reproducibility guarantee, future drift detection, and future AI-substrate reasoning over `_provisioning` evidence.

#### 5.4.1 Canonicalization spec (NEW round-1 #2 — highest-leverage addition)

**Locked: canonicalization rules for `gateway.json` output.** RFC 8785 (JSON Canonicalization Scheme) is the reference; v1 adopts a subset sufficient for the deterministic-provisioning guarantee.

| Rule | Lock |
|---|---|
| **Encoding** | UTF-8, NO BOM. |
| **Line endings** | LF (`\n`) only, regardless of generator host OS. PowerShell `Set-Content` must be invoked with `-NoNewline` + explicit LF assembly, OR via `[System.IO.File]::WriteAllText` with explicit UTF-8 encoding without BOM. |
| **Property ordering** | Alphabetical, recursive. Every nested object's keys sorted ascending. EXCEPT: the top-level root is ordered with `_provisioning` first (since `_` sorts before all alpha in ASCII), then `Gateway`, `Routes`, `Sinks`, `Sources` alphabetically. |
| **Whitespace** | Pretty-printed with 2-space indentation, LF separators. NO trailing whitespace on any line. Final newline at EOF. |
| **Number formatting** | Invariant culture. Integers as bare digits; doubles in shortest-round-trip format. No localized decimal separators (no commas). |
| **Boolean / null** | Lowercase JSON literals (`true`, `false`, `null`). |
| **String escaping** | JSON standard — `\"`, `\\`, `\n`, `\r`, `\t`, `\u00XX` for non-ASCII control characters. No locale-dependent escapes. |

Tests for canonicalization correctness (§7 deliverables): a Pester test that round-trips a fixture file through generator → re-read → re-emit → asserts byte-identical output.

#### 5.4.2 Deterministic array ordering (NEW round-2 #1)

**Locked: all generator-emitted arrays sorted deterministically before emission.**

| Array | Sort key |
|---|---|
| `Sources[]` | `InstanceId` ascending (string compare, ordinal) |
| `Sinks[]` | `InstanceId` ascending |
| `Routes[]` | `RouteId` ascending |
| Within each route, `SourceInstanceIds[]` | string ascending |
| Within each route, `SinkInstanceIds[]` | string ascending |
| Within each source, `Connection.dataPoints[]` | **catalog order, NOT alphabetical** — preserve the `BrotherTagMap` / `Focas2TagMap` canonical sequence so operators reading the file see paths grouped semantically. v3 reality-check confirms this is achievable (the catalog has a stable iteration order). |

The CSV row order is **not** preserved in the output — it's a fungible input. If an operator wants to inspect the rendered output in their CSV's original order, the sample diff tool can re-sort by `_provisioning.csvLineNumber` (a per-source debug field we'll add — open question Q-V2-A below).

Open question: see Q-V2-A in §10.2 for the catalog-iteration-order verification.

#### 5.4.3 Timezone + locale immunity (NEW round-2 #2)

**Locked invariants:**

- **Timestamps:** `_provisioning.generatedAt` is UTC ISO 8601 with explicit `Z` suffix (`2026-05-22T08:15:00Z`). Never local time. Never a timezone-named string ("Pacific Standard Time"). The generator computes the timestamp via `[DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", [System.Globalization.CultureInfo]::InvariantCulture)` or PowerShell-equivalent.
- **Numbers:** invariant culture for all numeric formatting (see §5.4.1). No localized decimal separators, no thousand separators, no exponential notation unless required by JSON-double precision rules.
- **Strings:** no machine-local strings in generated output. No hostname leaks (the generator does NOT stamp the host machine's name into the output), no user account name, no timezone abbreviation, no locale-dependent error messages baked into generated content.

The intent: a generator run on a Windows-Server-2019 box in Tokyo at 23:47 JST and a generator run on a Linux container in Frankfurt at 15:47 CET with identical input files produce **byte-identical output bytes** (differing only in `generatedAt`).

#### 5.4.4 Deterministic provisioning guarantee (NEW round-1 ⭐, umbrella statement)

**The locked invariant — Chip 3's load-bearing correctness claim:**

> Given the same Golden-source template, the same machines.csv, the same per-gateway sidecar config, the same generator version, and the same template schema version, the Provisioning Subsystem produces byte-identical `gateway.json` output after canonicalization — modulo only the `_provisioning.generatedAt` field.

This is the testable invariant. The Pester E2E suite encodes it as:

1. Run the generator with fixed inputs → output1.
2. Run the generator with the same inputs (different wall-clock time) → output2.
3. Strip `_provisioning.generatedAt` from both.
4. Assert `output1 == output2` byte-for-byte.

The guarantee becomes:

- **Audit guarantee** — operators can prove a deployed `gateway.json` was generated from a specific template+CSV+sidecar combination by re-running the generator and comparing fingerprints.
- **Reproducibility guarantee** — generation is environment-independent; a customer-site re-generation matches the in-house staging generation byte-for-byte.
- **Support guarantee** — when a customer reports an issue, support can rebuild their exact `gateway.json` from their inputs without needing the original generation environment.
- **Future drift-detection foundation** — when drift detection eventually ships, it has a reliable fingerprint to drift against. (Drift detection itself stays out of v1.)
- **Future AI-reasoning foundation** — when AI agents ever reason about provisioning state, the `_provisioning` block is canonical evidence per v2.3 §1.2 "Evidence packet" reserved terminology.

### 5.5 Generator CLI

Operator-facing PowerShell function:

```
.\generate.ps1 `
    -TemplatesDir .\templates `
    -MachinesCsv .\samples\machines-100cnc-customer-A.csv `
    -SidecarConfig .\samples\sidecar-100cnc-customer-A-line1.json `
    -OutputFile .\out\gateway-customer-a-line1.json `
    [-AllowOverwrite] `
    [-OverwriteReason "<reason>"]
```

Required: `-TemplatesDir`, `-MachinesCsv`, `-SidecarConfig` (or CLI param equivalents), `-OutputFile`. Optional: `-AllowOverwrite` (with mandatory `-OverwriteReason`, per §5.5.1), `-WhatIf` (prints rendered output to stdout, writes nothing).

**Exit codes:** 0 success, 1 validation failure, 2 refuse-overwrite, 3 I/O error, 4 sidecar/CLI conflict.

**Logging:** structured key=value lines (`level=info component=validator csv-rows=100 valid=100`).

#### 5.5.1 Future `-AllowOverwrite` flag (NEW round-1 #3, round-2 #3 extended)

**v1 acknowledges this future capability without implementing it.** The placeholder exists in the CLI signature; for v1 it's a stub that prints "Not implemented in v1; modify the sidecar or CSV instead of forcing overwrite of generated output" and exits non-zero.

When the flag eventually lands (post-v1, when a real operator scenario demands it), the contract is locked NOW so the implementation has no design ambiguity:

- **Mandatory `-OverwriteReason` parameter** — operator must supply a free-text reason. Generator refuses to proceed without it.
- **Audit-trail preservation** — the regenerated `_provisioning` block carries:
  - `previousConfigFingerprint`: the fingerprint of the file being overwritten (preserved on-disk in the audit trail)
  - `overwriteAccepted`: `true`
  - `overwriteReason`: the operator's free-text reason
  - `overwriteOperator`: the OS user that ran the generator (PowerShell `$env:USERNAME` on Windows)
  - `overwriteAt`: ISO 8601 UTC timestamp of the override
- **Distinguishable from fresh generation** — a `_provisioning` block with `overwriteAccepted: true` is operationally distinguishable from a fresh one, so support / audit can find regenerations later.
- **Without these markers, operators are expected to use the standard workflow** — edit the sidecar (broker host change), edit the CSV (machine list change), and regenerate over the file that the previous run produced. The standard refuse-overwrite check honors that flow because the previous file's fingerprint matches its `_provisioning.configFingerprint`.

The point of acknowledging it in v1 is to **prevent operators from manually deleting `_provisioning` blocks** to bypass refuse-overwrite. README explicitly documents: "do not edit or delete the `_provisioning` block; the `-AllowOverwrite -OverwriteReason` flow exists for the rare regeneration case."

#### 5.5.2 PowerShell shell vs rendering/validation logic (NEW round-1 #9)

**Locked design rule:** the PowerShell CLI is a **shell**, not the **logic**. Rendering + validation logic lives in .NET-callable form so future portability stays open.

Concretely:

| Layer | Implementation | Why |
|---|---|---|
| CLI parameter parsing, exit code conventions, structured logging | PowerShell `generate.ps1` | Operator-facing surface; PS-native UX. |
| CSV parsing + per-row validation | PowerShell | Native PS, no need for .NET |
| Template loading + placeholder substitution | PowerShell with helper functions in `lib/` | Pure string operations; PS-native is fine and faster than .NET round-trip |
| **JSON schema validation against canonical schema** | .NET via `NJsonSchemaConfigurationValidator` (called from PS or via shell-out per Q-V1-A) | Single source of truth; never reimplement in PS |
| **Canonicalization** (§5.4.1) | .NET via a thin wrapper class invoked from PS | Determinism critical; not safe to leave to PS's stringification quirks (line-ending behavior differs between PS 5.1 and PS 7.x) |
| **SHA-256 fingerprinting** | .NET (`System.Security.Cryptography.SHA256`) — directly invokable from PS via `[System.Security.Cryptography.SHA256]::Create()` | Determinism critical |
| Refuse-overwrite logic | PowerShell | High-level workflow; PS is fine |

Rationale: PowerShell is the operator surface for v1 (per Locked Q1). But if Linux deployments, containerized tooling, or fleet APIs ever need to drive generation, only the CLI shell needs replacing — the core rendering + canonicalization + validation logic is already .NET-portable. Without this discipline, business logic slowly hardcodes into `.ps1` and the eventual port becomes a rewrite.

The v3 reality-check resolves Q-V1-A (interop vs shell-out wiring) consistent with this rule — either choice keeps logic in .NET; the difference is just the marshaling mechanism.

---

## 6. The `_provisioning` block (9 fields, restated for visibility)

Per §3.3, every generated output carries this 9-field block at the JSON root. The block is **always present, never optional, never partial.** A generator-produced file with a missing or malformed `_provisioning` block is invalid by definition.

The 9 fields:

| Field | Type | Value source |
|---|---|---|
| `generatedBy` | string | constant `"bulk-provision"` |
| `generatorVersion` | string | semver — generator's binary version |
| `templateSchemaVersion` | integer | constant per generator — v1 locks at `1` |
| `templateId` | string | template filename minus `.json` |
| `fleetId` | string | CLI `-FleetId` / sidecar |
| `gatewayProvisioningId` | string | CLI `-GatewayProvisioningId` / sidecar — distinct from runtime `Gateway.GatewayId` per §3.3.2 |
| `generatedAt` | string (ISO 8601 UTC, `Z` suffix) | `[DateTime]::UtcNow` invariant culture |
| `configFingerprint` | string (`"sha256:<hex>"`) | computed from canonicalized file minus block |
| `csvFingerprint` | string (`"sha256:<hex>"`) | computed from input CSV bytes |

**Future-work fields** (NOT in v1, acknowledged in §13): `sourceTemplateChecksum`, `generatorInvocationId`. Round-1 review item #8 — defer until a real AI / diagnostics consumer exists.

**Schema validation requirement:** `docs/config-schemas/gateway-configuration.schema.json` gains an optional `_provisioning` root object with each of the 9 fields type-locked. v3 reality-check (Q-V1-B extended per §3.3.1) confirms current validator behavior on unknown roots.

---

## 7. Deliverables (concrete file list — v2 extended)

Adapted from v2 roadmap §3.4.4 with v2-elaboration additions:

| File / folder | Status | Notes |
|---|---|---|
| `tools/bulk-provision/templates/template-fanuc-v1.json` | new | ~65 baseline FOCAS2 tag paths; static fields per §5.1. |
| `tools/bulk-provision/templates/template-brother-v1.json` | new | ~75 BrotherTagMap paths; validates against `BrotherHttpSourceConfiguration.FromSourceInstance`. |
| `tools/bulk-provision/templates/template-modbus-v1.json` | new | Per-instance Modbus. Per-tag definitions out (separate importer). |
| `tools/bulk-provision/templates/MANIFEST.md` | **new (round-1 #10)** | Current vs deprecated templates with successor mapping. Hand-maintained. |
| `tools/bulk-provision/generate.ps1` | new | Main CLI per §5.5. ~150-200 lines PS. |
| `tools/bulk-provision/lib/Substitute-Placeholders.ps1` | new | Internal helper. Pure literal-substitution per §5.1.4. |
| `tools/bulk-provision/lib/Validate-AgainstSchema.ps1` | new | Wraps `NJsonSchemaConfigurationValidator` per Q-V1-A. |
| `tools/bulk-provision/lib/Canonicalize-Json.ps1` | **new (round-1 #2)** | Wraps .NET canonicalization helper. Tests round-trip determinism. |
| `tools/bulk-provision/lib/Stamp-Provisioning.ps1` | new | Computes fingerprints + stamps the 9-field `_provisioning` block. |
| `tools/bulk-provision/samples/machines-100cnc-customer-A.csv` | new | 100-row sample (80 Fanuc + 20 Brother). |
| `tools/bulk-provision/samples/sidecar-100cnc-customer-A-line1.json` | **new (round-1 #6)** | Per-gateway sidecar with broker details + provisioning identity. |
| `tools/bulk-provision/samples/sidecar-100cnc-customer-A-line2.json` | **new** | Second gateway sidecar example. Documents the per-gateway-sidecar pattern. |
| `tools/bulk-provision/samples/gateway-customer-a-line1.json` | new | Sample generator output for the 27-machine Line 1 gateway. Checked in for diffability. |
| `tools/bulk-provision/README.md` | new | Operator workflow, Golden-source-template rule, sidecar workflow, anti-templating-engine boundary, EREMOS V2 topic shape note, Studio `ImportDraftDialog` workflow, Modbus per-tag CSV disambiguation, **future `-AllowOverwrite` acknowledgment + audit-marker contract**. |
| `tools/bulk-provision/tests/Generator.Tests.ps1` | new | Pester — unit + end-to-end. ~25 tests. |
| `tools/bulk-provision/tests/Provisioning-Block.Tests.ps1` | new | Pester — `_provisioning` block validity, fingerprint correctness, refuse-overwrite. ~10 tests. |
| `tools/bulk-provision/tests/Determinism.Tests.ps1` | **new (round-1 ⭐)** | Pester — encodes the deterministic provisioning guarantee. Round-trips fixtures through generator twice and asserts byte-identical output modulo `generatedAt`. ~5 tests. |
| `tools/bulk-provision/tests/Canonicalization.Tests.ps1` | **new (round-1 #2 + round-2 #1 + #2)** | Pester — locks UTF-8 no BOM, LF, sorted keys, array ordering, locale immunity. ~10 tests. |
| `docs/config-schemas/gateway-configuration.schema.json` | edit | Add `_provisioning` as optional root with 9 fields type-locked. Document `_`-prefix permissive-namespace rule (per ADR-0015). |
| `docs/decisions/ADR-0015-reserved-underscore-namespace.md` | **new (round-2 #5)** | Documents the `_`-prefix reserved-namespace rule. Locks parser semantics + schema semantics + forward compatibility. **May land as a precondition to Chip 3 implementation if v3 reality-check shows the current parser rejects unknown roots.** ADR number tentative — final assignment at write time. |
| `tests/ElpisEdgeConnect.Integration.Tests/ProvisioningSubsystemTests.cs` | **new (per v1 Q-V1-E recommendation)** | C# end-to-end: invoke `pwsh.exe` against the sample CSV + sidecar, parse the resulting `gateway.json` through `ConfigurationManager.CreateDraftAsync`, assert 100 source instances resolve via `FromSourceInstance` factories. ~5 tests. |
| `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` | edit | Mark §11 "Bulk-provision generator + templates committed" row checked. |

Estimate: ~1.5-2 weeks per v2 roadmap §3.4.5. v2 additions tighten correctness; do not add significant implementation surface.

---

## 8. Definition of Done (v2 extended)

The 8-item checklist from v2 roadmap §3.4.5, extended:

- [ ] Subsystem architecture per §5 implemented (template schema, validation pipeline, rendering pipeline, provenance/identity, generator CLI).
- [ ] End-to-end test (`ProvisioningSubsystemTests.cs`): 100-row CSV + sidecar → generator → `gateway.json` → 100 source instances resolve via `Focas2SourceConfiguration.FromSourceInstance(...)` / `BrotherHttpSourceConfiguration.FromSourceInstance(...)`.
- [ ] Schema-violation test: deliberate invalid input → clear error → non-zero exit → no file written.
- [ ] Refuse-overwrite test: regenerating over a hand-edited file is rejected.
- [ ] Provenance/identity test: every generated file has a valid `_provisioning` block with all **9** fields (vs v1's 7).
- [ ] **Determinism test (NEW round-1 ⭐):** same inputs, two generator runs, byte-identical output modulo `generatedAt`.
- [ ] **Canonicalization test (NEW round-1 #2 + round-2 #1 + #2):** UTF-8 no BOM, LF, sorted keys, sorted arrays, no locale leakage.
- [ ] **Identity-separation test (NEW round-1 #4):** generator does NOT write `Gateway.GatewayId` (leaves the placeholder or the first-boot UUID untouched); does write `_provisioning.gatewayProvisioningId`.
- [ ] **Anti-templating-engine test (NEW round-2 #4):** a fixture template containing `{% if %}` or `{{ x | filter }}` syntax is rejected at template-load time with a clear error.
- [ ] **Underscore-namespace test (NEW round-2 #5):** a synthetic `_diagnostics` root added alongside `_provisioning` survives round-trip through `ConfigurationManager.CreateDraftAsync` (or, if ADR-0015 lands first as a precondition, this becomes a precondition-met check).
- [ ] README walks an operator through the install-day workflow, including sidecar editing for broker host changes.
- [ ] MANIFEST.md present with current/deprecated template listing.
- [ ] Sample `gateway-customer-a-line1.json` checked in.
- [ ] Deployment-readiness §11 acceptance signal flipped.
- [ ] **NO drift-detection logic added** (scope cap).
- [ ] **NO `src/ElpisEdgeConnect.Provisioning/` project created** (scope cap per v2.3 §1.1).
- [ ] **NO `{% if %}`, `{% for %}`, `|`-filter, or other templating-engine syntax in any template file** (round-2 #4 scope cap).
- [ ] All v2.3 §1.2 canonical terms used consistently across new artifacts. Anti-synonyms absent.
- [ ] Solution-wide test sweep clean: `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky"` still green.

---

## 9. Step-by-step implementation sequence (v2 — 18 steps)

Locked sequence for the implementation session(s). Two sessions: session 1 = steps 1-10 (mechanics + ADR), session 2 = steps 11-18 (E2E + tests + docs).

1. **v3 reality-check pass.** Resolve Q-V1-A (PS interop vs shell-out), Q-V1-B (does canonical parser accept unknown roots — answer drives whether ADR-0015 lands as Chip 3 precondition or part of), Q-V1-C (PowerShell version at customer site), Q-V2-A (catalog iteration order), Q-V2-B (ProvisioningSubsystemTests pwsh.exe invocation feasibility).
2. **ADR-0015 (NEW round-2 #5).** Write `docs/decisions/ADR-0015-reserved-underscore-namespace.md`. If v3 confirms canonical parser already accepts unknown roots, ADR documents observed behavior + locks it. If not, ADR + the small Core parser fix to permissively accept `_*` roots lands BEFORE step 3.
3. **Project scaffolding.** `tools/bulk-provision/` folder structure. No `.csproj`.
4. **Canonicalization helper (round-1 #2).** Implement `lib/Canonicalize-Json.ps1` first because templates depend on it. Pester test for canonicalization correctness (round-2 #1 + #2 covered).
5. **Template authoring (Fanuc).** `template-fanuc-v1.json` per §5.1.
6. **Template authoring (Brother).** `template-brother-v1.json` per §5.1.
7. **Template authoring (Modbus).** `template-modbus-v1.json` per §5.1.
8. **Template MANIFEST.md.** Current vs deprecated convention per §5.1.3.
9. **CLI skeleton.** `generate.ps1` parameter parsing, exit codes, structured logging. Sidecar + CLI conflict detection.
10. **Rendering pipeline.** `Substitute-Placeholders.ps1` helper. Anti-templating-engine validator (rejects `{% %}`, `|`, etc. — round-2 #4).
11. **Validation pipeline.** `Validate-AgainstSchema.ps1`. CSV pre-validation in CLI. NJsonSchema integration per Q-V1-A resolution.
12. **Provenance + Configuration identity.** `Stamp-Provisioning.ps1` — 9-field block. Refuse-overwrite check.
13. **Sample CSV + sample sidecar + sample output.** `samples/machines-100cnc-customer-A.csv`, two sidecars (line1, line2), one rendered output. Check in.
14. **Schema update.** Add `_provisioning` as optional root in `docs/config-schemas/gateway-configuration.schema.json` per ADR-0015.
15. **Pester tests.** `Generator.Tests.ps1`, `Provisioning-Block.Tests.ps1`, `Determinism.Tests.ps1`, `Canonicalization.Tests.ps1`. ~50 Pester tests total.
16. **C# end-to-end test.** `tests/ElpisEdgeConnect.Integration.Tests/ProvisioningSubsystemTests.cs` per Q-V1-E recommendation. ~5 tests.
17. **README authoring.** Operator walk-through. Golden-source discipline. Sidecar workflow. Anti-templating-engine boundary. `-AllowOverwrite` future-work acknowledgment + audit-marker contract. Studio `ImportDraftDialog` flow. EREMOS V2 topic shape note.
18. **Solution-wide sweep + commit.** All tests green, Pester suite green, deployment-readiness §11 flipped, commit + push + PR.

---

## 10. Open questions

### 10.1 Carried forward from v1 (still LOCKED to v3 reality-check)

| # | Item | Resolution status |
|---|---|---|
| Q21 / Q-V1-A | NJsonSchema PS-interop vs shell-out | OPEN. v3 reality-check resolves. |
| Q22 / Q-V1-B | Canonical parser unknown-root behavior | OPEN. v3 reality-check resolves. Drives whether ADR-0015 lands as precondition vs alongside. |
| Q-V1-C | PowerShell version at customer site | OPEN. Pending customer engineering confirmation. v1 floor assumes PS 5.1+. |
| Q-V1-D | Sample CSV IP-range convention | OPEN. Out-of-scope for Chip 3 v1; flag for install playbook. |
| Q-V1-E | End-to-end test placement | **PROVISIONALLY RESOLVED in v2 §7 — option (a) C# integration test invoking `pwsh.exe`.** Final confirmation in v3 (Q-V2-B below: is `pwsh.exe` invocation from xUnit reliable on this codebase's CI runner?). |
| Q-V1-F | Fail-loud on license-limit overflow | **RESOLVED in v2 (per v1 recommendation).** Generator does NOT check license limits; runtime apply does. Documented in README + this v2 §2 "What this is NOT". |
| Q-V1-G | Template versioning convention | **RESOLVED in v2 §5.1.3 (round-1 #10 downscoped).** Append-only versioning + MANIFEST.md. |
| **Q-V1-H** | **Sink + route templating** | **RETRACTED in v2 §5.1.2 (round-1 #6).** Superseded by MQTT-placeholder injection via sidecar. The cleaner architecture eliminates the question entirely. |

### 10.2 New v2-specific open questions (for v3 reality-check only — no v3 ChatGPT pass; v3 reality-check is in the implementation session)

| # | Area | Question |
|---|---|---|
| Q-V2-A | `Connection.dataPoints` array ordering | §5.4.2 locks "catalog order, not alphabetical" for tag paths. Does `BrotherTagMap` / `Focas2TagMap` expose a stable iteration order? If yes, the generator uses it. If no, fallback: alphabetical-by-canonical-tag-path. v3 reality-check confirms. |
| Q-V2-B | `pwsh.exe` invocation from xUnit | The C# `ProvisioningSubsystemTests.cs` shells out to `pwsh.exe` (or `powershell.exe` on 5.1) to run the generator. Is the test runner host (CI runner or dev machine) reliably set up for that? If not, fallback: a PS-only Pester end-to-end test that parses the generated JSON and validates structure (lighter, lower fidelity). v3 reality-check confirms feasibility. |
| Q-V2-C | ADR-0015 number | The ADR number `0015` is tentative. The current latest ADR is `0014-config-state-vs-runtime-state.md`. If M.2d.4's wizard-contract ADR lands first (per M.2d.4 v1 plan §10), this ADR becomes `0016`. v3 reality-check assigns final number. |
| Q-V2-D | Future `_diagnostics` namespace pre-emption | The underscore-namespace test (per §8 DoD) uses `_diagnostics` as a synthetic example. Should v2 reserve `_diagnostics` for the future Operational Intelligence layer per v2.3 §1.2? Recommendation: yes — name it in ADR-0015 as a reserved future namespace alongside `_provisioning`. Costs nothing now, blocks accidental reuse. |
| Q-V2-E | CSV-line-number debug field | §5.4.2 mentions `_provisioning.csvLineNumber` per-source as a debug aid for operators inspecting rendered output in CSV order. Should this be v1 scope, or future-work? Recommendation: future-work — keeps the `_provisioning` block stable at 9 fields for v1, adds it later if real demand emerges. Documented in §13 future-work. |

### 10.3 Reality-check items that may materially affect v2 locks

These are the open questions whose resolution could force a v2 amendment:

- **Q-V1-B / §3.3.1:** If the canonical parser does NOT permissively accept unknown roots, ADR-0015 + a small Core fix lands as a Chip 3 precondition. This adds ~2-3 days to the v1 estimate but does not change scope.
- **Q-V2-B:** If `pwsh.exe`-from-xUnit invocation is unreliable, the C# E2E test downgrades to PS-only Pester. Reduces test fidelity but preserves coverage.

Everything else is implementation-detail resolved in v3 reality-check without changing v2 scope.

---

## 11. Cross-references

### Project governance + architectural locks

- `CLAUDE.md` §3 — Locked Decision #1 protocol-agnostic core, #2 canonical data model, #5/#6/#7 three-layer licensing, **#19 gateway identity** (per-gateway UUID established at first start — distinct from `_provisioning.gatewayProvisioningId`).
- `docs/ARCHITECTURE_BLUEPRINT.md` Section 8 — Configuration system; `SourceInstanceConfig` shape the templates target.
- `docs/ARCHITECTURE_BLUEPRINT.md` Appendix A — Locked decisions.
- `docs/platform-principles.md` P6 — "Operational product, not developer tool." Provisioning Subsystem is operator-facing infrastructure.

### Roadmap predecessor docs (LOCKED scope inputs)

- [Phase 2 wrap-up v2 roadmap](2026-05-21-phase2-wrapup-roadmap-v2.md) §3.4 — Subsystem architecture, Q1-Q8 verdicts, DoD.
- [v2.3 roadmap](2026-05-21-phase2-wrapup-roadmap-v2.3.md) §1.1 — No-new-shared-abstractions rule (binding); §1.2 — Terminology freeze (binding); §1.3 — Platform-contracts deferred follow-up (ADR-0015 is an exception, not a violation).

### Predecessor (this plan trail's v1)

- [Chip 3 v1](2026-05-21-chip3-provisioning-subsystem-plan.md) — open questions Q-V1-A through Q-V1-H, partial answers, 8 v1-elaboration questions.

### Source code touchpoints (verified 2026-05-21)

- `src/ElpisEdgeConnect.Sources.Focas2/Focas2SourceConfiguration.cs` — Fanuc template's `FromSourceInstance` target.
- `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpSourceConfiguration.cs:99` — Brother template's target.
- `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherTagMap.cs` — append-only catalog source for Brother template paths.
- `src/ElpisEdgeConnect.SchemaValidation/NJsonSchemaConfigurationValidator.cs` — schema validator for Q-V1-A wiring.
- `src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs:47,54,65` — `SourceFocas2`, `SourceBrotherHttp`, `SourceModbusTcp` keys.
- `docs/config-schemas/gateway-configuration.schema.json` — canonical schema receiving optional `_provisioning` root.
- `src/ElpisEdgeConnect.Management/Components/Pages/ImportDraftDialog.razor` — Studio integration surface.

### Deployment context

- [100-CNC deployment readiness](2026-05-20-100-cnc-deployment-readiness.md) §3 Option A lock, §3.4.1 Golden-source-template discipline, §6 critical path, §7 customer answers, §11 acceptance signal.

---

## 12. Proposed ADR-0015 sketch

**Title:** `_`-prefixed root keys are reserved system metadata namespaces

**Status:** Proposed (lands either as Chip 3 precondition or alongside; final position TBD by v3 reality-check Q-V1-B).

**Context:** The Provisioning Subsystem (Chip 3) introduces `_provisioning` as the first instance of root-level system metadata in `gateway.json`. Without a convention rule, future metadata blocks (`_diagnostics`, `_migration`, `_ai`, `_audit`) will each renegotiate naming + schema-permissiveness independently.

**Decision:** Root-level keys beginning with `_` are reserved for system metadata namespaces.

**Scope:** Convention applies to **root-level keys only**, not nested. Nested objects continue to follow their own schemas (no `_foo` magic within nested `Sources[i]`).

**Parser semantics:** The canonical parser preserves `_*` root keys on round-trip (read → mutate → write) but does NOT interpret them in the data path. They are visible to tooling (generator, support scripts, future AI agents) but invisible to the runtime pipeline.

**Schema semantics:** `docs/config-schemas/gateway-configuration.schema.json` accepts `_*` root keys as additional properties at the root level. Each known namespace (`_provisioning` in v1) gets an optional sub-schema; unknown `_*` namespaces are permissively accepted without sub-schema validation (future-compatible).

**Forward compatibility:** New `_`-namespaces never require parser changes. Adding `_diagnostics` to the schema is purely additive.

**Consequences:**
- Provisioning, future diagnostics evidence, future AI metadata, future migration markers all coexist cleanly at the root.
- Operators learn the convention once: `_`-prefixed keys are system metadata, do not edit.
- The Drift detection milestone (post-v1, reserved) inherits this surface naturally.

**Reserved future namespaces (named in this ADR but not implemented):**
- `_diagnostics` — future Operational Intelligence layer evidence
- `_audit` — future audit-trail snapshots
- `_migration` — future config-format migration markers
- `_ai` — future AI advisory evidence packets

**Cross-reference:** Phase 2 wrap-up roadmap v2.3 §1.2 ("Evidence packet" reserved terminology) + Locked Decision #14 (AI in decision-support only, never in data path).

---

## 13. Future work (acknowledged, NOT v1 scope)

Per round-1 review items #3 + #8 and round-2 review item #3, these are explicitly acknowledged future capabilities. v2 references them so future planners do not rediscover the gaps:

- **`-AllowOverwrite -OverwriteReason` flag** (round-1 #3, round-2 #3) — see §5.5.1 for the locked contract. v1 ships the stub with a clear "not implemented" error; v-next implements the audit-trail-preserving regeneration flow.
- **Evidenceability fields in `_provisioning`** (round-1 #8) — `sourceTemplateChecksum` (per-source template hash, distinct from `configFingerprint` which is whole-file), `generatorInvocationId` (unique per generator run for audit correlation). Useful for future AI substrate reasoning over `_provisioning` evidence per v2.3 §1.2 "Evidence packet". Defer until a real consumer exists.
- **`_provisioning.csvLineNumber` per-source debug field** (Q-V2-E) — adds operator-friendly traceability between CSV row and rendered output. Defer until operators ask for it.
- **`src/ElpisEdgeConnect.Provisioning/` runtime project** (v2 roadmap §3.4.1) — promotion from `tools/` to runtime API access. Post-soak, post-install. Real demand required.
- **Drift detection** (v2.3 §1.2 reserved term) — `_provisioning` block is the foundation, but drift detection itself needs install-time data to drift against. Future milestone.
- **Template lifecycle automation** (round-1 #10 downscoped) — deprecation enforcement, upgrade tooling, template-evolution migrators. Trigger: ≥8 active templates in the repo.

---

**End of v2 draft. LOCKED — ready for v3 reality-check pass during implementation session.**

Per ChatGPT round-2 verdict: "no additional review cycle needed before v2." v3 is the reality-check pass (resolve Q-V1-A through Q-V1-D + Q-V2-A through Q-V2-E from inside the codebase) that happens at the start of the implementation session, not a separate ChatGPT review iteration.

The next operational step is one of:
1. **Wait for the parallel v1 plans (EREMOS V2, M.2c, M.2d.1-.4) to receive their own ChatGPT review + v2 ratification passes**, then sequence implementation per the Phase 2 wrap-up roadmap v2 §1.
2. **Or begin Chip 3 implementation now**, on its own branch, with v3 reality-check inline. v2 is sufficient for that.
