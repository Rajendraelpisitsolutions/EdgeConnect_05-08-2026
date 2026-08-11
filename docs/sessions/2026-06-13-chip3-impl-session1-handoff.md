# Chip 3 Implementation — Session 1 handoff

**Date:** 2026-06-13
**Author:** Claude (closing session 1 / opening session 2)
**Status:** Pre-flight handoff. Read before starting session 2 cold.
**Locked plan it hands off to:** `docs/sessions/2026-06-13-chip3-impl-session2-plan-v4-lock-final.md`

---

## 0. Why this doc exists

Session 1 shipped two PRs:

- **#146** — Reserved-namespace precondition + bulk-provision generator skeleton
- **#147** — Session 2 plan trail (v1 → v4 lock-final)

Both are merged. This doc captures the transient state that didn't fit into commit messages or PR bodies — what's on disk, what's deferred, the prerequisites you need before running anything, and the boundary between "shipped + tested" and "shipped + needs your pwsh-7 box to verify."

**Read this once, then read v4. v4 is the source of truth for session 2 scope; this doc is the source of truth for what session 1 left behind.**

---

## 1. What's on master from session 1

### 1.1 Reserved-namespace precondition (#146 phase 1)

- `docs/decisions/0030-reserved-underscore-namespace.md` — ADR locking `_provisioning` and `_diagnostics` as reserved root namespace; documents capture-with-warning contract for non-`_`-prefixed unknowns.
- `src/ElpisEdgeConnect.Core/Configuration/GatewayConfiguration.cs` — new `ExtensionData` property:
  ```csharp
  [JsonExtensionData]
  public IDictionary<string, JsonElement>? ExtensionData { get; init; }
  ```
  Without this, the parser silently dropped unknown roots (including `_provisioning`).
- `tests/ElpisEdgeConnect.Core.Tests/Configuration/GatewayConfigurationTests.cs` — two new tests:
  - `UnderscorePrefixedRoot_SurvivesRoundTrip`
  - `NonUnderscoreUnknownRoot_CapturedInExtensionData_ForWarningSurface`
- `tools/ValidateConfig/{ValidateConfig.csproj,Program.cs}` — CLI wrapping `NJsonSchemaConfigurationValidator`. Exits 0/1/2/3 with suspect-roots warning + Levenshtein typo suggestions.

### 1.2 NU1903 MessagePack suppression (#146 phase 1)

`src/ElpisEdgeConnect.Core/ElpisEdgeConnect.Core.csproj` has `NU1903` in `<NoWarn>` with an explicit comment block: MessagePack 2.5.187 vulnerability promoted to error by `TreatWarningsAsErrors`. Surgical unblock pending the proper 2.x → 3.x upgrade (follow-up chip task `task_07b2ff3a`).

**Carry-forward:** the proper MessagePack upgrade is NOT chip 3's job. When that follow-up chip runs, the NoWarn entry comes back out.

### 1.3 Bulk-provision generator skeleton (#146 phase 2)

Under `tools/bulk-provision/`:

| File | Purpose |
|---|---|
| `.gitignore` | Ignores `out/` |
| `generate.ps1` | ~280 LOC orchestrator. Parses CSV + sidecar, substitutes placeholders, canonicalizes, writes per-row gateway.json files + `run-summary.json` + `MANIFEST.txt`, invokes ValidateConfig. `#requires -Version 7.0` |
| `lib/Canonicalize-Json.ps1` | UTF-8 no BOM, LF endings, alphabetical keys with locked root order (`_provisioning` first) |
| `lib/Substitute-Placeholders.ps1` | Pure literal `{{ key }}` substitution + anti-templating-engine scope guards |
| `templates/template-fanuc-v1.json` | FOCAS2 canonical shape, port 8193 |
| `templates/template-brother-v1.json` | Brother HTTP canonical shape |
| `templates/template-modbus-v1.json` | Modbus TCP shape, port 502, empty `tags: []` |
| `templates/MANIFEST.md` | Placeholder taxonomy, substitution contract, static-field invariants, forward-compat instructions |
| `samples/sample-fanuc.csv` | 3-row smoke fixture for fanuc |
| `samples/sample-fanuc.gateway.yml` | Per-gateway sidecar fixture |

