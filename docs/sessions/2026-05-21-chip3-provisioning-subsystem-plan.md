# Chip 3 — Provisioning Subsystem (v1 plan)

**Status:** v1 — DRAFT. Inputs from v2/v2.1/v2.2/v2.3 roadmaps are LOCKED; v1-specific elaboration carries OPEN QUESTIONS for ChatGPT review pass and v3 reality-check.
**Date:** 2026-05-21
**Branch:** drafted on `claude/tender-edison-639a71`; implementation lands on its own branch post-ratification.
**Predecessor (locked roadmap):** [v2](2026-05-21-phase2-wrapup-roadmap-v2.md) §3.4 + [v2.2](2026-05-21-phase2-wrapup-roadmap-v2.2.md) §1.1 + [v2.3](2026-05-21-phase2-wrapup-roadmap-v2.3.md) §1.1-§1.3
**Predecessor (load-bearing dependency):** M.P2.4 Brother HTTP migration — [handoff 2026-05-21](2026-05-21-mp24-handoff.md). `BrotherHttpSourceConfiguration.FromSourceInstance` shape (read 2026-05-21) is what the Brother template renders into.
**Estimated size:** 1.5–2 weeks of focused work per v2 §3.4.5. v3 reality-check refines.
**Test baseline:** 2263 passing across 12 projects (post-M.P2.4). Target after Chip 3: ~2300 with ~35 new Pester + integration tests.

---

## 1. Goal

Deliver the **Provisioning Subsystem** — the canonical mechanism by which fleet-scale CNC source configurations enter the system. The first concrete shape is `tools/bulk-provision/`: per-protocol Golden-source templates, a per-gateway CSV, a PowerShell generator CLI that stamps each output `gateway.json` with a `_provisioning` block (provenance + Configuration identity), and an end-to-end test that proves the round-trip from CSV through `FromSourceInstance(...)` to a working source instance configuration. This is the hard prerequisite for the 100-CNC customer commissioning per the deployment-readiness §3 Option A lock and §6 critical path.

---

## 2. Architectural framing

### What this IS

- A first-class **subsystem** of EdgeConnect — not a one-off script. It owns: template schema, validation pipeline, rendering pipeline, provenance + Configuration identity, and the operator-facing generator CLI. Per v2 §3.4 review item #4 and v2.3 §1.2 terminology freeze: this is **the Provisioning Subsystem**, not "bulk-provision tooling."
- A **Golden-source template** workflow. Templates are version-controlled and treated as the single source of truth; operators never hand-edit generated `gateway.json` files. The locked discipline is captured in deployment-readiness §3 "Locked discipline — golden-source templates."
- A **schema-validated** boundary. Templates pass through the same canonical `GatewayConfiguration` schema (`docs/config-schemas/gateway-configuration.schema.json`) that `ConfigurationManager.CreateDraftAsync` enforces at apply time. No second validation surface.
- A **fail-loud** generator. Invalid input produces a clear error and a non-zero exit; the generator refuses to overwrite a file that has been hand-edited since the last generation.

### What this is NOT (locked scope caps)

- **NOT a drift-detection subsystem.** v2 §3.4.1 explicitly caps drift detection out of v1 scope: "drift detection deserves install-time data to drift against — we don't have that yet." The reserved terminology `Drift detection` (v2.3 §1.2) is for the future milestone, not v1. **No drift comparison logic ships in v1, even as a stub.** Trigger to pause + surface if the v1 implementation drifts toward it.
- **NOT a runtime API surface.** Lives at `tools/bulk-provision/` for v1. Promotion to `src/ElpisEdgeConnect.Provisioning/` with runtime API access is explicitly deferred — a future decision post-soak, post-install. v1 must NOT introduce `src/ElpisEdgeConnect.Provisioning/` even as an empty project. (v2 §3.4.1 location lock.)
- **NOT the Modbus per-tag CSV importer.** The Provisioning Subsystem is per-instance (one CSV row → one source instance). Modbus's per-tag CSV importer at `src/ElpisEdgeConnect.Sources.ModbusTcp/Import/` is per-tag (one CSV row → one register definition within one source). v2 §3.4.2 Q5: distinct tool, distinct README section. The README explicitly disambiguates.
- **NOT a Studio UI feature.** The operator-facing surface is the PowerShell CLI. The handoff into Studio is the existing `ImportDraftDialog.razor` (verified to exist at `src/ElpisEdgeConnect.Management/Components/Pages/ImportDraftDialog.razor`); the Provisioning Subsystem produces a file that operators import through that dialog.
- **NOT a template-inheritance system.** v2 §3.4.2 Q7 locks: out for v1. If a customer ever needs Fanuc-A800 vs Fanuc-A600 variants, two separate template files. Revisit if a real customer asks.
- **NOT a new shared abstraction** per v2.3 §1.1. The generator + templates are bounded code under `tools/bulk-provision/`, scoped to one operation. No "provisioning primitives" promoted into Core, Management, or any other shared surface.

### Locked invariants that anchor the work

From `CLAUDE.md` §3, `ARCHITECTURE_BLUEPRINT.md` Appendix A, and the v2 roadmap:

