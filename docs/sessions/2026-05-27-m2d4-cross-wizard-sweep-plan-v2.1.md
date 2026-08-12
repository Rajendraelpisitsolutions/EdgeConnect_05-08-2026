# M.2d.4 — Cross-wizard consistency sweep (v2.1 plan, LOCKED)

**Status:** v2.1 — Opus-4.7 review pass applied to v2. Pending user ratification.
**Date:** 2026-05-27
**Supersedes:** `docs/sessions/2026-05-26-m2d4-cross-wizard-sweep-plan-v2.md` (v2, Sonnet 4.6)
**v2 → v2.1 delta:** Four refinements from Opus review — (1) field-anchor naming convention, (2) visual-regression smoke gate, (3) banner empty-state behavioural rule, (4) PR strategy locked.
**Preconditions satisfied:** M.2d.1 ✓, M.2d.2 ✓, M.2d.3 ✓ (all on master, `421f112`)

---

## 1. Q1–Q7 Locked Decisions (unchanged from v2)

| # | Question | v2.1 Decision | Reasoning |
|---|---|---|---|
| Q1 | `IPerInstanceValidator<T>` interface | **ADR convention only, no C# interface** | Inventory shows no new per-item validators needed (FOCAS2, Brother, MQTT, OpcUa have zero per-instance items). The pattern is `static Validate(item, pathPrefix, errors)` — document as contract in ADR-0015; don't add a C# interface for one existing caller (ModbusTagValidator). |
| Q2 | MQTT topic-template validator in scope? | **Out of scope** | MQTT adapter validates topic template as a scalar (non-empty check). No per-item topic collection exists. ADR-0015 explicitly documents "not all wizards have per-instance items." |
| Q3 | Static helper vs. interface-implementing class | **Static convention** (as today) | ModbusTagValidator is already the canonical shape. Document `static Validate(item, pathPrefix, List<ValidationIssue>)` in ADR-0015. DI lift deferred until a real injection need emerges. |
| Q4 | Audit: automated or manual checklist? | **Manual checklist** | `tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md`. Reviewed at PR time. Automated test suite deferred to follow-up if drift becomes a problem. |
| Q5 | Next ADR number | **ADR-0015** | `ls docs/decisions/` confirms 0001–0014 exist; 0015 is next. M.2d.1/2/3 spawned no new ADRs. |
| Q6 | Should ADR include "how to add a new wizard" section? | **Yes** | Brief 5-step operational guide (WizardModel → validator composition → WizardShell/WizardSection → RegisterRoute → picker card). Acts as the extension contract for future protocols. |
| Q7 | Does Route belong in the per-instance-validator sweep? | **Partial fit** | Routes have no datatype-level per-item objects, but filter rules and transform rows are per-item. ADR-0015 documents route as "wiring wizard, partial fit" and notes that `RouteFilterEditorModel` / `DeadbandRow` / `ScaleRow` validation happens inline in the wizard model (no dedicated static validator needed — no adapter-side caller to share with). |

---

## 2. Inventory findings that reshape scope (unchanged from v2)

The inventory pass (run against master at `421f112`) surfaced one critical gap the v1 plan did not anticipate:

**Sink wizards (MQTT, OpcUa) never adopted WizardShell / WizardSection.**

All three source wizards (Focas2, Brother, Modbus) were migrated to `WizardShell + WizardSection` during M.2d.2. The sink wizards were added before M.2d.1 shipped and still use manual `MudStack + MudPaper` for their layout. This means:

| Aspect | Sources (Focas2, Brother, Modbus) | Sinks (MQTT, OpcUa) |
|---|---|---|
| Outer structure | `WizardShell` | Manual `MudStack` |
| Section headers | `WizardSection Index="N"` | `MudPaper` + hardcoded "N. Title" |
| Validation banner | `WizardValidationBanner` slot in `WizardShell` | No banner — manual `MudAlert` per section |
| Footer | `WizardActions` via `WizardShell` footer slot | `WizardActions` at bottom of manual stack (already consistent ✓) |

