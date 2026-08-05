# M.2d.4 — Cross-wizard consistency sweep (v2 plan, LOCKED)

**Status:** v2 — all open questions from v1 locked. Pending user ratification.
**Date:** 2026-05-26
**Supersedes:** `docs/sessions/2026-05-21-m2d4-cross-wizard-sweep-plan.md` (v1)
**Preconditions satisfied:** M.2d.1 ✓, M.2d.2 ✓, M.2d.3 ✓ (all on master, `421f112`)

---

## 1. Q1–Q7 Locked Decisions

| # | Question | v2 Decision | Reasoning |
|---|---|---|---|
| Q1 | `IPerInstanceValidator<T>` interface | **ADR convention only, no C# interface** | Inventory shows no new per-item validators needed (FOCAS2, Brother, MQTT, OpcUa have zero per-instance items). The pattern is `static Validate(item, pathPrefix, errors)` — document as contract in ADR-0015; don't add a C# interface for one existing caller (ModbusTagValidator). |
| Q2 | MQTT topic-template validator in scope? | **Out of scope** | MQTT adapter validates topic template as a scalar (non-empty check). No per-item topic collection exists. ADR-0015 explicitly documents "not all wizards have per-instance items." |
| Q3 | Static helper vs. interface-implementing class | **Static convention** (as today) | ModbusTagValidator is already the canonical shape. Document `static Validate(item, pathPrefix, List<ValidationIssue>)` in ADR-0015. DI lift deferred until a real injection need emerges. |
| Q4 | Audit: automated or manual checklist? | **Manual checklist** | `tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md`. Reviewed at PR time. Automated test suite deferred to follow-up if drift becomes a problem. |
| Q5 | Next ADR number | **ADR-0015** | `ls docs/decisions/` confirms 0001–0014 exist; 0015 is next. M.2d.1/2/3 spawned no new ADRs. |
| Q6 | Should ADR include "how to add a new wizard" section? | **Yes** | Brief 5-step operational guide (WizardModel → validator composition → WizardShell/WizardSection → RegisterRoute → picker card). Acts as the extension contract for future protocols. |
| Q7 | Does Route belong in the per-instance-validator sweep? | **Partial fit** | Routes have no datatype-level per-item objects, but filter rules and transform rows are per-item. ADR-0015 documents route as "wiring wizard, partial fit" and notes that `RouteFilterEditorModel` / `DeadbandRow` / `ScaleRow` validation happens inline in the wizard model (no dedicated static validator needed — no adapter-side caller to share with). |

---

## 2. Inventory findings that reshape scope

The inventory pass (run against master at `421f112`) surfaced one critical gap the v1 plan did not anticipate:

**Sink wizards (MQTT, OpcUa) never adopted WizardShell / WizardSection.**

All three source wizards (Focas2, Brother, Modbus) were migrated to `WizardShell + WizardSection` during M.2d.2. The sink wizards were added before M.2d.1 shipped and still use manual `MudStack + MudPaper` for their layout. This means:

| Aspect | Sources (Focas2, Brother, Modbus) | Sinks (MQTT, OpcUa) |
|---|---|---|
| Outer structure | `WizardShell` | Manual `MudStack` |
| Section headers | `WizardSection Index="N"` | `MudPaper` + hardcoded "N. Title" |
| Validation banner | `WizardValidationBanner` slot in `WizardShell` | No banner — manual `MudAlert` per section |
| Footer | `WizardActions` via `WizardShell` footer slot | `WizardActions` at bottom of manual stack (already consistent ✓) |

**This structural gap IS the primary M.2d.4 work.** Without fixing it, "interchangeable in feel" cannot be claimed. The scope decision is whether to adopt WizardShell for sinks in this milestone or defer.

> **Recommendation: Adopt WizardShell for both sink wizards in M.2d.4.** The plan's headline goal is "interchangeable in feel and identical in contract." WizardShell adoption is the prerequisite for `WizardValidationBanner` working correctly, for section numbers being auto-driven, and for the ADR-0015 component hierarchy claim to hold.

The Route wizard (`AddRoute.razor`) already uses manual layout too, but its structure is materially different (source/sink pickers, not protocol sections). **Route is excluded from WizardShell adoption** — it is documented as a "wiring wizard, different shape" in ADR-0015.

---

## 3. UX polish list — locked from M.2d.2/M.2d.3 experience

| Item | Action | Affects |
|---|---|---|
| P1 | Adopt WizardShell + WizardSection for MQTT sink | AddMqttDestination.razor (structural refactor) |
| P2 | Adopt WizardShell + WizardSection for OpcUa sink | AddOpcUaServerDestination.razor (structural refactor) |
| P3 | Wire WizardValidationBanner in MQTT + OpcUa wizards | WizardValidationBanner was deferred from M.2d.1/M.2d.3 — now land it in all 5 non-route wizards |
| P4 | Implement OnMessageClick scroll-to-field | WizardValidationBanner.razor + JS interop (deferred from M.2d.1) |
| P5 | Standardise Test Connection label | FOCAS2 stays "Browse Controller" (product name). All others use "Test Connection". |
| P6 | Cross-wizard consistency audit checklist | New markdown file covering §4 table (severity, link, auto-clear, save-gating, ordering) per wizard |

