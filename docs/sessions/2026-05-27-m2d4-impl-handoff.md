# M.2d.4 — Cross-wizard Consistency Sweep: Implementation Handoff

**Date:** 2026-05-27
**Branch:** `claude/m2d4-impl`
**Status:** Implementation complete — full test suite green, awaiting user smoke-test + commit/PR approval
**Plan reference:** `docs/sessions/2026-05-27-m2d4-cross-wizard-sweep-plan-v2.1.md`
**ADR:** `docs/decisions/0015-wizard-contract.md`
**Audit:** `tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md`
**Preconditions:** M.2d.1 + M.2d.2 + M.2d.3 all merged to master at `421f112`. This sub-milestone closes the M.2d sub-roadmap.

---

## What was built

M.2d.4 enforces a single written contract across all six wizards (3 source + 2 sink + 1 route) and closes the structural and behavioural gaps the inventory pass surfaced. The headline deliverables:

1. **ADR-0015** — 8-rule wizard contract organised across four layers (structure / contracts / behaviour / lifecycle) + 5-step "how to add a new wizard" operational guide.
2. **Sink WizardShell adoption** — MQTT and OpcUa Server sinks migrated from manual `MudStack + MudPaper` to `WizardShell + WizardSection`. Structural parity with sources.
3. **WizardValidationBanner wire-up** — banner was an M.2d.1 primitive with zero consumers; now wired across all 5 protocol wizards.
4. **Scroll-to-field JS interop** — clicking a validation message with a `FieldAnchor` selector calls `window.wizardValidation.scrollToFieldAnchor` (JS interop), scrolling the target field into view and focusing it.
5. **Field-anchor naming convention (R1)** — kebab-case CSS selectors prefixed with `#field-` (e.g. `#field-connection.broker-host`), rendered on inputs via MudBlazor `UserAttributes`.
6. **Cross-wizard consistency audit checklist** — captures the post-M.2d.4 state of all 6 wizards across 8 sections. Drift-detection artifact for future PRs.

---

## Files changed

### New
| File | Purpose |
|------|---------|
| `docs/decisions/0015-wizard-contract.md` | ADR — 8-rule wizard contract |
| `tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md` | Manual audit checklist |
| `src/ElpisEdgeConnect.Management/wwwroot/js/wizardValidation.js` | JS module for scroll-to-field interop |
| `docs/sessions/2026-05-27-m2d4-cross-wizard-sweep-plan-v2.1.md` | v2.1 plan (post-Opus review) |
| `docs/sessions/2026-05-27-m2d4-impl-handoff.md` | This document |

### Modified — shared primitives + wiring
| File | Change |
|------|--------|
| `src/ElpisEdgeConnect.Management/Components/Shared/WizardValidationBanner.razor` | `@inject IJSRuntime JS`; click handler now invokes `wizardValidation.scrollToFieldAnchor` JS interop, then optionally fires `OnMessageClick`. Swallows `JSException` and `InvalidOperationException` to keep the circuit alive. |
| `src/ElpisEdgeConnect.Management/Components/App.razor` | Registered `<script src="js/wizardValidation.js">` after `MudBlazor.min.js`. |

### Modified — sink wizards (structural refactor)
| File | Change |
|------|--------|
| `src/ElpisEdgeConnect.Management/Wizards/MqttSinkWizardModel.cs` | Added `Validate()` returning `IReadOnlyList<WizardValidationMessage>` aggregating all per-field checks with kebab-case `FieldAnchor` selectors. |
| `src/ElpisEdgeConnect.Management/Wizards/OpcUaServerSinkWizardModel.cs` | Same — aggregates Endpoint URL, Application URI, Namespace, Capacity, User Token Policies. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddMqttDestination.razor` | Full refactor — replaced manual `MudStack + MudPaper` with `WizardShell + WizardSection + WizardActions`. Test Connection button moved to WizardActions footer; result rendered inline in Connection section per ADR-0015 Rule 6. Field-anchor `id` attributes added via `UserAttributes`. WizardValidationBanner wired. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddOpcUaServerDestination.razor` | Same structural refactor. Namespace + Capacity sections converted from collapsible `MudExpansionPanel` to flat `WizardSection` with `Description` captions — collapsibility deferred. No Test Connection (acceptor carve-out per ADR Rule 6); the "no Test Connection" Info alert preserved in body. |

