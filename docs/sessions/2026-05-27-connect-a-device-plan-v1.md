# Connect-a-device — guided onboarding flow (v1 plan, BRIEF)

**Status:** v1 — DRAFT, pending user ratification on Q1–Q5 and then v2 lock.
**Date:** 2026-05-27
**Form:** Brief v1 per project planning cadence (`v1 → review → v2 → reality-check → v3`).
**Trigger:** Real-world UX friction surfaced during M.2d.4 smoke-testing. Current setup requires 3 separate wizards × 3 Apply ceremonies × 4 page-jumps to land one source-to-sink pipeline. Operators doing the common "connect a new device" task spend more time on ceremony than configuration.
**Hard precondition:** M.2d.4 merged to master. The flow embeds existing wizards as steps; embedding a moving target on a feature branch creates coupling.
**Estimated size:** 3–5 days (re-use-heavy; biggest unknowns are Q1 embedding mechanic and Q4 auto-probe blocking semantics).

---

## 1. Goal

A new operator opens Studio, hasn't configured anything yet, and wants to connect a CNC / PLC / device to a sink. Today they do three wizards, three Applies, jump four pages. After this milestone they go through **one guided flow** at `/onboard` that picks a source protocol → configures it → picks a destination protocol → configures it → reviews → one Apply. Data flows.

Not in scope:
- Multi-sink fan-out from the guided flow (v1 = 1 source : 1 sink).
- Replacing existing single-entity wizards. Operators editing one thing later use `/sources/{id}/edit` etc. — those stay.
- A "connect another device" parallel-add ceremony.
- Importing config from a file (separate flow).

---

## 2. Q1–Q5 — locked decisions (pending user ratification)

