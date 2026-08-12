# Chip 3 Implementation — Session 2 plan (v1)

**Date:** 2026-06-13
**Author:** Claude (session 1 → session 2 transition)
**Status:** v1 DRAFT — pending ChatGPT review pass per `feedback_planning_cadence.md`
**Parent plan:** `docs/sessions/2026-05-21-chip3-provisioning-subsystem-plan-v2.md` §9 (remaining steps)
**Session 1 PR:** [#146](https://github.com/elpisitsolutions/EdgeConnect/pull/146) — OPEN, MERGEABLE, awaiting user merge call

---

## 0. Branch / dependency decision (OPEN — needs user direction before kickoff)

PR #146 is open and not yet merged. Two paths for session 2's branch:

- **(A) — Merge #146 first.** Session 2 starts fresh from `master`. Cleanest history; session 2's PR diff stays small and focused on tests + auxiliary pieces. Recommended if no review pushback expected on #146.
- **(B) — Stack session 2 on `claude/chip3-impl-session1`.** Session 2 branches off session 1's HEAD. If #146 gets review changes requested, session 2 has to rebase. Acceptable if you want to keep #146 open longer for review feedback.

**Default recommendation:** (A), merge #146 first. Session 1 phases 1+2 were validated locally as cleanly as the sandbox allows, and the static-HTML rule + UI scope question (§4 below) means session 3 will have its own review cadence anyway — no value in keeping #146 lingering.

**This v1 plan assumes (A) and starts session 2 from `master` post-merge.** If you pick (B), the only thing that changes is the branch base — the scope below is identical.

---

## 1. Session 1 carry-forward (verbatim from PR #146 body)

These items were marked "deferred to user's pwsh-7 environment" or "deferred to session 2" in the PR:

- [ ] End-to-end smoke run: `pwsh ./tools/bulk-provision/generate.ps1 -Csv samples/sample-fanuc.csv -Sidecar samples/sample-fanuc.gateway.yml -Template template-fanuc -OutDir out/smoke -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 -GeneratedAt 2026-01-01T00:00:00Z`
- [ ] Deterministic-output regression (two pinned runs → byte-identical via `fc /b`)
- [ ] Pester tests for scope-guard rejections, deterministic-output, round-trip validate
- [ ] Modbus tag-CSV importer hook (v2 §1.3)
- [ ] Sidecar JSON schema
- [ ] Generated-config diff viewer (UI surface — see §4)

---

## 2. Proposed session 2 scope

In order of dependency:

### A. Deterministic-output verification (§5.4.4)

1. Run `generate.ps1` twice against `sample-fanuc` with pinned `-GatewayProvisioningId` + `-GeneratedAt`.
2. Confirm byte-identical outputs via `fc /b` (Windows) or `cmp` (Linux).
3. Commit one of the runs as `tools/bulk-provision/samples/expected-fanuc.gateway.json` — the frozen regression fixture for the Pester deterministic-output test.

**Done when:** committed expected fixture matches a fresh pinned run byte-for-byte.

### B. Pester test harness (`tools/bulk-provision/tests/`)

PowerShell Pester (v5) test files under the new `tests/` directory. Each `*.Tests.ps1`:

| File | Asserts |
|------|---------|
| `Substitute-Placeholders.Tests.ps1` | Per-row placeholder OUTSIDE Sources[] → throws with §5.5.3 message; per-gateway INSIDE Sources[] → throws; unresolved marker → throws with marker name; result not JSON → throws with deviceId; missing/extra placeholder dict keys → throw |
| `Canonicalize-Json.Tests.ps1` | UTF-8 no BOM output verified by reading file bytes; LF endings; root keys in locked order (`_provisioning` first); nested objects alphabetically sorted; output ends with single LF |
| `Generate.Tests.ps1` | Duplicate deviceId in CSV → throws; missing required column → throws; empty CSV → throws; happy-path round-trip generates the expected file count + populates `_provisioning` 9 fields |
| `Deterministic.Tests.ps1` | Two pinned runs → byte-identical output (asserts against `expected-fanuc.gateway.json`); generatedAt is the ONLY varying field across two unpinned runs |
| `RoundTripValidate.Tests.ps1` | Generated output passes through `tools/ValidateConfig` with exit code 0 for all three templates |

**Done when:** `Invoke-Pester` green from a clean checkout on pwsh 7.

### C. Sidecar JSON schema (`tools/bulk-provision/sidecar-schema.json`)

Codify the 9 per-gateway fields (see `templates/MANIFEST.md` Per-gateway table) as a JSON Schema. `generate.ps1` calls `NJsonSchema` to validate the parsed sidecar before substitution; failure aborts with the exact field + reason. Add a sub-test under `Generate.Tests.ps1` covering malformed sidecar rejection.

**Open question for ChatGPT review:** Schema embedded in `tools/bulk-provision/` vs. emitted into `docs/config-schemas/` like the gateway.json schema. Leaning toward `tools/bulk-provision/` because it's tool-local, not part of the canonical config surface.

### D. Missing sample fixtures

Currently only `sample-fanuc.{csv,gateway.yml}` ships. Session 2 adds:
- `samples/sample-brother.csv` + `samples/sample-brother.gateway.yml`
- `samples/sample-modbus.csv` + `samples/sample-modbus.gateway.yml`

Required for B's `RoundTripValidate.Tests.ps1` to cover all three templates.

### E. Operator README (`tools/bulk-provision/README.md`)

Operator-facing, not architect-facing. Sections:
1. What this tool does (one paragraph).
2. Prerequisites: pwsh 7, .NET 8 SDK (for the ValidateConfig invocation).
3. Quickstart: 3-command example using `sample-fanuc`.
4. CSV column reference + sidecar field reference (link to `templates/MANIFEST.md`).
5. Troubleshooting:
   - Suspect-roots warning (Levenshtein typo suggestion) — how to read it.
   - Anti-templating-engine guard errors — what they mean.
   - Schema validation failure — how to interpret.
6. Deterministic-output guarantee — what it does and doesn't promise.

**Done when:** committed and linked from the top-level README.

### F. Session 1 handoff doc (`docs/sessions/2026-06-13-chip3-impl-session1-handoff.md`)

Per standing rule (`feedback_handoff_branch_dependencies.md`), this lands on `master` before any "start session 2 cold" instruction. Captures: what shipped on #146, what's deferred, pwsh-7 prerequisite, branch-stacking story, link to v3 of this plan.

---

## 3. Out of scope for session 2 — defer to session 3

### G. Modbus tag-CSV importer hook (v2 §1.3)

Separate input flag (`-TagsCsv path/to/tags.csv`) that populates `Connection.tags[]` when `-Template template-modbus`. Defer because:

- Per-tag CSV column shape isn't locked yet — needs a planning pass against the existing Modbus tag wizard's data shape.
- May interact with the upcoming per-tag-CSV importer at the Studio layer (out of scope of chip 3 entirely).

### H. Generated-config diff viewer (UI surface)

A Studio screen that compares two `MANIFEST.txt` runs and visualizes what changed. **MUST start with a static HTML mockup** per `feedback_static_html_ui_review.md`. That mockup is a session 3 deliverable on its own — wiring into the Studio comes after sign-off.

---

## 4. Open questions for the ChatGPT review pass

1. **Pester version.** v5 is current; some EdgeConnect dev boxes may still be on v3/v4. Pin v5 explicitly in a `Pester.psd1` manifest under `tools/bulk-provision/tests/` or accept whatever's installed?
2. **Sidecar schema location** (see §C). `tools/bulk-provision/sidecar-schema.json` vs `docs/config-schemas/`.
3. **expected-fanuc.gateway.json fixture refresh policy.** When a template changes (e.g., template-fanuc-v2 ships), does the expected fixture need to be re-generated by hand, or do we wire a `-RegenerateFixtures` flag into generate.ps1 itself? Latter is convenient but creates a "the test fixture is whatever the generator currently emits" trap.
4. **README scope.** Operator-focused only (this plan's §E) or operator + architect (so future ADRs can link to one doc)? Leaning operator-only — architect notes already live in MANIFEST.md + the v2 plan + ADR-0030.
5. **Should session 2 also ship the `-RegenerateFixtures` flag or defer to session 3?** Depends on (3).
6. **Branch decision** (§0). User-input required.

---

## 5. Rough size estimate

| Item | LOC | Tests | Notes |
|------|-----|-------|-------|
| A — deterministic verify + expected fixture | 0 | 0 | manual + fixture commit |
| B — Pester harness | ~300 PS | ~25-30 tests | ramp time mostly Pester boilerplate |
| C — sidecar schema + validator wiring | ~80 PS + schema | 3-4 tests | leverages NJsonSchema already pulled |
| D — sample fixtures | ~30 bytes each | 0 | trivial |
| E — README | ~150 lines markdown | 0 | |
| F — handoff doc | ~80 lines markdown | 0 | |

**Total estimate:** 1-1.5 sessions. If §C runs long it spills into session 2.5.

---

## 6. Exit gate for session 2

Session 2 is complete when ALL of:
- [ ] PR #146 (session 1) merged to master.
- [ ] Deterministic-output regression fixture committed and Pester test asserts against it.
- [ ] `Invoke-Pester ./tools/bulk-provision/tests/` green from a clean checkout.
- [ ] All three templates have round-trip-validate Pester coverage (fanuc + brother + modbus sample fixtures all generate → ValidateConfig exit 0).
- [ ] Sidecar schema validates before substitution; malformed sidecar produces actionable error.
- [ ] Operator README linked from top-level repo README.
- [ ] Session 2 PR opened against master with both phases summarized.
- [ ] Session 1 handoff doc (§F) on master.

---

## 7. Process

Per `feedback_planning_cadence.md`:

1. ✅ **v1 (this doc)** — drafted, on `claude/chip3-impl-session2-plan` branch.
2. ⏳ **v2 (post-ChatGPT review)** — user sends v1 to ChatGPT, returns notes, I synthesize into v2.
3. ⏳ **v3 (reality-check)** — I scan v2 against current repo state, surface contradictions / dropped scope, lock open questions.
4. ⏳ **Implementation** — session 2 starts.

Per `feedback_pause_and_report.md`: every open question above is surfaced rather than silently resolved. Defaults are noted; choices are yours.