### 1.4 Session 2 plan trail (#147)

`docs/sessions/2026-06-13-chip3-impl-session2-plan-{v1,v2,v3-reality-check,v4-lock-final}.md` — the full v1→v4 plan trail. **v4 is the locked source. Read it before starting session 2 implementation.**

---

## 2. Prerequisites to start session 2

### 2.1 Tooling

- **.NET 8 SDK** — required for the ValidateConfig CLI invocation inside generate.ps1 and for the upcoming ValidateSidecar CLI in session 2.5. Check: `dotnet --list-sdks` shows `8.0.x`.
- **PowerShell 7+ (pwsh)** — `generate.ps1` declares `#requires -Version 7.0`. The dev sandbox at the time of session 1 had Windows PowerShell 5.1 only; pwsh 7 must be installed before any smoke run or session 2 implementation that touches the generator. Check: `pwsh --version` shows `7.x`.
- **Pester v5+** — needed for the session 2 Pester harness. Check: `Get-Module Pester -ListAvailable | Where-Object Version -ge 5.0.0`. Install if missing: `Install-Module Pester -MinimumVersion 5.0 -Force -Scope CurrentUser`.
- **git config** — Windows boxes typically have `core.autocrlf=true`. Session 2's `.gitattributes` LF lock (v4 §3.F1) will handle this automatically for files under `tools/bulk-provision/**`, but if you're configuring a fresh box: don't change autocrlf globally; let .gitattributes do the work.

### 2.2 Local mosquitto broker (optional, only for end-to-end smoke later)

If you want to wire generated configs into a live EREMOS V2 / EdgeConnect run, a local MQTT broker on `localhost:1883` (anonymous) is required. Not needed for session 2's deterministic-fixture work.

---

## 3. Carry-forwards (deferred from session 1)

### 3.1 End-to-end smoke verification — NEEDS pwsh-7 box, and NEEDS A.B0 to land first

The session 1 sandbox could not invoke `generate.ps1` (PowerShell 5.1 only). Smoke verification + the deterministic-fixture freeze are deferred to your dev box.

**IMPORTANT ORDERING:** the smoke-and-freeze run CANNOT be the first session 2 task. With the current generator code, `run-summary.json` writes absolute machine paths (`C:\dev\EdgeConnect\…`), which means a fixture frozen from a current-code run will never match a freeze on any other box — the deterministic contract collapses before it starts. **v4 §6 step A.B0 (the `run-summary` path portability fix) must land first.** Only then is the smoke + freeze meaningful.

The exact pinned command (run AFTER A.B0 is in place):

```pwsh
# from C:\dev\EdgeConnect\tools\bulk-provision> (or equivalent on Linux)
pwsh ./generate.ps1 `
    -Csv ./samples/sample-fanuc.csv `
    -Sidecar ./samples/sample-fanuc.gateway.yml `
    -Template template-fanuc `
    -OutDir ./tests/fixtures/expected/fanuc `
    -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 `
    -GeneratedAt 2026-01-01T00:00:00Z
```

Expected: 3 gateway.json files + `run-summary.json` (with relative forward-slash paths) + `MANIFEST.txt` in `./tests/fixtures/expected/fanuc/`, all validating via the inline ValidateConfig call. Repeat for `template-brother` and `template-modbus` per v4 §6 step D. Same shape for the second pinned re-run to byte-compare.

### 3.2 Deterministic-output regression — NEEDS pwsh-7 box

Two pinned runs against `sample-fanuc` must produce byte-identical trees. Use `fc /b` on Windows or `cmp -r` on Linux. This is the v4 §6 step D deliverable.

### 3.3 Carry-forwards baked into v4 scope

