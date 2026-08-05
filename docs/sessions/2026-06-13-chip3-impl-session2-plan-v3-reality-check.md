# Chip 3 Implementation — Session 2 plan (v3 reality-check)

**Date:** 2026-06-13
**Author:** Claude (post-v2-synthesis reality-check pass)
**Status:** v3 — LOCKED. Pre-flight unblocks; implementation may start once handoff doc lands.
**Predecessor:** `docs/sessions/2026-06-13-chip3-impl-session2-plan-v2.md`
**Cadence position:** v1 → ChatGPT review → v2 → **v3 (this doc)** → pre-flight (handoff doc) → implementation
**Method:** every locked decision below is grounded in a specific repo-state evidence reference. v2's four open questions are locked; three additional findings are surfaced and locked.

---

## 0. Reality-check methodology

For each v2 open question and for the broader plan I:
1. Read the actual code v2 referenced (ValidateConfig, NJsonSchemaConfigurationValidator, generate.ps1, lib/, MANIFEST).
2. Pressure-tested v2's recommended option against what's actually there.
3. Locked the decision OR flipped it when evidence pointed elsewhere.

The four v2 questions: **one flipped (Q1), three confirmed (Q2/Q3/Q4 — Q2 with added detail).** Three new findings: locked inline.

---

## 1. v2 open-question locks (with evidence)

### Q1 — NJsonSchema reuse strategy: **LOCKED → option (b) ValidateSidecar CLI** (FLIP from v2 recommendation)

**v2 recommended:** (a) extend `tools/ValidateConfig` with `-Schema <path>`.

**Reality-check evidence:**
- `tools/ValidateConfig/Program.cs` instantiates `NJsonSchemaConfigurationValidator` with NO parameters and validates against the hardwired gateway schema (line 64).
- `NJsonSchemaConfigurationValidator` constructor in `src/ElpisEdgeConnect.SchemaValidation/NJsonSchemaConfigurationValidator.cs:47-51`:
  ```csharp
  public NJsonSchemaConfigurationValidator() {
      var generator = ConfigurationSchemaFactory.CreateGenerator();
      _schema = generator.Generate(typeof(GatewayConfiguration));  // HARDWIRED
  }
  ```
  The schema is generated from `typeof(GatewayConfiguration)`, NOT loaded from disk. There is no public surface to swap it for an arbitrary on-disk schema.
- `Program.cs:78-123` has a 46-line ADR-0030 suspect-roots branch that reads `GatewayConfiguration.ExtensionData`. That branch is gateway-specific and does not apply to sidecar validation.
- Adding `-Schema <path>` therefore requires:
  - A new constructor on the validator that takes a `JsonSchema` instance (or a path).
  - A conditional in Program.cs to either run the suspect-roots branch (no `-Schema`) or skip it (`-Schema` set).
  - Two distinct error projections (gateway-aware vs schema-only).

That's ~40 LOC of conditional logic polluting a tool whose comment header reads "validates a gateway.json against the canonical schema." Wrong tool, wrong shape.

**Lock:** new `tools/ValidateSidecar/` CLI, ~80 LOC, single-purpose, references `NJsonSchema` directly:
```csharp
var schema = await JsonSchema.FromFileAsync(schemaPath).ConfigureAwait(false);
var errors = schema.Validate(json);
// Project errors into operator-friendly messages, exit 0/1/2/3.
```
Mirrors the existing `tools/*/Program.cs` single-purpose pattern (LicenseGen, SchemaGen, ModbusCsvImport, ValidateConfig). No reuse of `NJsonSchemaConfigurationValidator` (that class is permanently gateway-shaped per its B2 phase-1 lock).

**Also during this scope:** clean up `tools/ValidateConfig/ValidateConfig.csproj` comment that references the never-written `tools/bulk-provision/lib/Validate-AgainstSchema.ps1`. `generate.ps1` invokes ValidateConfig inline via `dotnet run --project` and that pattern works — no wrapper file is needed.

### Q2 — `Resolve-Path -Relative` base directory: **LOCKED → $PWD-relative + forward-slash normalization** (confirms v2 option ii with one addition)

**v2 recommended:** (i) OutDir parent.

**Reality-check evidence:**
- `generate.ps1` is documented (header `.EXAMPLE`) to be invoked from `tools/bulk-provision/` cwd (paths in the example are `.\samples\sample-fanuc.csv`).
- PowerShell's `Resolve-Path -LiteralPath x -Relative` is anchored on `$PWD`, not on any parameter argument's parent. There is no built-in "relative to OutDir parent" mode; we'd have to compute it manually.
- The CSV's parent directory and OutDir's parent directory are not always related — operator could have `-Csv ../config-master/sites.csv -OutDir gateways/site-a/`. Forcing one relative-to-the-other creates surprising paths.
- The natural anchor that survives across operator boxes is the operator's cwd. If they consistently run from `tools/bulk-provision/`, the paths consistently land as `samples/sample-fanuc.csv` etc.

