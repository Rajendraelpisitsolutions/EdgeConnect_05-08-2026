# Chip 3 Implementation — Session 2.5 plan (v1)

**Date:** 2026-06-14
**Author:** Claude (post-session-2-close, opening session 2.5)
**Status:** v1 DRAFT — pending ChatGPT review pass per `feedback_planning_cadence.md`
**Locked source for scope:** `docs/sessions/2026-06-13-chip3-impl-session2-plan-v4-lock-final.md` §5 (session 2.5 exit gate) and §2 (ValidateSidecar CLI contract)
**Parent context:** Session 2 closed via PR #150 (commit `8e86b5e` on master). All 14 session 2 exit-gate items shipped.

---

## 0. Why this scope is small

Session 2.5 is deliberately narrow because v4 split it out from session 2. The whole session ships one new .NET CLI, one schema file, one PowerShell wiring update, and one Pester test pass. v4 §2 already locked the CLI contract verbatim; v4 §5 already enumerates the 11-item exit gate. v1's job is to figure out the implementation order, surface open questions, and hand off to the ChatGPT review pass.

If v4 said it, v1 just locks it. Where v4 left room (YAML parser detail, error projection format), v1 makes a recommendation and flags the call for review.

---

## 1. Direct inputs from v4 (carry verbatim, do not relitigate)

### CLI contract (v4 §2)

```text
ValidateSidecar
    --schema  <path-to-sidecar-schema.json>      [required]
    --sidecar <path-to-sidecar.{yml,yaml,json}>  [required]
    --verbose                                    [optional]

exit 0 = sidecar well-formed AND validates against schema
exit 1 = schema-validation failure
exit 2 = file not found or unreadable
exit 3 = sidecar parse failure (malformed YAML / JSON)
exit 4 = unexpected internal error

stderr: <field-path-or-root>: <reason>, operator-friendly wrapped
raw NJsonSchema diagnostics ONLY when --verbose

constraints:
    NO ProjectReference to ElpisEdgeConnect.Core
    NO use of GatewayConfiguration type
    NO ADR-0030 suspect-roots logic
    YAML sidecar parsed BEFORE schema validation
    YAML → object → JSON-serialize → schema-validate pipeline
```

### Schema-feature allowlist (v4 §2)

```text
type, required, properties, additionalProperties: false,
enum, pattern, format (only "uuid"), minimum, maximum, minLength
```

Anything outside requires a per-feature local proof (small NJsonSchema test) before going in. v1 does NOT propose adding anything outside the allowlist.

### Sidecar schema content (v4 §2 example, locked here)

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

### YAML parser (v4 §2, provisional, may revisit)

**v4 lock:** YamlDotNet (de-facto .NET standard, MIT). v1 confirms — see §3 Q1 for the rationale and the review pass for confirmation.

---

## 2. Session 2.5 scope (implementation order)

### A. ValidateSidecar CLI

| File | Purpose |
|---|---|
| `tools/ValidateSidecar/ValidateSidecar.csproj` | net8.0 Exe, NuGet refs to `NJsonSchema` and `YamlDotNet`, `TreatWarningsAsErrors=true`, `Nullable=enable`, no `ProjectReference` |
| `tools/ValidateSidecar/Program.cs` | ~120 LOC, mirrors the `tools/ValidateConfig/Program.cs` shape: args parse → file checks → YAML parse → JSON serialize → schema load → validate → projected stderr → exit code |

**Done when:** `dotnet build tools/ValidateSidecar/ValidateSidecar.csproj` clean; `dotnet run --project tools/ValidateSidecar -- --schema X --sidecar Y` exits 0 on a valid pair and non-zero on each failure category.

### B. Sidecar schema

`tools/bulk-provision/sidecar-schema.json` per §1 above. Committed alongside the templates.

### C. generate.ps1 validation hook

Insert a sidecar-validate step at the top of `Invoke-BulkProvision`, BEFORE substitution:

```pwsh
# Per v4 §6 step E: validate the sidecar against the schema BEFORE any
# substitution. Failure aborts with the wrapped operator-friendly error.
if (-not $SkipValidate) {
    $sidecarValidator = (Resolve-Path "$PSScriptRoot/../ValidateSidecar/ValidateSidecar.csproj").Path
    $schemaPath       = (Resolve-Path "$PSScriptRoot/sidecar-schema.json").Path
    $result = & dotnet run --project $sidecarValidator -- `
        --schema $schemaPath --sidecar $sidecarAbs 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host $result
        throw "$($script:ErrCodes.SidecarSchemaViolation): sidecar validation failed (exit $LASTEXITCODE). Review stderr above."
    }
}
```

Adds one new error code to `generate.ps1`'s `$script:ErrCodes` table:
```text
SidecarSchemaViolation = 'BulkProvision.SidecarSchemaViolation'
```

**Done when:** `pwsh ./generate.ps1 -Csv ... -Sidecar broken.yml -Template ...` throws `BulkProvision.SidecarSchemaViolation` with the wrapped reason. The existing happy-path freeze still produces byte-identical output (the validate stage is read-only).

### D. Sidecar.Tests.ps1 — flesh out the 6 stubs

Replace each `It "..." -Pending {}` with a real assertion. All tests use the existing `sample-fanuc.gateway.yml` as the well-formed base + mutate it for negative cases.

| Test | Setup | Assert |
|---|---|---|
| validates a well-formed sidecar | use `sample-fanuc.gateway.yml` as-is | ValidateSidecar exit 0 |
| missing required field | drop `gatewayId` from a temp copy | exit 1, stderr contains `gatewayId` |
| extra unknown field (additionalProperties:false) | add `extraThing: x` | exit 1, stderr contains `extraThing` |
| wrong type on `mqttPort` | set to a quoted string | exit 1, stderr contains `mqttPort` |
| invalid pattern on `mqttClientIdPrefix` | set to `bad spaces here` | exit 1, stderr contains `mqttClientIdPrefix` |
| wraps raw NJsonSchema diagnostics | run twice (default + `--verbose`); default output should NOT contain NJsonSchema-internal phrasing (e.g., `KnownProperty`, `IntegerExpected` raw verbiage), `--verbose` output should | per-output regex check |

**Done when:** `pwsh ./tools/bulk-provision/tests/Invoke-Tests.ps1` shows 35 passing, 0 pending.

### E. README troubleshooting addendum

Add a `BulkProvision.SidecarSchemaViolation` section to the operator README's Troubleshooting list. Two-paragraph treatment per the existing pattern: what triggers it, how to fix.

### F. Solution-file update

`ElpisEdgeConnect.sln` adds `tools/ValidateSidecar/ValidateSidecar.csproj` so the whole-solution build picks it up.

### G. ADR consideration — YAML parser choice

v4 §2 said "may revisit at session 2.5 kickoff if it warrants an ADR." v1's recommendation is that it does NOT warrant one: YamlDotNet is a routine MIT NuGet dependency in the .NET ecosystem, and the alternative (hand-rolled flat-keys parser already in `generate.ps1`'s `ConvertFrom-Yaml-Minimal`) would commit the sidecar to "stays one level deep forever," which is a quiet design lock that we'd then have to undo later. No ADR; document the choice inline in `ValidateSidecar.csproj`'s XML header.

If ChatGPT review surfaces a real concern (security posture, transitive deps), promote to an ADR then.

---

## 3. Open questions for ChatGPT review

### Q1. YAML parser confirmation

**Recommendation:** YamlDotNet, no ADR, document choice inline.
**Alternative:** hand-rolled flat parser (extending the one in `generate.ps1`).
**Why ask:** v4 left this provisional. The trade-off is dependency surface vs forward-flexibility. YamlDotNet has had several CVEs in older versions; v1 should pin a known-good version (recommend latest stable, currently 16.x). Worth a second pair of eyes on the version pin + transitive deps.

### Q2. Error projection format — exact wording

v4 §2 says "operator-friendly wrapped" but doesn't lock the exact format. v1 proposal:

```text
Sidecar validation failed:
  <field-path>: <human-readable reason>
  <field-path>: <human-readable reason>
  ...
```

Where `<field-path>` is NJsonSchema's `Path` (e.g., `#/mqttPort`) projected to a dotted form (`mqttPort`), and `<human-readable reason>` is a fixed mapping from NJsonSchema's `Kind` enum to operator-readable strings:

| NJsonSchema Kind | Wrapped message |
|---|---|
| `NoAdditionalPropertiesAllowed` | "unknown field — schema does not permit additional properties" |
| `PropertyRequired` | "required field is missing" |
| `IntegerExpected` / `StringExpected` / etc. | "wrong type — expected <expected>" |
| `PatternMismatch` | "value does not match the required pattern" |
| `IntegerTooSmall` / `IntegerTooBig` | "value is out of range" |
| `StringTooShort` | "value cannot be empty" |
| (anything else) | "schema rule violation: <raw Kind>" — catch-all so we don't silently swallow new error kinds |

**Why ask:** I'm guessing at NJsonSchema's `Kind` enum names; review should confirm against actual 11.1.0 surface OR I'll verify during implementation.

### Q3. Sidecar.Tests.ps1 invocation pattern

Two ways for the Pester tests to exercise ValidateSidecar:

- **(a)** Invoke `dotnet run --project tools/ValidateSidecar` per test — matches the existing `RoundTripValidate.Tests.ps1` pattern for ValidateConfig. Pro: zero new infra. Con: ~3-5 seconds per test invocation (dotnet startup), so the 6 tests add ~20s to the suite.
- **(b)** Pre-publish ValidateSidecar to a known path during `BeforeAll`, then invoke the EXE directly. Pro: fast. Con: tests need `dotnet publish` orchestration.

**Recommendation:** (a). The test suite already spends ~16s on ValidateConfig invocations in RoundTripValidate; adding 20s for sidecar tests keeps the total under 40s, well within "fast feedback" range. (b) is a future optimization if the suite grows past ~3 minutes.

