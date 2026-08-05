# M.2b.6.2 — Smoke-driven wizard hardening (kickoff)

**Status:** **QUEUED** — next milestone after M.2b.6.1 merged
**Date:** 2026-05-20
**Form:** Kickoff / queueing note. Lighter plan-trail cadence proposed (see §6) — full v1 → ChatGPT review → v2 → v3 may be overkill for the scope.

---

## 0. Why this milestone

Three independent UX gaps surfaced during M.2b.6.1's end-to-end manual smoke pass (2026-05-19/20). Each was filed as its own follow-up chip; the user requested they ship as a single bundled "smoke-driven hardening" milestone rather than three sequential single-fix milestones.

**The shared theme**: every issue here was *caught by the operator during commissioning a real source* — not by automated tests, not by reviewers — because the test suite covers happy paths and code-level invariants, not the *cognitive friction surface* an operator hits when wiring up a fresh gateway.

**User quote** (2026-05-20):

> To be frank all these issues we should handle it gracefully. I will be happy if we can address all of them.

This milestone is the response. Three small fixes, one PR.

### The three triggers

1. **Modbus wizard accepts incoherent tag definitions** (uint16 + CDAB, float32 + AB, etc.) — Core's adapter validator catches at startup, crashing the boot sequence. The wizard layer should have caught it at row-add time per Locked N's eager-validation discipline.

2. **Studio doesn't surface which `current.json` is loaded** — when `EDGECONNECT_DATA_ROOT` env var is set (the dev-friendly override), the Studio reads from a different path than the default. Operators editing the default path see no effect; took several diagnostic rounds during M.2b.6.1 smoke to discover.

3. **Modbus wizard's port field defaults to 502** — the standard Modbus TCP port, correct for production PLCs. But the bundled test simulator (`tests/ElpisEdgeConnect.Integration.Tests/ModbusSimulator/server.py`) listens on **5020**. Operators following the simulator README hit a connect-failed circuit-breaker loop before realising the mismatch.

---

## 1. Scope (locked at kickoff)

**In scope** — three independent surfaces:

### A. Modbus wizard tag-table cross-validation

- Add `ModbusSourceWizardModel.ValidateByteOrderAgainstDatatype(datatype, byteOrder)` static method.
- Per-tag-row validator surfaces an inline error in the wizard's tag table when datatype byte-width and byteOrder length disagree.
- 2-byte datatypes (`bool`, `uint16`, `int16`) → byteOrder MUST be `AB` or null.
- 4-byte datatypes (`uint32`, `int32`, `float32`) → byteOrder MUST be one of `ABCD`, `BADC`, `CDAB`, `DCBA`.
- String datatypes (`string8`, `string16`) → byteOrder MUST be null.
- Save button disables while any tag row has a validation error.
- Composition not duplication (Locked N) — reuse the byte-width mapping the adapter uses internally if exposed; otherwise pull it into a shared helper.

### B. Studio surfaces active config path

- Startup banner log line at the same point `Gateway identity resolved: <uuid>` fires today: add `Configuration loaded from: <path>`.
- Config page caption (small monospaced text near the page header) showing the active path + a copy-to-clipboard icon.
- Override indicator: if `EDGECONNECT_DATA_ROOT` or `EDGECONNECT_CONFIG_DIR` is set, surface a small chip on the Config page naming the env var ("Override: EDGECONNECT_DATA_ROOT") so operators on shared dev machines spot it.
- No path-edit UI — the path remains environment-controlled.

### C. Modbus wizard port helper text

- One-line copy change in `AddModbusSource.razor` port field's `HelperText` parameter.
- New text: "TCP port. 502 for production Modbus TCP devices; 5020 for the bundled test simulator."

**Out of scope** (Locked deferrals, do not relitigate):

| Deferral | Goes to |
|---|---|
| Cross-validating wizard fields BEYOND datatype/byteOrder | M.2d Edit-via-Wizard or its own follow-up |
| Migrating Modbus wizard to a shared tag-table primitive | M.2e Shared List Infrastructure |
| Config-path EDIT via UI | Never — the path is environment-controlled by design |
| Retrofitting cross-validation to other source wizards (Focas2 / S7 / MTConnect) | Each protocol's own follow-up; they have different field shapes |
| Helper-text revisions to OTHER wizard fields (host, timeouts, retries) | Out of MVP scope; revisit if operator data shows friction |

---

## 2. Position in the roadmap

Inserts after **M.2b.6.1** (Inline Enable/Disable, merged) and before **M.2c** (Live Tag Watch + Runtime Tap). Naming follows the M.2b.3.1 precedent for smoke-driven follow-ups to wizard-family milestones.

```
M.2b.6     Destination Wizard           [merged, PR #10]
M.2b.6.1   Inline Enable/Disable        [merged, PR #13]
   ↓
M.2b.6.2   Smoke-driven UX hardening    ⭐ NEW — START NEXT
   ↓
M.2c       Live Tag Watch + Runtime Tap
M.2d       Edit-via-Wizard
M.2e       Shared List Infrastructure
...
```

---

## 3. Sketched deliverables (for v1 plan to refine)

