# Chip 3 Implementation — Session 2 plan (v4: lock-approval final)

**Date:** 2026-06-13
**Author:** Claude (post-ChatGPT lock approval pass)
**Status:** **LOCKED — implementation may begin against this doc.** Supersedes v3 for any ambiguity; v3's reality-check evidence remains authoritative.
**Predecessor:** `docs/sessions/2026-06-13-chip3-impl-session2-plan-v3-reality-check.md`
**Cadence position:** v1 → ChatGPT review → v2 → reality-check → v3 → **ChatGPT lock approval → v4 (this doc)** → pre-flight → implementation

---

## 0. Why v4 exists

ChatGPT's v3 review verdict: **"Approve v3 lock, with the YAML-parse caveat added,"** plus seven specific addenda and a recommendation to **split session 2 at the sidecar boundary**. v4 folds those into a single locked doc so implementation runs against one source of truth rather than v3 plus a thread of replies. Every clause below is grounded in v3 evidence or ChatGPT's lock-approval message; nothing new is introduced silently.

---

## 1. Session-split decision: ACCEPTED

| Session | Scope | Rationale |
|---|---|---|
| **Session 2** | Deterministic foundation + Pester harness + LF lock + cwd discipline + regex bug fix + operator README. No sidecar schema, no ValidateSidecar. | Ships the regression-safety foundation as a tight, mergeable PR. |
| **Session 2.5** | `tools/ValidateSidecar/` CLI + `sidecar-schema.json` + `generate.ps1` validation hook + malformed-sidecar Pester tests + README troubleshooting addition. | The Q1 flip turned sidecar validation from "small wiring" into a new .NET tool. Carrying it inside session 2 would delay the foundation; isolating it as session 2.5 keeps both PRs reviewable. |

Per ChatGPT: "I would rather get session 2 merged as a tight regression-safety PR than make it carry a new validator executable at the same time." Locked.

---

## 2. ValidateSidecar CLI contract (LOCKED for session 2.5)

ChatGPT required the YAML-parse caveat be explicit in the locked contract. Here it is.

### CLI surface

```text
ValidateSidecar
    --schema  <path-to-sidecar-schema.json>      [required]
    --sidecar <path-to-sidecar.{yml,yaml,json}>  [required]
    --verbose                                    [optional]
```

> Post-v4-approval precision fix (ChatGPT lock-approval verdict): since
> ValidateSidecar is a new .NET CLI rather than a PowerShell advanced
> function, `-Verbose` is ambiguous without being a declared flag. The
> `--verbose` switch is part of the contract.

### Acceptance criteria

```text
exit 0 = sidecar is well-formed AND validates against schema
exit 1 = schema-validation failure (sidecar parses but violates schema)
exit 2 = sidecar / schema file not found or unreadable
exit 3 = sidecar parse failure (malformed YAML / JSON)
exit 4 = unexpected internal error

stderr lines on failure include:
    <field-path-or-root>: <reason>
    operator-friendly wrapped messages

diagnostic rule:
    operator-facing stderr NEVER emits raw NJsonSchema diagnostics
        by default;
    raw NJsonSchema diagnostics MAY be emitted to stderr ONLY when
        --verbose is supplied.

constraints:
    NO ProjectReference to ElpisEdgeConnect.Core
    NO use of GatewayConfiguration type
    NO ADR-0030 suspect-roots logic
    YAML sidecar parsed BEFORE schema validation
    YAML → object → JSON-serialize → schema-validate pipeline
```

### YAML parsing implementation note

The .NET ecosystem has no first-party YAML parser in BCL. Two reasonable choices:

- **YamlDotNet** (de-facto standard, MIT) — `IDeserializer.Deserialize<Dictionary<string, object>>(yamlText)`, then `JsonSerializer.Serialize(dict)` → JSON string → hand to NJsonSchema. Stable, widely used.
- **Hand-rolled flat-keys parser** mirroring the one already in `generate.ps1` (Read-Sidecar → ConvertFrom-Yaml-Minimal at line 109-129). Zero dependency, but commits us to "sidecars stay one-level-deep forever."

**Lock (provisional, may revisit in session 2.5 kickoff):** YamlDotNet. Schemas may grow nested fields later (sub-objects for MQTT, optional fleet metadata); a real parser is cheaper than a flat-parser-with-escape-hatch. Decision deferred to session 2.5 ADR if it turns into a debate.

### Schema portability discipline

Per ChatGPT: "Avoid relying on fancy 2020-12-only schema features unless the implementation test proves them locally. Keep the sidecar schema boring and portable."

Locked allowlist of constructs in `sidecar-schema.json`:
```text
type
required
properties
additionalProperties: false
enum
pattern
format (only "uuid" — proven by ValidateConfig already)
minimum / maximum
minLength
```

Anything outside this list requires a per-feature local proof (small test against NJsonSchema 11.1.0) before it goes in the schema.

---

## 3. Locked-by-lock decisions (v3 locks + ChatGPT addenda)

### Q1 — ValidateSidecar CLI (LOCKED, session 2.5)