- **Canonical data model (Locked #2)** — templates produce `SourceInstanceConfig` JSON DTOs; the generator does not bypass the canonical config layer.
- **Schema-first config (B1)** — generated files validate against the same `GatewayConfiguration` schema used by `NJsonSchemaConfigurationValidator` (`src/ElpisEdgeConnect.SchemaValidation/NJsonSchemaConfigurationValidator.cs`, verified to exist with a callable surface).
- **Append-only catalog semantics** (v2.3 §1.2) — Brother (and FOCAS2) tag-map paths within a template are stable within an M.PX.x line. Templates may not invent paths that don't exist in `BrotherTagMap` / FOCAS2's catalog.
- **No legacy DTO leaks** (M.P2.4 §2 lock) — templates emit `SourceInstanceConfig` shape and connection JSON consumable by `Focas2SourceConfiguration.FromSourceInstance` / `BrotherHttpSourceConfiguration.FromSourceInstance`. They never reference legacy `CncMachineData` or `MachineConfig` shapes.
- **Audit-chain integrity** (deployment-readiness §3 enforcement) — generator output flows through Studio's draft → validate → apply round-trip; the `_provisioning` block survives that round-trip and is queryable post-apply.

---

## 3. Locked inputs from the roadmap (do not relitigate)

These are LOCKED by v2 + v2.2 + v2.3. v1 elaborates within these locks; questions about these decisions are out of scope.

### 3.1 Architecture (v2 §3.4.1)

The five-component subsystem architecture:

```
┌─────────────────────────────────────────────────┐
│              Provisioning Subsystem             │
│                                                  │
│  ┌──────────────┐    ┌──────────────────────┐  │
│  │ Template     │    │ Validation Pipeline   │  │
│  │ Schema       │───→│ (JSON Schema + cross- │  │
│  │              │    │  field checks)        │  │
│  └──────────────┘    └──────────────────────┘  │
│         │                       │                │
│         ▼                       ▼                │
│  ┌──────────────────────────────────────────┐  │
│  │ Rendering Pipeline                        │  │
│  │ template + CSV row → SourceInstanceConfig │  │
│  └──────────────────────────────────────────┘  │
│         │                                        │
│         ▼                                        │
│  ┌──────────────────────────────────────────┐  │
│  │ Provenance + Configuration Identity       │  │
│  │ (_provisioning block on every output)     │  │
│  └──────────────────────────────────────────┘  │
│         │                                        │
│         ▼                                        │
│  ┌──────────────────────────────────────────┐  │
│  │ Generator CLI                             │  │
│  │ (PowerShell — operator-facing surface)    │  │
│  └──────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

### 3.2 Locked decisions from v2 §3.4.2 (verdicts Q1-Q8)

| Q | Lock |
|---|---|
| Q1 PowerShell vs Python | **PowerShell.** Windows toolchain, no Python dep at customer site. |
| Q2 CSV columns | **`make,instanceId,deviceId,deviceName,host,enabled`** — per-gateway CSV. |
| Q3 Provenance format | **`_provisioning` root block** (JSON object), canonical parser ignores unknown roots. NOT a JSON-comment header. Merged with Configuration identity per v2 §3.4.3. |
| Q4 Schema validation | **NJsonSchema via PowerShell**, falling back to `dotnet run --project src/ElpisEdgeConnect.SchemaValidation` if PS interop is awkward. v3 reality-check resolves exact wiring (this is v1 Q21 from v2 §5.2). |
| Q5 Modbus per-tag CSV composition | **Distinct tool, distinct README section.** README disambiguates. |
| Q6 Edited-file detection | **Content hash** — SHA-256 of canonicalized JSON minus the `_provisioning` block. |
| Q7 Template inheritance | **Out for v1.** Two separate templates if variants ever needed. |
| Q8 Studio integration | Studio's `ImportDraftDialog.razor` (verified to exist) accepts the generator's output. README documents the workflow. v3 reality-check confirms exact UX (v1 Q22 from v2 §5.2). |

### 3.3 `_provisioning` block schema (v2 §3.4.3 LOCKED structure)

Every generated `gateway.json` carries exactly this `_provisioning` root block:

```json
{
  "_provisioning": {
    "generatedBy": "bulk-provision",
    "generatorVersion": "1.0.0",
    "templateId": "fanuc-standard-v1",
    "fleetId": "100cnc-customer-A",
    "generatedAt": "2026-05-22T08:15:00Z",
    "configFingerprint": "sha256:<hash of file MINUS _provisioning block>",
    "csvFingerprint": "sha256:<hash of input machines.csv>"
  },
  "Gateway": { ... },
  "Sources": [ ... ],
  "Sinks": [ ... ],
  "Routes": [ ... ]
}
```

**Field-by-field purpose (locked rationale per v2 §3.4.3):**

- `generatedBy` — distinguishes Provisioning-Subsystem output from hand-edited / Studio-wizard-authored configs. Refuse-overwrite check looks for this.
- `generatorVersion` — drift between generator versions when re-generating later is detectable.
- `templateId` — which `template-fanuc-v1` / `template-brother-v1` / `template-modbus-v1` was applied. Drift analysis later can ask "was this fleet generated from template-fanuc-v1 or v2?"
- `fleetId` — identifies the customer / fleet. Future fleet management needs this.
- `generatedAt` — ISO 8601 UTC. Support / debugging timeline.
- `configFingerprint` — SHA-256 of the file content MINUS the `_provisioning` block itself. Refuse-overwrite check: recomputed fingerprint matches → file is untouched; mismatch → hand-edited → generator refuses to overwrite.
- `csvFingerprint` — SHA-256 of the input CSV. Enables "did this fleet's machines list change since last generation?" for future support workflows.

**Configuration identity** (v2.3 §1.2 canonical term) is the subset `templateId + fleetId + configFingerprint + csvFingerprint`. The remaining three fields (`generatedBy + generatorVersion + generatedAt`) are pure provenance.

**Schema validation requirement:** the canonical `GatewayConfiguration` schema must explicitly accept `_provisioning` as an optional root. v3 reality-check confirms whether the schema currently treats unknown roots permissively or rejects them (v1 Q23 from v2 §5.2).

### 3.4 Deliverables (v2 §3.4.4)

Locked file list — v3 reality-check refines paths but does not add or drop files:

| Folder/file | Purpose |
|---|---|
| `tools/bulk-provision/templates/template-fanuc-v1.json` | Golden-source Fanuc template. Polling 3000 ms, ~65 baseline tags. Placeholders for `instanceId`, `deviceId`, `deviceName`, `connection.ipAddress`. |
| `tools/bulk-provision/templates/template-brother-v1.json` | Golden-source Brother template. Polling 3000 ms, ~75 tags (incl. tools). Placeholders for `instanceId`, `deviceId`, `deviceName`, `connection.baseUrl`. |
| `tools/bulk-provision/templates/template-modbus-v1.json` | Golden-source Modbus template (per-instance — distinct from the per-tag importer). |
| `tools/bulk-provision/generate.ps1` | Generator CLI. Schema validation + provenance/identity stamping + refuse-overwrite check. |
| `tools/bulk-provision/samples/machines-100cnc-customer-A.csv` | 100-row sample (80 Fanuc + 20 Brother per deployment-readiness §7-Q5). |
| `tools/bulk-provision/samples/gateway-fanuc-line1.json` | Sample generated output (one of 4 customer gateways). |
| `tools/bulk-provision/README.md` | CSV format, template structure, Golden-source rule, regeneration workflow, EREMOS V2 topic shape, Studio "Import draft" workflow, distinction from Modbus per-tag CSV importer. |
| `tools/bulk-provision/tests/` | Pester tests — generator unit tests + end-to-end test loading generated output via `ConfigurationManager.CreateDraftAsync`. |
| `docs/config-schemas/gateway-configuration.schema.json` | Add `_provisioning` as an optional root object. |

### 3.5 Definition of Done (v2 §3.4.5)

Eight-item checklist; all must pass before Chip 3 closes:

- [ ] Subsystem architecture per §3.1 implemented (template schema, validation pipeline, rendering pipeline, provenance/identity, generator CLI).
- [ ] End-to-end test: 100-row CSV → generator → `gateway.json` → 100 source instances resolve via `Focas2SourceConfiguration.FromSourceInstance(...)` / `BrotherHttpSourceConfiguration.FromSourceInstance(...)`.
- [ ] Schema-violation test: deliberate invalid input → clear error → non-zero exit → no file written.
- [ ] Refuse-overwrite test: regenerating over a hand-edited file is rejected with a clear error.
- [ ] Provenance/identity test: every generated file has a valid `_provisioning` block with all 7 fields.
- [ ] README walks an operator through the install-day workflow.
- [ ] Sample `gateway-fanuc-line1.json` checked in.
- [ ] Deployment-readiness §11 acceptance signal — "Bulk-provision generator + templates committed" row checked.
- [ ] **NO drift-detection logic added** (scope cap).

---

## 4. v2.3 implementation-discipline locks (apply to this v1)

Carried verbatim from v2.3 §1.1-§1.3:

### 4.1 No-new-shared-abstractions discipline

Per v2.3 §1.1, during Chip 3 implementation:

- **Allowed:** local PowerShell helpers (private functions), file-scoped types under `tools/bulk-provision/`, Pester test fixtures, deployment utilities scoped to one operation.
- **Forbidden:** new shared framework abstractions, reusable UI shells, Runtime Tap helpers (M.2c territory), generic provisioning primitives that anyone outside `tools/bulk-provision/` could consume, cross-wizard contracts (M.2d territory).

**Trigger to pause + surface:** if implementing Chip 3 tempts the work toward a `src/ElpisEdgeConnect.Provisioning/` project, a shared "provisioning primitives" namespace, or any abstraction reusable outside `tools/bulk-provision/`, **stop and report.** The right answer is "defer." The wrong answer is "I'll do a temporary version and we'll harden it later."

### 4.2 Terminology freeze

Per v2.3 §1.2, all artifacts produced by this v1 (commit messages, code comments, test names, README, doc updates) use these exact canonical terms:

- **Provisioning Subsystem** — not "bulk-provision tooling", "config generator", or "provisioning scripts."
- **Golden-source template** — not "master template", "reference template", or "canonical template."
- **`_provisioning` block** — not "provenance header", "config metadata", or "identity block."
- **Configuration identity** — refers specifically to `templateId + fleetId + configFingerprint + csvFingerprint`. Not "config fingerprint" alone (ambiguous — that's only one field).
- **Drift detection** — reserved name; out of v1 scope. Never appears in code, comments, or test names that ship in v1.

Anti-synonyms surfaced during implementation are flagged and replaced before commit.

### 4.3 No promotion to platform contract surface

Per v2.3 §1.3: the `_provisioning` block and Golden-source-template workflow are platform-level behavioral guarantees emerging in this milestone, but v2.3 explicitly defers the `docs/contracts/` folder. v1 does NOT create `docs/contracts/`; the `_provisioning` block schema lives in `docs/config-schemas/gateway-configuration.schema.json`, the workflow discipline lives in `tools/bulk-provision/README.md`, and the Configuration-identity field-by-field rationale lives in this plan trail (referenced by the README).

---

## 5. Subsystem architecture in detail

### 5.1 Template Schema

Each Golden-source template is a `gateway.json`-shaped JSON file with **placeholder strings** in instance-specific fields. The static fields (tag definitions, polling settings, retry/backoff, transforms) are baked in; the per-machine variables flow in from the CSV.

**Placeholder convention:** `{{ instanceId }}`, `{{ deviceId }}`, `{{ deviceName }}`, `{{ host }}`, `{{ enabled }}`. Curly-brace-with-space delimiters chosen so a stray missing replacement is visually loud in the generated file (and breaks JSON validation, which is a feature not a bug).

**Per-protocol shape:**

- `template-fanuc-v1.json` — placeholders land in `Sources[i].InstanceId`, `Sources[i].DeviceId`, `Sources[i].DeviceName`, `Sources[i].Connection.ipAddress`. Static fields: `ProtocolName = "focas2"`, `Connection.port = 8193`, `Polling.IntervalMs = 3000`, `Connection.dataPoints = [...~65 baseline paths...]`. Matches `Focas2SourceConfiguration.FromSourceInstance` consumption shape.
- `template-brother-v1.json` — placeholders land in `Sources[i].InstanceId`, `Sources[i].DeviceId`, `Sources[i].DeviceName`, `Sources[i].Connection.baseUrl`. Static fields: `ProtocolName = "brother-http"`, `Polling.IntervalMs = 3000`, `Connection.dataPoints = [...~75 tool-inclusive paths from BrotherTagMap...]`. Matches `BrotherHttpSourceConfiguration.FromSourceInstance` (verified at `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpSourceConfiguration.cs:99`).
- `template-modbus-v1.json` — per-instance Modbus template. Placeholders land in `Sources[i].InstanceId`, `Sources[i].DeviceId`, `Sources[i].DeviceName`, `Sources[i].Connection.host`. Note that the per-tag register definitions are NOT in this template — those flow through the separate Modbus per-tag CSV importer (see v2 §3.4.2 Q5 disambiguation).

Templates also contain the `Gateway`, `Sinks`, and `Routes` blocks. v1 keeps those minimal: one gateway block (placeholders for `GatewayId`, `GatewayName`, `Site`), one MQTT sink pointing at `localhost:1883` (operator edits this post-generation; documented in README as an explicit step), and one route per source-class (Fanuc-sources-to-MQTT, Brother-sources-to-MQTT). The 100-CNC deployment has 4 gateways = 4 generator runs = 4 output files; per-gateway customization stays within the CSV.

### 5.2 Validation Pipeline

Two-stage validation:

1. **CSV validation** — required columns present, no duplicate `instanceId`, `host` parses as IP or hostname, `make` is one of `{fanuc, brother, modbus}`, `enabled` is one of `{true, false}` (case-insensitive). PowerShell-native checks; clear per-row error messages with line numbers.
2. **JSON schema validation** — the rendered `gateway.json` (post-substitution, pre-write) validates against `docs/config-schemas/gateway-configuration.schema.json` using NJsonSchema. Per v2 §3.4.2 Q4, the validator surface lives at `src/ElpisEdgeConnect.SchemaValidation/NJsonSchemaConfigurationValidator.cs` (verified). v3 reality-check resolves whether PS calls NJsonSchema directly (PS can load .NET assemblies via `Add-Type` / `Import-Module`) or shells out to a `dotnet run` wrapper. v1 carries this as open question Q-V1-A below.

**Failure mode:** validation failure prints the offending error (CSV line + column for CSV errors; JSON schema path + reason for schema errors), exits non-zero, writes no file. Per DoD: "schema-violation test → clear error → non-zero exit → no file written."

### 5.3 Rendering Pipeline

For each row of `machines.csv`:

1. Read the row.
2. Select the template per `row.make`.
3. Substitute placeholders → produce one `SourceInstanceConfig` JSON.
4. Append to `Sources[]` array in the accumulating output config.

After all rows: assemble the final `gateway.json` candidate (`Gateway` + `Sources` + `Sinks` + `Routes` blocks). Compute `configFingerprint` and `csvFingerprint`. Stamp `_provisioning` block. Validate (per §5.2 stage 2). Write the file.

**Determinism:** identical template + identical CSV must produce a byte-identical output file (per v2.3 §1.2 "Deterministic replay" canonical term — this is the provisioning equivalent). Sorted property order, fixed JSON serialization options, stable timestamp source (`generatedAt` is the only time-varying field; everything else hashes to the same bytes). This is testable: run the generator twice on the same inputs, diff outputs except for `generatedAt`.

### 5.4 Provenance + Configuration Identity stamping

Per §3.3, the `_provisioning` block is stamped onto every output. Order of operations matters:

1. Render the substantive content (`Gateway` + `Sources` + `Sinks` + `Routes`).
2. Canonicalize the JSON (sorted keys, no insignificant whitespace) → compute SHA-256 → that's `configFingerprint`.
3. Hash the input CSV bytes → SHA-256 → `csvFingerprint`.
4. Stamp the `_provisioning` block with both fingerprints + the other 5 fields (`generatedBy`, `generatorVersion`, `templateId`, `fleetId`, `generatedAt`).
5. Serialize the final output (`_provisioning` block first, then `Gateway`, `Sources`, `Sinks`, `Routes` — alphabetical-with-leading-underscore-first is the conventional order).

**Refuse-overwrite check** (when re-generating over an existing file):

1. Read existing file.
2. Extract its `_provisioning` block → grab stored `configFingerprint`.
3. Strip the `_provisioning` block → canonicalize → SHA-256 → that's the recomputed fingerprint.
4. If stored ≠ recomputed → file was hand-edited → refuse to overwrite, exit non-zero with a clear error pointing the operator to the Golden-source-template discipline.
5. If stored == recomputed → file is generator-output → safe to overwrite.

### 5.5 Generator CLI

Operator-facing surface. PowerShell function signature:

```
.\generate.ps1 `
    -TemplatesDir .\templates `
    -MachinesCsv .\samples\machines-100cnc-customer-A.csv `
    -GatewayId GW-CUSTOMER-A-1 `
    -FleetId 100cnc-customer-A `
    -OutputFile .\out\gateway-customer-a-1.json `
    [-Force]
```

Required parameters: `-TemplatesDir`, `-MachinesCsv`, `-GatewayId`, `-FleetId`, `-OutputFile`. Optional: `-Force` (bypasses refuse-overwrite — emits a warning, NOT silent). `-WhatIf` (per PowerShell convention) prints the rendered output to stdout without writing the file.

**Exit codes:** 0 on success, 1 on validation failure, 2 on refuse-overwrite, 3 on I/O error.

**Logging:** to stdout, structured key=value lines (`level=info component=validator csv-rows=100 valid=100`). README documents the lines an operator can grep for.

---

## 6. The `_provisioning` block (restated for visibility)

Per §3.3, every generated output carries this 7-field block at the JSON root. The block is **always present, never optional, never partial**. A generator-produced file with a missing or malformed `_provisioning` block is invalid by definition — the refuse-overwrite check treats it as hand-edited.

The 7 fields and what each carries:

| Field | Type | Value source |
|---|---|---|
| `generatedBy` | string | Constant `"bulk-provision"` for v1. Future generators (e.g., Studio-wizard-export) would stamp a different value. |
| `generatorVersion` | string | The generator's semantic version (e.g., `"1.0.0"`). Bumps when generator behavior changes. |
| `templateId` | string | The template filename minus `.json` (e.g., `"fanuc-standard-v1"`). v1 has one template per protocol; future template variants would use distinct ids. |
| `fleetId` | string | Operator-supplied via `-FleetId` CLI parameter. Identifies the customer / fleet. |
| `generatedAt` | string (ISO 8601 UTC) | Generator capture time. The only time-varying field. |
| `configFingerprint` | string (`"sha256:<hex>"`) | SHA-256 of the canonicalized file content MINUS the `_provisioning` block itself. |
| `csvFingerprint` | string (`"sha256:<hex>"`) | SHA-256 of the input CSV bytes. |

**Schema validation requirement:** `docs/config-schemas/gateway-configuration.schema.json` (read 2026-05-21; current root has no `_provisioning` property) gains an optional `_provisioning` object with each of the 7 fields type-locked. v3 reality-check confirms whether the existing NJsonSchema validator currently treats unknown roots permissively (i.e., would `_provisioning` pass through without explicit schema update?) or rejects them — this is v2 §5.2 Q23, carried into v1 as Q-V1-B.

---

## 7. Deliverables (concrete file list)

Adapted from v2 §3.4.4 deliverables, with v1 path locks:

| File / folder | Status | Notes |
|---|---|---|
| `tools/bulk-provision/templates/template-fanuc-v1.json` | new | ~65 baseline FOCAS2 tag paths; `ProtocolName = "focas2"`; polling 3000 ms. |
| `tools/bulk-provision/templates/template-brother-v1.json` | new | ~75 BrotherTagMap tag paths (tool-inclusive); `ProtocolName = "brother-http"`; polling 3000 ms. Validates against `BrotherHttpSourceConfiguration.FromSourceInstance` shape. |
| `tools/bulk-provision/templates/template-modbus-v1.json` | new | Per-instance Modbus template. Per-tag register definitions out (separate importer). |
| `tools/bulk-provision/generate.ps1` | new | Main CLI per §5.5. ~150-200 lines PS. |
| `tools/bulk-provision/lib/Substitute-Placeholders.ps1` | new | Internal helper (private to `tools/bulk-provision/` per v2.3 §1.1). |
| `tools/bulk-provision/lib/Validate-AgainstSchema.ps1` | new | Internal helper. Wires to NJsonSchema per Q-V1-A resolution. |
| `tools/bulk-provision/lib/Stamp-Provisioning.ps1` | new | Internal helper — computes fingerprints + stamps the `_provisioning` block. |
| `tools/bulk-provision/samples/machines-100cnc-customer-A.csv` | new | 100 rows: 80 Fanuc (192.168.10.x) + 20 Brother (192.168.11.x); see v3 reality-check Q-V1-D for IP-range convention. |
| `tools/bulk-provision/samples/gateway-fanuc-line1.json` | new | Sample generator output for the 27-machine Fanuc Line 1 gateway. Checked in for diffability across future generator versions. |
| `tools/bulk-provision/README.md` | new | Operator workflow, Golden-source-template rule, EREMOS V2 topic shape, Studio `ImportDraftDialog` workflow, Modbus per-tag CSV disambiguation. |
| `tools/bulk-provision/tests/Generator.Tests.ps1` | new | Pester tests — unit + end-to-end. ~25 tests. |
| `tools/bulk-provision/tests/Provisioning-Block.Tests.ps1` | new | Pester tests — `_provisioning` block validity, fingerprint correctness, refuse-overwrite. ~10 tests. |
| `docs/config-schemas/gateway-configuration.schema.json` | edit | Add `_provisioning` as an optional root object with 7 fields type-locked. v3 reality-check (Q-V1-B) confirms whether existing validator behavior already accepts unknown roots. |
| `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` | edit | Mark §11 "Bulk-provision generator + templates committed" row checked. |

Estimate: ~1.5-2 weeks per v2 §3.4.5. Open question Q-V1-C carries the PowerShell version risk that could extend this.

---

## 8. Definition of Done (restated from §3.5)

The 8-item checklist from v2 §3.4.5:

- [ ] Subsystem architecture per §5 implemented (template schema, validation pipeline, rendering pipeline, provenance/identity, generator CLI).
- [ ] End-to-end test: 100-row CSV → generator → `gateway.json` → 100 source instances resolve via `Focas2SourceConfiguration.FromSourceInstance(...)` / `BrotherHttpSourceConfiguration.FromSourceInstance(...)`.
- [ ] Schema-violation test: deliberate invalid input → clear error → non-zero exit → no file written.
- [ ] Refuse-overwrite test: regenerating over a hand-edited file is rejected with a clear error.
- [ ] Provenance/identity test: every generated file has a valid `_provisioning` block with all 7 fields.
- [ ] README walks an operator through the install-day workflow.
- [ ] Sample `gateway-fanuc-line1.json` checked in.
- [ ] Deployment-readiness §11 acceptance signal — "Bulk-provision generator + templates committed" row checked.
- [ ] **NO drift-detection logic added** (scope cap — pause + surface trigger).

Implicit DoD additions (v1 elaboration):

- [ ] Solution-wide test sweep clean: `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky"` still green (Provisioning Subsystem itself is PowerShell, but the end-to-end test loads the generated file into `ConfigurationManager` and that path must remain clean).
- [ ] All v2.3 §1.2 canonical terms used consistently across new artifacts. Anti-synonyms absent.
- [ ] No code added under `src/`. Subsystem lives entirely at `tools/bulk-provision/` + the one `docs/config-schemas/gateway-configuration.schema.json` edit.
- [ ] Pester tests pass on Windows PowerShell 5.1 AND PowerShell 7.x (Q-V1-C resolution determines whether 5.1 is in scope).

---

## 9. Step-by-step implementation sequence

Locked sequence for the implementation session(s). Two sessions are likely: session 1 = steps 1-8 (mechanics), session 2 = steps 9-15 (end-to-end + tests + docs). v3 reality-check may compress.

1. **Reality-check pass (v3)** — read `src/ElpisEdgeConnect.SchemaValidation/` to resolve Q-V1-A (PS interop vs `dotnet run` shell-out for schema validation). Read `docs/config-schemas/gateway-configuration.schema.json` and the canonical parser to resolve Q-V1-B (does it accept unknown roots today?). Confirm Q-V1-C (PowerShell version at customer site).
2. **Project scaffolding** — `tools/bulk-provision/` folder structure created. `.gitignore` carve-out for any generated `out/` folders. No `.csproj` (this is PowerShell, not .NET).
3. **Template authoring (Fanuc)** — `template-fanuc-v1.json` with ~65 baseline tag paths from the FOCAS2 catalog. Validate manually against `Focas2SourceConfiguration.FromSourceInstance` shape — every required field present, types match.
4. **Template authoring (Brother)** — `template-brother-v1.json` with ~75 paths from `BrotherTagMap` (read 2026-05-21). Validate against `BrotherHttpSourceConfiguration.FromSourceInstance` shape (verified at `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpSourceConfiguration.cs:99`).
5. **Template authoring (Modbus)** — `template-modbus-v1.json`. Per-instance only; per-tag CSV importer disambiguation in README.
6. **CLI skeleton** — `generate.ps1` parameter parsing, exit code conventions, structured logging. No business logic yet.
7. **Rendering pipeline** — `Substitute-Placeholders.ps1` helper; CLI threads CSV rows through templates and accumulates `Sources[]`.
8. **Validation pipeline** — `Validate-AgainstSchema.ps1` helper; CSV pre-validation in CLI; NJsonSchema integration per Q-V1-A resolution.
9. **Provenance + Configuration identity** — `Stamp-Provisioning.ps1` helper; canonicalization + SHA-256 + the 7-field stamp. Refuse-overwrite check wired into CLI.
10. **Sample CSV + sample output** — `samples/machines-100cnc-customer-A.csv` (100 rows) + run generator + check in `samples/gateway-fanuc-line1.json`.
11. **Schema update** — add `_provisioning` as an optional root object in `docs/config-schemas/gateway-configuration.schema.json`. Regenerate schema if it's auto-generated (per Q-V1-B v3 resolution).
12. **Pester tests** — `Generator.Tests.ps1` + `Provisioning-Block.Tests.ps1`. ~35 tests across both files.
13. **End-to-end test** — load generated `gateway.json` into `ConfigurationManager.CreateDraftAsync` via a lightweight C# integration test or PS-driven harness; assert 100 sources resolve through `FromSourceInstance` factories without exceptions. Open question Q-V1-E for exact placement of this test.
14. **README authoring** — walk-through, Golden-source-template discipline (copy-paste from deployment-readiness §3.4.1), Modbus per-tag CSV disambiguation, Studio `ImportDraftDialog` workflow, EREMOS V2 topic shape note (`eremos/{gatewayId}/cnc/{sourceId}/{tagName}`), troubleshooting (exit codes, common errors).
15. **Solution-wide sweep + commit** — `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky"` green; Pester suite green; deployment-readiness §11 checkbox flipped; commit + push + PR.

---

## 10. Open questions for v2 ratification + v3 reality-check

### 10.1 Carried verbatim from v2 §5.2

| # | Item | Question (LOCKED to carry into v3 reality-check) |
|---|---|---|
| Q21 | Chip 3 | Does `src/ElpisEdgeConnect.SchemaValidation/` already exist with a callable surface? Reality-check determines whether PS interop is feasible or we shell out. |
| Q22 | Chip 3 Studio integration | Verify Studio's M.2a Config page actually has an "Import draft from JSON" button today. Reality-check the workflow. |
| Q23 | Chip 3 schema | Add `_provisioning` to `docs/config-schemas/gateway-configuration.schema.json` — verify the canonical parser ignores unknown roots (it should, but confirm). |

**v1 partial answers (pre-v3 reality-check):**

- **Q21:** `src/ElpisEdgeConnect.SchemaValidation/` confirmed to exist with `NJsonSchemaConfigurationValidator` exposing a callable `ValidateAsync(string json, CancellationToken)` (verified 2026-05-21). v3 still resolves the PS-interop-vs-shell-out call.
- **Q22:** `src/ElpisEdgeConnect.Management/Components/Pages/ImportDraftDialog.razor` confirmed to exist (verified 2026-05-21). v3 confirms the operator UX walkthrough.
- **Q23:** Schema reviewed (2026-05-21); root has no `_provisioning` property today. v3 confirms whether NJsonSchema's default behavior accepts unknown roots or rejects them (the canonical schema doesn't currently use `additionalProperties: false` at root, suggesting permissive — but confirm).

