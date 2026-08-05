# Chip 3 Implementation — Session 2 plan (v2)

**Date:** 2026-06-13
**Author:** Claude (post-ChatGPT review pass)
**Status:** v2 — pending v3 reality-check
**Predecessor:** `docs/sessions/2026-06-13-chip3-impl-session2-plan-v1.md`
**Review notes incorporated:** ChatGPT review verdict "approve direction, request v2 with scope/order fixes" — see [PR #147](https://github.com/elpisitsolutions/EdgeConnect/pull/147) thread.
**Parent plan:** `docs/sessions/2026-05-21-chip3-provisioning-subsystem-plan-v2.md` §9

---

## 0. Review decisions incorporated in v2

| Decision | Resolution |
|---|---|
| Session 2 starts from `master` after #146 merge | ✅ #146 merged (`0bed6b7`). Session 2 implementation branch will be cut from updated `master`. |
| Session 1 handoff doc | **Promoted to pre-flight** (committed to `master` BEFORE the session 2 implementation PR opens). Not part of session 2's exit gate. |
| Pester v5 enforcement | Tests launched via `tools/bulk-provision/tests/Invoke-Tests.ps1`, which checks `Get-Module Pester -ListAvailable` for v5+ and fails with `Install-Module Pester -MinimumVersion 5.0` guidance if missing. |
| Expected-output fixtures location | `tools/bulk-provision/tests/fixtures/expected/` (NOT `samples/`). README points operators to one example. |
| Deterministic regression contract | Whole output tree (file count + relative paths + bytes per file), not single JSON. |
| `run-summary.json` portability | **New scope item** — generator currently emits absolute machine paths in `run-summary.json.{csv,sidecar}`. Fixed in session 2 to relative-to-OutDir, otherwise deterministic-tree contract fails on different boxes. |
| Unpinned-generatedAt test framing | Pin `GatewayProvisioningId`, omit `GeneratedAt`, parse both outputs, replace `_provisioning.generatedAt` with sentinel, canonicalize, assert equality. |
| Sidecar schema location | `tools/bulk-provision/sidecar-schema.json`. NOT in `docs/config-schemas/`. |
| `-RegenerateFixtures` flag in `generate.ps1` | **Will NOT ship in session 2.** If needed later, lives in `tools/bulk-provision/tests/Update-ExpectedFixtures.ps1` (dev/test helper, not operator-facing). |
| README scope | Operator-only. Architecture notes stay in `MANIFEST.md` / ADR-0030 / session docs. |
| Error-assertion strategy | Substring-on-stable-tokens. Concrete: prepend stable error codes (e.g. `BulkProvision.PlaceholderScopeViolation: …`) to existing `throw` strings; Pester asserts `$_.Exception.Message -match 'BulkProvision\.PlaceholderScopeViolation'` plus `Sources[]` / `deviceId` / marker name. NO exact full-message assertions. |
| Sidecar error wrapping | Raw NJsonSchema diagnostics wrapped into `Sidecar validation failed: <field> <reason>` operator-friendly messages. Raw diagnostics stay in a `-Verbose` output channel for debugging. |
| `§C may spill` contradiction | Resolved: sidecar schema is **required for session 2 exit**. Scope renegotiation is a deliberate user decision, not a pre-authorized spill. |

---

## 1. Pre-flight gates before session 2 implementation

These items land on `master` BEFORE the session 2 implementation branch is cut. None of them belong inside session 2's PR.

- [x] PR #146 merged (`0bed6b7` on `master`).
- [ ] **Session 1 handoff doc** at `docs/sessions/2026-06-13-chip3-impl-session1-handoff.md` — captures: what shipped on #146, pwsh-7 prerequisite, sample fixtures location, known carry-forwards. Committed to `master` via its own small PR. Per standing rule `feedback_handoff_branch_dependencies.md`.
- [ ] Session 2 implementation branch (`claude/chip3-impl-session2`) cut from updated `master` AFTER both above.

---

## 2. Session 2 scope (in implementation order)

Order revised per ChatGPT §5 to put fixtures before the harness that needs them.

### A. Deterministic Fanuc fixture

1. From `master` post-handoff-doc, run on pwsh 7:
   ```pwsh
   ./tools/bulk-provision/generate.ps1 `
       -Csv samples/sample-fanuc.csv `
       -Sidecar samples/sample-fanuc.gateway.yml `
       -Template template-fanuc `
       -OutDir out/expected-fanuc `
       -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 `
       -GeneratedAt 2026-01-01T00:00:00Z
   ```
2. Run a SECOND pinned generation into `out/expected-fanuc-2`. `fc /b` (or `cmp -r`) the two trees byte-for-byte to confirm the deterministic contract holds locally.
3. Commit one tree as `tools/bulk-provision/tests/fixtures/expected/fanuc/` — preserving the relative layout: `cnc-001.gateway.json`, `cnc-002.gateway.json`, `cnc-003.gateway.json`, `run-summary.json`, `MANIFEST.txt`. The whole tree is the regression baseline.

**Done when:** committed tree under `tests/fixtures/expected/fanuc/` matches a fresh pinned run byte-for-byte across all files (including `run-summary.json` and `MANIFEST.txt`).

**Hard dependency:** §B0 below (run-summary portability fix) must land first, else the absolute paths in `run-summary.json` break the contract on the next reviewer's box.

### B. Generator hardening for deterministic-tree contract

#### B0. `run-summary.json` machine-path fix (NEW — found during review)

`generate.ps1` currently writes:
```ps1
$summary = [ordered]@{
    ...
    csv     = $csvAbs       # absolute path — breaks portability
    sidecar = $sidecarAbs   # absolute path — breaks portability
    ...
}
```

Replace with relative paths computed against the CSV's parent directory (or `$PSScriptRoot` for stability):
```ps1
csv     = (Resolve-Path -LiteralPath $Csv -Relative)
sidecar = (Resolve-Path -LiteralPath $Sidecar -Relative)
```

This is a 4-line change but it's a deterministic-contract-blocker.

**Done when:** the same `-Csv ./samples/sample-fanuc.csv` produces `run-summary.json.csv = "./samples/sample-fanuc.csv"` on any pwsh-7 host, not `C:\dev\EdgeConnect\…\sample-fanuc.csv`.

#### B1. Stable error codes prepended to substitution throws

`Substitute-Placeholders.ps1` currently throws raw strings. Add a code prefix per error path:

| Throw site | Stable code |
|---|---|
| Per-row placeholder outside `Sources[]` | `BulkProvision.PlaceholderScopeViolation` |
| Per-gateway placeholder inside `Sources[]` | `BulkProvision.PlaceholderScopeViolation` |
| Unresolved marker after substitution | `BulkProvision.UnresolvedPlaceholder` |
| Result not valid JSON | `BulkProvision.RenderedJsonInvalid` |
| Placeholder dict missing/extra keys | `BulkProvision.PlaceholderRegistryMismatch` |
| Template `_provisioning` marker absent / duplicated | `BulkProvision.ProvisioningMarkerShapeMismatch` |

`generate.ps1` gets the same treatment for its own throws:

| Throw site | Stable code |
|---|---|
| CSV missing required column | `BulkProvision.CsvMissingColumn` |
| CSV duplicate deviceId | `BulkProvision.CsvDuplicateDeviceId` |
| CSV empty / header-only | `BulkProvision.CsvEmpty` |
| Sidecar not `.json/.yml/.yaml` | `BulkProvision.SidecarFormatUnsupported` |
| Sidecar YAML malformed line | `BulkProvision.SidecarYamlMalformed` |
| Sidecar schema validation failure | `BulkProvision.SidecarSchemaViolation` |
| Schema-validate-stage failure | `BulkProvision.ConfigSchemaViolation` |

Pester asserts on the code prefix + 1-2 substring tokens; never on the full message.

### C. Missing sample fixtures

`tools/bulk-provision/samples/sample-brother.{csv,gateway.yml}` and `sample-modbus.{csv,gateway.yml}` — needed by the all-template round-trip tests in D.

Each fixture mirrors the fanuc shape:
- 3 rows minimum (mixed enabled true/false).
- Sidecars use the same `gatewayProvisioningId` shape as fanuc for cross-template determinism testing later.

**Done when:** both new sample pairs commit; `generate.ps1` happy-path runs against each.

### D. Pester test harness (`tools/bulk-provision/tests/`)

Test files mapped to assertions:

#### D1. `Invoke-Tests.ps1` (test runner wrapper)

Checks Pester v5+ is installed:
```pwsh
$pester = Get-Module Pester -ListAvailable | Where-Object Version -ge 5.0.0 | Select-Object -First 1
if (-not $pester) { throw "Pester v5+ required. Run: Install-Module Pester -MinimumVersion 5.0 -Force -Scope CurrentUser" }
```
Then invokes `Invoke-Pester` against the `tests/` directory with structured output config (`PassThru`, `Output.Verbosity`, `Run.Path`) per Pester v5 conventions.

#### D2. `Substitute-Placeholders.Tests.ps1`

NEGATIVE PATHS (per v1):
- Per-row placeholder OUTSIDE Sources[] → throws with `BulkProvision.PlaceholderScopeViolation` + marker name + `Sources[]` substring.
- Per-gateway placeholder INSIDE Sources[] → throws with same code + marker name + `Sources[]` substring.
- Unresolved marker → throws with `BulkProvision.UnresolvedPlaceholder` + marker name.
- Result not JSON → throws with `BulkProvision.RenderedJsonInvalid` + deviceId substring.
- Missing/extra placeholder dict keys → throws with `BulkProvision.PlaceholderRegistryMismatch`.

POSITIVE PATHS (NEW — added per ChatGPT §6):
- Per-row placeholder INSIDE Sources[] + per-gateway placeholder OUTSIDE Sources[] → succeeds, returns JSON-parseable string with all markers resolved.
- Empty per-row dict still passes if template has no per-row markers (forward-compat: future template might use only per-gateway).

#### D3. `Canonicalize-Json.Tests.ps1`

- UTF-8 no BOM verified by reading raw bytes — first 3 bytes are NOT `EF BB BF`.
- LF line endings — output contains `0x0A` but no `0x0D 0x0A`.
- Root keys in locked order — `_provisioning` first, then canonical roots, then unknown `_*`, then unknown non-`_`.
- Nested object keys alphabetically sorted at every depth.
- Single trailing LF (no double-blank EOF).
- **Array order is preserved** (NEW per ChatGPT §6) — input `["c","a","b"]` round-trips as `["c","a","b"]`, NOT `["a","b","c"]`. The locked sort is for object keys only; arrays are positional.

#### D4. `Generate.Tests.ps1`

- Duplicate deviceId → throws `BulkProvision.CsvDuplicateDeviceId` + the duplicated id.
- Missing required column → throws `BulkProvision.CsvMissingColumn` + column name.
- Empty CSV → throws `BulkProvision.CsvEmpty`.
- Happy-path against `sample-fanuc` produces 3 output files + run-summary.json + MANIFEST.txt; each output has a 9-field `_provisioning` block.

#### D5. `Deterministic.Tests.ps1`

PINNED-BOTH:
- Two pinned runs against `sample-fanuc` produce byte-identical output trees (every file, including `run-summary.json` and `MANIFEST.txt`).
- Asserts against the frozen `tests/fixtures/expected/fanuc/` tree.

PINNED-ID-ONLY (per ChatGPT §4):
```pwsh
# Pin GatewayProvisioningId. Omit GeneratedAt.
# Run twice. Parse both outputs. Replace _provisioning.generatedAt with "<<sentinel>>".
# Canonicalize both. Assert equality.
```

#### D6. `RoundTripValidate.Tests.ps1`

For each of `template-fanuc`, `template-brother`, `template-modbus`:
- Run generator against the matching sample fixture.
- Pipe every output `.gateway.json` through `tools/ValidateConfig`.
- Assert exit code 0.

Hard dependency on §C (sample fixtures committed).

#### D7. `Sidecar.Tests.ps1`

(Lives with the harness but depends on §E shipping the schema first.)

- Well-formed sidecar passes.
- Sidecar missing required field → throws `BulkProvision.SidecarSchemaViolation` + missing field name.
- Sidecar with extra unknown field (NOT `_`-prefixed) → throws (because `additionalProperties: false`).
- Sidecar with wrong type on `mqttPort` (string instead of int) → throws with the operator-friendly wrapped message, NOT raw NJsonSchema diagnostics.

### E. Sidecar JSON schema (`tools/bulk-provision/sidecar-schema.json`)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "Bulk-provision sidecar",
  "type": "object",
  "additionalProperties": false,
  "required": [
    "gatewayId", "gatewayName", "gatewayProvisioningId",
    "fleetId", "site",
    "mqttHost", "mqttPort", "mqttQos", "mqttClientIdPrefix"
  ],
  "properties": {
    "gatewayId":             { "type": "string", "format": "uuid" },
    "gatewayName":           { "type": "string", "minLength": 1 },
    "gatewayProvisioningId": { "type": "string", "format": "uuid" },
    "fleetId":               { "type": "string", "minLength": 1 },
    "site":                  { "type": "string", "minLength": 1 },
    "mqttHost":              { "type": "string", "minLength": 1 },
    "mqttPort":              { "type": "integer", "minimum": 1, "maximum": 65535 },
    "mqttQos":               { "type": "integer", "minimum": 0, "maximum": 2 },
    "mqttClientIdPrefix":    { "type": "string", "minLength": 1, "pattern": "^[A-Za-z0-9_-]+$" }
  }
}
```

Wiring in `generate.ps1`:
1. Parse sidecar (YAML or JSON) into an object.
2. Serialize the object to a temp JSON string.
3. Hand to `NJsonSchema` (via a slim helper that wraps the SchemaValidation module call, or by re-invoking `tools/ValidateConfig` with a `-Schema` flag — TBD in implementation).
4. On failure, project NJsonSchema's `path` + `kind` into an operator-friendly `Sidecar validation failed: <field> <reason>` and throw `BulkProvision.SidecarSchemaViolation`. Raw NJsonSchema text is surfaced via `Write-Verbose`.

ChatGPT §8 wording adopted verbatim: "Parse YAML sidecar into an object, then validate that object against sidecar-schema.json." Documented in the README.

### F. Operator README (`tools/bulk-provision/README.md`)

Operator-only. Sections:
1. What this tool does (one paragraph).
2. Prerequisites — pwsh 7+ (with check-command), .NET 8 SDK.
3. Quickstart — 3-command example with `sample-fanuc`.
4. CSV column reference + sidecar field reference (LINKS to `templates/MANIFEST.md`).
5. Troubleshooting:
   - `BulkProvision.PlaceholderScopeViolation` — what it means + fix.
   - `BulkProvision.CsvDuplicateDeviceId` — what it means + fix.
   - `BulkProvision.SidecarSchemaViolation` — what it means + fix.
   - Suspect-roots warning interpretation (from ValidateConfig).
6. Deterministic-output guarantee — operator-facing summary, links out to MANIFEST + plan docs for architecture.

NO architecture content.

---

## 3. Out of scope for session 2 — confirmed deferred to session 3

(Unchanged from v1 §3.)

### G. Modbus tag-CSV importer hook
### H. Generated-config diff viewer (UI surface — static HTML mockup first)

---

## 4. Open questions for v3 reality-check pass

(v1 had 6; all 6 resolved. v2 surfaces these new ones for the v3 pass.)

1. **NJsonSchema reuse in PowerShell.** v2 §E says "via a slim helper or by re-invoking ValidateConfig with a -Schema flag — TBD in implementation." v3 reality-check should pick one before implementation starts. Options:
   - (a) Add a sidecar-validate switch to `tools/ValidateConfig` so generate.ps1 shells out twice (once for sidecar, once for output). Pro: single .NET-side validator. Con: more process spawns per run.
   - (b) Write a tiny `tools/ValidateSidecar/` CLI mirroring ValidateConfig. Pro: clear separation. Con: two CLIs in `tools/`.
   - (c) PS-side schema validation via `Add-Type` on `NJsonSchema.dll` from the existing build output. Pro: no extra CLI. Con: fragile dependency on build-output paths.
   - **Recommendation:** (a) — extend ValidateConfig with `-Schema <path>` accepting any schema. Reuses the wrapping logic for operator-friendly errors.

2. **`Resolve-Path -Relative` base directory.** For `run-summary.json.{csv,sidecar}` portability, relative to what? Two choices:
   - (i) `$OutDir` parent — implies operator must keep CSV near OutDir.
   - (ii) `$PWD` at invocation time — implies run-summary is reproducible iff the operator runs from the repo root.
   - **Recommendation:** (i) with a documented assumption "the CSV path is computed relative to OutDir's parent." Then the determinism is "same relative layout → same run-summary." v3 pass should pressure-test this.

3. **Fixture refresh discipline.** v2 §A says manual refresh. When `template-fanuc-v1.json` gets a content tweak in a later session, what's the process? Implicit answer: hand-regen + commit fixture diff in same PR + review the diff. v3 should write this down as a discipline note OR add a forward-compat `tests/Update-ExpectedFixtures.ps1` helper now.

4. **Brother / Modbus deterministic fixtures.** v2 §A only freezes the fanuc fixture. Should v2 also freeze brother + modbus expected trees, OR is round-trip-validate (D6) enough coverage for those? v3 pass should pick.

---

## 5. Rough size estimate (revised)

| Item | LOC | Tests | Notes |
|------|-----|-------|-------|
| Pre-flight: session 1 handoff doc | ~80 md | 0 | small PR ahead of session 2 |
| A — deterministic Fanuc fixture (freeze) | 0 | 0 | manual run + tree commit |
| B0 — run-summary path fix | ~5 PS | 0 | trivial |
| B1 — error code prefixes | ~30 PS | 0 | search-replace through 2 files |
| C — brother + modbus sample fixtures | ~60 bytes | 0 | trivial |
| D1-D7 — Pester harness | ~350 PS | ~30 tests | bulk of session |
| E — sidecar schema + wiring | ~100 PS + schema JSON | (3-4 in D7) | depends on Q1 above |
| F — operator README | ~180 md | 0 | |

**Total estimate (still session 2):** ~1.5 sessions if Q1 picks option (a), ~2 if option (c) (more fragile, more debugging).

---

## 6. Exit gate for session 2

Session 2 implementation PR is complete when ALL of:

- [x] Pre-flight gate clean (session 1 PR merged, handoff doc on master).
- [ ] `tools/bulk-provision/tests/fixtures/expected/fanuc/` committed, regenerated cleanly via pinned-both rerun.
- [ ] `Invoke-Tests.ps1` enforces Pester v5+ and runs all `*.Tests.ps1` green from a clean checkout.
- [ ] Round-trip validate (D6) green across all three templates (fanuc + brother + modbus).
- [ ] Sidecar schema rejects malformed sidecars with operator-friendly messages; D7 asserts.
- [ ] `run-summary.json` paths are portable (no absolute machine paths).
- [ ] All throws carry stable `BulkProvision.*` error codes; tests assert on codes + tokens, never full messages.
- [ ] Operator README linked from top-level repo README.
- [ ] Session 2 implementation PR opened against master.

---

## 7. Process (cadence position)

1. ✅ **v1** — drafted, on PR #147.
2. ✅ **ChatGPT review** — verdict "approve direction, request v2 with fixes."
3. ✅ **v2 (this doc)** — drafted, committed to same branch + PR #147.
4. ⏳ **v3 reality-check** — Claude scans v2 against current repo state, resolves §4 questions, locks open items.
5. ⏳ **Pre-flight** — session 1 handoff doc lands on master.
6. ⏳ **Implementation** — session 2 branch cut from updated master.
