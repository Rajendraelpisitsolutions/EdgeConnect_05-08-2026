# Expected fixtures — deterministic-output regression baseline

This directory holds frozen output trees from `generate.ps1` runs with
pinned ids. `Deterministic.Tests.ps1` asserts that fresh runs against
the three sample fixtures produce byte-identical trees.

## Layout

```
expected/
├── fanuc/    # frozen output of `generate.ps1` against sample-fanuc
├── brother/  # frozen output against sample-brother
└── modbus/   # frozen output against sample-modbus
```

Each tree contains:
- `<deviceId>.gateway.json` × 3 — one per CSV row.
- `run-summary.json` — run metadata.
- `MANIFEST.txt` — flat SHA-256 list.

## Refresh discipline (v4 §3.Q3 lock)

Fixtures are **never edited in place**. Two valid refresh triggers:

1. **A template version bumps** — when `template-<protocol>-v2.json` ships,
   the corresponding `expected/<protocol>-v2/` tree freezes alongside.
   The existing `expected/<protocol>/` tree stays bound to v1.

2. **A generator-logic fix** changes the canonical output bytes.
   In that case, the refresh is a DELIBERATE review event, not an
   automated regeneration. Follow the procedure below.

## Refresh procedure

```pwsh
# from C:\dev\EdgeConnect\tools\bulk-provision> (or equivalent on Linux)
pwsh ./generate.ps1 `
    -Csv ./samples/sample-fanuc.csv `
    -Sidecar ./samples/sample-fanuc.gateway.yml `
    -Template template-fanuc `
    -OutDir ./tests/fixtures/expected/fanuc `
    -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 `
    -GeneratedAt 2026-01-01T00:00:00Z

# Repeat for brother + modbus.

# Then:
#   1. git diff tests/fixtures/expected/
#   2. Review the diff carefully — every byte change is a template-
#      contract change.
#   3. Commit the diff with explicit human review, NOT auto-staged.
```

## Pinned values per template

| Template | GatewayProvisioningId | GeneratedAt |
|---|---|---|
| fanuc   | `11111111-1111-1111-1111-111111111111` | `2026-01-01T00:00:00Z` |
| brother | `22222222-2222-2222-2222-222222222222` | `2026-01-01T00:00:00Z` |
| modbus  | `33333333-3333-3333-3333-333333333333` | `2026-01-01T00:00:00Z` |

The `GatewayProvisioningId` values match each sample sidecar's pinned
id; using a different value here breaks the byte-comparison contract.

## Why this is NOT a `-RegenerateFixtures` flag

Per v4 §3.Q3: a "generator regenerates its own fixtures" flag would
create a circular trap — fixtures become "whatever the generator
currently emits," which means the test asserts nothing meaningful.
Manual refresh + human diff review is the discipline that keeps the
deterministic contract honest.