**This structural gap IS the primary M.2d.4 work.** Without fixing it, "interchangeable in feel" cannot be claimed.

The Route wizard (`AddRoute.razor`) uses manual layout too, but its structure is materially different (source/sink pickers, not protocol sections). **Route is excluded from WizardShell adoption** — documented as a "wiring wizard, different shape" in ADR-0015.

---

## 3. UX polish list — locked from M.2d.2/M.2d.3 experience (unchanged from v2)

| Item | Action | Affects |
|---|---|---|
| P1 | Adopt WizardShell + WizardSection for MQTT sink | AddMqttDestination.razor (structural refactor) |
| P2 | Adopt WizardShell + WizardSection for OpcUa sink | AddOpcUaServerDestination.razor (structural refactor) |
| P3 | Wire WizardValidationBanner in MQTT + OpcUa wizards (Focas2/Brother/Modbus audit) | All 5 protocol wizards |
| P4 | Implement OnMessageClick scroll-to-field | WizardValidationBanner.razor + JS interop (deferred from M.2d.1) |
| P5 | Standardise Test Connection label | FOCAS2 stays "Browse Controller" (product name). All others use "Test Connection". |
| P6 | Cross-wizard consistency audit checklist | New markdown file covering §4 table (severity, link, auto-clear, save-gating, ordering) per wizard |

---

## 4. Refinement R1 — Field anchor naming convention (NEW in v2.1)

`WizardValidationBanner.OnMessageClick` scrolls to + focuses the target field via a DOM `id`. The convention is locked in v2.1:

### Naming rule

**DOM `id` format:** `field-{anchor}` where `{anchor}` matches `WizardValidationMessage.FieldAnchor`.

**`FieldAnchor` format:** kebab-case, hierarchical path delimited by `.`.

Examples:
- `field-instance-id` — top-level identity field
- `field-connection.host` — host inside the connection section
- `field-connection.port`
- `field-security.cert-path`
- `field-tag-definitions.3.byte-order` — row 3 in the tag table, byte-order column (Modbus only)

### Ownership contract

| Side | Responsibility |
|---|---|
| Wizard model (`*.cs`) | When emitting a `WizardValidationMessage`, sets `FieldAnchor` to the kebab-case path that matches the razor template's DOM id. |
| Razor template (`*.razor`) | Each input field that can have validation issues must declare `id="field-{anchor}"` via MudBlazor's `UserAttributes`. |
| Per-instance validator (`*.cs`) | Returns `pathPrefix` in kebab-case (e.g. `"tag-definitions.3"`); the wizard model concatenates with field name to produce the FieldAnchor. |

### MudBlazor mechanics

MudBlazor inputs don't take a raw `id` prop. Use `UserAttributes`:

```razor
<MudTextField @bind-Value="_model.Host"
              Label="Host *"
              UserAttributes="@(new Dictionary<string, object?> { ["id"] = "field-connection.host" })" />
```

ADR-0015 documents this pattern; the audit checklist verifies every field with a potential validation message has the correct `id`.

### Why kebab-case

CSS-selector friendly, matches existing data-testid convention, avoids C# property-name leakage into the DOM contract (renaming a wizard model property doesn't break field IDs).

---

## 5. Refinement R2 — Visual regression smoke gate (NEW in v2.1)

WizardShell adoption for MQTT and OpcUa is mechanically safe (logic, parameters, edit-mode preserved) but visually invasive — section spacing, header band styling, Test Connection panel placement, edit-mode banner position. **No automated visual-diff tool exists in this codebase.**

### Smoke gate procedure (gates merge of PR)

Before merging M.2d.4, the user performs side-by-side comparison:

1. **Capture baseline screenshots** (one per wizard, before any structural changes):
   - Add MQTT destination (no fields filled)
   - Add MQTT destination (after Test Connection)
   - Edit MQTT destination (pre-filled, runtime hydration banner visible)
   - Add OpcUa Server destination
   - Edit OpcUa Server destination