### Modified — source wizards (banner wiring only — already on WizardShell)
| File | Change |
|------|--------|
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddFocas2Source.razor` | Added `<WizardValidationBanner>` widget + `BuildValidationMessages()` razor helper aggregating `_instanceIdError`. Added `id="field-instance-id"` via `UserAttributes`. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddBrotherHttpSource.razor` | Same pattern. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddModbusSource.razor` | Same + extra: `BuildValidationMessages()` also aggregates per-tag-row errors via `ModbusSourceWizardModel.ValidateTag`. Per-row entries have `FieldAnchor=null` because the table cells don't carry stable DOM ids yet (deferred). |

### Modified — tests
| File | Change |
|------|--------|
| `tests/ElpisEdgeConnect.Management.Tests/Components/Shared/WizardValidationBannerTests.cs` | Added 2 new tests: (1) `Click_MessageWithFieldAnchor_InvokesScrollToFieldAnchorJsInterop` — verifies JS interop is called with the exact selector argument via bUnit's strict-mode JSInterop. (2) `Click_MessageWithoutFieldAnchor_DoesNotInvokeJsInterop` — verifies non-anchored messages produce zero JS invocations. |

---

## Test count

**Pre-M.2d.4 (master `421f112`):** 2,507 passing, 1 skipped.
**Post-M.2d.4:** **2,509 passing, 1 skipped** (676 → 678 in Management.Tests; rest unchanged).

The skipped test is the pre-existing flaky MQTT reconnect (`Gate5_BrokerOutageReconnect_AdapterRecoversWithin5Seconds`).

---

## R2 visual-regression smoke gate — what the user needs to verify

Per the v2.1 plan §5, the WizardShell adoption for sinks is mechanically safe but visually invasive. **Before merging, please verify the following 5 flows in Studio:**

1. **Add MQTT destination** (`/destinations/new/mqtt`)
   - Header band: cloud icon + title "Add MQTT destination" + subtitle.
   - Five numbered sections (Identity, Connection, Authentication, Topic policy, Routing).
   - Footer: Test Connection + Cancel + Save buttons in `WizardActions`.
   - Validation banner shows current errors above sections; clicking an error scrolls to + focuses the field.

2. **Add MQTT destination → Test Connection**
   - Click Test Connection in footer → result appears as inline panel **inside the Connection section** (NOT as a snackbar, per ADR-0015 Rule 6).

3. **Edit MQTT destination** (pencil icon from `/destinations` → `/destinations/{id}/edit`)
   - Same structure, pre-filled. InstanceId disabled, "Editing runtime configuration" version banner from `SinkEditRouter` above the shell.
   - Routing section hidden (Edit never mutates routes).

4. **Add OpcUa Server destination** (`/destinations/new/opcua`)
   - Five numbered sections (Identity, Server, Namespace, Security, Capacity).
   - Namespace and Capacity now flat `WizardSection`s with description captions (previously were collapsible expansion panels — accept the trade-off or surface as a follow-up).
   - Footer: Cancel + Save only (no Test Connection — acceptor carve-out).
   - "OpcUa Server destinations do not support Test Connection" Info alert preserved near the bottom.

5. **Edit OpcUa Server destination**
   - Same structure as Add, pre-filled, routing section hidden.

**Acceptable deltas:** WizardSection's auto-numbered chip appearance vs. the pre-M.2d.4 hardcoded "N. Title" text; padding/margin refinements that fall within the shell design language.

**Unacceptable deltas:** missing sections, reordered fields, broken Test Connection panel, lost edit-mode banner, regressed StaleEditWarningBanner.

---

## ADR-0015 review surface

The ADR is the load-bearing deliverable. Three items I flagged for your review when I drafted it:

1. **R6 OpcUa carve-out reasoning** — I documented "OpcUaServer has no Test Connection because binding has runtime side effects (acceptor design)." Was this the actual reason, or is there other historical context I missed?
2. **R7 Add vs Edit save-flow asymmetry** — Add = POST draft (visits Config page to apply); Edit = PUT direct-apply. I justified this as "Add is exploratory, Edit is focused mutation." If you consider that user-hostile, amending the ADR before merge would change implementation downstream.
3. **R8 No localStorage / no auto-save** — Locked. Future contributors couldn't add tab-close persistence without amending the ADR. Confirm this is the intent.

You said "Proceed" after I flagged these, which I took as implicit approval. Final review before merge is still yours.

---

## Smoke-test checklist (gates merge)

- [ ] R2 visual-regression check passes for MQTT add + edit + Test Connection result placement
- [ ] R2 visual-regression check passes for OpcUa Server add + edit (Namespace + Capacity sections flat ✓ or ✗?)
- [ ] Source wizards (Focas2, Brother, Modbus) still render correctly (banner now visible above first section)
- [ ] Clicking a validation banner message scrolls to + focuses the target field
- [ ] All existing edit flows + StaleEditWarningBanner concurrency still work as in M.2d.3
- [ ] ADR-0015 reads as a contract you'd review a new wizard against

---

## What ships with M.2d closure

M.2d.4 closes the M.2d sub-roadmap. After merge:
- M.2d.1 shared primitives (shipped 2026-05-21) — all 5 primitives in use
- M.2d.2 source edit-via-wizard (shipped 2026-05-22) — Focas2/Brother/Modbus edit
- M.2d.3 sink + route edit-via-wizard (shipped 2026-05-26) — MQTT/OpcUa/Route edit
- M.2d.4 consistency sweep (this PR) — ADR-0015 locked + structural alignment + banner adoption

Next: phase 2 wrap-up roadmap §3.8 (or whatever the user picks as the next milestone).

---

## Known follow-ups (tracked, not regressions)

1. **Route wizard banner wiring** — deferred per ADR-0015 Q7 (route is a wiring wizard with partial fit). `RouteFilterEditorModel` has its own inline validation; banner wiring is a follow-up when those validators are next touched.
2. **Modbus per-row tag-cell anchors** — banner aggregates per-row errors today but with `FieldAnchor=null`. Designing stable per-cell DOM ids is a future enhancement.
3. **OpcUa Namespace / Capacity collapsibility** — flattened in M.2d.4. If operator feedback wants collapsibility, add it as a `WizardSection` parameter rather than reverting to ad-hoc expansion panels.
