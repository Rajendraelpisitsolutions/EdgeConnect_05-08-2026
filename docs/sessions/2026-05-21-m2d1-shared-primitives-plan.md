# M.2d.1 — Shared wizard primitives (v1 plan trail, BRIEF)

**Status:** v1 — DRAFT, open questions below, awaiting ChatGPT review pass.
**Date:** 2026-05-21
**Roadmap anchor:** [v2 §3.7.1](2026-05-21-phase2-wrapup-roadmap-v2.md), refined by [v2.3 §1.1–§1.2](2026-05-21-phase2-wrapup-roadmap-v2.3.md).
**Sub-milestone:** first of four M.2d sub-milestones (M.2d.1 → .2 → .3 → .4).
**Estimated size:** ~3–4 days per v2 §3.7.1.

---

## 1. Goal

Extract the common UX vocabulary that already exists, ad-hoc, across the six existing wizards (3 source + 2 sink + 1 route) into a small set of shared Razor components and one C# context type. M.2d.1 **touches no wizard.** It only creates the components that M.2d.2/.3 will then adopt and M.2d.4 will sweep into consistency. This sub-milestone is component-only: build the primitives, ship them with their own unit tests, leave every wizard unchanged.

---

## 2. Why this is first in the M.2d sub-sequence

M.2d v1 (single sweep) was identified as a trap in roadmap v2 §3.7. The four-sub-milestone split puts primitives first, on purpose:

| Sub-milestone | Touches | Why ordered this way |
|---|---|---|
| **M.2d.1 (this plan)** | Adds `Components/Shared/Wizard*.razor` + `Wizards/EditModeContext.cs`. No wizard edited. | Lets M.2d.2/.3 adopt a stable shared shape. If we extracted primitives **while** rewriting source wizards, every primitive design choice would be entangled with one specific wizard's accidental shape — and we'd ship six versions of "almost-the-same shell." |
| M.2d.2 | Source wizards adopt the M.2d.1 components + add Test Connection where appropriate (Brother backfill). | Source wizards are the riskiest to break; doing them as a coherent batch lets us catch primitive-design defects early. |
| M.2d.3 | Sink + route wizards adopt the M.2d.1 components. | Strictly downstream of M.2d.2 — any primitive bugs surfaced in .2 are fixed before .3 lands. |
| M.2d.4 | Cross-wizard validation + UX polish + ADR. | Final sweep. Touches all six wizards. |

