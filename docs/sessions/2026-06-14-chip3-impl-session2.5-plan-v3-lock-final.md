# Chip 3 Implementation — Session 2.5 plan (v3: reality-check + lock-final)

**Date:** 2026-06-14
**Author:** Claude (post-v2-review reality-check)
**Status:** **LOCKED — implementation may begin against this doc.** v3 is intentionally lock-final per ChatGPT's v2 verdict ("expect v3 to be lockable without needing a v4").
**Predecessor:** `docs/sessions/2026-06-14-chip3-impl-session2.5-plan-v2.md`
**Cadence position:** v1 → ChatGPT review → v2 → ChatGPT v2 review (approve for v3) → **v3 (this doc) = LOCK**.

---

## 0. v2 review verdict + how the seven addenda land

ChatGPT's v2 verdict: **"Approve for v3 reality-check, but not lock-final yet."** Seven required addenda:

| # | Addendum | Lock in v3 |
|---|---|---|
| A1 | Add tests for `.json` support, parse-failure exit 3, unsupported extension | §7 D table — 3 new rows |
| A2 | Add at least one `generate.ps1` integration test | §7 D table — 2 new rows (schema-invalid sidecar through generator + `-SkipValidate` bypass) |
| A3 | Path projection must tolerate both `#/x` and `x` shapes + fallback to `error.Property` for missing-required | §4 — formal rule |
| A4 | Don't lock exact verbose example to `PropertyRequired: #` | §3 — loosened "verify field name + raw kind" |
| A5 | Add `IntegerTooBig` defensively to range bucket if it exists in 11.1.0 | §3 — added; **confirmed in §1 grounding** |
| A6 | YAML normalization contract for future nested fields | §6 — small recursive helper locked in |
| A7 | Clarify CLI argument errors (missing/unknown flag) | §5 — exit 2 + usage, NOT ValidateConfig's quirky exit 0 |

---

## 1. Reality-check grounding (verified before lock)

### 1.1 NJsonSchema 11.1.0 ValidationErrorKind enum (web-verified)

Source: `https://raw.githubusercontent.com/RicoSuter/NJsonSchema/v11.1.0/src/NJsonSchema/Validation/ValidationErrorKind.cs`.

Complete confirmed enum members in 11.1.0 (45 total):

```text
Unknown, StringExpected, NumberExpected, IntegerExpected,
BooleanExpected, ObjectExpected, PropertyRequired, ArrayExpected,
NullExpected, PatternMismatch, StringTooShort, StringTooLong,
NumberTooSmall, NumberTooBig, IntegerTooBig, TooManyItems,
TooFewItems, ItemsNotUnique, DateTimeExpected, DateExpected,
TimeExpected, TimeSpanExpected, UriExpected, IpV4Expected,
IpV6Expected, GuidExpected, NotAnyOf, NotAllOf, NotOneOf,
ExcludedSchemaValidates, NumberNotMultipleOf, IntegerNotMultipleOf,
NotInEnumeration, EmailExpected, HostnameExpected,
TooManyItemsInTuple, ArrayItemNotValid, AdditionalItemNotValid,
AdditionalPropertiesNotValid, NoAdditionalPropertiesAllowed,
TooManyProperties, TooFewProperties, Base64Expected,
NoTypeValidates, UuidExpected
```

Spot checks against ChatGPT's v2 review:
- **`IntegerTooBig` EXISTS** in 11.1.0 → A5 add is grounded.
- **`IntegerTooSmall` does NOT exist** in 11.1.0 → v2's removal was correct.
- **Both `UuidExpected` AND `GuidExpected` exist** → mapping table accommodates both.
- Several kinds v2's table did NOT cover but that could plausibly fire on our schema: `Unknown`, `NullExpected`, `StringTooLong`, `AdditionalPropertiesNotValid`. Catch-all already handles them; explicit entries are unnecessary.

### 1.2 YamlDotNet 17.1.0 (web-verified)

Source: `https://www.nuget.org/packages/YamlDotNet/17.1.0`.

