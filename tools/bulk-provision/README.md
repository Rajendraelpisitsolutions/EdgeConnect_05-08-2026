# bulk-provision

Offline provisioner that generates `gateway.json` configs in bulk from a
CSV of devices plus a per-gateway sidecar.

Use it when you need to stand up many EdgeConnect gateways with the same
template — e.g. 100 FOCAS2 CNCs across a factory, each on its own
gateway box. One CSV row per device, one sidecar per gateway fleet, one
command produces one validated config per row.

This README is for the operator running the tool. Architecture, the
canonical placeholder taxonomy, and locked design decisions live in
[`templates/MANIFEST.md`](./templates/MANIFEST.md) and the [chip 3
plan trail](../../docs/sessions/) (search for `chip3-impl-session*`).

## Prerequisites

- **PowerShell 7+** (`pwsh`). The generator declares `#requires -Version 7.0`.
  Check: `pwsh --version` shows `7.x`.
- **.NET 8 SDK**. Needed because the validate stages invoke
  `tools/ValidateSidecar` and `tools/ValidateConfig` via `dotnet run`.
  Check: `dotnet --list-sdks` shows `8.0.x`.
- **Pester v5+** (only if you want to run the tests).
  Check: `Get-Module Pester -ListAvailable | Where-Object Version -ge 5.0.0`.
  Install: `Install-Module Pester -MinimumVersion 5.0 -Force -Scope CurrentUser`.

## Quickstart

Run from `tools/bulk-provision/` cwd:

```pwsh
cd tools/bulk-provision

pwsh ./generate.ps1 `
    -Csv ./samples/sample-fanuc.csv `
    -Sidecar ./samples/sample-fanuc.gateway.yml `
    -Template template-fanuc `
    -OutDir ./out/my-first-run
```

You'll get one `<deviceId>.gateway.json` per CSV row, plus a
`run-summary.json` and a `MANIFEST.txt` SHA-256 listing, in `./out/my-first-run/`.

Two validate stages run automatically:

1. **Sidecar schema** — `tools/ValidateSidecar` checks the sidecar against
   `tools/bulk-provision/sidecar-schema.json` BEFORE any substitution.
   Catches typos and shape mistakes the moment they happen instead of
   letting them flow into N generated configs.
2. **Per-output config schema** — `tools/ValidateConfig` checks every
   generated `*.gateway.json` against the canonical gateway schema.

If either stage fails, the generator aborts with the stderr from the
validator. `-SkipValidate` disables BOTH stages — use only for local
debugging against deliberately broken inputs, and re-run without it
before deploying to a gateway box.

## CSV format

Required columns (extras tolerated and ignored):

| Column | Type | Notes |
|---|---|---|
| `deviceId` | string | Must be unique within the CSV. Becomes part of the output filename. |
| `deviceName` | string | Human label for the device. |
| `host` | string | IPv4 or hostname. Operator validates reachability separately. |
| `enabled` | `true`/`false` | Lower-case JSON booleans. |

Duplicate `deviceId` aborts the run with `BulkProvision.CsvDuplicateDeviceId`.

## Sidecar format

YAML or JSON, one level deep. See
[`templates/MANIFEST.md`](./templates/MANIFEST.md) "Per-gateway placeholders"
for the full field list. Example:

```yaml
gatewayId: "00000000-0000-0000-0000-000000000001"
gatewayName: "edge-acme-site-a"
gatewayProvisioningId: "11111111-1111-1111-1111-111111111111"
fleetId: "fleet-acme"
site: "site-a"
mqttHost: "127.0.0.1"
mqttPort: 1883
mqttQos: 1
mqttClientIdPrefix: "edge-acme"
```

(Sidecar schema enforcement lands in session 2.5 with the
`tools/ValidateSidecar/` CLI; until then, missing fields surface as
unresolved placeholders.)

## Pinned generation (for deterministic / reproducible runs)

