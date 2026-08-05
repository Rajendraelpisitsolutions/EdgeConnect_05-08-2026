# M.2d.1 — Shared wizard primitives (v2 plan trail, LOCKED)

**Status:** v2 — LOCKED after two ChatGPT review passes. 11 items folded in: 9 from round-1 review (5 Q1-Q5 open-question verdicts + 4 architectural amendments) + 2 from round-2 (final layout/extensibility tightenings). 0 rejected. Per ChatGPT round-2: "proceed directly to v2 drafting."
**Date:** 2026-05-21
**Predecessor:** [v1 (open questions)](2026-05-21-m2d1-shared-primitives-plan.md)
**Roadmap anchor:** [v2 §3.7.1](2026-05-21-phase2-wrapup-roadmap-v2.md), refined by [v2.3 §1.1–§1.2](2026-05-21-phase2-wrapup-roadmap-v2.3.md).
**Sub-milestone:** first of four M.2d sub-milestones (M.2d.1 → .2 → .3 → .4).
**Estimated size:** ~3–4 days per roadmap §3.7.1. v2 amendments tighten architecture without growing implementation surface.

---

## 0. What v2 changed from v1

ChatGPT review across two rounds surfaced 11 items. **9 round-1 + 2 round-2 = 11 items folded; 0 rejected.** The headline correction is structural: **`WizardSection` added as the sixth primitive** so `WizardShell` does not own section structure.

### Round 1 (9 items)

| # | Item | Verdict | Where in v2 |
|---|---|---|---|
| 1 | `WizardShell` — narrow it | ✅ Accept — page frame ONLY | §5.1 (narrowed) |
| 2 | **`WizardSection` — add as sixth primitive** | ✅ **Strongly agree — biggest amendment** | §5.2 (NEW) |
| 3 | `WizardValidationBanner` — defer scroll/focus to M.2d.4 | ✅ Accept — keep `FieldAnchor` parameter; no-op click in v1 | §5.3 (narrowed) |
| 4 | `WizardWatchSlot` — strict no-op default | ✅ Accept (Q1) | §5.4 |
| 5 | `WizardActions` — accept | ✅ Accept | §5.5 |
| 6 | `EditModeContext` — keep under `Wizards/` | ✅ Accept (Q2) | §5.6 |
| 7 | Section collapse → M.2d.4 | ✅ Accept (Q3) | §6 out-of-scope |
| 8 | `SaveState` parameter-only | ✅ Accept (Q4) | §5.1 (passthrough only) |
| 9 | bUnit reality-check first | ✅ Accept (Q5) | §7 step 0 |

### Round 2 (2 items)

| # | Item | Verdict | Where in v2 |
|---|---|---|---|
| 10 | **`WizardSection` should NOT own `MudGrid`** — keep layout-agnostic | ✅ **Agree** — section owns chrome, NOT layout model | §5.2 (refined) |
| 11 | **`WizardActions` should accept `AdditionalActions` RenderFragment** — cheap future-proofing | ✅ **Agree** — RenderFragment slot costs nothing now | §5.5 (extended) |

### Retractions / supersessions

- v1 §5.1 said `WizardShell` "renders the header band, the load-state guard, and a slot for the section content + footer." v2 §5.1 narrows: header + load-state + `ChildContent` + `Footer` — no section ownership.
- v1 §5.2 had `WizardValidationBanner.LinkBehavior` performing the actual scroll/focus. v2 keeps the parameter surface but the click is a no-op (scroll/focus implementation lands in M.2d.4).
- v1 §5 had five deliverables; v2 has **six** (added `WizardSection.razor`).
- v1 §4.3 described the numbered section paper as a recurring pattern; v2 maps it to the new `WizardSection` primitive explicitly.

---

## 1. Goal

Unchanged from v1 §1. Extract the common UX vocabulary across the six existing wizards (3 source + 2 sink + 1 route) into a small set of shared Razor components and one C# context type. M.2d.1 touches no wizard. It only creates the components that M.2d.2/.3 will adopt and M.2d.4 will sweep into consistency.

**v2 framing addition:** the primitives are deliberately **composable**, not framework-shaped. A wizard authors instances of each primitive and arranges them; no primitive consumes or configures another. This is the architectural guard against "wizard framework" creep that ChatGPT round-1 flagged.