If M.2d.1 ships clean and M.2d.2 finds the primitives wrong, the cost is small (one PR's worth of M.2d.1 changes + the M.2d.2 work). If M.2d.1 were folded into M.2d.2, finding a primitive defect would mean reworking a wizard PR mid-flight.

---

## 3. Relationship to v2.3 §1.1 (no-new-shared-abstractions rule)

**This milestone is NOT a violation of v2.3 §1.1.** The rule says: "during the Option B implementation window (Chip 4 + Chip 5 + offline-scenario test), do not extract shared abstractions; defer until the dedicated plan trails land." That window covers Chip 4/5/offline-test only — it does NOT cover the seven dedicated plan trails, including this one. M.2d.1's **explicit mandate from roadmap v2 §3.7.1** is to extract the shared wizard primitives. The mandate is the dedicated plan trail; the plan trail is the green light.

Future readers seeing "shared wizard components introduced" should not flag this as a §1.1 violation — they should consult this plan trail, see the v2 §3.7.1 anchor, and confirm M.2d.1 is exactly the work §1.1 was deferring.

---

## 4. Cross-wizard pattern audit

The six wizards already exhibit a remarkably consistent layout (each was authored against a shared mental model, but without shared code). The patterns that recur across all six and are candidates for extraction:

### 4.1 Header band

Every wizard opens with the same Row:

- `MudIconButton` (back arrow) → `Nav.NavigateTo(<parent route>)`.
- `MudIcon` (protocol-specific icon).
- `MudText Typo="h5"` (title) + `MudText Typo="body2"` (subtitle).

Variance: icon + title text + back destination. Everything else identical.

### 4.2 Load-state guard + load-error banner

Every wizard has the same three-state opening:

```
if (_loadError is not null)   → MudAlert.Error
else if (_currentConfig is null) → MudProgressLinear (loading)
else                           → render sections
```

Identical in all six. Variance: none.

### 4.3 Numbered section paper

Every section is wrapped in:

```
<MudPaper Elevation="1" Class="pa-4 mb-4">
  <MudText Typo="subtitle1" Class="mb-3">N. <title></MudText>
  <MudGrid Spacing="2"> ... fields ... </MudGrid>
</MudPaper>
```

Variance: section index, title, field content.

### 4.4 Per-section validation banner (inline `MudAlert`)

Several wizards already show field-level errors inline after the relevant section. Pattern is inconsistent — Brother uses `Error=`/`ErrorText=` on the field itself; FOCAS2 + Modbus drop a `MudAlert` below the MudGrid; MQTT uses `Dense="true"` on a `MudAlert`. Worth standardising in M.2d.4, but M.2d.1 can offer `WizardValidationBanner` as the canonical surface.

### 4.5 Save / Cancel button row (footer)

All six wizards end with the same pattern:

```
<MudStack Row Justify=FlexEnd>
  <MudButton (text)   OnClick=Cancel  Disabled=_busy >Cancel</MudButton>
  <MudButton (filled) OnClick=Save    Disabled=_busy || !CanSave >Save as draft</MudButton>
</MudStack>
```

Variance: text on the Save button ("Save as draft" everywhere today; will become "Save changes" in Edit mode per M.2d.2/.3).

### 4.6 Test Connection panel (where present)

Three of the six surface a probe-style action:

- FOCAS2: "Browse Controller" (probe + render axes/series/type).
- Brother HTTP: none today (M.P2.4 Q12 deferred — backfill in M.2d.2).
- Modbus: none today.
- MQTT sink: "Test Connection" (POST `/api/v1/sinks/test-connection/mqtt`).
- OPC UA Server sink: deliberately NONE (server-side bind has side effects; documented).
- Route: none today.

Shape is consistent where present: a button (busy state), a progress indicator, a result-rendering MudAlert (success/error variant), a caption explaining what the probe does and does not commit. Worth offering a `WizardActions` component that exposes a `TestConnection` slot.

### 4.7 Draft summary preview

Three of the six (FOCAS2, Brother, Modbus, MQTT, OPC UA, Route) render a "Draft summary" `MudPaper` with bulleted "This draft will:" before the footer. The exact bullets are wizard-specific; the wrapping container is identical. M.2d.1 could offer a `WizardShell` slot for this, but the slot itself is just a child render fragment — no abstraction needed beyond the shell. **Not extracted in v1 unless review pass insists.**

### 4.8 Section-collapse + section-error indicators

Not present today. None of the six wizards collapse sections or show per-section error counts. v2 §3.7.1 names "save state" as part of `WizardShell`'s contract — section-error indicators are a natural future surface but **out of scope for M.2d.1** unless review pass elaborates. Flagged as open question Q3 below.

---

## 5. Deliverables (per roadmap v2 §3.7.1)

All paths relative to `src/ElpisEdgeConnect.Management/`.

### 5.1 `Components/Shared/WizardShell.razor`

Renders the header band (§4.1), the load-state guard (§4.2), and a slot for the section content + footer. Parameters:

- `Icon` (the `MudIcon` reference, e.g. `Icons.Material.Filled.PrecisionManufacturing`).
- `Title`, `Subtitle` (strings).
- `BackHref` (string — the parent route for the back arrow).
- `IsLoading` (bool — drives the `MudProgressLinear` branch).
- `LoadError` (string? — drives the `MudAlert.Error` branch).
- `ChildContent` (RenderFragment — the numbered sections).
- `Footer` (RenderFragment — Save/Cancel/Test Connection row; defaults to the standard `WizardActions` arrangement if not supplied).
- `SaveState` (enum — `Editing` | `Saving` | `Saved` | `Failed`; M.2d.2/.3 may drive a transient toast or footer chip from this).

DoD: renders correctly under unit test for each `SaveState` value + load-state branch. **No wizard consumes it yet.**

### 5.2 `Components/Shared/WizardValidationBanner.razor`

A standardised severity-aware banner surfacing errors + warnings with optional anchor-to-field behavior. Parameters:

- `Severity` (`Error` | `Warning` | `Info` — maps to MudBlazor).
- `Messages` (`IReadOnlyList<WizardValidationMessage>` — each carrying `Code`, `Path`, `Message`, and optional `FieldAnchor`).
- `LinkBehavior` — when a message has a `FieldAnchor`, clicking the message scrolls + focuses the anchored field (M.2d.4 standardises this; M.2d.1 only needs the parameter surface).

DoD: unit tests cover the three severities + the multi-message bullet list rendering.

### 5.3 `Components/Shared/WizardWatchSlot.razor`

A placeholder for the M.2c **Runtime Tap** Watch session embed. M.2d.1 ships this as a deliberate no-op: it renders nothing if M.2c is not wired (which is the case today, since M.2c hasn't started). Once M.2c lands, the slot's body resolves to the live-tag table; until then, M.2d.2/.3 can drop the component into wizard sections without any visual side-effect.

Parameters (v1):

- `SourceInstanceId` (string).
- `TagPaths` (`IReadOnlyList<string>?` — optional server-side filter).
- `Available` (bool — set by the host via DI sniff or a feature-flag; defaults to `false` until M.2c lands and flips it to `true`).

DoD: when `Available=false`, the component renders nothing (zero DOM impact). Unit test pins this behavior. Q1 below ratifies the precise placeholder contract.

### 5.4 `Components/Shared/WizardActions.razor`

The Save / Cancel / (optional) Test Connection footer row. Parameters:

- `OnSave`, `OnCancel`, `OnTestConnection` (EventCallback; the latter is optional).
- `SaveLabel` (string; defaults to `"Save as draft"`; M.2d.2/.3 pass `"Save changes"` in Edit mode).
- `CancelLabel` (string; defaults to `"Cancel"`).
- `TestConnectionLabel` (string; defaults to `"Test Connection"`).
- `CanSave` (bool — drives the disabled state of the Save button).
- `Busy` (bool — drives the disabled state of all buttons + the loading indicator).

DoD: unit tests cover button disabled-state matrix + which buttons render based on which callbacks are supplied.

### 5.5 `Wizards/EditModeContext.cs`

A C# type (not a Razor component) that discriminates Add vs Edit and supplies the existing config to a wizard model when Edit mode is active. Shape (v1, illustrative — not code):

- `WizardMode` enum (`Add` | `Edit`).
- `ExistingInstanceId` (`string?` — present only when `Mode == Edit`).
- A small loader method that resolves the existing source/sink/route from the loaded `GatewayConfiguration`.

Roadmap v2 §3.7.1 places this under `Wizards/` (sibling to `Focas2SourceWizardModel.cs` etc.), not under `Components/Shared/`. **Open question Q2 below challenges this placement** — the file is closer to a UI primitive than a wizard model. v1 plan defers to v2 §3.7.1's placement unless Q2 ratifies otherwise.

DoD: unit tests for both modes; Edit-mode loader correctly resolves a known sample config into the right model.

---

## 6. Definition of Done

- [ ] All five deliverables exist under the paths in §5.
- [ ] Each component has at least one unit test pinning its DoD-stated behavior (≥ ~10 tests in total).
- [ ] **No wizard depends on the new components yet.** This is verified by `grep` over the existing six wizards — zero references to `WizardShell` / `WizardValidationBanner` / `WizardWatchSlot` / `WizardActions` / `EditModeContext`.
- [ ] M.2d.2/.3 plans cite this milestone's deliverables as their adoption target.
- [ ] Zero new test failures in the existing 2263-test baseline. Net delta: ~+10 tests for the new components.

Out of scope (deferred to M.2d.2/.3/.4):

- Wiring any wizard to the new primitives.
- Section-collapse UX (open question Q3).
- ADR formalising the wizard contract — that lands in M.2d.4 per roadmap v2 §3.7.4.

---

## 7. Step-by-step implementation sequence

1. **Audit pass** — re-read the six wizards (`AddFocas2Source`, `AddBrotherHttpSource`, `AddModbusSource`, `AddMqttDestination`, `AddOpcUaServerDestination`, `AddRoute`) and confirm §4's pattern list is exhaustive. Update §4 if new patterns surface.
2. **Skeleton commit** — create `Components/Shared/Wizard{Shell,ValidationBanner,WatchSlot,Actions}.razor` and `Wizards/EditModeContext.cs` as empty skeletons with the parameter surfaces of §5. No logic yet. Build passes; no tests yet.
3. **`WizardShell`** — implement header band + load-state guard + ChildContent + Footer slot. Unit test all four load-state branches + each SaveState value.
4. **`WizardActions`** — implement Save / Cancel / optional Test Connection rendering. Unit test the disabled-state matrix.
5. **`WizardValidationBanner`** — implement the three-severity rendering + multi-message bullet list. Unit test severity routing and message-link parameter surface.
6. **`WizardWatchSlot`** — implement the `Available=false → renders nothing` short-circuit. Unit test the no-op contract. (The `Available=true` branch is a placeholder that calls into a `IRuntimeTap` interface from M.2c — for v1, the parameter is wired but the body is a `MudAlert.Info "Watch session not yet available (M.2c)"` placeholder that only renders when `Available=true && M.2c-not-actually-wired`. This is a debugging seam, not a user-facing feature; rip it out the moment M.2c lands.)
7. **`EditModeContext`** — implement the enum + Edit-mode loader. Unit test both modes against a sample config.
8. **Final consistency pass** — verify zero existing-wizard references to the new components (grep check + commit message + CI guard if cheap). Verify the test baseline holds.

---

## 8. Open questions for v2 ratification

### Q1 — `WizardWatchSlot` placeholder behavior contract

Roadmap v2 §3.7.1 says "renders nothing if M.2c not yet wired; ready for M.2d.2/.3 to populate." Two reasonable interpretations:

- **Strict no-op:** when `Available=false`, the component returns `RenderFragment.Empty`. Zero DOM. Operators see nothing where the watch panel will eventually be.
- **Debug-time placeholder:** when `Available=false`, render a small `MudAlert.Info "Live Tag Watch will appear here once M.2c is wired (canonical tag paths: {TagPaths})"`. Useful during M.2d.2/.3 development to confirm the slot is in the right place; ugly in production.

**Recommendation:** strict no-op for the production code path; the M.2d.2/.3 developer toggle is environment-variable-gated (`EDGECONNECT_WIZARD_WATCH_PLACEHOLDER=true`) so it's never on by default. v2 should ratify or reject.

### Q2 — `EditModeContext.cs` placement: `Wizards/` or `Components/Shared/`?

Roadmap v2 §3.7.1 places it under `Wizards/`. Counter-argument: the existing `Wizards/` files are all per-protocol *models* (`Focas2SourceWizardModel`, `ModbusSourceWizardModel`, etc.). `EditModeContext` is protocol-agnostic — it's a UI primitive that discriminates Add vs Edit across every wizard. A more natural sibling would be `Components/Shared/EditModeContext.cs` (next to `WizardShell` etc.).

**Recommendation:** keep it under `Wizards/` to honor roadmap v2 §3.7.1 as the locked placement. If v2 review surfaces a stronger argument for `Components/Shared/`, accept that.

### Q3 — Section-collapse + per-section error counts: M.2d.1 or M.2d.4?

Roadmap v2 §3.7.1 says `WizardShell` provides "header + numbered sections + footer + save state." It does NOT say section-collapse. The current six wizards do not collapse sections. M.2d.4's mandate is the "cross-wizard consistency sweep + UX polish" — section-collapse fits there more naturally.

**Recommendation:** **defer section-collapse to M.2d.4.** M.2d.1 ships `WizardShell` with section-collapse absent. M.2d.4 either adds it after measuring real wizard pain or punts. v2 should ratify.

### Q4 — Should `WizardShell.SaveState` drive a toast, a footer chip, or both?

The `SaveState` enum is part of `WizardShell`'s parameter surface (§5.1), but its consumers — and the UX effect of each state — are M.2d.2/.3's call. M.2d.1 needs only to expose the parameter; the visual effect can be a no-op in v1. v2 should confirm.

**Recommendation:** M.2d.1 exposes `SaveState` as a parameter with no UX side-effect (passthrough to children only). M.2d.2/.3 plans decide what to render.

### Q5 — Where do the WizardShell unit tests live?

The existing pattern is project-adjacent tests: `tests/ElpisEdgeConnect.Management.Tests/` for non-Razor logic. Razor component tests need bUnit (or similar). Does the repo already have a Razor component test project?

**Recommendation:** if bUnit is already present in `tests/ElpisEdgeConnect.Management.Tests/`, use it; else add the bUnit package + a minimal sample as part of M.2d.1's scope. Reality-check in v2.

---

## 9. Cross-references

- **Roadmap anchor:** [v2 §3.7.1](2026-05-21-phase2-wrapup-roadmap-v2.md). Sub-milestone within the M.2d split (v2 §3.7).
- **Implementation-discipline:** [v2.3 §1.1](2026-05-21-phase2-wrapup-roadmap-v2.3.md) (no-new-shared-abstractions rule — does NOT block this milestone per §3 above). [v2.3 §1.2](2026-05-21-phase2-wrapup-roadmap-v2.3.md) (canonical terminology: **Runtime Tap**, **Watch session** — used in `WizardWatchSlot`'s contract).
- **Downstream plans (parallel drafts):**
  - `2026-05-21-m2d2-source-wizards-plan.md` — consumes M.2d.1's components, adds Brother Test Connection (M.P2.4 Q12 backfill).
  - `2026-05-21-m2d3-sink-route-editors-plan.md` — consumes M.2d.1's components for MQTT + OPC UA + Route.
  - `2026-05-21-m2d4-cross-wizard-sweep-plan.md` — finalises validation patterns + ships the wizard-contract ADR.
- **Upstream plan (informs `WizardWatchSlot`):** `2026-05-21-m2c-live-tag-watch-plan.md` — locks the Runtime Tap surface that `WizardWatchSlot` eventually consumes. M.2d.1 ships the slot's no-op surface so it's ready when M.2c lands.
- **Structural reference:** `2026-05-20-mp24-brother-http-plan.md` (M.P2.4 plan trail — used as the prose-shape reference for this brief v1).

---

**End of v1. Open questions Q1–Q5 → ChatGPT review pass → v2.**
