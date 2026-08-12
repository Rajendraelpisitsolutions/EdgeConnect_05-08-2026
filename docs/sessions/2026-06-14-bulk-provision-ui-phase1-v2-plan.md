# Bulk-Provision UI Phase 1 — v2 plan (post-ChatGPT review)

**Date:** 2026-06-14
**Author:** Claude (post-v1-review synthesis)
**Status:** v2 — pending v3 reality-check.
**Predecessor:** `docs/sessions/2026-06-14-bulk-provision-ui-phase1-v1-plan.md`
**Customer context:** 100 CNCs — FOCAS2 (Fanuc) + Brother HTTP (Brother Speedio etc.) + MTConnect (Okuma). 64 same tags TODAY across all 100, Tooling-driven divergence TOMORROW.

---

## 0. Review decisions incorporated in v2

ChatGPT's v1 verdict: **"Approve direction, request v2 before lock."** Six Q-decisions + ten required changes. All folded below.

| Decision | Resolution |
|---|---|
| **Q1 — Per-row protocol** | One protocol per run. Mixed-protocol CSV out of scope. NEW: "Create another batch" UX on the success screen makes the two-run workflow feel intentional. |
| **Q2 — MTConnect baseline dataPoints** | NOT a blind universal baseline. Phase 1 baseline derived from the customer's current 64-tag requirement. NEW: `64-tag parity artifact` (§5) documenting FOCAS2 ↔ MTConnect semantic mapping. Bulk-provision still does NOT probe at runtime. Tooling tags excluded from Phase 1 baseline. |
| **Q3 — MTConnect CSV column** | FLIPPED from v1's default: use `baseUrl` for MTConnect CSV, not `host`. Template uses `{{ baseUrl }}` verbatim. No hardcoded `http://{{ host }}:5000/`. UI shows protocol-specific CSV help. |
| **Q4 — Sources page entry** | Confirmed: Bulk import button on Sources page header only. No Connect-a-device branch in Phase 1. |
| **Q5 — Mockup vs Razor alignment** | 1:1 USER-FACING (states, wording, validation moments, summary layout). Razor component structure may differ internally. Any user-visible flow change requires a mockup amendment PR before implementation. |
| **Q6 — Cadence** | NOT lightest. v1 → v2 → **v3 focused reality-check** → lock → PR M mockup → PR I-0/I-1/I-2 implementation. v3 scope tightly bounded to the 6 questions in §11. |

| # | Required change | Where in v2 |
|---|---|---|
| 1 | Session 2.5 merged as hard precondition | §1 |
| 2 | "LOCKED at v1" renamed to "proposed v1 scope" | §2 |
| 3 | Implementation split: PR M → PR I-0 → PR I-1 → PR I-2 | §4 |
| 4 | Draft-batch creation + partial-failure policy | §6 |
| 5 | Service security + temp-workspace requirements | §7 |
| 6 | 64-tag FOCAS2/MTConnect parity artifact | §5 |
| 7 | MTConnect CSV uses `baseUrl` (folded into §2 + §5) | §2, §5 |
| 8 | Brother/Modbus inherited, do not block FOCAS2+MTConnect | §10 |
| 9 | Preview validation as hard gate before submit | §8 |
| 10 | Duplicate / existing-source checks | §9 |

---

## 1. Preconditions

- [x] Chip 3 PRs #146–152 merged to master (offline generator + Pester harness + Session 2.5 `ValidateSidecar` + `sidecar-schema.json`).
- [x] Session 2.5 closure verified on user's pwsh-7 box (43/43 Pester tests green, byte-deterministic fixtures intact).
- [x] `tools/bulk-provision/sidecar-schema.json` is stable on master and matches the 9 fields the wizard's sidecar form will render.

**All preconditions already met.** v2 documents them so the implementation PRs can reference a stable foundation.

---

## 2. Phase 1 proposed scope (renamed from v1's "LOCKED")

### 2.1 In scope

