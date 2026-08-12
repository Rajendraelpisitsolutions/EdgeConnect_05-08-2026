# Chip 3 Implementation — Session 2.5 plan (v2)

**Date:** 2026-06-14
**Author:** Claude (post-ChatGPT v1 review)
**Status:** v2 — pending v3 reality-check.
**Predecessor:** `docs/sessions/2026-06-14-chip3-impl-session2.5-plan-v1.md`
**Locked source:** `docs/sessions/2026-06-13-chip3-impl-session2-plan-v4-lock-final.md` §2 + §5

---

## 0. Review decisions incorporated in v2

| Decision | Resolution |
|---|---|
| **Q1 — YAML parser** | YamlDotNet confirmed, no ADR. **Version pinned to 17.1.0** (NOT 16.x from v1; 17.1.0 carries the latest security improvements without the 18.0.0 `TypeInspector`/`ITypeInspector` breaking change). Dependency-audit gate added: `dotnet list tools/ValidateSidecar/ValidateSidecar.csproj package --vulnerable --include-transitive`. Bumping to 18.0.0 requires explicit local proof in v3. |
| **Q2 — Error projection format** | Header + one-error-per-line format approved. Mapping table **rewritten against the actual `ValidationErrorKind` enum** (no fabricated kinds; `NumberTooSmall`/`NumberTooBig` instead of `IntegerTooSmall`/`IntegerTooBig`). Catch-all default emits `schema rule violation` ONLY — never leaks the raw `Kind` name. Raw NJsonSchema verbiage stays behind `--verbose`. |
| **Q3 — Sidecar.Tests.ps1 invocation** | `dotnet build` ONCE in `BeforeAll`, then `dotnet run --no-build --project ... -- args` per test. Pays one build cost; keeps per-test invocation fast. No `dotnet publish` in session 2.5. |
| **Q4 — Validation hook slot** | Top of `Invoke-BulkProvision`, after path resolution, **BEFORE `Read-Sidecar`**. Means malformed YAML surfaces as validator exit 3 with operator-friendly wrapping, not as a less-precise PowerShell YAML parse error. Stderr preservation hardened: use `[Console]::Error.WriteLine($line)` to re-emit captured stderr lines, not `Write-Host`. |
| **Q5 — `-SkipValidate` scope** | Bypasses BOTH the sidecar validate AND the per-output ValidateConfig stage. Documented in README + `-SkipValidate` parameter help with explicit risk warning. |
| **Schema/test contradiction — `mqttQos`** | **CRITICAL FIX:** `mqttQos` becomes `enum: [0, 1, 2]` (not `minimum: 0, maximum: 2`). v4 exit gate requires "invalid enum" test coverage; v1's schema had no enum field. v2 fixes the schema; the existing range test moves to `mqttPort`. |
| **UUID-format coverage** | New negative test added for `gatewayId: not-a-uuid`. Maps to NJsonSchema's `UuidExpected` kind. v4 allowlist explicitly permits `format: uuid`; session 2.5 should prove it works. |
| **`.gitattributes` LF lock for ValidateSidecar** | v4 §3.F1 deferred ValidateSidecar's `.cs`/`.csproj` LF lock lines to 2.5. v2 lists this as a scope item; lines append to the repo-root `.gitattributes` from session 2. |
| **Explicit `.json` vs `.yml` parsing rule** | v1 implied YAML pipeline for all sidecar formats. v2 splits parsing by extension: `.yml`/`.yaml` → YamlDotNet; `.json` → `System.Text.Json.JsonDocument`. Both normalize to a JSON string before NJsonSchema. Avoids "YAML happens to parse JSON" as a hidden implementation contract. |

---

## 1. Direct inputs from v4 (LOCKED, no change)

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
    YAML / JSON sidecar parsed BEFORE schema validation
    YAML or JSON → object → JSON-serialize → schema-validate pipeline
```

### Schema-feature allowlist (v4 §2)

```text
type, required, properties, additionalProperties: false,
enum, pattern, format (only "uuid"), minimum, maximum, minLength
```

---

## 2. Revised sidecar schema content (LOCKED for v2)

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
    "mqttQos":               { "type": "integer", "enum": [0, 1, 2] },
    "mqttClientIdPrefix":    { "type": "string", "minLength": 1, "pattern": "^[A-Za-z0-9_-]+$" }
  }
}
```

Changes from v1:
- `mqttQos`: `minimum: 0, maximum: 2` → `enum: [0, 1, 2]` (satisfies v4 exit-gate "invalid enum" test requirement; better semantic model for QoS).
- `mqttPort`: keeps `minimum: 1, maximum: 65535` (range test stays here).