- **Net8.0 dependencies: ZERO.** Confirms ChatGPT's claim.
- **Published 2026-04-28.** MIT-licensed.
- **No security advisories** on the listing.
- Newer version (18.0.0, published 2026-05-21) is available with a breaking `TypeInspectorSkeleton` / `ITypeInspector` change. v3 holds at 17.1.0.

### 1.3 NJsonSchema 11.1.0 pinned in `ElpisEdgeConnect.SchemaValidation.csproj` (line 32)

Confirmed via grep:

```xml
<PackageReference Include="NJsonSchema" Version="11.1.0" />
```

ValidateSidecar pins the same version. No package upgrade in session 2.5.

### 1.4 `tools/ValidateConfig/Program.cs` argument-handling quirk (reality-check finding)

ValidateConfig's `Main`:

```csharp
if (args.Length != 1 || args[0] is "--help" or "-h")
{
    Console.Error.WriteLine("Usage: ...");
    return 0;          // ← exit 0 on bad invocation
}
```

That's an existing quirk: invoking with 0 args, 2+ args, or unknown flags returns exit 0 (clean) but with usage text on stderr. **ValidateSidecar SHALL NOT mirror this.** Per ChatGPT A7, ValidateSidecar returns exit 2 on missing/unknown flag + usage on stderr. The two CLIs will be inconsistent on this point; not worth retrofitting ValidateConfig in this PR.

### 1.5 Stderr capture under Pester + `[Console]::Error.WriteLine`

Cannot be web-verified — depends on the user's pwsh-7 + Pester 5.7.1 runtime behavior. Verified during implementation by:

1. Running a schema-invalid sidecar against `generate.ps1` interactively in pwsh 7 → confirm operator-friendly stderr surfaces.
2. Running the corresponding Pester test → confirm the same stderr surfaces in `$result` captured by `& dotnet run --no-build ... 2>&1`.

If the Pester capture mode swallows the stderr stream differently from interactive pwsh, the test asserts on `$result` (already mixed via `2>&1`); the operator-facing UX still works because `[Console]::Error.WriteLine` is the validator's own concern, not generate.ps1's.

---

## 2. LOCKED sidecar schema (unchanged from v2)

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

---

## 3. LOCKED error projection mapping (v3 final)

### Default operator output format

```text
Sidecar validation failed:
  <display-path>: <wrapped reason>
  <display-path>: <wrapped reason>
  ...
```

### `Kind` → wrapped-reason mapping (grounded in §1.1 enum)

| NJsonSchema kind | Default operator message |
|---|---|
| `NoAdditionalPropertiesAllowed` | `unknown field — schema does not permit additional properties` |
| `PropertyRequired` | `required field is missing` |
| `StringExpected` | `wrong type — expected string` |
| `IntegerExpected` | `wrong type — expected integer` |
| `NumberExpected` | `wrong type — expected number` |
| `BooleanExpected` | `wrong type — expected boolean` |
| `ObjectExpected` | `wrong type — expected object` |
| `ArrayExpected` | `wrong type — expected array` |
| `PatternMismatch` | `value does not match the required pattern` |
| `NumberTooSmall` / `NumberTooBig` / `IntegerTooBig` | `value is out of range` |
| `StringTooShort` | `value cannot be empty` |
| `NotInEnumeration` | `value is not one of the allowed values` |
| `GuidExpected` / `UuidExpected` | `value must be a valid UUID` |
| anything else (catch-all) | `schema rule violation` |

**Default catch-all NEVER emits the raw `Kind` name.** v4 §2 lock honored.

### Verbose output (when `--verbose` is supplied)

Append a `[raw] <Kind>: <Path>` line under each operator message:

```text
Sidecar validation failed:
  mqttPort: wrong type — expected integer
    [raw] IntegerExpected: <whatever NJsonSchema reports>
  gatewayId: required field is missing
    [raw] PropertyRequired: <whatever NJsonSchema reports>
```