| # | Question | Recommendation | Reasoning |
|---|---|---|---|
| Q1 | Re-use existing wizards as embedded steps, or duplicate-and-tailor? | **Re-use, with `[Parameter] bool EmbeddedMode`** | Each protocol wizard is already organised around `WizardSection` blocks (ADR-0015 Rule 1). Adding an `EmbeddedMode` flag that suppresses the wizard's own footer + `Save as draft` button is ~half a day per wizard. Full duplication = 5× more files to maintain, breaks ADR-0015's "the wizard is the single surface for protocol-instance authoring." |
| Q2 | URL? | **`/onboard`** | `/connect` collides with "connect to broker" (the operator's natural reading). `/devices/new` conflates "device" with "pipeline" — devices are sources only. `/quick-start` is long. `/onboard` is short, verb-clear, and matches the empty-state mental model ("I'm onboarding my first device"). |
| Q3 | Empty-state CTA on Sources / Destinations / Routes tabs? | **Yes — primary CTA when all three tabs are empty; secondary when not.** | When Sources + Sinks + Routes lists are all empty, the operator is in the bootstrap state. Top of Overview shows `[Connect a device →]` as the primary action. Existing `[Add source] [Add destination] [Add route]` buttons stay (for the targeted-edit case). |
| Q4 | Auto-run Test Connection in the guided flow? | **Yes — auto-run where supported, surface as Warning (not blocking).** | The probe is idempotent (ADR-0015 Rule 6). Running it automatically saves a click. A failure shouldn't block the operator's flow — they may know it'll fail right now and plan to fix it post-save. Surface failure as a non-blocking Warning banner. OpcUa carve-out per Rule 6: no probe, skip the auto-run for it. |
| Q5 | Multiple sinks in v1? | **No — 1:1 only. Add "+ another destination" inline in v2.** | The route wizard already supports multi-sink (multi-select). Adding it as an inline "+" in the guided flow is a small addition once the 1:1 flow is solid. Scope discipline: v1 = ship the 80% case. |

---

## 3. Architecture sketch

### 3.1 The route — `/onboard`

A 6-step linear wizard, rendered by a new `OnboardingFlow.razor` component that hosts existing wizards as embedded steps:

| Step | Component | What the operator does |
|---|---|---|
| 1 | `ChooseSourceProtocol.razor` (existing — re-use protocol picker cards) | Picks one of: FOCAS2, Brother HTTP, Modbus TCP, MTConnect, S7 |
| 2 | `ChooseDestinationProtocol.razor` (existing) | Picks one of: MQTT, OPC UA Server |
| 3 | `Add{Protocol}Source.razor` (existing, **`EmbeddedMode=true`**) | Configures the source — same fields, same validation, same Test Connection probe. No footer (the parent owns Next/Back). |
| 4 | `Add{Protocol}Destination.razor` (existing, `EmbeddedMode=true`) | Same — destination configuration. |
| 5 | `AddRoute.razor` (existing, `EmbeddedMode=true`, **pre-populated**) | Source pre-selected (the step-3 instance), sink pre-selected (the step-4 instance), filter = `*` (all tags), buffer + delivery = defaults. Operator can override if they want. |
| 6 | New `ReviewAndConnect.razor` | Summary card showing the 3 entities about to be created. One big **`[Connect]`** button → POST a single draft + immediate Apply. Success → `/onboard/done` with a live tag counter. |

### 3.2 The single Apply

Today, each wizard does its own `POST /api/v1/config/drafts`, then the operator navigates to `/config` and clicks Apply. The guided flow needs **atomic bundled apply**:

1. New `WizardConfigMerger.BuildBundledOnboardingDraft(source, sink, route)` — composes the three entities into one `GatewayConfiguration` draft. Validates inter-record references (route's `SourceInstanceId` resolves to `source`; route's `SinkInstanceIds[0]` resolves to `sink`).
2. New endpoint `POST /api/v1/onboarding/apply` that:
   - Validates the bundled draft (same Schema → Typed → Cross-record pipeline as the existing Apply).
   - Creates the draft → applies it in one transaction.
   - Returns 200 + apply result on success, 400 + validation errors on failure.
3. The Review step renders validation errors inline if Apply fails (same `WizardValidationBanner` surface).

### 3.3 The `EmbeddedMode` seam

Each existing protocol wizard gains:

```csharp
/// <summary>
/// When true, the wizard is being hosted inside the Onboarding flow. The
/// wizard renders its sections normally but suppresses its own footer
/// (WizardActions) and its own save handlers. Submission is owned by the
/// parent flow, which collects the wizard's `BuildSourceInstance()` /
/// `BuildSinkInstance()` output via an EventCallback.
/// </summary>
[Parameter] public bool EmbeddedMode { get; set; }

/// <summary>
/// Fired by the wizard whenever its model becomes valid (CanSave returns true).
/// Onboarding flow uses this to enable the parent's [Next] button. Ignored
/// in standalone mode.
/// </summary>
[Parameter] public EventCallback<{Protocol}WizardModel> OnModelChanged { get; set; }
```

In the razor template:

```razor
@if (!EmbeddedMode)
{
    <WizardActions OnSave="..." OnCancel="..." ... />
}
```

Effort: ~half a day per wizard × 5 wizards = ~2.5 days. The bulk of the milestone.

### 3.4 What stays untouched

- All five protocol wizards' field layout, validation, hydration, edit mode.
- ADR-0015's wizard contract — Connect-a-device is a **new "wizard kind"** (a *meta-wizard* that hosts others). ADR-0016 (new) or ADR-0015 amendment documents the meta-wizard pattern.
- `/sources/new/{protocol}`, `/destinations/new/{protocol}`, `/routes/new` — the existing standalone routes stay for the edit-one-thing path.
- The Config page + Apply ceremony — operators using the standalone wizards still go through it.

---

## 4. Deliverables

| Deliverable | Type | Notes |
|---|---|---|
| `OnboardingFlow.razor` | New | The parent meta-wizard. Owns Next/Back navigation, summary, single Apply call. |
| `ReviewAndConnect.razor` | New | Step 6 summary + Connect button + result surface. |
| `EmbeddedMode` parameter on 5 existing wizards | Edit (small per wizard) | Plus `OnModelChanged` callback. |
| `WizardConfigMerger.BuildBundledOnboardingDraft()` | New | Pure function — composes Source + Sink + Route into one `GatewayConfiguration`. Validates inter-record references. |
| `OnboardingApi.cs` (new) — `POST /api/v1/onboarding/apply` | New | Atomic bundled draft + apply. |
| Empty-state CTA on Sources / Destinations / Routes pages | Edit (small) | Renders `[Connect a device →]` as primary action when list is empty. |
| `docs/decisions/0016-onboarding-meta-wizard.md` (or 0015 amendment) | New ADR / amendment | Documents the meta-wizard pattern as an extension of ADR-0015. |
| Tests | New tests | `OnboardingFlowTests` (bUnit, validates the step transitions + EmbeddedMode rendering); `BuildBundledOnboardingDraftTests` (xUnit, validates inter-record reference resolution); `OnboardingApiTests` (validates the atomic apply flow). |
| `docs/sessions/2026-05-XX-connect-a-device-handoff.md` | Handoff | End-of-session. |

---

## 5. Implementation steps (8 steps, with rough sizing)

| # | Step | Effort |
|---|---|---|
| 1 | Branch `claude/connect-a-device-impl` from master (post M.2d.4 merge). Write v2 plan with locked Q1–Q5. | 0.5 day |
| 2 | Draft ADR-0016 (or amend 0015) — meta-wizard pattern. User review before code. | 0.5 day |
| 3 | Add `EmbeddedMode` + `OnModelChanged` to all 5 protocol wizards. One PR section, mechanical. Tests verify standalone mode unchanged. | 1 day |
| 4 | Build `OnboardingFlow.razor` skeleton — Step 1 + Step 2 (protocol pickers) with Next/Back wiring. | 0.5 day |
| 5 | Wire Steps 3, 4, 5 (embedded source / sink / route wizards) with pre-population logic for Step 5. | 1 day |
| 6 | `WizardConfigMerger.BuildBundledOnboardingDraft` + `OnboardingApi` + Step 6 review screen. | 1 day |
| 7 | Empty-state CTAs on Sources / Destinations / Routes pages. | 0.5 day |
| 8 | Cross-flow smoke testing, handoff doc, PR. | 0.5 day |

**Total: 4.5 days.** Realistic range 3.5–6 days depending on Q1 embedding-mechanic surprises.

---

## 6. Definition of done

- [ ] `/onboard` flow completes end-to-end from empty config → running pipeline → live tag counter, with all 5 source protocols × 2 sink protocols (10 combinations smoke-tested).
- [ ] Existing single-entity wizards behaviour unchanged when invoked at their original URLs (`EmbeddedMode=false` is the default — should be invisible to existing callers).
- [ ] Empty-state CTA visible + functional on Sources / Destinations / Routes tabs.
- [ ] Atomic apply: a failure at validate-time leaves config untouched (the operator returns to the review screen to fix).
- [ ] Test Connection auto-runs in steps 3 and 4 (where supported); failures show Warning banner but don't block Next.
- [ ] ADR-0016 (or 0015 amendment) landed and user-approved.
- [ ] Solution-wide test sweep clean, zero warnings.
- [ ] Handoff doc cross-references this plan + ADR-0016 + any chips spawned during implementation.

---

## 7. Open questions for v2 ratification

Most of Q1–Q5 are locked above with recommendations. **The user needs to ratify before v2 closes.** Two new questions surface from the architecture:

### Q6 — Where does `/onboard` live in nav?

Options:
- **(a)** Top-level nav item between Overview and Sources (visible always).
- **(b)** Only surfaced from empty states (no nav item; appears as CTA on empty Sources / Destinations / Routes).
- **(c)** Both.

Recommendation: **(c)** — top-level nav item AND empty-state CTAs. The nav item makes it discoverable for repeat use ("connect another device"); the empty-state CTAs make it the first-run primary action.

### Q7 — What happens to the embedded wizard's "Test Connection" result if the operator clicks Back and then Forward?

Options:
- **(a)** Re-run automatically (wasteful but always-fresh).
- **(b)** Cache the last result (cheap, but stale if field changed).
- **(c)** Discard the result; operator clicks Test Connection again if they want.

Recommendation: **(b)** — cache the last probe result keyed by the field values that produced it. If the operator changes a field, the cached result is invalidated. Same memoisation pattern as the existing single-entity wizards' probe state.

---

## 8. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `EmbeddedMode` surfaces tangled state in existing wizards (e.g. wizard's own `OnInitializedAsync` does work that conflicts with parent ownership) | Medium | Medium | Step 3 lands the mechanical addition + tests first. Step 4 is the integration test. Any wizard that resists embedding cleanly gets a chip to refactor before merging. |
| Atomic apply fails mid-write (DB error, schema regression) and partially applies | Low | High | The existing `IConfigurationManager.ApplyAsync` is already atomic at the file-system level (write to `.tmp`, fsync, rename). Bundled draft is one draft — same atomicity guarantees apply. |
| Operator gets confused if Step 5 (Route) shows defaults they didn't ask for (e.g. `Filter = *`) | Medium | Low | Step 5's Route wizard has a "Review wiring" header that calls out: "We've prefilled this route from your earlier steps. Adjust below if needed." Inline help text. |
| Adding `[Parameter] EmbeddedMode` causes the existing wizard PRs (M.2d.4) to merge-conflict | Low | Low | M.2d.4 merges first (hard precondition). Connect-a-device branches from the post-merge tip. |
| Test Connection auto-running in Step 3 makes the flow feel slow if the probe times out (default 5–10s) | Medium | Low | Use a shorter timeout for the auto-probe (3s) + spinner with "Verifying connection…" copy. Failure surfaces as Warning, not Error. Operator can continue. |

---

## 9. Cross-references

- ADR-0014 (config-state vs runtime-state are distinct surfaces): `docs/decisions/0014-config-state-vs-runtime-state.md`
- ADR-0015 (wizard contract — the spec this milestone honours): `docs/decisions/0015-wizard-contract.md`
- M.2d.4 v2.1 plan (the milestone that locks the wizard primitives this flow embeds): `docs/sessions/2026-05-27-m2d4-cross-wizard-sweep-plan-v2.1.md`
- Follow-up chips doc (origin of this milestone): `docs/sessions/2026-05-27-followup-chips.md`
- Existing wizards being embedded: `src/ElpisEdgeConnect.Management/Components/Pages/{SourceWizards,SinkWizards,RouteWizards}/`
- Picker components being re-used: `src/ElpisEdgeConnect.Management/Components/Pages/{ChooseSourceProtocol,ChooseDestinationProtocol}.razor`

---

**End of v1 brief. Awaiting user ratification of Q1–Q7 verdicts. v2 will lock decisions and refine the implementation sequence based on user input.**