### 10.2 New v1-specific open questions

| # | Area | Question |
|---|---|---|
| Q-V1-A | Generator + schema validation wiring | Can PowerShell call NJsonSchema directly via `Add-Type` / `Import-Module` against the compiled `ElpisEdgeConnect.SchemaValidation.dll`, or do we shell out via `dotnet run --project src/ElpisEdgeConnect.SchemaValidation`? Direct interop is faster + simpler operator UX (no .NET SDK required at customer site if only validation runs); shell-out is simpler to wire (no PS interop quirks). v3 reality-check decides. |
| Q-V1-B | Schema permissiveness | Does the canonical parser (`NJsonSchemaConfigurationValidator`) currently accept unknown root properties like `_provisioning`? If yes, the schema update is documentation-only (we add the optional `_provisioning` root for tooling clarity but no runtime behavior changes). If no, the schema update is load-bearing and must land before any generated file flows through `ConfigurationManager.CreateDraftAsync`. **v3 reality-check resolves; impacts step 11 sequencing.** |
| Q-V1-C | PowerShell version at customer site | What PowerShell version is the customer site running? Windows PowerShell 5.1 (Windows Server 2016/2019 default) and PowerShell 7.x differ on `Add-Type` semantics, JSON serialization defaults, and SHA-256 cmdlet shape. v1 assumes 5.1+ as the floor (matches the deployment-readiness §8.5 nightly-backup script's pattern). v3 confirms with customer engineering; if 5.1 only, the test matrix doubles. |
| Q-V1-D | CSV IP-range convention | The sample CSV's 100 rows need realistic-looking IPs. v1 uses 192.168.10.x for Fanuc + 192.168.11.x for Brother. Does the customer's actual flat-network topology use a different range? Sample CSV is illustrative, but the customer's real CSV will replace it — confirm at install time. Out-of-scope for v1; flag for the install playbook. |
| Q-V1-E | End-to-end test placement | The step 13 end-to-end test that asserts "100 source instances resolve via `FromSourceInstance(...)` without exceptions" — where does it live? Three options: (a) a new C# integration test under `tests/ElpisEdgeConnect.Integration.Tests/ProvisioningSubsystemTests.cs` invoking `pwsh.exe` to run the generator (heaviest, most realistic); (b) a Pester test that calls `dotnet test` against a tiny helper; (c) a PowerShell-only test that parses the generated JSON manually and validates structure (lightest, lowest fidelity). v3 reality-check picks. Recommendation: (a) for production-fidelity, but only if `pwsh.exe` invocation from xUnit is reliable on the CI runner. |
| Q-V1-F | Fail-loud on license-limit overflow | If a CSV has more rows than the gateway's license module allows (e.g., a license caps source-focas2 at 50 instances and the CSV has 80 Fanuc rows), should the generator fail-loud at generation time or let `ConfigurationManager.CreateDraftAsync` catch it later? v1 recommendation: generator does NOT check license limits (license enforcement is the runtime's job per Locked Decision #5/#6/#7); generator just produces what the CSV says. README documents the customer-facing flow: apply → see license violation in Studio → adjust either license or CSV. v3 confirms this stance. |
| Q-V1-G | Template versioning | When `template-fanuc-v1.json` ships and later needs to evolve (new tag path added, polling default changed), is the new file named `template-fanuc-v2.json` (preserving v1 for already-deployed fleets) or does `template-fanuc-v1.json` mutate in place (breaking determinism for re-runs against the old version)? v1 recommendation: append-only versioning — new file, new `templateId` value, v1 file frozen. v3 confirms; informs README's "future template updates" section. |
| Q-V1-H | Sink + route templating | Templates contain `Gateway`, `Sources`, `Sinks`, `Routes` blocks (per §5.1). For v1, sinks and routes are minimal hardcoded blocks (one MQTT sink at `localhost:1883`, one route per protocol). Should they be operator-configurable via the CSV (extra columns) or via a separate companion `sinks-and-routes.json` per gateway? v1 recommendation: hardcoded minimal blocks; operator edits the generated file's sink connection settings as the **one** documented exception to the no-hand-edit rule (because broker host/port is genuinely per-deployment), and the refuse-overwrite check warns rather than blocks for sink-only diffs. **This is a meaningful escape-hatch decision — flag prominently for ChatGPT review.** Open for v2 ratification. |

### 10.3 Reality-check items that may materially affect v2 locks

These are the open questions whose resolution could force a v2 amendment. Calling out explicitly so the v2 ratification pass knows where the risk lives:

- **Q-V1-H** is the highest-risk: if hand-editing sink connection settings is forbidden, operators must run the generator EVERY time the broker host changes — operationally heavy. If hand-editing is permitted, the refuse-overwrite check needs nuance (sink-only diffs OK, source diffs blocked). The cleanest answer might be a **separate `sinks-and-routes-overlay.json`** alongside `gateway.json`, generated independently, never touched by the source-template flow. That would be a v2 amendment, not a v1 elaboration.
- **Q-V1-B** could force step 11's schema update to be load-bearing rather than documentation-only. Doesn't change v2 scope, but changes implementation sequencing.
- **Q-V1-F** (license-limit fail-loud) — if the customer's license caps differ from the 100-machine assumption, the generator's silence-by-design might surprise operators. v3 reality-check + README documentation is the mitigation; not a v2 lock change.

---

## 11. Cross-references

### Project governance + architectural locks

- `CLAUDE.md` §3 — Architectural locks (Locked Decision #1 protocol-agnostic core, #2 canonical data model, #5/#6/#7 three-layer licensing + offline RSA-signed + expiration behavior).
- `CLAUDE.md` §7 — Working conventions (code style, doc, testing, commit cadence).
- `docs/ARCHITECTURE_BLUEPRINT.md` Section 8 — Configuration system; the `SourceInstanceConfig` shape the templates target.
- `docs/ARCHITECTURE_BLUEPRINT.md` Appendix A — Locked decisions referenced from §2 above.
- `docs/platform-principles.md` P6 — "Operational product, not developer tool." The Provisioning Subsystem is operator-facing infrastructure, not a developer convenience.

### Roadmap predecessor docs (LOCKED scope inputs)

- [v2 roadmap (LOCKED)](2026-05-21-phase2-wrapup-roadmap-v2.md) §3.4 — Subsystem architecture, locked decisions Q1-Q8, `_provisioning` block schema, deliverables, DoD.
- [v2.1 roadmap](2026-05-21-phase2-wrapup-roadmap-v2.1.md) — Runtime-observability boundary (touches M.2c, not Chip 3).
- [v2.2 roadmap](2026-05-21-phase2-wrapup-roadmap-v2.2.md) §1.1 — Principle-escalation threshold (governs whether any Chip 3 invariant ever gets promoted into `docs/platform-principles.md`; verdict for v1: stays in this plan).
- [v2.3 roadmap (LOCKED)](2026-05-21-phase2-wrapup-roadmap-v2.3.md) §1.1 — No-new-shared-abstractions rule (binding); §1.2 — Terminology freeze (binding); §1.3 — Platform-contracts deferred follow-up (binding).

### Sibling plan-trail style references

- [M.P2.4 v1 plan](2026-05-20-mp24-brother-http-plan.md) — structural style template for this v1 (sections, tone, open-question framing).
- [M.P2.4 handoff](2026-05-21-mp24-handoff.md) — completed Brother HTTP migration; this Provisioning Subsystem renders templates that consume `BrotherHttpSourceConfiguration.FromSourceInstance`.

### Deployment context

- [100-CNC deployment readiness](2026-05-20-100-cnc-deployment-readiness.md) §3 (bulk source provisioning strategy — Option A lock), §3.4.1 (Golden-source-template discipline — copy-paste source for the README), §6 (critical path placement), §7 (locked customer answers: 3000 ms polling, ~65-75 tags, 80/20 split), §11 (acceptance signal — "Bulk-provision generator + templates committed" row).

### Source code touchpoints (verified 2026-05-21)

- `src/ElpisEdgeConnect.Sources.Focas2/Focas2SourceConfiguration.cs` — `FromSourceInstance` factory shape the Fanuc template renders into.
- `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpSourceConfiguration.cs:99` — `FromSourceInstance` factory shape the Brother template renders into.
- `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherTagMap.cs` — canonical Brother tag catalog; Brother template paths must be a subset.
- `src/ElpisEdgeConnect.SchemaValidation/NJsonSchemaConfigurationValidator.cs` — schema validator surface for Q-V1-A wiring decision.
- `src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs:47,54,65` — `SourceFocas2`, `SourceBrotherHttp`, `SourceModbusTcp` keys referenced by template `ProtocolName` values.
- `docs/config-schemas/gateway-configuration.schema.json` — canonical schema receiving the `_provisioning` optional root.
- `src/ElpisEdgeConnect.Management/Components/Pages/ImportDraftDialog.razor` — Studio integration surface for Q22 verification.

---

**End of v1 draft.** Awaiting ChatGPT review pass. Eight new v1-specific open questions (Q-V1-A through Q-V1-H) plus the three carried-from-v2 questions (Q21/Q22/Q23) need verdicts before v2 of this plan trail locks. Implementation does NOT start until both the v2 plan trail of this milestone AND the parallel v1 plan trails for the other Phase 2 wrap-up items are ratified per v2.3 §3.