To produce byte-identical output across runs and across hosts, pin both
the provisioning id and the timestamp:

```pwsh
pwsh ./generate.ps1 `
    -Csv ./samples/sample-fanuc.csv `
    -Sidecar ./samples/sample-fanuc.gateway.yml `
    -Template template-fanuc `
    -OutDir ./tests/fixtures/expected/fanuc `
    -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 `
    -GeneratedAt 2026-01-01T00:00:00Z
```

Re-running the same command (same cwd, same pins) produces a tree that
compares byte-equal via `fc /b` on Windows or `cmp -r` on Linux.

## Available templates

| Template id | Protocol | Notes |
|---|---|---|
| `template-fanuc` | FANUC FOCAS2, port 8193 | Polling 3000ms; 9 canonical dataPoint prefixes |
| `template-brother` | Brother HTTP | Polling 3000ms; 8 canonical dataPoint prefixes |
| `template-modbus` | Modbus TCP, port 502, unitId 1 | Polling 1000ms; empty `tags[]` — per-tag definitions ship via separate Modbus CSV importer |

See [`templates/MANIFEST.md`](./templates/MANIFEST.md) for the locked
static fields, placeholder taxonomy, and instructions for adding a new
template.

## Troubleshooting

The generator surfaces stable `BulkProvision.*` error codes on every
throw. When you see one, the prefix points you directly to the problem.

### `BulkProvision.PlaceholderScopeViolation`

A placeholder is being used in the wrong scope: per-row inside Sources
only; per-gateway anywhere else. If you edited a template, check that
`{{ deviceId }}`-style markers stay inside `Sources[…]` and
`{{ gatewayName }}`-style markers stay outside.

### `BulkProvision.UnresolvedPlaceholder`

A `{{ name }}` marker survived substitution. Two causes:
- The marker name isn't in the placeholder registry (see
  `templates/MANIFEST.md`). Either add it to both the registry and the
  manifest, or remove it from the template.
- A typo in the marker. Compare against the manifest table.

### `BulkProvision.CsvDuplicateDeviceId`

Two CSV rows share a `deviceId`. The generator can't decide which one
"wins," so it aborts. Edit the CSV so every `deviceId` is unique.

### `BulkProvision.CsvMissingColumn`

The CSV is missing one of the four required columns (`deviceId`,
`deviceName`, `host`, `enabled`). Fix the header row.

### `BulkProvision.CsvEmpty`