A typical wizard's composition becomes:

```razor
<WizardShell Title="..." Icon="..." BackHref="..." IsLoading="..." LoadError="...">
  <ChildContent>
    <WizardSection Index="1" Title="Identity">
      <MudGrid Spacing="2"> ... fields ... </MudGrid>
    </WizardSection>
    <WizardSection Index="2" Title="Connection">
      <MudGrid Spacing="2"> ... fields ... </MudGrid>
    </WizardSection>
    <WizardValidationBanner Severity="Warning" Messages="..." />
    <WizardSection Index="3" Title="Polling">
      <MudGrid Spacing="2"> ... fields ... </MudGrid>
    </WizardSection>
  </ChildContent>
  <Footer>
    <WizardActions OnSave="..." OnCancel="..." OnTestConnection="..." />
  </Footer>
</WizardShell>
```

Six primitives, each with one job. Wizards compose them rather than configuring one fat shell. **The `MudGrid` lives inside the wizard, not the section** (round-2 #10): some sections will eventually need tables, custom layouts, side-by-side panes, etc.; locking `MudGrid` into the section forecloses those.

---

## 2. Why this is first in the M.2d sub-sequence

Unchanged from v1 §2. M.2d.1 ships primitives; M.2d.2 adopts in source wizards; M.2d.3 in sink/route; M.2d.4 sweeps consistency + ships the ADR.

If M.2d.1 ships clean and M.2d.2 finds the primitives wrong, the cost is small (one PR's worth of M.2d.1 changes + the M.2d.2 work). If M.2d.1 were folded into M.2d.2, finding a primitive defect would mean reworking a wizard PR mid-flight.

---

## 3. Relationship to v2.3 §1.1 (no-new-shared-abstractions rule)

Unchanged from v1 §3. **M.2d.1 is explicitly NOT a violation** of the no-new-shared-abstractions rule. v2.3 §1.1 covered the Chip 4/5/offline-test implementation window; M.2d.1's mandate from roadmap v2 §3.7.1 is to extract shared wizard primitives. The mandate is the dedicated plan trail; the plan trail is the green light.

ChatGPT round-1 explicitly confirmed: "M.2d.1 is allowed to create shared primitives. So my concern is not 'don't create shared abstractions.' My concern is: create the smallest safe primitives, not a heavy wizard framework." v2 honours that by narrowing `WizardShell` + adding `WizardSection` + keeping each primitive single-responsibility.

---

## 4. Cross-wizard pattern audit (unchanged from v1 except §4.3 mapping)

The six wizards already exhibit a remarkably consistent layout. The patterns that recur and are candidates for extraction:

### 4.1 Header band → `WizardShell`

Every wizard opens with the same Row: back-arrow button → protocol icon → title + subtitle.

### 4.2 Load-state guard + load-error banner → `WizardShell`

Every wizard has the same three-state opening:

```
if (_loadError is not null)   → MudAlert.Error
else if (_currentConfig is null) → MudProgressLinear (loading)
else                           → render sections
```

### 4.3 Numbered section paper → `WizardSection` (NEW v2 mapping)

Every section is wrapped in:

```
<MudPaper Elevation="1" Class="pa-4 mb-4">
  <MudText Typo="subtitle1" Class="mb-3">N. <title></MudText>
  <MudGrid Spacing="2"> ... fields ... </MudGrid>   ← MudGrid is wizard's choice, NOT section's
</MudPaper>
```

**v2 amendment (round-1 #2):** this pattern gets its own primitive `WizardSection.razor`. v1 left this responsibility implicit in `WizardShell`'s `ChildContent`; v2 promotes it.

**v2 amendment (round-2 #10):** the `MudGrid` is NOT part of `WizardSection`. The section owns the chrome (`MudPaper` + numbered title + optional description); the layout inside the section is the wizard's choice. Most wizards will use `MudGrid`, but a future wizard with a table, chart, or custom pane is unblocked.

### 4.4 Per-section validation banner → `WizardValidationBanner`

Inconsistent today (Brother uses field-level errors, FOCAS2/Modbus drop a MudAlert below the MudGrid, MQTT uses Dense=true). M.2d.1 offers `WizardValidationBanner` as the canonical surface; **M.2d.4** sweeps existing wizards onto it. The `FieldAnchor` click-to-scroll behaviour exists as a parameter in v1 but as a **no-op in M.2d.1** (round-1 #3) — scroll/focus implementation lands in M.2d.4 alongside cross-wizard validation standardisation.

### 4.5 Save / Cancel button row → `WizardActions`

All six wizards end with the same Save/Cancel pattern. `WizardActions` extracts this. **v2 round-2 #11 amendment:** accepts an `AdditionalActions` RenderFragment slot for future buttons (Validate, Deploy, Preview, Reset, Runtime-Tap-launch, etc.) without needing to break the API later.

### 4.6 Test Connection panel (where present) → `WizardActions`

Shape is consistent where present. `WizardActions` exposes an optional `OnTestConnection` callback. Wizards without a probe leave it unset; wizards with one supply it.

### 4.7 Draft summary preview → NOT extracted

Three of six render a "Draft summary" `MudPaper` with bulleted "This draft will:" before the footer. The wrapping container is identical; the bullets are wizard-specific. **Not extracted in v2** — putting it through a primitive would require either templated render or each wizard implementing it inline (which is what they do today). The pattern's existence is noted; M.2d.4 can revisit if real friction emerges.

### 4.8 Section-collapse + per-section error counts → DEFER TO M.2d.4

Not present today. **Deferred to M.2d.4** per Q3 verdict (round-1 #7). Adding section-collapse to M.2d.1 would force `WizardSection` to grow state (expanded/collapsed) and `WizardShell` to coordinate error-counts across sections — exactly the "god component" failure mode round-1 warned against. M.2d.4 either adds it after measuring real wizard pain or punts.

---

## 5. Deliverables — six primitives

All paths relative to `src/ElpisEdgeConnect.Management/`.

### 5.1 `Components/Shared/WizardShell.razor` (NARROWED in v2)

The page frame. Knows about: header band, load-state guard, `ChildContent` (where sections + banners live), and an optional `Footer` slot. **Does NOT know about:** section structure, save-state visualization, validation anchoring, Runtime Tap behaviour, layout, or any other primitive's concern.

Parameters (final v2 set):

- `Icon` (`MudIcon`) — protocol-specific icon for the header band.
- `Title` (string) — header title text.
- `Subtitle` (string) — header subtitle text.
- `BackHref` (string) — parent route for the back-arrow button.
- `IsLoading` (bool) — drives the `MudProgressLinear` branch.
- `LoadError` (string?) — drives the `MudAlert.Error` branch.
- `ChildContent` (RenderFragment) — where the wizard arranges its `WizardSection` instances + `WizardValidationBanner` instances + anything else.
- `Footer` (RenderFragment?) — defaults to nothing if not supplied. Wizards typically put a `WizardActions` instance here.
- `SaveState` (enum: `Editing` | `Saving` | `Saved` | `Failed`) — **parameter-only in M.2d.1** (round-1 #8 / Q4 verdict). No visual side-effect. Passthrough so M.2d.2/.3 can decide to render a footer chip or toast or nothing.

DoD: renders correctly under unit test for each load-state branch (Loading / Error / Content). `SaveState` value passes through to consumers. **No section-rendering test exists here** — that's `WizardSection`'s job.

**Anti-coupling lock (NEW v2):** `WizardShell.razor`'s markup contains zero `MudPaper`-with-numbered-title constructs. A `grep` check in §6 DoD enforces this.

### 5.2 `Components/Shared/WizardSection.razor` (NEW v2 — round-1 #2)

One numbered card. The section primitive. Owns chrome only — layout is the wizard's choice (round-2 #10).

Parameters:

- `Index` (int) — section number (e.g., 1, 2, 3) for the title prefix.
- `Title` (string) — the section title (e.g., "Identity", "Connection", "Polling").
- `Description` (string?) — optional help text rendered as a `MudText Typo="caption"` directly under the title.
- `ChildContent` (RenderFragment) — the section body. Whatever layout the wizard chooses (typically `MudGrid Spacing="2"` for field grids, but tables / charts / panes are all unblocked).

Renders:

```
<MudPaper Elevation="1" Class="pa-4 mb-4">
  <MudText Typo="subtitle1" Class="mb-3">{Index}. {Title}</MudText>
  @if (!string.IsNullOrEmpty(Description))
  {
    <MudText Typo="caption" Class="mb-2">{Description}</MudText>
  }
  @ChildContent
</MudPaper>
```

**Critical: NO `MudGrid` inside the section** (round-2 #10). The section is a content container; the wizard decides whether its content is a grid, table, flex layout, custom pane, etc.

DoD: renders correctly for the canonical (Index + Title + ChildContent) case + the optional-Description case. The `ChildContent` is rendered verbatim with no enclosing layout container.

### 5.3 `Components/Shared/WizardValidationBanner.razor` (NARROWED in v2)

A severity-aware banner surfacing errors + warnings. Standardises the inconsistent banner styles in the current six wizards (Brother field-level, FOCAS2/Modbus inline MudAlert, MQTT Dense=true).

Parameters:

- `Severity` (`Error` | `Warning` | `Info`) — maps to MudBlazor's `MudAlert` severity classes.
- `Messages` (`IReadOnlyList<WizardValidationMessage>`) — each carrying `Code` (string), `Path` (string), `Message` (string), and optional `FieldAnchor` (string?).
- `OnMessageClick` (`EventCallback<WizardValidationMessage>`) — fired when a message with `FieldAnchor` is clicked. **In M.2d.1 this is a no-op surface** (round-1 #3); the parameter exists for API stability so M.2d.2/.3 don't need to change call sites later. **Scroll/focus implementation lands in M.2d.4.**

`WizardValidationMessage` record (lives next to the component):

```csharp
public sealed record WizardValidationMessage(
    string Code,
    string Path,
    string Message,
    string? FieldAnchor);
```

DoD: unit tests cover the three severities + multi-message bullet list + the `OnMessageClick` callback being invoked when a message with `FieldAnchor` is clicked (even though M.2d.1's wizards don't yet handle the callback).

### 5.4 `Components/Shared/WizardWatchSlot.razor` (Q1 verdict — strict no-op default)

A placeholder for the M.2c **Runtime Tap** Watch session embed. Default behaviour: renders nothing.

Parameters:

- `SourceInstanceId` (string).
- `TagPaths` (`IReadOnlyList<string>?`) — optional server-side filter.
- `Available` (bool) — set by the host via DI sniff or a feature-flag. **Defaults to `false`** until M.2c lands and flips it to `true`.

**Per Q1 verdict (round-1 #4):**

- **Production default — strict no-op.** When `Available=false`, the component returns an empty render fragment. Zero DOM impact.
- **Optional debug-time placeholder** — env-var-gated (`EDGECONNECT_WIZARD_WATCH_PLACEHOLDER=true`). When the env var is set AND `Available=false`, renders a small `MudAlert.Info "Live Tag Watch will appear here once M.2c is wired (source: {SourceInstanceId}, tags: {TagPaths})"`. Useful during M.2d.2/.3 development; never on by default in production.

DoD: when `Available=false` (and the debug env var is unset), the component renders zero DOM. Unit test pins this. The debug placeholder is unit-tested but not exercised in CI by default.

### 5.5 `Components/Shared/WizardActions.razor` (EXTENDED in v2 — round-2 #11)

The Save / Cancel / (optional) Test Connection / (optional) additional actions footer row.

Parameters:

- `OnSave` (EventCallback) — required.
- `OnCancel` (EventCallback) — required.
- `OnTestConnection` (EventCallback?) — optional; when null, the Test Connection button is not rendered.
- `SaveLabel` (string; defaults to `"Save as draft"`) — M.2d.2/.3 pass `"Save changes"` in Edit mode.
- `CancelLabel` (string; defaults to `"Cancel"`).
- `TestConnectionLabel` (string; defaults to `"Test Connection"`).
- `CanSave` (bool) — drives the disabled state of the Save button.
- `Busy` (bool) — drives the disabled state of all buttons + the loading indicator.
- **`AdditionalActions` (RenderFragment?) — NEW v2 round-2 #11.** Optional slot for arbitrary action buttons (Validate, Deploy, Preview, Reset, Runtime-Tap-launch, troubleshooting actions) that may emerge in future milestones. Rendered between Test Connection (if present) and the Save/Cancel pair. When null, occupies zero space.

DoD: unit tests cover the button disabled-state matrix + which buttons render based on which callbacks/slots are supplied. A test specifically verifies `AdditionalActions` content renders in the correct position.

### 5.6 `Wizards/EditModeContext.cs` (Q2 verdict — kept under `Wizards/`)

A C# type discriminating Add vs Edit and supplying the existing config to a wizard model when Edit mode is active. **Stays under `Wizards/`** per Q2 verdict (round-1 #6) and roadmap v2 §3.7.1.

Shape (v1, illustrative — not code):

- `WizardMode` enum (`Add` | `Edit`).
- `ExistingInstanceId` (string?) — present only when `Mode == Edit`.
- A small loader method that resolves the existing source/sink/route from the loaded `GatewayConfiguration`.

DoD: unit tests for both modes; Edit-mode loader correctly resolves a known sample config into the right model.

---

## 6. Definition of Done

- [ ] All **six** deliverables exist under the paths in §5.
- [ ] Each component has at least one unit test pinning its DoD-stated behaviour (≥ ~12 tests total; v1 said ~10 — v2 adds 2 for `WizardSection`).
- [ ] **No wizard depends on the new components yet.** Verified by `grep` over the existing six wizards — zero references to `WizardShell` / `WizardSection` / `WizardValidationBanner` / `WizardWatchSlot` / `WizardActions` / `EditModeContext`.
- [ ] **Anti-coupling check:** `grep` over `WizardShell.razor` markup confirms zero `MudPaper`-with-numbered-title constructs (the section structure lives in `WizardSection`, not `WizardShell`).
- [ ] **Anti-layout-coupling check:** `grep` over `WizardSection.razor` markup confirms zero `MudGrid` constructs (the layout lives in the consuming wizard, not the section).
- [ ] M.2d.2/.3 plans cite this milestone's deliverables as their adoption target.
- [ ] Zero new test failures in the existing test baseline.

**Out of scope (deferred to M.2d.2/.3/.4):**

- Wiring any wizard to the new primitives — M.2d.2/.3.
- Section-collapse UX (Q3 / round-1 #7) — M.2d.4 if at all.
- ADR formalising the wizard contract — M.2d.4.
- `WizardValidationBanner`'s scroll/focus click-handling (round-1 #3) — M.2d.4.
- `WizardShell.SaveState`'s visual rendering (Q4 / round-1 #8) — M.2d.2/.3 decide per-wizard.
- Adding actual `AdditionalActions` content to any wizard — M.2d.2/.3/.4 as future buttons emerge.

---

## 7. Step-by-step implementation sequence (9 steps; was 8 in v1)

1. **bUnit reality-check (Q5 / round-1 #9).** Inspect `tests/ElpisEdgeConnect.Management.Tests/` for existing bUnit usage. If present, reuse. If absent, add `bunit` PackageReference to the test project as part of step 2's commit. **First operational task — confirms tooling before any new code is authored.**
2. **Audit pass.** Re-read the six wizards (`AddFocas2Source`, `AddBrotherHttpSource`, `AddModbusSource`, `AddMqttDestination`, `AddOpcUaServerDestination`, `AddRoute`) and confirm §4's pattern list is exhaustive. Update §4 if new patterns surface.
3. **Skeleton commit.** Create the six files as empty skeletons with the parameter surfaces of §5:
   - `Components/Shared/WizardShell.razor`
   - **`Components/Shared/WizardSection.razor`** (NEW v2 sixth file)
   - `Components/Shared/WizardValidationBanner.razor`
   - `Components/Shared/WizardWatchSlot.razor`
   - `Components/Shared/WizardActions.razor`
   - `Wizards/EditModeContext.cs`
   No logic yet. Build passes; no tests yet.
4. **`WizardShell` implementation.** Header band + load-state guard + `ChildContent` + `Footer` slot. **No section-rendering** (the anti-coupling lock is enforced by lack of `MudPaper`-with-numbered-title in the markup). Unit test all three load-state branches + the `SaveState` passthrough.
5. **`WizardSection` implementation (NEW step).** `MudPaper` chrome + numbered title + optional description + `ChildContent`. **No `MudGrid`** (round-2 #10). Unit test the canonical case (Index + Title + ChildContent), the optional-Description case, and the no-layout-container guarantee (a `ChildContent` of `<table>...</table>` renders correctly).
6. **`WizardActions` implementation.** Save / Cancel / optional Test Connection + the `AdditionalActions` RenderFragment slot (round-2 #11). Unit test the disabled-state matrix + which buttons render based on supplied callbacks + `AdditionalActions` positioning.
7. **`WizardValidationBanner` implementation.** Three-severity rendering + multi-message bullet list + `OnMessageClick` callback parameter surface (no-op behaviour in v1 per round-1 #3). Unit tests cover severity routing + message-list rendering + the callback being invoked when a message with `FieldAnchor` is clicked (even though M.2d.1 doesn't consume it).
8. **`WizardWatchSlot` implementation.** `Available=false` → empty render. `EDGECONNECT_WIZARD_WATCH_PLACEHOLDER=true && Available=false` → debug placeholder. Unit test both paths.
9. **`EditModeContext` implementation + final consistency pass.** Implement the enum + Edit-mode loader. Unit test both modes against a sample config. Verify zero existing-wizard references to the new components (grep check + commit message + the two anti-coupling grep checks from §6 DoD).

---

## 8. Open questions — all RESOLVED in v2

v1's Q1-Q5 are resolved per the verdict table in §0:

| Q | v1 status | v2 resolution |
|---|---|---|
| Q1 — `WizardWatchSlot` placeholder behaviour | Open | **Resolved: strict no-op default; env-var-gated debug placeholder.** §5.4. |
| Q2 — `EditModeContext.cs` placement | Open | **Resolved: under `Wizards/` per roadmap v2 §3.7.1.** §5.6. |
| Q3 — Section-collapse: M.2d.1 or M.2d.4? | Open | **Resolved: defer to M.2d.4.** §6 out-of-scope. |
| Q4 — `SaveState` UX side-effect (toast / chip / both)? | Open | **Resolved: parameter-only in M.2d.1; M.2d.2/.3 decide per-wizard rendering.** §5.1. |
| Q5 — bUnit availability | Open | **Resolved: reality-check at implementation step 1. If absent, add as part of M.2d.1 scope.** §7. |

**No new v1-or-v2 open questions remain.** All architectural decisions are locked. v3 reality-check (during implementation session) confirms the bUnit availability (step 1) and the audit-pass pattern list (step 2) — these are reality-check items, not architectural questions.

---

## 9. Cross-references

- **Roadmap anchor:** [v2 §3.7.1](2026-05-21-phase2-wrapup-roadmap-v2.md). Sub-milestone within the M.2d split (v2 §3.7).
- **Implementation discipline:** [v2.3 §1.1](2026-05-21-phase2-wrapup-roadmap-v2.3.md) (no-new-shared-abstractions — does NOT block this milestone per §3 above). [v2.3 §1.2](2026-05-21-phase2-wrapup-roadmap-v2.3.md) (canonical terminology: **Runtime Tap**, **Watch session** — used in `WizardWatchSlot`'s purpose).
- **Predecessor (this plan trail's v1):** [M.2d.1 v1](2026-05-21-m2d1-shared-primitives-plan.md).
- **Downstream plans (parallel drafts, adopt M.2d.1's primitives):**
  - [`m2d2-source-wizards-plan.md`](2026-05-21-m2d2-source-wizards-plan.md) — consumes the six primitives in source wizards + adds Brother Test Connection (M.P2.4 Q12 backfill).
  - [`m2d3-sink-route-editors-plan.md`](2026-05-21-m2d3-sink-route-editors-plan.md) — consumes the six primitives in sink + route wizards.
  - [`m2d4-cross-wizard-sweep-plan.md`](2026-05-21-m2d4-cross-wizard-sweep-plan.md) — finalises validation patterns (incl. `WizardValidationBanner`'s scroll/focus implementation) + ships the wizard-contract ADR.
- **Upstream (informs `WizardWatchSlot`):** [M.2c v2.1](2026-05-21-m2c-live-tag-watch-plan-v2.1.md) — Runtime Tap surface that `WizardWatchSlot` consumes once M.2c lands.
- **Structural style reference:** [M.P2.4 v1](2026-05-20-mp24-brother-http-plan.md) — prose-shape reference for plan trails.

---

**End of v2. LOCKED — ready for v3 reality-check pass during implementation session.**

Per ChatGPT round-2 verdict: "proceed directly to v2 drafting." No further ChatGPT review iteration needed before v3. v3 reality-check resolves the bUnit availability (step 1) and audit-pass confirmation (step 2) from inside the codebase during the implementation session.