| File | Status | Surface |
|---|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/ModbusSourceWizardModel.cs` | edit | A — add `ValidateByteOrderAgainstDatatype` |
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddModbusSource.razor` | edit | A — inline row-error rendering; C — port helper text |
| `src/ElpisEdgeConnect.Management/Components/Pages/Config.razor` | edit | B — config path caption + override chip |
| `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs` | edit | B — startup-banner log line |
| `src/ElpisEdgeConnect.Management/Contracts/ConfigPathInfoDto.cs` *(possible new)* | possible new | B — wire shape for surfacing path metadata to Config page (only if not derivable from existing `/api/v1/config` response) |
| `tests/ElpisEdgeConnect.Management.Tests/ModbusSourceWizardModelTests.cs` | edit | A — ~10 new cross-validation tests |
| `tests/ElpisEdgeConnect.Management.Tests/ConfigPathDisplayTests.cs` *(new)* | new | B — assert env-var override surfaces correctly |

**Estimate:** ~100-150 LOC implementation + ~15 tests. One focused session of work after planning.

---

## 4. Open questions for v1 plan

| # | Question |
|---|---|
| Q1 | **Where does the byte-width metadata live today?** If `ModbusTcpSourceAdapter.InitializeAsync` consults a shared lookup table for the byteorder check, the wizard validator should reuse the same table (compositional). If the check is inline-only, v1 plan needs to extract a small shared helper. Either way, no duplication. |
| Q2 | **Config-path API surface** — does `/api/v1/config` already surface the active path? If yes, the Config page reads it from the existing response. If no, do we add it to `GatewayConfiguration` DTO, add a sibling `/api/v1/config/path` endpoint, or expose via DI to Razor directly? Simplest path likely wins. |
| Q3 | **Override-chip granularity** — distinguish between `EDGECONNECT_DATA_ROOT` (broad) and `EDGECONNECT_CONFIG_DIR` (narrower) when both could be set, or surface the resolved-path source as a single string ("env: EDGECONNECT_DATA_ROOT" vs "default")? |
| Q4 | **Tag-table inline error rendering** — does the existing tag table have a row-error slot today, or do we need to add one? If new slot, where does it live visually (below the row, inside an MudAlert in the cell, tooltip on the offending field)? |
| Q5 | **Port helper text wording final** — "5020 for the bundled test simulator" mentions an artifact (the test simulator) that production operators don't have. Acceptable, or rephrase ("5020 if you're testing locally with a Modbus simulator")? |

---

## 5. Cadence (proposed — lighter than M.2b.6.1)

Given the small scope and that each fix is independent + well-bounded, propose:

1. **v1 plan** — resolves Q1-Q5, locks file-by-file deliverables. ChatGPT review pass optional.
2. **v2 amendment** — *if* ChatGPT review surfaces architectural concerns. Skip if the v1 plan is structurally clean.
3. **Reality check** — SKIP. The three fixes touch surfaces we know well (wizard model + Razor edit + composition root); no architectural unknowns to investigate.
4. **Implementation** — single focused session.
5. **Smoke** — manual verification of each fix in turn:
   - A: open Add Modbus, try uint16 + CDAB → expect inline error + save disabled
   - B: set EDGECONNECT_DATA_ROOT, restart Studio → expect log line + Config page caption
   - C: open Add Modbus, hover port field → expect 5020 mention in helper text

**Reserve the right** to promote to a full v1→v2→v3 cadence if v1 plan reveals more architectural complexity than expected (e.g. config-path surfacing requires a new contract that affects other consumers).

---

## 6. Anti-silent-scope-expansion principle

Same as M.2b.6.1's handoff §10:

> Any tradeoff surfaced during implementation that isn't covered by the v1 plan produces a v2 amendment file, not a quiet absorption into the implementation PR.

Examples of what would be silent scope expansion (do NOT do without v2):

- "I'll also clean up some unrelated helper-text on the OPC UA wizard while I'm here" — no. Each wizard's helper text is its own follow-up if needed.
- "The Modbus wizard tag table feels cluttered; let me restructure it" — no. M.2e Shared List Infrastructure owns wizard-table chrome refactors.
- "While adding the override chip, I'll also surface license info" — no. License UI is M.2l.
- "I noticed the Config page would benefit from a 'rotate version' button" — no. Out of any current milestone.

When in doubt: pause, surface, ask.

---

## 7. References

- M.2b.6.1 plan trail and handoff: `docs/sessions/2026-05-19-mp2b6-1-*.md`
- M.2b.6.1 implementation PR #13 (merged as `89f5eca`)
- Spawned task chips (5 total, 3 absorbed into this milestone):
  - #2: Modbus wizard cross-validate datatype + byteorder → §1.A above
  - #3: Studio surfaces active config path → §1.B above
  - #4: Modbus wizard port helper text → §1.C above
- Roadmap v2: `docs/sessions/2026-05-19-post-mp2b6-product-roadmap-v2.md`
- Platform principles P6 (operational product, not developer tool) — primary motivation
- Locked N from M.2b.5/6 v3 plan: eager-validation composition discipline (referenced in §1.A)

---

**End of M.2b.6.2 kickoff. v1 plan starts in next session.**