### Q4. Where does sidecar validation slot into generate.ps1's flow?

v1 §2.C puts it at the top of `Invoke-BulkProvision` BEFORE substitution. But it could also go inside `Read-Sidecar`, right after the YAML parse.

**Recommendation:** top of `Invoke-BulkProvision`, after both `$csvAbs`/`$sidecarAbs` resolve. Rationale: `Read-Sidecar` is shaped for parsing; mixing in schema validation muddies its purpose. Easier to skip the validate stage via `-SkipValidate` if both validators live at the same level.

### Q5. Should the existing `-SkipValidate` flag bypass sidecar validation too?

`-SkipValidate` currently bypasses the per-output ValidateConfig stage. Should it also bypass sidecar validation?

- **Pro:** symmetry, debugging workflow consistent.
- **Con:** sidecar validation runs ONCE per generator invocation; ValidateConfig runs per row. They have different cost profiles, and the sidecar case is the cheaper one. Skipping sidecar validation rarely saves real time but loses operator safety.

**Recommendation:** `-SkipValidate` bypasses BOTH. Operator can always re-run with validation enabled before committing config to a gateway box. v1 doesn't propose a separate `-SkipSidecarValidate` flag.

---

## 4. Out of scope for session 2.5

- Sidecar schema versioning — currently single-file, no v2 path. If we add `template-fanuc-v2.json` and need a different sidecar shape, that's a future session.
- Sidecar nested fields (sub-objects) — schema allowlist supports flat fields only. YAML parser choice (YamlDotNet) leaves the door open; no work needed in 2.5.
- Sidecar templating (DRY across multiple sidecars) — not asked for. Operators write one sidecar per gateway, currently small enough not to warrant DRY.

---

## 5. Rough size estimate

| Item | LOC | Tests |
|---|---|---|
| A — `ValidateSidecar/{csproj,Program.cs}` | ~120 C# | (exercised via D) |
| B — `sidecar-schema.json` | ~40 lines JSON | 0 |
| C — generate.ps1 hook + new ErrCode | ~15 PS | (exercised via D) |
| D — Sidecar.Tests.ps1 flesh-out | ~150 PS | 6 active (was pending) |
| E — README addendum | ~20 lines md | 0 |
| F — ElpisEdgeConnect.sln add | 4 lines | 0 |
| G — YamlDotNet pin note in csproj | 8 lines XML comment | 0 |

**Total estimate:** 1 session (~30-40 minutes of focused work on the user's pwsh-7 box, much of which is `dotnet build` + `Invoke-Tests.ps1` runs).

Sandbox-runnable: A, B, F, G. The Sidecar.Tests.ps1 flesh-out depends on D needing the CLI to actually run — can be written here, verified on the pwsh-7 box.

---

## 6. Exit gate (LOCKED from v4 §5, no changes proposed)

- [ ] `tools/ValidateSidecar/{ValidateSidecar.csproj,Program.cs}` committed; CLI surface per v4 §2 contract.
- [ ] CLI validates parsed YAML sidecar data against `tools/bulk-provision/sidecar-schema.json`.
- [ ] CLI has stable exit-code behavior per v4 §2 acceptance criteria.
- [ ] `sidecar-schema.json` committed; uses only the v4 §2 portable-feature allowlist.
- [ ] `generate.ps1` invokes sidecar validation BEFORE substitution; failure aborts with operator-friendly error.
- [ ] YAML parser choice locked (YamlDotNet vs hand-rolled) with an ADR if it warrants one.
- [ ] Malformed-sidecar Pester tests cover: missing required field, extra unknown field, wrong type on numeric, invalid enum, invalid pattern.
- [ ] Wrapped error projection asserted to NOT leak raw NJsonSchema diagnostics into operator-facing stderr by default; raw diagnostics emitted only when `--verbose` is supplied.
- [ ] Operator README troubleshooting section updated with sidecar-validation error guidance.
- [ ] Session 2.5 implementation PR opened against master.
- [ ] Forward-compat for nested sidecar fields (sub-objects) addressed in YAML-parser choice or documented as a known boundary.

---

## 7. Process (cadence position)

1. ⏳ **v1 (this doc)** — drafted, on `claude/chip3-impl-session2.5-plan` branch.
2. ⏳ **ChatGPT review** — user sends v1 to ChatGPT; review notes return.
3. ⏳ **v2** — synthesize review into a v2.
4. ⏳ **v3 reality-check** — Claude scans v2 against actual repo state, locks open questions.
5. ⏳ Optional **v4 lock-final** if ChatGPT's review requires it (session 2's v4 was added because the review had specific addenda + a session-split decision; session 2.5's review is likely smaller so v3 may suffice).
6. ⏳ **Implementation** against the locked plan.

---

## 8. What I need from you / from ChatGPT

- **User:** approve v1 going to ChatGPT, OR push back on the scope before review.
- **ChatGPT:** review the five open questions in §3 + the projected error format in Q2 specifically.