**Backend (small chip-3 followup):**
- New template `template-mtconnect-v1.json` mirroring the existing `template-fanuc/brother/modbus-v1.json` pattern.
- New sample fixtures `samples/sample-mtconnect.{csv,gateway.yml}`.
- New frozen tree `tests/fixtures/expected/mtconnect/`.
- `templates/MANIFEST.md` extended with the MTConnect row + protocol-specific CSV column convention (FOCAS2/Brother/Modbus use `host`; MTConnect uses `baseUrl`).
- Pester `Deterministic.Tests.ps1` and `RoundTripValidate.Tests.ps1` extended to cover MTConnect (template-driven; mostly an array addition).
- Per §5: 64-tag parity artifact committed alongside the MTConnect template.

**Studio wizard:**
- New Razor page `/sources/bulk-provision` (path locked unless v3 reality-check surfaces a conflict).
- Wraps the chip-3 generator via `BulkProvisionService` (server-side wrapper). Service obeys §7 security + temp-workspace requirements.
- Operator picks ONE protocol per run from a 4-template picker:
  - FOCAS2 → `template-fanuc-v1.json`
  - Brother HTTP → `template-brother-v1.json`
  - Modbus TCP → `template-modbus-v1.json`
  - MTConnect → `template-mtconnect-v1.json` (new)
- CSV upload — column shape depends on selected template:
  - FOCAS2/Brother/Modbus: `deviceId, deviceName, host, enabled`
  - MTConnect: `deviceId, deviceName, baseUrl, enabled`
  - UI surfaces a template-specific "download CSV template" link per selection.
- Sidecar form — 9 fields per `sidecar-schema.json`.
- Preview pane — per-source summary table + raw JSON toggle. **Submit is DISABLED until preview generation + validation pass.** (§8)
- Submit creates a draft-batch through the existing `POST /api/v1/config/drafts` flow, with partial-failure semantics per §6.
- Confirmation screen offers "Create another batch" entry per Q1.

### 2.2 Out of scope (LOCKED — deferred to Phase 2 or later)

| Deferral | Where it goes |
|---|---|
| **Per-row tag variation** (Tooling-enabled CNCs) | Phase 2 — new kickoff after Phase 1 merges. |
| **Per-row protocol selection** (mixed in one upload) | Phase 2. Phase 1's "Create another batch" UX makes the two-run workflow palatable. |
| **MTConnect runtime probe-and-discover** (auto-populate dataPoints from `/probe`) | Out of bulk-provision; that's the existing single-source `AddMTConnectSource.razor` wizard's job. |
| **Tooling pack** (Tool/Life, Tool/Offsets) in MTConnect baseline | Phase 2 — only adds tags that are NOT in the customer's current 64-tag set. |
| **Template authoring in Studio** | Templates remain version-controlled files (chip-3 MANIFEST discipline). |
| **Tag CSV / per-tag-detail import** | Modbus already has `tools/ModbusCsvImport`; FOCAS2/MTConnect equivalents out of Phase 1. |
| **Live connectivity check during preview** | Phase 1 surfaces only the lightweight checks in §9. Live probe is Phase 2 or later. |

---

## 3. Standing rule: STATIC HTML MOCKUP FIRST

Unchanged from v1 §2. Mockup PR M is the first concrete deliverable. Implementation PR I-* is gated on operator sign-off of the mockup.

Mockup states (revised from v1 to match the §2 / §5 / §6 / §8 / §9 additions):

1. **Sources page entry** — header gets "Bulk import" button alongside existing "Add Source".
2. **Wizard Step 1 — Template picker** — 4-card grid (FOCAS2 / Brother HTTP / Modbus TCP / MTConnect).
3. **Wizard Step 2 — CSV upload + protocol-specific help** — file picker; protocol-specific column-shape help text; "download CSV template" link; parsed preview table with per-row status.
4. **Wizard Step 3 — Sidecar form** — 9 fields with `sidecar-schema.json` constraints visible inline.
5. **Wizard Step 4 — Preview** — per-source summary table (instanceId, host/baseUrl, deviceClass, route name) + sidecar validation status + generated-config validation status + per-row warnings + "show raw JSON" toggle. **Submit disabled until valid.**
6. **Wizard Step 5 — Submit confirmation** — batch result: `Created N draft configs / View draft batch / Create another batch`. NOT a single draft id.
7. **Error states** — CSV duplicate-deviceId, missing required column, sidecar schema violation, generator failure, partial batch failure.

