# Bulk-Provision UI Phase 1 — v1 plan

**Date:** 2026-06-14
**Author:** Claude (post-chip-3 wrap, customer scenario locked)
**Status:** v1 DRAFT — pending ChatGPT review pass per `feedback_planning_cadence.md`
**Predecessor:** `docs/sessions/2026-05-21-bulk-provision-ui-kickoff.md` (kickoff confirming Option B)
**Customer context:** 100 CNCs, mixed FOCAS2 + MTConnect, 64 same tags TODAY, Tooling-driven divergence TOMORROW.

---

## 0. Why this exists

Chip 3 shipped the offline bulk-provision generator (PRs #146 #150 #152). Operator runs `pwsh ./generate.ps1 -Csv ... -Sidecar ... -Template ... -OutDir ...` and gets N validated `gateway.json` files.

**The customer needs the same outcome from Studio**, not the command line. Per user direction 2026-06-14:

- **Q1 — Promote May 21 kickoff to v1 plan.** Confirmed.
- **Q2 — Ship Phase 1 (same tags) first; defer Phase 2 (Tooling-divergence per-machine tags) to a separate kickoff.** Confirmed.
- **NEW requirement** — Phase 1 must cover **both FOCAS2 and MTConnect** in the first delivery.

The MTConnect inclusion is a real backend extension (no MTConnect template in `tools/bulk-provision/` today), not just a UI-side dropdown change.

---

## 1. Phase 1 scope (LOCKED at v1; ChatGPT may reshape)

### 1.1 In scope

**Backend (chip-3 generator extension):**
- New template `template-mtconnect-v1.json` mirroring the existing `template-fanuc/brother/modbus-v1.json` pattern.
- New sample fixtures `samples/sample-mtconnect.{csv,gateway.yml}`.
- New frozen tree `tests/fixtures/expected/mtconnect/`.
- `templates/MANIFEST.md` extended with the MTConnect row + placeholder taxonomy delta (if any).
- Pester suite picks up the new template automatically via the existing template-driven tests (Deterministic, RoundTripValidate).

**Studio wizard:**
- New Razor page `/sources/bulk-provision` (path locked in v1, may be `bulk-import` per UX).
- Wraps the chip-3 generator via a server-side service (`BulkProvisionService`).
- Operator picks ONE protocol per run from a 4-template picker:
  - FOCAS2 → `template-fanuc-v1.json`
  - Brother HTTP → `template-brother-v1.json`
  - Modbus TCP → `template-modbus-v1.json`
  - MTConnect → `template-mtconnect-v1.json` (new)
- CSV upload (`deviceId, deviceName, host, enabled`).
- Sidecar form (9 fields from sidecar-schema.json).
- Preview pane (per-source summary table).
- Submit → existing `POST /api/v1/config/drafts` flow.

### 1.2 Out of scope (LOCKED — deferred to Phase 2 or later)

| Deferral | Where it goes |
|---|---|
| **Per-row tag variation** (Tooling enabled on some CNCs) | Phase 2 — new kickoff after Phase 1 merges. |
| **Per-row protocol selection** (mixed FOCAS2 + MTConnect in ONE upload) | Phase 2 OR explicit decision in ChatGPT review (§4 Q1). Default assumption: operator does TWO runs — one per protocol. |
| **MTConnect runtime probe-and-discover** (auto-populate dataPoints from `/probe`) | Out of bulk-provision; that's the existing AddMTConnectSource wizard's job. Bulk-provision uses a static baseline template. |
| **Template authoring in Studio** | Templates remain version-controlled files (chip-3 MANIFEST discipline). |
| **Tag CSV / per-tag-detail import** | Modbus already has `tools/ModbusCsvImport`; FOCAS2/MTConnect equivalents are out of Phase 1. |

### 1.3 Phase 2 carry-forward note

Phase 2 will need (NOT in scope here, just naming the dependency):
- Generator-level support for per-row dataPoints override (new CSV column or per-machine sidecar).
- FOCAS2 + MTConnect tag catalogs surfaced as a programmatic resource (`Focas2DataPointGroup` for FOCAS2 exists; MTConnect equivalent is currently per-agent via `/probe`).
- UX for representing "Base 64 tags + optional Tooling pack" (named profiles vs per-row column vs catalog drilldown — design question for Phase 2 mockup pass).

---

## 2. Standing rule: STATIC HTML MOCKUP FIRST

Per `feedback_static_html_ui_review.md`: any UI gets a static HTML mockup for operator sign-off BEFORE any Razor wiring. **Phase 1 ships in two PRs, in this order:**

1. **PR M (mockup)** — static HTML mockup of all 5 wizard screens. NO Razor, NO real CSS dependencies on the Blazor side. Plain HTML page(s) the user can open in a browser, screenshot, mark up, and approve.
2. **PR I (implementation)** — after PR M merges with operator sign-off, write the actual Razor + backend code matching the approved mockup.

The mockup PR is THE FIRST DELIVERABLE. No backend work, no Razor scaffolding, no CSS pre-work in PR I until PR M is approved.

### 2.1 Mockup PR scope

Static HTML page(s) showing:

1. **Sources page entry** — header with new "Bulk import" button alongside existing "Add Source".
2. **Wizard Step 1 — Template picker** — 4-card grid (FOCAS2 / Brother HTTP / Modbus TCP / MTConnect), each card showing the template id + a short description.
3. **Wizard Step 2 — CSV upload** — file picker + a preview table showing the parsed rows + per-row validation status.
4. **Wizard Step 3 — Sidecar form** — 9 fields rendered as a form with the v3 §2 schema constraints visible (UUID format hint on gateway ids, pattern hint on mqttClientIdPrefix, etc.).
5. **Wizard Step 4 — Preview** — per-source summary table (instanceId, host, deviceClass, route name) + "show raw JSON" toggle.
6. **Wizard Step 5 — Submit confirmation** — draft id + link to standard validate-and-apply flow.
7. **Error states** — CSV parse error per-row; sidecar field error; generator error.

Goal: operator can walk through all states in a static browser without running anything. Mockup uses placeholder data — no live generator integration.

### 2.2 Where the mockup lives

`docs/mockups/bulk-provision-ui-phase1/` — new directory. One `index.html` with all 6 states as sections, or separate files per state. v1 plan does NOT lock the directory structure; mockup PR can pick whichever reads better.

---

## 3. Implementation order (post-mockup-approval)

| Step | Description | Files |
|---|---|---|
| **B** | Backend MTConnect template + fixtures + frozen tree | `tools/bulk-provision/templates/template-mtconnect-v1.json`, `samples/sample-mtconnect.{csv,gateway.yml}`, `tests/fixtures/expected/mtconnect/`, `templates/MANIFEST.md` update |
| **S1** | `BulkProvisionService` — server-side wrapper around the chip-3 generator | `src/ElpisEdgeConnect.Management/Services/BulkProvisionService.cs` (or similar) |
| **S2** | API endpoint | `src/ElpisEdgeConnect.Management/Api/BulkProvisionApi.cs`, DTOs |
| **S3** | Razor page + state model | `src/ElpisEdgeConnect.Management/Components/Pages/BulkProvision.razor`, `BulkProvisionModel.cs` |
| **S4** | Sources page header — add "Bulk import" button | `src/ElpisEdgeConnect.Management/Components/Pages/Sources.razor` |
| **T1** | Backend tests (Pester picks up B automatically) | run `Invoke-Tests.ps1` on pwsh-7 box |
| **T2** | Management tests | `tests/ElpisEdgeConnect.Management.Tests/BulkProvision{Service,Api,Model}Tests.cs` |

The implementation PR ships B + S1-S4 + T1-T2 in one commit; the mockup PR is its precondition.

---

## 4. Open questions for ChatGPT review

### Q1 — Per-row protocol selection in Phase 1

**Default position:** Phase 1 forces one protocol per run. Customer with 60 FOCAS2 + 40 MTConnect does TWO bulk-provision runs.

**Argument for in-Phase-1:** customer's 100-CNC fleet is genuinely mixed; one upload is more operator-friendly than two; the CSV `protocol` column would be additive and doesn't preclude per-row tags later.

**Argument against:** UI complexity grows (per-row sidecar override? per-protocol sidecar segments?); generator currently picks ONE template per invocation, so this is a non-trivial backend change too; Phase 2 is the natural home for per-row variation.

**ChatGPT recommendation requested.**

### Q2 — MTConnect template baseline dataPoints

MTConnect agents expose observations discovered at runtime via `/probe`. The template has to pre-declare a baseline of common ones. Two options:

- **Universal baseline:** Hardcode the most common MTConnect observations (`execution`, `mode`, `program`, `linelabel`, `partcount`, `alarm`, `feedoverride`, `spindlespeed`, `tool`, etc.). Adapter logs warnings for missing observations per agent. Operator forks the template if their fleet needs a different baseline.
- **Probe-once-and-pin:** Operator probes ONE representative agent first (using the existing AddMTConnectSource discover flow), captures the available observations, that becomes their custom template they check in. Bulk-provision uses the checked-in template.

Default lean: universal baseline for v1 (faster to ship); operators with unusual fleets fork the template.

### Q3 — CSV `host` column vs `baseUrl` for MTConnect

FOCAS2: `host` = IP address (e.g. `192.168.1.10`). Template assembles `ipAddress: {{ host }}, port: 8193`.
MTConnect: `AgentBaseUrl` = full URL (e.g. `http://192.168.1.10:5000/`). Template needs `{{ host }}` to expand to a URL.

Options:
- **(a) Generic `host` column, template assembles URL.** Template hardcodes `http://{{ host }}:5000/` — works for default-port, plain HTTP agents only. Customer with HTTPS or non-default port forks the template.
- **(b) Add `baseUrl` column for MTConnect rows.** CSV schema becomes protocol-aware. Awkward but explicit.
- **(c) Allow `host` to contain a full URL when protocol = MTConnect.** Operator puts `http://...:5000/` directly into the `host` column.

Default lean: (a) for v1; documented in MANIFEST as a per-template convention.

### Q4 — Sources page entry point UX

May 21 kickoff §4 Q5: "Sources page header — Bulk import button; Connect-a-device flow — 'got multiple machines?' branch. Both? Lock in v1."

Default lean: Sources page header button ONLY in Phase 1. Connect-a-device entry is a separate UX surface that can wait for Phase 2.

### Q5 — Mockup states vs Razor states alignment

Mockup PR ships 7 states (§2.1 list). Should implementation PR commit to 1:1 alignment with the mockup, or can the Razor implementation refactor states during build?

Default lean: 1:1 alignment for v1 — that's why we mockup. If a state needs to change during implementation, it returns to the mockup pass first (small mockup-amendment PR).

### Q6 — Cadence weight

May 21 kickoff §5 proposed "lighter than chip 3" cadence: v1 → optional ChatGPT review → optional v2 → implementation. Skip reality-check (chip-3 covered the underlying questions).

Phase 1 now includes a new MTConnect template — that's real backend scope, not just UI wrapping. Should cadence stay light or escalate to full v1 → v2 → v3-reality-check → lock per chip-3 pattern?

Default lean: light cadence still applies for the UI; treat the MTConnect-template backend as a small chip-3 followup that piggybacks into Phase 1's mockup pass.

---

## 5. Size estimate

| Item | LOC | Notes |
|---|---|---|
| PR M — static HTML mockup | ~400 lines HTML/CSS | Single session |
| PR I.B — MTConnect template + fixtures | ~80 JSON + 60 fixtures | 0.5 session |
| PR I.S1-S4 — Studio wizard | ~400 LOC C#/Razor | 2 sessions |
| PR I.T2 — Management tests | ~25-30 tests | 0.5 session |
| PR I total | | ~3 sessions |

**Total: 1 session mockup + ~3 sessions implementation = 4 sessions.** Real customer feature, not a small patch.

---

## 6. Phase 2 placeholder

Phase 2 (per-machine tag variation, Tooling case) is **explicitly NOT in this plan**. Customer doesn't need it today. Phase 2 kickoff drafts after Phase 1 merges.

Phase 2 will inherit Phase 1's:
- Bulk-provision wizard surface (extends, doesn't replace)
- MTConnect template (extends for per-machine override)
- Sample fixtures (extend with override-column examples)

Phase 2's design questions (saved for the Phase 2 kickoff, not v1 here):
- UX for "100 rows × variable tag selection" — named profiles vs per-row column vs catalog drilldown.
- MTConnect's per-agent `/probe` reality — bulk-provision doesn't probe; operator pre-curates the catalog per template.
- FOCAS2 `Focas2DataPointGroup` integration — surface the existing single-source-wizard catalog as a programmatic resource for bulk override.

---

## 7. Cadence position

1. ⏳ **v1 (this doc)** — committed to `claude/bulk-provision-ui-phase1-plan` branch, PR open for ChatGPT review.
2. ⏳ ChatGPT review pass.
3. ⏳ v2 synthesis (only if review surfaces meaningful changes).
4. ⏳ Optional v3 reality-check — skip if v2 is structurally clean and §4 Q6 default holds.
5. ⏳ **PR M (mockup)** — static HTML mockup for operator sign-off.
6. ⏳ **PR I (implementation)** — backend MTConnect template + Studio wizard + tests.
7. ⏳ Phase 2 kickoff (separate doc, after Phase 1 merges).

User actions required:
- Approve v1 lock OR pushback on the §4 question defaults.
- Decide cadence weight per §4 Q6.