| Item | v4 location | Session |
|---|---|---|
| `run-summary.json` absolute-path bug → relative + `/`-normalized | §3.Q2, §6 A.B0 | Session 2 |
| Stable `BulkProvision.*` error code prefixes | §6 A.B1 | Session 2 |
| `Set-ProvisioningBlock` regex fragility (F3) | §3.F3, §6 A.B1 | Session 2 |
| `.gitattributes` LF lock (F1) | §3.F1, §6 A.B2 | Session 2 |
| Brother + Modbus sample fixtures | §6 C | Session 2 |
| Sidecar schema + ValidateSidecar CLI | §2 contract, §6 E | Session 2.5 |

---

## 4. Known sharp edges

### 4.1 The sandbox where session 1 ran has no pwsh-7

Reflected in the commit message of `92c4e21` ("End-to-end smoke test … is deferred to the user's pwsh-7 environment"). Anyone restarting in a fresh sandbox must verify pwsh 7 is on PATH BEFORE attempting to run `generate.ps1` for the deterministic-fixture freeze.

### 4.2 Unicode-in-PowerShell trap (already mitigated)

Session 1 hit a parser error in `generate.ps1` because PS 5.1 reads UTF-8-without-BOM as Windows-1252, breaking strings that contain `—` / `▶` / `✓` / `⚠`. Fix already applied (ASCII-only string literals); template + lib files are clean. Session 2 should keep status messages ASCII-only by convention.

### 4.3 LinkedIn-marketing diffs in the worktree

Throughout session 1, two pre-existing modified files showed up in `git status`:
- `docs/marketing/linkedin-content-plan.md` (modified)
- `docs/sessions/2026-06-13-linkedin-posting-handoff.md` (modified)

These belong to a different in-flight track. **Do not stage them in chip-3 PRs.** They've been carried as uncommitted local diffs the whole session and survived the v4 PR merge.

### 4.4 ValidateConfig.csproj has a stale comment

The csproj's XML header mentions a future `tools/bulk-provision/lib/Validate-AgainstSchema.ps1` wrapper that was never written. `generate.ps1` invokes ValidateConfig directly via `dotnet run --project`. Cleanup queued for session 2 step G.

---

## 5. Next actions (locked, in order)

1. **You merge this pre-flight PR.** This file lands on master.
2. **Cut session 2 implementation branch** from updated master: `claude/chip3-impl-session2`.
3. **Work through v4 §6 in order** — sandbox-runnable subset first:
   - A.B0 — `run-summary.json` path portability fix (REQUIRED before any smoke/freeze)
   - A.B1 — `BulkProvision.*` error codes + F3 regex callback fix
   - A.B2 — repo-root `.gitattributes` LF lock
   - C — brother + modbus sample fixtures
   - G-partial — ValidateConfig.csproj stale-comment cleanup
   - F (write the Pester `*.Tests.ps1` files; runtime verification deferred to pwsh-7 box)
4. **Handoff back to your pwsh-7 box** for steps that the sandbox cannot run:
   - D — pinned smoke + freeze × 3 templates (Fanuc, Brother, Modbus), using the command in §3.1 above
   - F — verify Pester harness green via `Invoke-Tests.ps1`
   - G — finalize operator README with verified error messages
5. Session 2.5 follows after session 2 merges, against the v4 §2 ValidateSidecar contract.

---

## 6. Cross-references

- `docs/sessions/2026-06-13-chip3-impl-session2-plan-v4-lock-final.md` — **the source of truth for session 2 scope.**
- `docs/sessions/2026-06-13-chip3-impl-session2-plan-v3-reality-check.md` — rationale evidence for v4 decisions.
- `docs/sessions/2026-05-21-chip3-provisioning-subsystem-plan-v2.md` — parent plan (where the subsystem scope was originally locked).
- `docs/sessions/2026-05-21-chip3-provisioning-subsystem-v3-reality-check.md` — session 1 reality-check, including the ADR-0030 origin story.
- `docs/decisions/0030-reserved-underscore-namespace.md` — ADR for the reserved namespace.
- `tools/bulk-provision/templates/MANIFEST.md` — placeholder taxonomy.
- PR #146 (merged) — session 1 implementation.
- PR #147 (merged) — session 2 plan trail.