**Phrasing fix from v1:** v1 said "all 5 wizard screens" but listed 7 states. v2 calls it "5 wizard steps + Sources entry + error states."

---

## 4. Implementation order (after mockup approval)

Implementation splits into FOUR PRs after PR M:

| PR | What | LOC est | Why split |
|---|---|---|---|
| **PR M** | Static HTML mockup | ~400 HTML/CSS | First deliverable; operator sign-off gate |
| **PR I-0** | Backend MTConnect template + fixtures + 64-tag parity artifact + Pester extension | ~150 (template + samples + parity doc) | Independently reviewable; validates the generator change before any UI calls it |
| **PR I-1** | `BulkProvisionService` + API endpoint + DTOs + service-layer tests | ~250 C# + 15 tests | Isolates the security/temp-workspace requirements (§7) from UI surface |
| **PR I-2** | Razor page + Sources page button + UI/model tests | ~350 Razor/C# + 12 tests | Final integration; matches mockup states 1:1 user-facing per Q5 |

ChatGPT's exact framing adopted: "If Claude wants fewer PRs, at least avoid 'one commit.' Make it multiple commits with clear boundaries."

---

## 5. 64-tag FOCAS2/MTConnect parity artifact (NEW)

**Deliverable:** `docs/sessions/2026-06-14-bulk-provision-ui-phase1-64-tag-parity.md` OR a MANIFEST subsection (lock format in v3 reality-check).

**Why it matters:** without it, Phase 1 could "support MTConnect" while the generated MTConnect template represents *different* operational data than what FOCAS2 collects. Customer would think they're getting parity; they wouldn't be.

**Sections required:**

1. **The 64 tags** — enumerated by canonical name. **BLOCKED on customer enumeration** — the user/customer needs to share the actual list before PR I-0 can commit. v3 reality-check surfaces this as an unblock requirement.
2. **FOCAS2 dataPoint group mapping** — which `template-fanuc-v1.json` dataPoints prefixes produce each of the 64.
3. **MTConnect observation mapping** — which MTConnect DataItem each of the 64 maps to (`execution`, `mode`, `program`, `partcount`, etc.).
4. **Coverage gaps** — tags that are FOCAS2-only (no MTConnect equivalent), MTConnect-only, or vendor-specific.
5. **Tooling exclusion** — explicit note that Tooling-related tags (`Tool/Life/*`, `Tool/Offsets/*`) are NOT in the Phase 1 baseline. Phase 2 adds them.

**Until the customer enumeration lands, the MTConnect template ships with the v1-recommended common-baseline observations** (`execution`, `mode`, `program`, `linelabel`, `partcount`, `alarm`, `feedoverride`, `spindlespeed`, `tool`, axis position) — flagged as "interim baseline pending 64-tag parity sign-off."

### 5.1 MTConnect template mini exit gate

Per ChatGPT's recommendation:

- [ ] `template-mtconnect-v1.json` committed.
- [ ] `sample-mtconnect.{csv,gateway.yml}` committed.
- [ ] `tests/fixtures/expected/mtconnect/` frozen tree committed.
- [ ] `RoundTripValidate.Tests.ps1` covers MTConnect.
- [ ] `Deterministic.Tests.ps1` covers MTConnect (template-driven addition).
- [ ] `templates/MANIFEST.md` documents MTConnect placeholders + `baseUrl` column convention + baseline dataPoint list.
- [ ] 64-tag parity artifact committed (interim baseline OR customer-enumerated).

---

## 6. Draft-batch creation + partial-failure policy

**Open question raised by v1 review:** for 100 CNCs, does one submit create 100 drafts?

**Locked answer in v2:**

- Phase 1 creates a **draft batch** containing N generated configs (one per CSV row).
- If the underlying `POST /api/v1/config/drafts` endpoint only accepts one draft at a time, `BulkProvisionService` loops internally, but the UI presents **one batch result**.
- **Partial failure MUST be visible.** Drafts 1-72 succeed, draft 73 fails, drafts 74-100 attempted independently — the operator sees:
  ```
  Created 99 draft configs (1 failed)
  Successful: cnc-001 ... cnc-072, cnc-074 ... cnc-100
  Failed: cnc-073 — <wrapped error from generator/draft API>
  Actions: [View successful drafts] [Retry failed] [Create another batch]
  ```
