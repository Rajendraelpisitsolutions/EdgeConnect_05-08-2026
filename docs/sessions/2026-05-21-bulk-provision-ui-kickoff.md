# Bulk-provision UI — Studio wrapper over Chip 3 (kickoff)

**Status:** **QUEUED** — depends on Chip 3 (Provisioning Subsystem) shipping first
**Date:** 2026-05-21
**Form:** Kickoff / queueing note. Lighter plan-trail cadence proposed (see §5).

---

## 0. Why this milestone

The Chip 3 Provisioning Subsystem ([v2 plan](2026-05-21-chip3-provisioning-subsystem-plan-v2.md), [v3 reality-check](2026-05-21-chip3-provisioning-subsystem-v3-reality-check.md)) builds a PowerShell-based offline generator: operator authors `template-{protocol}-v1.json` + `machines.csv` + sidecars, runs `generate.ps1`, gets a complete `gateway.json`, pastes it into the Studio's existing **Import draft from JSON** dialog.

That path is **operationally correct for the 100-CNC fleet customer** — git-ops friendly, version-controlled, reproducible. But it's friction for:

- **Smaller deployments** (5-machine pilots) where PowerShell tooling is overkill.
- **Customers not running PowerShell** as a primary ops surface.
- **Exploratory / one-off** bulk imports where the artifact doesn't need long-term versioning.
- **Operators discovering the platform** through Studio — they shouldn't have to leave the app to bulk-provision.

**Option B**, confirmed by user 2026-05-21: build a thin Studio UI page that wraps the same provisioning core Chip 3 ships. Single shared library; offline tool is one consumer, Studio page is the second. Same generator logic, two operator-facing paths.

> **User decision quote** (2026-05-21):
> *"Option B."* — picked from {A: do nothing new, B: chip-3 + Studio UI on top, C: UI-only skipping the offline tool}.

---

## 1. Scope (locked at kickoff)

### In scope

- **Studio page** at `/sources/bulk-provision` (or similar; lock in v1 plan).
- **Wraps the same provisioning library** Chip 3 builds. No duplication; UI is a thin shell.
- **CSV upload** — operator uploads `machines.csv` matching Chip 3's locked format (`make,name,ip,enabled` + optional columns).
- **Sidecar form** — broker host, MQTT QoS, `gatewayProvisioningId`, etc. (the 9 fields from Chip 3 v2 §6 that vary per gateway).
- **Template picker** — selects from the chip-3-shipped templates (`template-fanuc-v1.json`, `template-brother-v1.json`, `template-modbus-v1.json`).
- **Preview pane** — shows the generated `gateway.json` (or a per-source summary table) before the operator commits.
- **Draft creation** — same POST `/api/v1/config/drafts` endpoint the existing Import dialog uses.
- **Operator validates + applies** via the standard wizard flow. No new validation path.

### Out of scope (locked deferrals)

| Deferral | Goes to |
|---|---|
| Template authoring in Studio | Templates remain version-controlled files. Customer authors a new template offline + checks it in. Chip 3 §5.1.3 MANIFEST discipline applies. |
| Bulk modification of existing sources | This is **provisioning** (new sources only), not editing. M.2d Edit-via-Wizard owns existing-source mutation. |
| Template marketplace / sharing | Not in any current milestone. |
| Auto-CSV-generation from network scan | Discovery tools are a separate domain (likely an M.2c+ feature). |
| Multi-vendor canonical alignment (FOCAS2 ↔ Brother same downstream names) | **Chip 3.1** — separate kickoff (see §7 references). |
| Per-row template column in CSV (heterogeneous fleet) | **Chip 3.1** companion; UI wrapper inherits whatever CSV shape Chip 3.1 lands. |
| Replacing the existing Import-draft-from-JSON dialog | That dialog stays. Bulk-provision page is additive. |

If a Locked deferral becomes a smoke-blocker after v1 lock, that's a v2 amendment input — not a silent in-flight scope expansion.

---

## 2. Hard dependency on Chip 3

This milestone CANNOT start implementation until Chip 3 ships. Specifically:

| Chip 3 artifact | Why this milestone needs it |
|---|---|
| `tools/bulk-provision/` PowerShell generator | The Studio service either calls into it OR invokes a shared C# library — either way the chip-3 implementation defines the source of truth. |
| Template files (`template-fanuc-v1.json` etc.) | The Studio's template picker selects from these. |
| `ADR-0030 — reserved underscore namespace` | The Studio service produces JSON with the `_provisioning` block; preservation through `ConfigurationManager.CreateDraftAsync` depends on Core's `[JsonExtensionData]` change. |
| `tools/ValidateConfig/` CLI (v3 reality-check addition) | The Studio service may call this for pre-submit validation. |

**Sequencing rule**: this milestone's v1 plan can be drafted in parallel with Chip 3 implementation (so it's ready the moment Chip 3 ships), but **implementation starts only after Chip 3 PRs are merged**.

---

## 3. Sketched deliverables (for v1 plan to refine)

| File | Status | Surface |
|---|---|---|
| `src/ElpisEdgeConnect.Management/Components/Pages/BulkProvision.razor` | new | Studio page at `/sources/bulk-provision`. CSV upload + sidecar form + template picker + preview + commit. |
| `src/ElpisEdgeConnect.Management/Components/Pages/BulkProvisionModel.cs` | new | POCO state machine — upload state, parsing state, preview state, error states. Mirrors M.2b.6.1 `EnableDisableConfirmDrawerModel` discipline. |
| `src/ElpisEdgeConnect.Management/Api/BulkProvisionApi.cs` | new | POST endpoint: accepts CSV + sidecar + template name; returns generated draft id (or preview JSON). |
| `src/ElpisEdgeConnect.Management/Wizards/BulkProvisionService.cs` *(or similar)* | new | Server-side wrapper around the Chip 3 generator library. Single source of truth for invocation logic. |
| `src/ElpisEdgeConnect.Management/Contracts/BulkProvisionRequestDto.cs` | new | Wire shape. |
| `src/ElpisEdgeConnect.Management/Contracts/BulkProvisionResponseDto.cs` | new | Wire shape — preview JSON OR draft id, discriminated by `outcome`. |
| `tests/ElpisEdgeConnect.Management.Tests/BulkProvisionServiceTests.cs` | new | ~10 tests — generator invocation, CSV parse errors, template-not-found, etc. |
| `tests/ElpisEdgeConnect.Management.Tests/BulkProvisionModelTests.cs` | new | ~8 tests — state machine transitions. |
| `tests/ElpisEdgeConnect.Management.Tests/BulkProvisionApiTests.cs` | new | ~6 tests — endpoint surface + status codes. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Sources.razor` | edit | Header: add **Bulk import** button alongside existing **Add Source**. |
| `docs/decisions/<NN>-...md` | possible new | Only if v1 plan surfaces a real architectural decision. |

**Estimate**: ~300-400 LOC + ~25-30 tests. **1-2 sessions** of work after Chip 3 ships.

---

## 4. Open questions for v1 plan

| # | Question |
|---|---|
| Q1 | **Chip-3 invocation surface from Studio** — does the chip-3 library expose a clean C# API, or is the only entry point the PowerShell generator? If PS-only, `BulkProvisionService` shells out to `pwsh.exe` on the gateway host (same pattern Chip 3's `ProvisioningSubsystemTests.cs` uses). If a C# library exists, direct calls — cleaner but may require refactoring Chip 3's structure. v1 plan reads chip-3's final shape and decides. |
| Q2 | **CSV input mode** — drag-and-drop file upload? File picker button? Textarea paste? Recommendation: file picker first (operators familiar with browser file uploads), drag-and-drop as enhancement. |
| Q3 | **Sidecar form vs paste** — render the 9 sidecar fields as a form (operator types broker host etc.) or accept a sidecar JSON paste? Recommendation: form. The paste path is for offline-tool users. UI users should not have to know the sidecar JSON shape. |
| Q4 | **Preview shape** — raw JSON syntax-highlighted? Per-source summary table (instanceId + protocol + host)? Both, switchable? Recommendation: per-source table by default with a "show raw JSON" toggle. |
| Q5 | **Entry points** — Sources page header gets a **Bulk import** button. Connect-a-device flow gets a "got multiple machines?" branch. Both? Lock in v1. |
| Q6 | **Partial-failure UX** — if the generator rejects 3 of 50 CSV rows (validation errors), do we surface a preview with the 47 valid rows + error list, or block until the operator fixes the CSV? Recommendation: block — partial-provision drafts are operationally dangerous. |
| Q7 | **License-gate scope** — does bulk-provisioning need its own license module (`management-bulk-provision`), or is it free with Connectivity Studio? Recommendation: free with Studio. Doesn't add platform capability beyond what the existing Import-draft-from-JSON dialog already permits. |
| Q8 | **CSV-line-number debugging hint** — when an error fires, surface "row 27 (`Line2-CNC-22`) — host field empty" with a clickable link to scroll the operator to that row in the upload preview. (Echoes the `_provisioning.csvLineNumber` Q-V2-E deferral — UI version is more tractable than the JSON-block version.) |

---

## 5. Cadence (proposed — lighter than Chip 3)

This milestone is UI wrapping over an existing library. No new architectural surface; no new validation logic; no Core changes. Proposed cadence:

1. **v1 plan** — resolves Q1-Q8, locks file-by-file deliverables. ChatGPT review pass optional.
2. **v2 amendment** — *if* ChatGPT review surfaces architectural concerns. Skip if v1 plan is structurally clean.
3. **Reality check** — SKIP. Chip 3's reality-check covered the underlying questions (parser behavior, schema validation, pwsh availability). This milestone inherits.
4. **Implementation** — 1-2 focused sessions after Chip 3 ships.
5. **Smoke** — manual verification:
   - Upload a known-good `machines-100cnc-customer-A.csv` (Chip 3 sample) → expect preview matches the offline-generator output byte-for-byte.
   - Upload a CSV with row-level errors → expect block + per-row error list.
   - Apply preview as draft → expect standard validate + apply flow runs.

**Reserve the right** to promote to a full v1 → v2 → v3 cadence if v1 plan reveals more architectural complexity than expected (e.g. Q1's answer is "no C# API exists, requires significant chip-3 refactoring").

---

## 6. Anti-silent-scope-expansion principle

Same as M.2b.6.1's handoff §10. Examples of what would be silent scope expansion (do NOT do without v2):

- "I'll also add a 'duplicate source' button while I'm here" — no. That's M.2d Edit-via-Wizard's job.
- "Templates should be editable in Studio" — explicitly out of scope. Chip 3's golden-source-template rule applies.
- "Add a 'scan network' button to auto-populate the CSV" — separate future feature.
- "Cross-vendor canonical name alignment" — that's Chip 3.1.
- "Make the Modbus per-tag CSV importer accessible from this same page" — different domain. Stays as the existing `tools/ModbusCsvImport/` flow.

When in doubt: pause, surface, ask.

---

## 7. References

- **Option B selected by user 2026-05-21** (this session). See conversation summary in §0.
- [Chip 3 v2 plan](2026-05-21-chip3-provisioning-subsystem-plan-v2.md) — the offline subsystem this milestone wraps.
- [Chip 3 v3 reality-check](2026-05-21-chip3-provisioning-subsystem-v3-reality-check.md) — preconditions inherited.
- [100-CNC deployment readiness](2026-05-20-100-cnc-deployment-readiness.md) — primary customer use case for the OFFLINE path; this milestone is for the OTHER customers.
- M.2b.5/6/6.1 kickoff lineage — sets the kickoff-doc pattern this file follows.
- Platform principles P6 (operational product, not developer tool) — primary motivation for the UI variant alongside the PowerShell tool.

### Future milestones flagged but not in scope here

- **Chip 3.1 — Multi-vendor canonical alignment + per-row template assignment.** Surfaces from the 2026-05-21 conversation about "100 CNCs, different vendors, same tags" + "50 CNCs, different vendors, different tags". Would extend Chip 3 with: optional `customerCanonicalSchemaRef` in `_provisioning`, per-row `template` column in CSV, auto-generated route Transforms.TagMapping. Separate kickoff to be written.
- **EREMOS V2 shared canonical tag dictionary.** Cross-project work in `shared-knowledge/contracts/`. EdgeConnect publishes canonical names; EREMOS consumes them with tenant overrides. Outside this milestone; coordinated via the cross-project knowledge folder.

---

**End of Bulk-provision UI kickoff. v1 plan starts in a session AFTER Chip 3 PRs merge.**