2. **Apply WizardShell adoption (Steps 4–5 below).**
3. **Capture post-refactor screenshots** — same five views.
4. **Compare:** section ordering, vertical rhythm, header band style, footer button alignment must match. Acceptable deltas: WizardSection auto-numbering chip appearance, padding refinements that fall within the M.2d.1 shell design language. Unacceptable deltas: missing sections, reordered fields, lost edit-mode banner, broken Test Connection panel.

The DoD includes the smoke gate as a hard requirement.

---

## 6. Refinement R3 — Banner empty-state behavioural rule (NEW in v2.1)

ADR-0015 explicitly locks:

> **The validation banner has NO success state.** When `Messages` is null or empty, `WizardValidationBanner` renders zero DOM. There is no "All good ✓" green banner. Absence is the success signal.

This is already the de-facto behaviour (inventory confirms `WizardValidationBanner` early-returns on empty Messages). Codifying it in the ADR prevents regression — future contributors might reasonably add an "✓ Valid" state to make validation feel more responsive; the ADR documents why that's wrong (signal-to-noise, false confidence in passive forms).

---

## 7. Refinement R4 — PR strategy (NEW in v2.1)

**Locked: single PR for full M.2d.4 sweep.**

Considered alternative: split into PR-A (ADR + scroll-to-field + banner wiring) and PR-B (WizardShell sink adoption). Rejected because:

1. ADR-0015 is the load-bearing deliverable. If sinks haven't conformed structurally, the ADR has to say "sinks pending alignment" — half-aspirational, half-factual. ADRs should reflect the current state, not a target state.
2. The cross-wizard audit checklist cannot be all-green until sinks adopt the shell — splitting means PR-A ships with an audit that has known-open boxes.
3. M.2d closure is one event in the handoff doc; splitting fragments it.

**Effort acknowledgment:** the realistic effort estimate for M.2d.4 with WizardShell adoption is **3–4 days**, not the v1 plan's "2–3 days." If Track C (sinks shell adoption) surfaces unexpected visual issues, that may extend further. The user owns the merge gate — slips are surfaced, not silently absorbed.

If the user later reverses this decision (e.g., wants ADR sooner), the implementation steps below explicitly mark Track A (steps 1–3) as the natural split point.

---

## 8. Revised deliverables (updated for v2.1 refinements)

| Deliverable | Type | Notes |
|---|---|---|
| `AddMqttDestination.razor` WizardShell adoption | Edit | Replace manual MudStack/MudPaper with WizardShell + WizardSection. Keep all logic, parameters, edit mode — structure only. |
| `AddOpcUaServerDestination.razor` WizardShell adoption | Edit | Same. |
| `WizardValidationBanner` scroll-to-field implementation | Edit | `OnMessageClick` fires JS interop (`scrollIntoView({behavior:'smooth', block:'center'}) + focus()`) on element with `id="field-{message.FieldAnchor}"`. |
| **Field anchor `id` attributes** on all validatable fields | Edit | All 5 protocol wizards. MudBlazor `UserAttributes` pattern per R1. |
| Wire `WizardValidationBanner` in all 5 protocol wizards | Edit | Sources gain it; sinks gain it via WizardShell adoption. |
| `docs/decisions/0015-wizard-contract.md` | New ADR | Documents: component hierarchy, per-instance validator convention, **field-anchor naming convention (R1)**, edit-vs-add discrimination, Test Connection semantics, save-flow contract, persistence boundary, **banner empty-state rule (R3)**, "how to add a new wizard." |
| `tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md` | New doc | Manual audit checklist per §4 table, per wizard. Includes field-anchor coverage verification (R1). |
| Tests for WizardValidationBanner scroll-to-field | New tests | Component test verifying `OnMessageClick` invokes JS interop with correct selector. |
| `docs/sessions/2026-05-XX-m2d4-handoff.md` | Handoff | End-of-session; M.2d closure cross-reference. |