- **No silent partial creation.** If any draft fails, the failure surface is the primary UX, not a footnote.
- **No automatic rollback** of already-created drafts. Each draft is independent in the existing Studio's draft system, and rolling back 72 drafts because draft 73 failed is more destructive than helpful.

This policy is implemented in `BulkProvisionService` and asserted in PR I-1's service-layer tests.

---

## 7. Service security + temp-workspace requirements

`BulkProvisionService` wraps a command-line generator and accepts operator-uploaded CSV + sidecar content. Real attack surface. Lock the following in PR I-1:

- **Template id allowlist.** Operator picks from `{fanuc, brother, modbus, mtconnect}`. Service rejects any value outside the allowlist. **Never** accept a free-form path as the `-Template` argument.
- **Server-owned temp workspace.** Per-request temp directory at e.g. `%TEMP%\elpis-bulk-provision-{requestId}\`. Service is the only writer. Cleaned up after preview commits OR after request fails.
- **Uploaded content goes ONLY into the workspace.** CSV file and sidecar file are written to the temp workspace; never read from arbitrary user-supplied paths.
- **Generator output stays inside the workspace.** `-OutDir` always points at a workspace subdirectory.
- **Argument-list invocation, never shell string concatenation.** Service uses `Process.Start(ProcessStartInfo)` with `ArgumentList` populated explicitly. No `cmd /c` or `pwsh -Command` with concatenated strings.
- **Size + row limits.** Hard limits: CSV ≤ 1 MB, ≤ 1000 rows. Sidecar ≤ 64 KB. Anything above → 400 Bad Request before invocation.
- **Stdout/stderr capture.** Generator's stderr is read into the service and mapped to operator-friendly errors per the existing `BulkProvision.*` code scheme. Raw stderr is logged but not surfaced to the UI by default.
- **No process-level access to host filesystem outside the workspace.** Service does not let the generator read CSV/sidecar files from arbitrary paths even if their content is valid.

Phase 2 may relax some of these (e.g., per-tag CSVs may need larger limits), but Phase 1 keeps the security envelope tight.

---

## 8. Preview validation as hard gate before submit

Submit button is **disabled until ALL of**:

- CSV parses successfully (no malformed rows).
- All rows have the required columns for the selected protocol.
- No duplicate `deviceId` or `deviceName` within the upload (§9).
- Sidecar form passes `sidecar-schema.json` validation.
- Generator dry-run produces N output files with zero errors.
- Each output file passes `ValidateConfig` schema check.

The preview summary surfaces:

```
Protocol: MTConnect
Template: template-mtconnect-v1
Rows uploaded: 40
Valid rows: 40
Generated configs: 40
Sidecar: Valid
Generated-config validation: Passed
Warnings: 3 missing optional observations (rows cnc-014, cnc-019, cnc-031)
```

Warnings do NOT block submit; only errors do. Operator can review the warnings list and decide.

---

## 9. Duplicate / existing-source checks

CSV-level checks (Phase 1):

- **Duplicate `deviceId` in upload** → CSV parse error per existing chip-3 `BulkProvision.CsvDuplicateDeviceId`. Already implemented.
- **Duplicate `deviceName` in upload** → NEW soft check (warning, not blocker). Same name might be intentional across deployments.
- **`deviceId` collides with an existing source in the current gateway config** → blocker; surface as "deviceId 'cnc-073' already exists in current configuration." The check queries the existing config via the management API.
- **`deviceName` collides with an existing source** → warning, not blocker.

NOT in Phase 1:

- Live host/baseUrl reachability check — that's expensive and may surface false negatives (network-isolated factory). Phase 2 may add it as an OPTIONAL "validate connectivity" toggle.

---

## 10. Brother / Modbus inheritance posture

ChatGPT's framing adopted verbatim:

> **Must-have for Phase 1:**
> - FOCAS2 batch provisioning works.
> - MTConnect batch provisioning works.
>
> **Nice-to-have / inherited:**
> - Brother and Modbus appear in the picker if existing templates pass the same service contract.

Brother and Modbus already have stable templates from chip-3. They should "fall out" of Phase 1 with zero extra work, but if Brother/Modbus UI polish surfaces a complication, **defer it — don't let it block the FOCAS2 + MTConnect customer path.**

PR I-2 acceptance criteria call out FOCAS2 + MTConnect explicitly; Brother + Modbus get "verified by the same Pester service-contract tests."

---

## 11. Open questions for v3 reality-check (NEW — bounded set)

Per Q6, v3 is a focused reality-check, NOT a full chip-3 ceremony. The v3 pass should answer ONLY these:

| # | Question | Why grounded in repo state matters |
|---|---|---|
| RC1 | **Single gateway.json vs N gateway.json files?** The existing offline generator produces ONE gateway.json PER CSV ROW with the SAME `gatewayId` from the sidecar. Is that the right model for Studio? If 100 CNCs share ONE gateway box, the wizard should produce ONE config with N sources, not N configs. **Need to inspect a frozen fixture tree (e.g. `tests/fixtures/expected/fanuc/`) to confirm the per-file source count, then either confirm the model or surface as an amendment.** |
| RC2 | Does `sidecar-schema.json` actually match the 9 fields the sidecar form will render? Diff sidecar-schema.json `required` + `properties` against the v2 §2.1 form list. |
| RC3 | Does the chip-3 generator already accept arbitrary CSV column names like `baseUrl`, or is the column list hardcoded? If hardcoded to `host`, PR I-0 needs a small generator extension. |
| RC4 | What exact MTConnect source config shape does `Sources.MTConnect/MTConnectSourceConfiguration.cs` expect? Confirm `AgentBaseUrl` is the field name and the template's placeholder maps cleanly. |
| RC5 | Does `AddMTConnectSource.razor` already define reusable observation/dataPoint defaults that the template can inherit? Diff against `template-mtconnect-v1.json` baseline observations. |
| RC6 | Does `POST /api/v1/config/drafts` support 100 sequential creates in a session, or rate-limit? Inspect the existing draft API + the audit-log shape. |

Each question has a 5-minute repo-grep answer. v3 reality-check should NOT relitigate v2's design decisions, only ground-check the implementation assumptions.

---

## 12. Size estimate (revised with PR split)

| PR | LOC | Tests | Notes |
|---|---|---|---|
| PR M — static HTML mockup | ~400 HTML/CSS | 0 | 1 session |
| PR I-0 — MTConnect template + fixtures + parity doc | ~150 (template) + ~80 (parity doc) | 2 new template-driven tests (auto via Pester) | 0.5-1 session; **blocked on customer 64-tag enumeration for parity sign-off** |
| PR I-1 — BulkProvisionService + API | ~250 C# | ~15 tests (security + temp workspace + partial failure) | 1 session |
| PR I-2 — Razor + Sources entry + tests | ~350 C#/Razor | ~12 UI/model tests | 1-1.5 sessions |

**Total: ~4 sessions** (mockup + 3 implementation). Real customer feature.

The MTConnect template's "interim baseline" (per §5) lets PR I-0 ship without the customer's 64-tag enumeration; the parity doc commits with `Interim baseline — pending customer enumeration` until the user provides the actual list. That avoids blocking the whole pipeline on one unblock.

---

## 13. Cadence position

1. ✅ v1
2. ✅ ChatGPT v1 review ("approve direction, request v2 before lock")
3. ✅ **v2 synthesis (this doc)** — committed to same branch + PR #153
4. ⏳ v3 focused reality-check (RC1-RC6 from §11)
5. ⏳ Lock
6. ⏳ PR M (static HTML mockup) — first concrete deliverable
7. ⏳ PR I-0 (MTConnect backend) — independent of UI; can land in parallel with PR M review
8. ⏳ PR I-1 (BulkProvisionService + API)
9. ⏳ PR I-2 (Razor + Sources entry)
10. ⏳ Phase 2 kickoff (after Phase 1 fully merges)

User actions required after v3 lock:

- **Enumerate the customer's 64 tags** (or confirm interim baseline acceptable for first customer rollout).
- **Approve the mockup PR M** before any Razor code lands.