**Per A4:** the verbose `[raw]` line preserves whatever NJsonSchema emits for `Kind` + `Path` — no guess about whether it's `#`, `gatewayId`, etc. Tests assert presence of the kind name + the field name; do not assert the exact path string.

---

## 4. LOCKED display-path projection (v3 final, per A3)

Inputs from NJsonSchema's `ValidationError`:
- `error.Path` — may be `""`, `"#"`, `"#/mqttPort"`, `"mqttPort"`, or `"mqtt.host"` depending on the rule and code path.
- `error.Property` — the property name in scope (relevant for missing-required errors).

Projection algorithm (locked):

```csharp
static string ToDisplayPath(string? rawPath, string? property)
{
    var p = rawPath ?? string.Empty;

    // Normalize "#" / "#/..." prefix.
    if (p.StartsWith("#/")) p = p.Substring(2);
    else if (p == "#")      p = string.Empty;

    // Empty path + a Property → use the property name.
    // Covers PropertyRequired and missing-key style errors.
    if (string.IsNullOrEmpty(p))
    {
        return string.IsNullOrEmpty(property) ? "<root>" : property!;
    }

    // Leave dotted paths ("mqtt.host") and bare property names alone.
    return p;
}
```

Tests assert: `<root>` for root-level errors with no property, `<property-name>` for missing-required errors, plain bare names for property-level errors.

---

## 5. LOCKED CLI argument error rule (v3 final, per A7)

| Invocation | Exit | Stderr |
|---|---|---|
| `--help` / `-h` / no args | `0` | usage summary |
| Missing required `--schema` OR `--sidecar` | `2` | `ValidateSidecar: missing required argument: --schema` (or `--sidecar`) + usage summary |
| Unknown flag | `2` | `ValidateSidecar: unknown argument: <flag>` + usage summary |
| Duplicate flag | `2` | `ValidateSidecar: duplicate argument: <flag>` + usage summary |
| `--schema X --sidecar Y` (both present, paths resolved) | proceed per §1.4 acceptance criteria | as usual |

Sharing exit 2 between "argument problem" and "file-not-found" is intentional — both are "you gave the CLI something it can't act on." Exit 3 stays reserved for "file is present but parse failed."

---

## 6. LOCKED YAML normalization helper (v3 final, per A6)

The CLI's YAML → object → JSON-string pipeline is locked as a small **recursive** helper, not just a one-off `Deserialize<Dictionary<string, object?>>`. This makes the forward-compat claim for nested sidecars honest now, even though the current schema is flat.

Locked normalization contract:

```text
YAML mapping       -> Dictionary<string, object?>
YAML sequence      -> List<object?>
YAML scalar bool   -> bool
YAML scalar number -> long OR double (whichever parses cleanly)
YAML scalar string -> string
YAML scalar null   -> null
non-string mapping key -> parse failure, exit 3
unexpected YAML kind   -> parse failure, exit 3
```

Then `System.Text.Json.JsonSerializer.Serialize(normalized, options)` produces the JSON string handed to `JsonSchema.Validate`.

For `.json` sidecars: skip the YAML normalization step entirely — read file → `JsonDocument.Parse` → re-serialize to canonical string for validation. Malformed JSON → exit 3 with operator-friendly message.

---

## 7. LOCKED session 2.5 scope (in implementation order)

### A. `tools/ValidateSidecar/` CLI

| File | Purpose |
|---|---|
| `ValidateSidecar.csproj` | net8.0 Exe; `NJsonSchema 11.1.0` + `YamlDotNet 17.1.0` package refs; `TreatWarningsAsErrors=true`; `Nullable=enable`; NO `ProjectReference`. XML header documents YamlDotNet 17.1.0 choice + dep-audit-gate command. |
| `Program.cs` | ~160 LOC. Arg parse per §5 → file checks (exit 2) → extension dispatch per §6 (exit 3 on parse failure) → `JsonSchema.FromFileAsync(schemaPath)` → `schema.Validate(json)` → project per §3 + §4 (exit 1 on errors, 0 on clean). Catch-all unhandled → exit 4 with type+message on stderr. |

### B. Sidecar schema