**Dropped from v1 (scope collapse, unchanged from v2):**
- `IPerInstanceValidator<T>` interface, Focas2RegisterValidator, BrotherDataPointValidator, MqttTopicTemplateValidator, OpcUaServerNodeIdValidator, Modbus refactor.

---

## 9. Implementation steps (10 steps, with track markers)

| Step | Track | Work |
|---|---|---|
| 1 | A | Branch `claude/m2d4-impl`. Inventory pass: fill in the cross-wizard consistency audit checklist first (forces reading every wizard systematically — gaps explicit before coding). |
| 2 | A | Draft ADR-0015 (`docs/decisions/0015-wizard-contract.md`) — write the contract **before** touching code. Includes R1 field-anchor convention + R3 banner empty-state rule. Surface to user for review before merge. |
| 3 | A | Implement `OnMessageClick` scroll-to-field in `WizardValidationBanner.razor` with `IJSRuntime` JS interop. Unit test. |
| 4 | C | **Capture baseline screenshots (R2 smoke gate).** Adopt WizardShell + WizardSection in `AddMqttDestination.razor`. Add field-anchor `id` attributes (R1). Wire `WizardValidationBanner`. |
| 5 | C | Adopt WizardShell + WizardSection in `AddOpcUaServerDestination.razor`. Add field-anchor `id` attributes. Wire `WizardValidationBanner`. |
| 6 | B | Wire `WizardValidationBanner` into source wizards (Focas2, Brother) where it isn't wired. Add field-anchor `id` attributes. Verify Modbus banner shows summary as well as per-row errors. |
| 7 | B | Standardise Test Connection labels (P5): verify all three source wizards pass correct label to `WizardActions`. |
| 8 | A+B+C | Close any remaining audit checklist gaps. Fill in the final checklist tick-marks. **User performs R2 visual-regression smoke compare.** |
| 9 | A | ADR-0015 final review pass — user approves before merge. |
| 10 | A+B+C | Full test suite, zero warnings, handoff doc, M.2d closure commit. |

Track markers: **A** = ADR + scroll-to-field (Opus-review natural split point if reversed); **B** = banner wiring + label cleanup; **C** = WizardShell sink adoption (highest visual-regression risk).

---

## 10. Definition of done

- [ ] All 5 protocol wizards (Focas2, Brother, Modbus, MQTT, OpcUa) use `WizardShell + WizardSection`.
- [ ] `WizardValidationBanner` wired in all 5. `OnMessageClick` scrolls to + focuses the target field.
- [ ] Field-anchor `id` attributes (R1) applied uniformly. Convention documented in ADR-0015.
- [ ] Cross-wizard consistency audit checklist all green (zero open boxes).
- [ ] `docs/decisions/0015-wizard-contract.md` landed and user-approved.
- [ ] Banner empty-state rule (R3) codified in ADR-0015.
- [ ] Test Connection labels consistent (only FOCAS2 differs by product name — documented in ADR).
- [ ] **R2 visual-regression smoke gate passed** — user confirms MQTT + OpcUa add and edit flows render without unacceptable deltas vs pre-refactor baseline screenshots.
- [ ] `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky"` clean, zero warnings.
- [ ] M.2d handoff doc cross-references all four sub-milestone handoffs + ADR-0015.

---

## 11. v2 → v2.1 change log

| # | Refinement | Source | Where applied |
|---|---|---|---|
| R1 | Field-anchor naming convention | Opus review identified missing DOM-contract spec | New §4, deliverables, step 4–6, DoD |
| R2 | Visual regression smoke gate | Opus review identified no automated visual-diff | New §5, step 4 (baseline capture), step 8 (compare), DoD |
| R3 | Banner empty-state behavioural rule | Opus review identified invisible behaviour worth codifying | New §6, ADR-0015 outline |
| R4 | PR strategy locked (single PR) | Opus review surfaced split-PR option | New §7, effort estimate updated to 3–4 days |