The CSV has a header row but no device rows. At least one device row
is required. (If you actually want to provision zero devices, just
don't run the tool.)

### `BulkProvision.CsvNotFound` / `BulkProvision.SidecarNotFound` / `BulkProvision.TemplateNotFound`

One of the input paths doesn't exist. Most common causes:
- Running `generate.ps1` from the wrong cwd (it should be
  `tools/bulk-provision/`, see "Quickstart" above).
- A typo in the `-Csv`, `-Sidecar`, or `-Template` argument.
- For `TemplateNotFound`: the `-Template` value is a logical id like
  `template-fanuc`; the generator resolves it against
  `templates/<id>-v<version>.json`. If you bumped `-TemplateVersion`
  but the matching file doesn't exist, this fires.

### `BulkProvision.SidecarFormatUnsupported` / `BulkProvision.SidecarYamlMalformed`

Sidecar extension must be `.json`, `.yml`, or `.yaml`. The
`SidecarYamlMalformed` code comes from generate.ps1's lightweight
internal parser used during placeholder substitution; for sidecar
schema validation, `tools/ValidateSidecar` runs first and produces a
more precise error — usually `BulkProvision.SidecarSchemaViolation`
(below) instead.

### `BulkProvision.SidecarSchemaViolation`

The sidecar parsed successfully but failed schema validation against
`tools/bulk-provision/sidecar-schema.json`. The stderr block above the
error line includes one operator-friendly message per violation, with
the field path on the left and the wrapped reason on the right. Common
shapes:

- `gatewayId: required field is missing` — add the missing field.
- `mqttQos: value is not one of the allowed values` — only 0, 1, 2 are valid.
- `gatewayId: value must be a valid UUID` — fix the format.
- `extraThing: unknown field — schema does not permit additional properties` — remove the extra field, or check for a typo on a canonical field name.

For raw NJsonSchema diagnostics (kind + path), re-run
`tools/ValidateSidecar` directly with `--verbose`:

```pwsh
dotnet run --project tools/ValidateSidecar -- `
    --schema tools/bulk-provision/sidecar-schema.json `
    --sidecar path/to/your.gateway.yml `
    --verbose
```

The verbose output appends a `[raw]` line under each operator message
showing NJsonSchema's `Kind` and `Path` values.

### `BulkProvision.RenderedJsonInvalid`

After placeholder substitution, the resulting per-row JSON failed to
parse. The error message includes the offending `deviceId`. Usually
caused by a CSV value containing an unescaped quote, brace, or
backslash that broke JSON syntax when injected literally. Sanitize the
CSV value (e.g., quote-encode it, or simply remove the troublesome
character).

### `BulkProvision.ConfigSchemaViolation`

A generated output failed the canonical configuration schema. Inspect
the stderr block above the error line for the field path + reason.
Common causes: placeholder rendered the wrong type (string where a
number was expected), or the template was edited to drop a required
field.

### `ValidateConfig: WARNING — N suspect root key(s)`

Not an error — but worth attention. Per ADR-0030, unknown root keys
that aren't `_`-prefixed usually mean a typo on a canonical field
(`Soures` instead of `Sources`, etc). The CLI emits Levenshtein-distance
suggestions; cross-check against the template before shrugging it off.

### ValidateSidecar exit codes (for direct CLI debugging)

When invoking `tools/ValidateSidecar` outside of `generate.ps1`:

| Exit | Meaning |
|------|---------|
| 0 | Sidecar is well-formed AND validates against the schema |
| 1 | Schema-validation failure (one or more rule violations) |
| 2 | Argument problem OR file not found / unreadable |
| 3 | Sidecar parse failure (malformed YAML / JSON, unsupported extension) |
| 4 | Unexpected internal error |

## Deterministic-output guarantee

The generator promises **byte-identical output** when:
- The CSV is byte-identical.
- The sidecar is byte-identical.
- The template version is the same.
- `-GatewayProvisioningId` is pinned.
- `-GeneratedAt` is pinned.
- The cwd is the same (the generator serializes a `$PWD`-relative path
  into `run-summary.json`).

When you only pin `-GatewayProvisioningId` (not `-GeneratedAt`), the
outputs differ ONLY in the `_provisioning.generatedAt` field.

If `fc /b` reports any other diff between two pinned runs, that's a
regression — please file an issue with both output trees.

## Tests

```pwsh
pwsh ./tests/Invoke-Tests.ps1
```

The wrapper enforces Pester v5+ and prints install guidance if it's
missing. The full Pester suite covers placeholder scope guards (incl.
adversarial replacements with `$`, `\`, `$1`), canonical-JSON byte-level
shape (UTF-8 no BOM, LF, sorted object keys, preserved array order),
CSV validation, three-template round-trip validation, and the
deterministic-tree contract for all three templates.

## Reference

- Plan trail: [`docs/sessions/2026-06-13-chip3-impl-session2-plan-v4-lock-final.md`](../../docs/sessions/2026-06-13-chip3-impl-session2-plan-v4-lock-final.md)
- ADR-0030 (reserved underscore namespace): [`docs/decisions/0030-reserved-underscore-namespace.md`](../../docs/decisions/0030-reserved-underscore-namespace.md)
- Placeholder taxonomy + template manifest: [`templates/MANIFEST.md`](./templates/MANIFEST.md)
- Fixture refresh discipline: [`tests/fixtures/expected/README.md`](./tests/fixtures/expected/README.md)