**v2 missed:** Windows `Resolve-Path -Relative` returns backslash separators (`.\samples\…`) while Linux returns forward-slash (`./samples/…`). Both targets must produce the SAME bytes for the deterministic-tree contract. Therefore: after `Resolve-Path -Relative`, normalize `\` → `/` before writing into `run-summary.json`.

**Lock:**
```ps1
$csvRel     = (Resolve-Path -LiteralPath $Csv -Relative)     -replace '\\', '/'
$sidecarRel = (Resolve-Path -LiteralPath $Sidecar -Relative) -replace '\\', '/'
```
Pinned-run cwd is locked to `tools/bulk-provision/`. The deterministic-fixture freeze command must be documented with that cwd.

### Q3 — Fixture refresh discipline: **LOCKED → manual refresh, no helper script** (confirms v2)

**Reality-check evidence:**
- `tools/bulk-provision/templates/MANIFEST.md` "Adding a new template" section already documents the discipline: when content needs to change, bump the version and ship `-v2.json` alongside `-v1.json`. Templates are version-locked at the filename layer.
- That means existing fixtures are NEVER edited in place — they're tied to a specific template version. New fixture commits accompany new template versions.
- A helper script for "regenerate fixtures" would imply "the current generator is the source of truth for what's correct," which is exactly the circular trap ChatGPT flagged.
- The only context where a helper would help: bulk re-freeze after a generator-logic change (not a template change). That hasn't happened yet and isn't planned for session 2; YAGNI.

**Lock:** manual refresh. The discipline note goes in `tests/fixtures/expected/README.md` (one paragraph) explaining: "to refresh, bump the template version and add a new fixture tree alongside the old one. To re-freeze after a generator bug-fix, run the pinned commands in v3 §3.A and commit the diff with explicit human review."

### Q4 — Brother / Modbus deterministic freezes: **LOCKED → freeze all three** (confirms v2's "recommend all three" question option)

**Reality-check evidence:**
- Canonicalization (`lib/Canonicalize-Json.ps1`) is template-agnostic. Same code path runs across all three templates.
- BUT: each template has distinct structural quirks that exercise different canonicalization branches:
  - Fanuc: 9 dataPoints prefixes (medium array)
  - Brother: 8 dataPoints prefixes (medium array)
  - Modbus: empty `tags: []` array AND `Polling.IntervalMs = 1000` (vs 3000 for others — number formatting)
- Empty-array canonicalization is the specific branch in `Write-CanonicalArray:207-209` (returns `[]` literal vs the indented multi-item form). Without modbus, that branch has zero deterministic-output coverage.
- Storage cost per fixture tree: ~3-5 KB × 3 = ~10-15 KB total. Marginal.

**Lock:** freeze all three. Fixture trees committed at:
- `tools/bulk-provision/tests/fixtures/expected/fanuc/`
- `tools/bulk-provision/tests/fixtures/expected/brother/`
- `tools/bulk-provision/tests/fixtures/expected/modbus/`

`Deterministic.Tests.ps1` asserts byte-equality across all three.

---

## 2. New v3 findings (LOCKED — surfaced during reality-check)

### F1 — Git CRLF auto-conversion will break the deterministic-tree contract on Windows

**Evidence:** Every `git add` during this session produced warnings like
```
warning: in the working copy of 'tools/bulk-provision/generate.ps1', LF will be replaced by CRLF the next time Git touches it
```
That's git's `core.autocrlf=true` (Windows default) converting LF to CRLF on checkout. The canonicalize layer writes LF on output, but if the fixture files in `tests/fixtures/expected/` get CRLF on a fresh `git clone` on Windows, the byte comparison against a freshly generated LF tree will FAIL on the very test that's supposed to prove determinism.

**Lock:** Ship `tools/bulk-provision/.gitattributes` forcing LF on all fixture + template + sample files:
```
# Force LF on every file the deterministic-output contract touches.
# Without this, Windows git checkout converts LF → CRLF and the byte-
# identical fixture comparison in Deterministic.Tests.ps1 fails.
tests/fixtures/expected/**/*.json text eol=lf
tests/fixtures/expected/**/*.txt  text eol=lf
templates/*.json                   text eol=lf
samples/*.json                     text eol=lf
samples/*.csv                      text eol=lf
samples/*.yml                      text eol=lf
*.ps1                              text eol=lf
```
Pester wrapper script also forced to LF (matches the `#requires -Version 7.0` pwsh-7-on-Linux pattern). Add this as session 2 scope item **B2**.

### F2 — Fixture refresh requires the pinned-run cwd to be unambiguous

**Evidence:** v2 §A's pinned-run example was:
```
./tools/bulk-provision/generate.ps1 -Csv samples/sample-fanuc.csv ...
```
That implies cwd is the repo root (so `./tools/bulk-provision/generate.ps1` resolves) but `-Csv samples/sample-fanuc.csv` then resolves to `samples/sample-fanuc.csv` from the REPO ROOT, not from `tools/bulk-provision/samples/sample-fanuc.csv`. Path inconsistency.

The deterministic fixture commit must be reproducible by anyone running the same command — so the cwd has to be a locked convention.

**Lock:** Pinned-run cwd is `tools/bulk-provision/`. The exact pinned command for §3.A becomes:
```pwsh
# from C:\dev\EdgeConnect\tools\bulk-provision> (or its non-Windows equivalent)
pwsh ./generate.ps1 `
    -Csv ./samples/sample-fanuc.csv `
    -Sidecar ./samples/sample-fanuc.gateway.yml `
    -Template template-fanuc `
    -OutDir ./tests/fixtures/expected/fanuc `
    -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 `
    -GeneratedAt 2026-01-01T00:00:00Z
```
Q2's `Resolve-Path -Relative` will then produce `./samples/sample-fanuc.csv` (anchored to `tools/bulk-provision/`) — portable across hosts.

This goes in `tests/fixtures/expected/README.md` and operator README §6 (Deterministic-output guarantee).

### F3 — `Set-ProvisioningBlock` regex contains a fragile inverse-escape that needs cleanup

**Evidence:** Session 1's `lib/Substitute-Placeholders.ps1:148`:
```ps1
return [regex]::Replace($Json, $pattern, [System.Text.RegularExpressions.Regex]::Escape($replacement) -replace '\\(.)','$1')
```
The `Regex::Escape` followed by `-replace '\\(.)','$1'` UNDOES the regex-escape. Net behavior is "use $replacement as-is, but pretend we cared about escaping." It works because the replacement string doesn't contain regex backreference metacharacters (`$1`, `$$`, etc.) — but if a future provisioning block ever contained a literal `$`, this would silently break.

**Lock:** Replace the call with `[regex]::Replace($Json, $pattern, { param($m) $replacement })` (callback-based replacement, which bypasses backreference interpretation entirely). 2-line change. Add to scope item **B1** alongside the error-code prefixes.

---

## 3. Locked session 2 scope (final, ordered for implementation)

Identical to v2 §2 except:
- Q1 lock changes "extend ValidateConfig" → "new ValidateSidecar CLI"
- Q2 lock adds the `\` → `/` normalization
- Q4 lock expands "fanuc only" → "all three templates"
- F1 adds new scope item B2 (.gitattributes)
- F2 locks the pinned-run cwd
- F3 adds the regex cleanup to B1

### Pre-flight (NOT in session 2 implementation PR)

- [ ] Session 1 handoff doc `docs/sessions/2026-06-13-chip3-impl-session1-handoff.md` lands on master via its own small PR. Per standing rule.

### Session 2 implementation order

| Step | Description | Files touched |
|---|---|---|
| **A** | Generator hardening (B0+B1+B2 below) — must land before fixture freeze | `generate.ps1`, `lib/Substitute-Placeholders.ps1`, new `.gitattributes` |
| **B0** | `run-summary.json` path portability fix (`Resolve-Path -Relative` + `\` → `/`) | `generate.ps1` |
| **B1** | Stable `BulkProvision.*` error code prefixes on every throw + Set-ProvisioningBlock regex cleanup (F3) | `generate.ps1`, `lib/Substitute-Placeholders.ps1` |
| **B2** | `.gitattributes` LF lock on fixtures/templates/samples/scripts (F1) | new `tools/bulk-provision/.gitattributes` |
| **C** | Brother + Modbus sample fixtures | new `samples/sample-brother.{csv,gateway.yml}`, `samples/sample-modbus.{csv,gateway.yml}` |
| **D** | Pinned-run deterministic freeze for all three templates (Q4) | new `tests/fixtures/expected/{fanuc,brother,modbus}/`, new `tests/fixtures/expected/README.md` |
| **E** | Sidecar schema + new `tools/ValidateSidecar/` CLI (Q1) + generate.ps1 wiring | new `sidecar-schema.json`, new `tools/ValidateSidecar/{ValidateSidecar.csproj,Program.cs}`, `generate.ps1` |
| **F** | Pester harness — `Invoke-Tests.ps1` + 7 test files per v2 §D | new `tests/Invoke-Tests.ps1`, new `tests/*.Tests.ps1` |
| **G** | Operator README + ValidateConfig.csproj comment cleanup | new `tools/bulk-provision/README.md`, edit `tools/ValidateConfig/ValidateConfig.csproj` |

### Hard dependency graph

```
A.B0 (run-summary fix) ───┐
A.B1 (error codes)        ├──→ D (freeze) ──→ F.Deterministic.Tests
A.B2 (.gitattributes) ────┘
                          
C (brother+modbus fixtures) ──→ D, F.RoundTripValidate.Tests
E (ValidateSidecar + schema) ──→ F.Sidecar.Tests
F (Pester harness) ──→ G (README references error codes from F's tests)
```

### Exit gate for session 2

PR is complete when ALL:

- [x] Pre-flight gate clean (session 1 PR merged ✓, handoff doc on master TBD).
- [ ] `tests/fixtures/expected/{fanuc,brother,modbus}/` committed; pinned-run cwd documented; reruns produce byte-identical trees.
- [ ] `Invoke-Tests.ps1` enforces Pester v5+ and runs all `*.Tests.ps1` green from a clean checkout.
- [ ] Round-trip validate green across all three templates.
- [ ] Sidecar schema rejects malformed sidecars with operator-friendly wrapped messages.
- [ ] `run-summary.json` paths are forward-slash-relative, portable across Windows + Linux.
- [ ] All throws carry `BulkProvision.*` error code prefixes; Pester asserts on codes + tokens.
- [ ] `Set-ProvisioningBlock` regex cleanup (F3) applied.
- [ ] `.gitattributes` LF lock committed; verified via `git check-attr -a tests/fixtures/expected/fanuc/cnc-001.gateway.json` returns `eol: lf`.
- [ ] Operator README linked from top-level repo README.
- [ ] ValidateConfig.csproj's stale wrapper-file comment cleaned up.
- [ ] Session 2 implementation PR opened against master.

---

## 4. Revised size estimate (post-reality-check)

| Item | LOC | Tests | Delta from v2 |
|---|---|---|---|
| Pre-flight: session 1 handoff doc | ~80 md | 0 | unchanged |
| A.B0 — run-summary path fix + slash normalize | ~6 PS | 0 | +1 LOC for normalization |
| A.B1 — error code prefixes + regex cleanup | ~35 PS | 0 | +5 LOC for F3 regex cleanup |
| A.B2 — .gitattributes | ~10 lines | 0 | **NEW** |
| C — brother + modbus sample fixtures | ~60 bytes | 0 | unchanged |
| D — pinned-run freeze × 3 templates + fixture README | ~80 lines md + 3 trees | 0 | **was fanuc-only; now ×3** |
| E — ValidateSidecar CLI (NEW) + sidecar-schema.json + wiring | ~80 C# + ~40 schema + ~20 PS | 0 | **bigger than v2's "extend ValidateConfig" estimate** |
| F — Pester harness (Invoke-Tests.ps1 + 7 *.Tests.ps1) | ~400 PS | ~32 tests | +2 tests (positive guard + array order) |
| G — operator README + ValidateConfig.csproj cleanup | ~180 md + 3 lines | 0 | +3 LOC for the csproj cleanup |

**Revised total estimate:** 1.5-2 sessions. The ValidateSidecar CLI is the swing factor — a new .NET project under `tools/` requires solution-file update, csproj boilerplate, the small wrapper logic, build verification, all consuming time the v2 estimate didn't budget for.

If session 2 is constrained to ~1 session: defer E (sidecar schema + ValidateSidecar) into a session 2.5. Session 2 then ships A + C + D + F + G and the Pester harness's `Sidecar.Tests.ps1` is stubbed (`pending: "sidecar schema not yet in scope"`). **Default recommendation: ship the full v3 scope as one session 2.**

---

## 5. Process state after v3 lock

1. ✅ v1
2. ✅ ChatGPT review
3. ✅ v2 synthesis
4. ✅ **v3 reality-check (this doc)** — committed to same branch + PR #147.
5. ⏳ **PR #147 merges** when user approves v3.
6. ⏳ Pre-flight: session 1 handoff doc on master via tiny PR.
7. ⏳ Session 2 implementation PR.

User actions required:
- Approve v3 lock (or pushback on any of the 4 locks / 3 new findings).
- Decide on session 2 size: full scope (~1.5-2 sessions) vs split at the sidecar boundary (~1 session + sidecar-in-session-2.5).