---

## 3. NJsonSchema error projection mapping (LOCKED for v2)

Default operator-facing output:

```text
Sidecar validation failed:
  <field-path-or-root>: <wrapped reason>
  <field-path-or-root>: <wrapped reason>
  ...
```

Field-path projection (lightweight, no full JSON Pointer parser):
```text
"#"           -> "<root>"
"#/mqttPort"  -> "mqttPort"
"#/mqtt.host" -> "mqtt.host"
```

Mapping table (sourced from NJsonSchema's `ValidationErrorKind.cs`):

| NJsonSchema kind                  | Default operator message                                       |
|-----------------------------------|----------------------------------------------------------------|
| `NoAdditionalPropertiesAllowed`   | `unknown field — schema does not permit additional properties` |
| `PropertyRequired`                | `required field is missing`                                    |
| `StringExpected`                  | `wrong type — expected string`                                 |
| `IntegerExpected`                 | `wrong type — expected integer`                                |
| `NumberExpected`                  | `wrong type — expected number`                                 |
| `BooleanExpected`                 | `wrong type — expected boolean`                                |
| `ObjectExpected`                  | `wrong type — expected object`                                 |
| `ArrayExpected`                   | `wrong type — expected array`                                  |
| `PatternMismatch`                 | `value does not match the required pattern`                    |
| `NumberTooSmall` / `NumberTooBig` | `value is out of range`                                        |
| `StringTooShort`                  | `value cannot be empty`                                        |
| `NotInEnumeration`                | `value is not one of the allowed values`                       |
| `GuidExpected` / `UuidExpected`   | `value must be a valid UUID`                                   |
| anything else                     | `schema rule violation`                                        |

**Default catch-all NEVER includes the raw `Kind` name.** v4 lock: raw NJsonSchema diagnostics only behind `--verbose`.

Verbose output (only when `--verbose` is supplied):
```text
Sidecar validation failed:
  mqttPort: wrong type — expected integer
    [raw] IntegerExpected: #/mqttPort
  gatewayId: required field is missing
    [raw] PropertyRequired: #
```

---

## 4. Session 2.5 scope (in implementation order)

### A. `tools/ValidateSidecar/` CLI

| File | Purpose |
|---|---|
| `ValidateSidecar.csproj` | net8.0 Exe, NuGet refs `NJsonSchema 11.1.0` + `YamlDotNet 17.1.0`, `TreatWarningsAsErrors=true`, `Nullable=enable`, NO `ProjectReference`. XML header documents YAML-parser choice + dep-audit-gate command. |
| `Program.cs` | ~140 LOC. Args parse → file existence checks (exit 2) → extension-based parse: `.yml`/`.yaml` via YamlDotNet → `Dictionary<string, object?>` → `System.Text.Json.JsonSerializer.Serialize` to JSON; `.json` direct via `JsonDocument.Parse` (exit 3 on parse failure). Then `JsonSchema.FromFileAsync(schemaPath)` → `schema.Validate(json)`. Project errors via §3 mapping. Exit 1 on validation failure, 0 on clean, 4 on unhandled. |

**Done when:** `dotnet build tools/ValidateSidecar/ValidateSidecar.csproj` clean; manual exits match v4 §2 acceptance criteria across all five exit codes.

### B. Sidecar schema

`tools/bulk-provision/sidecar-schema.json` per §2 above.

### C. generate.ps1 validation hook

At the top of `Invoke-BulkProvision`, AFTER path resolution but **BEFORE `Read-Sidecar`**:

```pwsh
if (-not $SkipValidate) {
    $sidecarValidator = (Resolve-Path "$PSScriptRoot/../ValidateSidecar/ValidateSidecar.csproj").Path
    $schemaPath       = (Resolve-Path "$PSScriptRoot/sidecar-schema.json").Path
    $result = & dotnet run --project $sidecarValidator -- `
        --schema $schemaPath --sidecar $sidecarAbs 2>&1
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        # Re-emit captured stderr lines on the host's stderr stream, NOT
        # via Write-Host (which sends to host output and mixes channels).
        foreach ($line in $result) {
            [Console]::Error.WriteLine($line)
        }
        throw "$($script:ErrCodes.SidecarSchemaViolation): sidecar validation failed (exit $exit). Review stderr above."
    }
}
```

New error code in `generate.ps1`'s `$script:ErrCodes`:
```text
SidecarSchemaViolation = 'BulkProvision.SidecarSchemaViolation'
```

**Done when:**
- Happy-path sidecar passes silently (validator exits 0).
- Broken sidecar throws `BulkProvision.SidecarSchemaViolation` AND surfaces the wrapped operator-readable stderr.
- `-SkipValidate` bypasses BOTH this stage AND the existing per-output ValidateConfig stage. Updated `-SkipValidate` parameter help text:
  ```
  -SkipValidate disables BOTH sidecar schema validation AND generated
  gateway-config validation. Use only for local debugging; re-run
  without -SkipValidate before deploying generated configs.
  ```

### D. Sidecar.Tests.ps1 — flesh out 6 stubs + add 2 new tests

Total: 8 tests (6 v1 stubs + 2 new). Uses `dotnet build` ONCE in `BeforeAll`, then `dotnet run --no-build` per test.

| Test | Setup | Assert |
|---|---|---|
| validates a well-formed sidecar | `sample-fanuc.gateway.yml` as-is | exit 0 |
| missing required field | drop `gatewayId` | exit 1, stderr `gatewayId` substring, default output contains `required field is missing` |
| extra unknown field (`additionalProperties:false`) | add `extraThing: x` | exit 1, stderr `extraThing` substring |
| wrong type on `mqttPort` (string instead of int) | quote it | exit 1, default output contains `wrong type — expected integer`, NOT `IntegerExpected` |
| **NEW — invalid enum on `mqttQos`** | set `mqttQos: 3` | exit 1, stderr `mqttQos` substring, default output contains `value is not one of the allowed values`, NOT `NotInEnumeration` |
| invalid pattern on `mqttClientIdPrefix` | set to `bad spaces here` | exit 1, default output contains `value does not match the required pattern` |
| **NEW — invalid UUID format on `gatewayId`** | set to `not-a-uuid` | exit 1, default output contains `value must be a valid UUID` |
| wraps raw NJsonSchema diagnostics | run twice (default + `--verbose`) | default output does NOT contain raw kind names; `--verbose` output DOES contain `[raw]` lines |

**Done when:** `Invoke-Tests.ps1` shows 37 active + 0 pending. (Session 2: 29 active + 6 pending → session 2.5: 29 + 8 = 37 active + 0 pending.)

### E. README troubleshooting addendum

Add a `BulkProvision.SidecarSchemaViolation` section to the operator README per the existing pattern. Two paragraphs: what triggers it (well-formed YAML that violates the schema vs malformed YAML/JSON that fails to parse — different exit codes), how to fix (check field path in the wrapped message, fix the value in the sidecar, re-run).

Also update the `-SkipValidate` section to clarify the bypass risk per §4.C.

### F. Solution-file update

`ElpisEdgeConnect.sln` adds `tools/ValidateSidecar/ValidateSidecar.csproj` so the whole-solution build picks it up.

### G. `.gitattributes` LF lock for ValidateSidecar

Append to the repo-root `.gitattributes` (already exists from session 2 A.B2):

```gitattributes
tools/ValidateSidecar/**/*.cs     text eol=lf
tools/ValidateSidecar/**/*.csproj text eol=lf
```

Verify via `git check-attr -a tools/ValidateSidecar/Program.cs` → `eol: lf`.

### H. YamlDotNet csproj header note (NOT a separate ADR)

`ValidateSidecar.csproj` XML header includes a 6-line note: chose YamlDotNet 17.1.0 because hand-rolled flat parser would lock sidecars to one level forever, and 17.1.0 carries the latest security improvements without the 18.0.0 `ITypeInspector` breaking change. Reference v4 §2 + this v2 §0 entry.

### I. Dependency audit gate

After implementing A and before declaring it done, run:

```bash
dotnet restore tools/ValidateSidecar/ValidateSidecar.csproj
dotnet list tools/ValidateSidecar/ValidateSidecar.csproj package --vulnerable --include-transitive
```

Expected: no vulnerable packages reported. If anything surfaces, pause and escalate (likely Newtonsoft.Json transitive from NJsonSchema; deal with it inline).

---

## 5. Open questions for v3 reality-check

1. **NJsonSchema 11.1.0 vs latest (11.6.1)** — the existing `ElpisEdgeConnect.SchemaValidation` project pins 11.1.0. ValidateSidecar's csproj should match (avoid drift) OR upgrade both (latest stable). ChatGPT review referenced the master branch's `ValidationErrorKind.cs`; v3 should diff 11.1.0's enum values against the master surface to confirm the §3 mapping table is accurate for 11.1.0. If 11.1.0 lacks `UuidExpected`, fall back to `GuidExpected` (likely present in both per ChatGPT's review).

2. **YamlDotNet 17.1.0 availability + transitive graph** — v3 reality-check runs `dotnet add package YamlDotNet --version 17.1.0` in a throwaway project to confirm: (a) 17.1.0 exists on NuGet; (b) it has zero transitives for `net8.0` per ChatGPT's review; (c) `dotnet list package --vulnerable --include-transitive` returns clean.

3. **Stderr capture semantics in PowerShell** — the v2 §4.C hook does `2>&1` to merge stderr into the pipeline output, then re-emits each line via `[Console]::Error.WriteLine`. Verify that this actually surfaces in the parent shell's stderr stream (vs being swallowed by PowerShell's host) under both `pwsh` interactive and Pester contexts.

4. **`dotnet build` once + `dotnet run --no-build` semantics under Pester** — confirm `dotnet build` in `BeforeAll` actually accelerates subsequent `dotnet run --no-build` invocations vs re-triggering an incremental build. Should be the case but worth checking on the pwsh-7 box.

---

## 6. Revised size estimate

| Item | LOC | Tests |
|---|---|---|
| A — ValidateSidecar CLI | ~140 C# + csproj boilerplate | (exercised via D) |
| B — sidecar-schema.json | ~40 lines JSON | 0 |
| C — generate.ps1 hook + new ErrCode | ~20 PS | (exercised via D) |
| D — Sidecar.Tests.ps1 flesh-out + 2 new tests | ~200 PS | 8 active (was 6 pending) |
| E — README addendum + `-SkipValidate` warning | ~30 lines md | 0 |
| F — ElpisEdgeConnect.sln add | 4 lines | 0 |
| G — .gitattributes LF lock for ValidateSidecar | 2 lines | 0 |
| H — YamlDotNet csproj header note | 6 lines XML | 0 |
| I — Dependency audit (one-shot gate, not committed) | 0 LOC | 0 |

**Total:** "one small session" — the new CLI + schema + tests + solution update + dep restore are each routine but cumulative. ChatGPT's wisdom: do not promise a minute count.

Sandbox-runnable: A (CLI), B (schema), F (sln), G (gitattributes), H (csproj header). Verification via `dotnet build` works in sandbox. The Sidecar.Tests.ps1 flesh-out (D) can be written here, runtime verified on the pwsh-7 box. C requires running generate.ps1 which is pwsh-7 only.

---

## 7. Exit gate (LOCKED from v4 §5, unchanged)

- [ ] `tools/ValidateSidecar/{ValidateSidecar.csproj,Program.cs}` committed; CLI surface per v4 §2 + v2 §1 contract.
- [ ] CLI validates parsed YAML/JSON sidecar data against `tools/bulk-provision/sidecar-schema.json`.
- [ ] CLI has stable exit-code behavior per v4 §2 acceptance criteria.
- [ ] `sidecar-schema.json` committed; uses only the v4 §2 portable-feature allowlist (now including `enum` on `mqttQos`).
- [ ] `generate.ps1` invokes sidecar validation BEFORE substitution; failure aborts with operator-friendly error.
- [ ] YAML parser choice locked (YamlDotNet 17.1.0) with explanation in csproj header (no ADR).
- [ ] Malformed-sidecar Pester tests cover: missing required field, extra unknown field, wrong type on numeric, **invalid enum** (new), invalid pattern, **invalid UUID format** (new).
- [ ] Wrapped error projection asserted to NOT leak raw NJsonSchema diagnostics into operator-facing stderr by default; raw diagnostics emitted only when `--verbose` is supplied.
- [ ] Operator README troubleshooting section updated with sidecar-validation error guidance; `-SkipValidate` bypass risk documented.
- [ ] Session 2.5 implementation PR opened against master.
- [ ] Forward-compat for nested sidecar fields (sub-objects) addressed in YAML-parser choice or documented as a known boundary.

---

## 8. Process (cadence position)

1. ✅ v1
2. ✅ ChatGPT review
3. ✅ **v2 (this doc)** — committed to same branch + PR #151.
4. ⏳ v3 reality-check — Claude scans v2 against actual repo state (NJsonSchema 11.1.0 enum values, YamlDotNet 17.1.0 NuGet surface, stderr capture semantics). Lock open §5 questions.
5. ⏳ Optional v4 lock-final — only if v3 surfaces an unexpected addendum (likely unnecessary given v2's tight scope).
6. ⏳ Implementation against the locked plan.