v3 lock: new CLI instead of extending ValidateConfig. Confirmed.

**ChatGPT addenda:**
- CLI surface contract spelled out in v4 §2 (above). Locked.
- YAML → object → JSON → schema validation pipeline locked.
- Schema-feature allowlist locked.

### Q2 — `$PWD`-relative path serialization (LOCKED, session 2)

v3 lock: `$PWD`-relative + `\` → `/` normalization. Confirmed.

**ChatGPT addenda:**
- Normalized `/` paths are for **serialized output only** (run-summary.json, MANIFEST.txt) — NOT for filesystem operations inside the script. Filesystem ops use whatever PowerShell hands back from Resolve-Path natively.
- Pester assertion is on **serialized output**, not on internal script behavior:
  ```text
  Windows and Linux relative path serialization uses /     ← CORRECT
  the script internally uses / for all paths               ← WRONG; over-tests
  ```

### Q3 — Manual fixture refresh (LOCKED, session 2)

v3 lock: no `-RegenerateFixtures` flag. Confirmed.

**ChatGPT addendum (location flip):**
- Refresh discipline note goes under `tests/fixtures/expected/README.md` (dev-facing), NOT the operator README. The 4-step procedure:
  ```text
  To refresh expected fixtures:
  1. Run the pinned generation command from tools/bulk-provision/.
  2. Compare the full generated tree against tests/fixtures/expected/.
  3. Copy changed files manually.
  4. Review the diff as a template-contract change.
  ```

### Q4 — Freeze all three templates (LOCKED, session 2)

v3 lock: freeze fanuc + brother + modbus. Confirmed.

**ChatGPT addendum (test-shape sharpening):**
- Layout exactly as v3 §1 Q4:
  ```text
  tools/bulk-provision/tests/fixtures/expected/fanuc/
  tools/bulk-provision/tests/fixtures/expected/brother/
  tools/bulk-provision/tests/fixtures/expected/modbus/
  ```
- `Deterministic.Tests.ps1` compares **full output tree**:
  ```text
  same relative file paths
  same file count
  same bytes per file
  ```
  Not just `*.gateway.json`. Covers `run-summary.json` + `MANIFEST.txt` too.

### F1 — `.gitattributes` LF lock (LOCKED, session 2)

v3 lock: ship .gitattributes. Confirmed.

**ChatGPT addendum (scope tightening):**
- Lock is path-scoped to `tools/bulk-provision/**`, NOT global:
  ```gitattributes
  tools/bulk-provision/**/*.ps1  text eol=lf
  tools/bulk-provision/**/*.psm1 text eol=lf
  tools/bulk-provision/**/*.psd1 text eol=lf
  tools/bulk-provision/**/*.json text eol=lf
  tools/bulk-provision/**/*.yml  text eol=lf
  tools/bulk-provision/**/*.yaml text eol=lf
  tools/bulk-provision/**/*.csv  text eol=lf
  tools/bulk-provision/**/*.md   text eol=lf
  tools/bulk-provision/**/*.txt  text eol=lf
  ```
- File lives at **repo-root** `.gitattributes` (so the path patterns are repo-relative), NOT at `tools/bulk-provision/.gitattributes`. If the repo already has a root `.gitattributes`, append to it; otherwise create.
- ValidateSidecar's `.cs` / `.csproj` lines added in session 2.5 only.

### F2 — Pinned-run cwd discipline (LOCKED, session 2)

v3 lock: cwd is `tools/bulk-provision/`. Confirmed.

**ChatGPT addenda (test enforcement):**
- Pester tests enforce cwd explicitly per test:
  ```powershell
  $BulkProvisionRoot = Resolve-Path "$PSScriptRoot/.."
  Push-Location $BulkProvisionRoot
  try {
      # run generator
  } finally {
      Pop-Location
  }
  ```
- Operator README quickstart uses the same cwd. No "works from my shell" surprises.

### F3 — Regex callback replacement (LOCKED, session 2)

v3 lock: callback-based regex replacement in `Set-ProvisioningBlock`. Confirmed.

**ChatGPT addendum (regression test, new):**
- Add a Pester test where a placeholder value contains adversarial replacement characters: `$`, `\`, `$1`. The test asserts the value is preserved literally, not interpreted as a regex backreference pattern.
- Test data: a CSV row with `deviceName = "Mill `$Pro-`$1\Site"` (or equivalent adversarial string). Generated config must contain `Mill $Pro-$1\Site` verbatim, NOT a regex-interpreted form.

---

## 4. Session 2 exit gate (LOCKED, narrower than v3)

Per ChatGPT's revised gates. Session 2 implementation PR is complete when ALL:

- [x] PR #146 merged to master.
- [ ] Session 1 handoff doc on master (separate pre-flight PR).
- [ ] Pester v5 wrapper / version check committed at `tools/bulk-provision/tests/Invoke-Tests.ps1`.
- [ ] Brother and Modbus sample fixtures added at `samples/sample-{brother,modbus}.{csv,gateway.yml}`.
- [ ] Expected fixtures frozen for Fanuc, Brother, and Modbus under `tests/fixtures/expected/{fanuc,brother,modbus}/`.
- [ ] Deterministic test compares full generated output trees (file count + relative paths + bytes per file), not just `*.gateway.json`.
- [ ] Path serialization in `run-summary.json` normalized to `/`.
- [ ] `.gitattributes` LF lock added (repo-root, path-scoped to `tools/bulk-provision/**`).
- [ ] Pinned deterministic runs executed from `tools/bulk-provision/` cwd; documented in fixture README.
- [ ] Callback-based placeholder replacement fix included in `lib/Substitute-Placeholders.ps1`.
- [ ] Regression test covers literal replacements containing `$`, `\`, and `$1`.
- [ ] Round-trip ValidateConfig coverage exists for all three generated templates.
- [ ] Operator README updated and linked from top-level repo README.
- [ ] ValidateConfig.csproj stale wrapper-file comment removed or corrected.
- [ ] Session 2 implementation PR opened against master.

**Not in session 2 exit gate** (moved to 2.5): sidecar schema, ValidateSidecar CLI, malformed-sidecar tests, generate.ps1 validation hook.

---

## 5. Session 2.5 exit gate (NEW, LOCKED)

Session 2.5 implementation PR is complete when ALL:

- [ ] `tools/ValidateSidecar/{ValidateSidecar.csproj,Program.cs}` committed; CLI surface per v4 §2 contract.
- [ ] CLI validates parsed YAML sidecar data against `tools/bulk-provision/sidecar-schema.json`.
- [ ] CLI has stable exit-code behavior per v4 §2 acceptance criteria.
- [ ] `sidecar-schema.json` committed; uses only the v4 §2 portable-feature allowlist.
- [ ] `generate.ps1` invokes sidecar validation BEFORE substitution; failure aborts with operator-friendly error.
- [ ] YAML parser choice locked (YamlDotNet vs hand-rolled) with an ADR if it warrants one.
- [ ] Malformed-sidecar Pester tests cover: missing required field, extra unknown field (additionalProperties:false), wrong type on a numeric field, invalid enum, invalid pattern.
- [ ] Wrapped error projection asserted to NOT leak raw NJsonSchema diagnostics into operator-facing stderr by default; raw diagnostics emitted only when `--verbose` is supplied.
- [ ] Operator README troubleshooting section updated with sidecar-validation error guidance.
- [ ] Session 2.5 implementation PR opened against master.
- [ ] Forward-compat for nested sidecar fields (sub-objects) addressed in YAML-parser choice or documented as a known boundary.

---

## 6. Implementation order (LOCKED for session 2)

Identical to v3 §3 minus E:

```
Pre-flight: session 1 handoff doc → master (separate small PR)

Session 2:
A.B0 — run-summary path portability fix (incl. \ → / normalization)
A.B1 — BulkProvision.* error code prefixes + F3 regex callback fix
A.B2 — .gitattributes LF lock (repo-root, path-scoped)
C    — brother + modbus sample fixtures
D    — pinned-run deterministic freeze × 3 templates + fixture README
F    — Pester harness (Invoke-Tests + 5 *.Tests.ps1; sidecar test stubbed)
G    — operator README + ValidateConfig.csproj cleanup

Session 2.5 (separate PR after session 2 merges):
E    — sidecar-schema.json + ValidateSidecar CLI + generate.ps1 wiring
F'   — Sidecar.Tests.ps1 fleshed out (was stubbed in session 2)
G'   — README sidecar troubleshooting addendum
```

Pester test files in session 2 (5, not 7 — sidecar test stubbed `pending`):

1. `Substitute-Placeholders.Tests.ps1` — neg + pos guard tests, F3 adversarial-replacement test.
2. `Canonicalize-Json.Tests.ps1` — UTF-8 no BOM, LF, root order, object-key sort, array order preserved, single trailing LF.
3. `Generate.Tests.ps1` — CSV validation, happy-path, `_provisioning` 9 fields, path serialization uses `/`.
4. `Deterministic.Tests.ps1` — pinned-both byte-equality vs frozen fixtures (all 3 templates, full tree); pinned-id-only with generatedAt-sentinel normalization.
5. `RoundTripValidate.Tests.ps1` — generator → ValidateConfig exit 0 for all 3 templates.

Plus stubbed:
- `Sidecar.Tests.ps1` — `It "validates sidecar schema" -Pending "deferred to session 2.5"` placeholder.

---

## 7. Process state after v4 lock

1. ✅ v1
2. ✅ ChatGPT review
3. ✅ v2 synthesis
4. ✅ v3 reality-check
5. ✅ ChatGPT lock approval with caveats
6. ✅ **v4 lock-final (this doc)**
7. ⏳ PR #147 merges with v4 as the locked plan
8. ⏳ Pre-flight: session 1 handoff doc → master via tiny PR
9. ⏳ Session 2 implementation
10. ⏳ Session 2.5 implementation (after session 2 merges)

**No more planning iterations expected.** v4 supersedes v3 for any ambiguity; v3's reality-check evidence is preserved as the rationale source. Implementation proceeds against v4's §3 + §4 + §5 + §6.