**Out of scope for v2 polish list (explicitly deferred):**
- Keyboard navigation (Esc/Ctrl+Enter) — M.2d.5+
- Snackbar copy unification — already consistent from WizardActions
- Loading-state placeholder choice (MudProgressLinear vs MudSkeleton) — non-material

---

## 4. Revised deliverables

| Deliverable | Type | Notes |
|---|---|---|
| `AddMqttDestination.razor` WizardShell adoption | Edit | Replace manual MudStack/MudPaper with WizardShell + WizardSection. Keep all logic, parameters, edit mode — structure only. |
| `AddOpcUaServerDestination.razor` WizardShell adoption | Edit | Same. |
| `WizardValidationBanner` scroll-to-field implementation | Edit | `OnMessageClick` fires a JS `scrollIntoView` + `focus()` on the element with `id="field-{message.FieldAnchor}"`. |
| Wire `WizardValidationBanner` in all 5 protocol wizards | Edit | Focas2, Brother, Modbus already have the shell — add banner wiring. MQTT and OpcUa gain it via WizardShell adoption. |
| `docs/decisions/0015-wizard-contract.md` | New ADR | The load-bearing deliverable. Documents component hierarchy, per-instance validator convention, edit vs add contract, Test Connection semantics, save-flow contract, persistence boundary, "how to add a new wizard." |
| `tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md` | New doc | Manual audit checklist per §4 table, per wizard. |
| Tests for WizardValidationBanner scroll-to-field | New tests | Component test or Playwright snapshot (decide at implementation time). |
| `docs/sessions/2026-05-26-m2d4-handoff.md` | Handoff | End-of-session; M.2d closure cross-reference. |

**Dropped from v1 deliverables (scope collapse from Q-resolution):**
- `IPerInstanceValidator<T>` interface (no C# interface needed — ADR convention only)
- `Focas2RegisterValidator` (no per-instance FOCAS2 items to validate)
- `BrotherDataPointValidator` (same)
- `MqttTopicTemplateValidator` (Q2 = out of scope)
- `OpcUaServerNodeIdValidator` (no per-NodeId items in wizard)
- Modbus refactor (ModbusTagValidator shape unchanged — documents as canonical in ADR)

---

## 5. Implementation steps (10 steps)

| Step | Work |
|---|---|
| 1 | Branch `claude/m2d4-impl`. Inventory pass: fill in the cross-wizard consistency audit checklist first (this forces reading every wizard systematically — gaps become explicit before coding). |
| 2 | Draft ADR-0015 (`docs/decisions/0015-wizard-contract.md`) — write the contract **before** touching code so the edits in steps 3–7 implement the written contract, not the other way around. Surface to user for review before merge. |
| 3 | Implement `OnMessageClick` scroll-to-field in `WizardValidationBanner.razor` with `IJSRuntime` JS interop. Unit test. |
| 4 | Adopt WizardShell + WizardSection in `AddMqttDestination.razor`. Keep all logic/parameters/edit-mode unchanged — structure only. Wire `WizardValidationBanner`. |
| 5 | Adopt WizardShell + WizardSection in `AddOpcUaServerDestination.razor`. Same. |
| 6 | Wire `WizardValidationBanner` into source wizards (Focas2, Brother) where it isn't wired yet. Modbus already has it via the tag-row inline errors — verify the banner also shows summary. |
| 7 | Standardise Test Connection labels (P5): verify all three source wizards pass correct label to `WizardActions`. |
| 8 | Close any remaining audit checklist gaps. Fill in the final checklist tick-marks. |
| 9 | ADR-0015 final review pass — user approves before merge. |
| 10 | Full test suite, zero warnings, handoff doc, M.2d closure commit. |

---

## 6. Definition of done

- [ ] All 5 protocol wizards (Focas2, Brother, Modbus, MQTT, OpcUa) use `WizardShell + WizardSection`.
- [ ] `WizardValidationBanner` wired in all 5. `OnMessageClick` scrolls to + focuses the target field.
- [ ] Cross-wizard consistency audit checklist all green (zero open boxes).
- [ ] `docs/decisions/0015-wizard-contract.md` landed and user-approved.
- [ ] Test Connection labels consistent (only FOCAS2 differs by product name — documented in ADR).
- [ ] `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky"` clean, zero warnings.
- [ ] M.2d handoff doc cross-references all four sub-milestone handoffs + ADR-0015.