`tools/bulk-provision/sidecar-schema.json` per §2.

### C. generate.ps1 validation hook

Top of `Invoke-BulkProvision`, after path resolution, **BEFORE** `Read-Sidecar`:

```pwsh
if (-not $SkipValidate) {
    $sidecarValidator = (Resolve-Path "$PSScriptRoot/../ValidateSidecar/ValidateSidecar.csproj").Path
    $schemaPath       = (Resolve-Path "$PSScriptRoot/sidecar-schema.json").Path
    $result = & dotnet run --project $sidecarValidator -- `
        --schema $schemaPath --sidecar $sidecarAbs 2>&1
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        foreach ($line in $result) {
            [Console]::Error.WriteLine($line)
        }
        throw "$($script:ErrCodes.SidecarSchemaViolation): sidecar validation failed (exit $exit). Review stderr above."
    }
}
```

New error code:
```text
SidecarSchemaViolation = 'BulkProvision.SidecarSchemaViolation'
```

`-SkipValidate` parameter help text updated to:
```
-SkipValidate disables BOTH sidecar schema validation AND generated
gateway-config validation. Use only for local debugging; re-run
without -SkipValidate before deploying generated configs.
```

### D. Pester harness — `Sidecar.Tests.ps1` + `Generate.Tests.ps1` (v3 final test list)

`BeforeAll` pattern: one `dotnet build` of `tools/ValidateSidecar/` (`--nologo`); each test then uses `dotnet run --no-build --project ... -- args`.

**Sidecar.Tests.ps1 — 12 tests** (was 8 in v2):

| # | Test | Setup | Assert |
|---|---|---|---|
| 1 | valid YAML sidecar | `sample-fanuc.gateway.yml` as-is | exit 0 |
| 2 | valid JSON sidecar (A1) | hand-craft equivalent JSON from sample-fanuc fields | exit 0 |
| 3 | missing required field | drop `gatewayId` | exit 1, stderr `gatewayId` + `required field is missing` |
| 4 | extra unknown field | add `extraThing: x` | exit 1, stderr contains `extraThing` |
| 5 | wrong type on `mqttPort` | quote it | exit 1, stderr `wrong type — expected integer`, NOT `IntegerExpected` |
| 6 | invalid enum on `mqttQos` | set `mqttQos: 3` | exit 1, stderr `mqttQos` + `value is not one of the allowed values`, NOT `NotInEnumeration` |
| 7 | invalid pattern on `mqttClientIdPrefix` | set to `bad spaces here` | exit 1, stderr `value does not match the required pattern` |
| 8 | invalid UUID on `gatewayId` | set to `not-a-uuid` | exit 1, stderr `gatewayId` + `value must be a valid UUID` |
| 9 | malformed YAML parse failure (A1) | tab-indented or unbalanced YAML | exit 3, operator-friendly stderr, no raw exception stack |
| 10 | malformed JSON parse failure (A1) | trailing comma or missing brace | exit 3, operator-friendly stderr |
| 11 | unsupported extension (A1) | `sidecar.txt` with readable content | exit 3, stderr says expected `.yml`/`.yaml`/`.json` |
| 12 | wraps raw NJsonSchema diagnostics | run schema-invalid case twice (default + `--verbose`) | default has NO raw `Kind` names; `--verbose` contains `[raw]` + the kind name + the field name (A4 — do not assert exact path string) |

**Generate.Tests.ps1 — 2 new tests** (A2):

| # | Test | Setup | Assert |
|---|---|---|---|
| G1 | generate.ps1 rejects schema-invalid sidecar before substitution | sidecar with `gatewayId: not-a-uuid`, valid CSV | throws `BulkProvision.SidecarSchemaViolation`; stderr includes `gatewayId` + `valid UUID` text |
| G2 | `-SkipValidate` bypasses sidecar validation | same broken sidecar + `-SkipValidate` | NO throw; produces 3 output files (schema-invalid sidecar's data still flows through substitution, which is the documented bypass behavior) |

**Total Pester (post-session-2.5):** 29 (session 2 active) + 12 + 2 = **43 active tests + 0 pending**.

### E. README troubleshooting addendum

- New `BulkProvision.SidecarSchemaViolation` section with two paragraphs (what fires it; how to read the wrapped message + fix the field).
- Update `-SkipValidate` documentation to explicitly state it bypasses BOTH stages.
- Quick-reference table of CLI exit codes for direct ValidateSidecar usage during debugging.

### F. Solution-file update

`ElpisEdgeConnect.sln` adds `tools/ValidateSidecar/ValidateSidecar.csproj`. Standard Visual Studio block injection.

### G. `.gitattributes` LF lock for ValidateSidecar

Append to repo-root `.gitattributes` (from session 2 A.B2):

```gitattributes
tools/ValidateSidecar/**/*.cs     text eol=lf
tools/ValidateSidecar/**/*.csproj text eol=lf
```

Verified via `git check-attr -a tools/ValidateSidecar/Program.cs` returning `eol: lf`.

### H. Dependency audit gate

After A lands, run:

```bash
dotnet restore tools/ValidateSidecar/ValidateSidecar.csproj
dotnet list tools/ValidateSidecar/ValidateSidecar.csproj package --vulnerable --include-transitive
```

Expected: no vulnerable packages. NJsonSchema 11.1.0 has transitives (`Namotion.Reflection`, `Newtonsoft.Json`, `NJsonSchema.Annotations`, `System.Text.Json`); audit is not ceremonial. If something fires, pause and escalate before PR.

---

## 8. LOCKED exit gate (v4 §5 unchanged)

- [ ] `tools/ValidateSidecar/{ValidateSidecar.csproj,Program.cs}` committed; CLI surface per v4 §2 + v3 §5 contract.
- [ ] CLI validates parsed YAML/JSON sidecar data against `tools/bulk-provision/sidecar-schema.json`.
- [ ] CLI has stable exit-code behavior per v4 §2 + v3 §5 acceptance criteria.
- [ ] `sidecar-schema.json` committed; uses only the v4 §2 portable-feature allowlist (including `enum` on `mqttQos`).
- [ ] `generate.ps1` invokes sidecar validation BEFORE substitution; failure aborts with operator-friendly error.
- [ ] YamlDotNet 17.1.0 locked; explanation in csproj header; no ADR.
- [ ] Malformed-sidecar Pester tests cover: missing required field, extra unknown field, wrong type on numeric, invalid enum, invalid pattern, invalid UUID format, malformed YAML, malformed JSON, unsupported extension.
- [ ] Wrapped error projection asserted to NOT leak raw NJsonSchema diagnostics into operator-facing stderr by default; raw diagnostics emitted only when `--verbose` is supplied.
- [ ] generate.ps1 integration tests cover: schema-invalid sidecar throws `BulkProvision.SidecarSchemaViolation`; `-SkipValidate` bypasses it.
- [ ] Operator README troubleshooting section updated with sidecar-validation error guidance; `-SkipValidate` bypass risk documented.
- [ ] Session 2.5 implementation PR opened against master.

---

## 9. Cadence position after v3 lock

1. ✅ v1
2. ✅ ChatGPT v1 review
3. ✅ v2 synthesis
4. ✅ ChatGPT v2 review ("approve for v3, not lock yet")
5. ✅ **v3 (this doc) = LOCK-FINAL**
6. ⏳ Implementation against v3 §7 scope

User actions required:
- Approve v3 lock (or pushback on any addendum).
- After approval, decide session 2.5 execution style:
  - **(A)** Implement sandbox-runnable subset (A CLI + B schema + F sln + G gitattributes + H csproj header) on session-2.5 branch; user runs Pester runtime on pwsh-7 box (similar split to session 2).
  - **(B)** Hold all implementation until everything can land together on pwsh-7 box (smaller PR, slower wall-clock).

Recommendation: (A), same shape as session 2. Most of the LOC ships from sandbox; only the Pester runtime verification + actual fixture run blocks on pwsh-7.
