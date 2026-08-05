# Connect-a-device — guided onboarding flow (v2.1 plan, LOCKED)

**Status:** v2.1 — Step 0 reality-check complete (no blockers). N1–N3 constraints folded in. Ready for implementation.
**Date:** 2026-05-27
**Supersedes:** [`v2 plan`](./2026-05-27-connect-a-device-plan-v2.md) (only delta is §3.4–§3.6 below; balance of v2 still applies)
**Step 0 output:** [`step0-reality-check.md`](./2026-05-27-connect-a-device-step0-reality-check.md)
**Hard precondition satisfied:** M.2d.4 merged at commit `b813410`.
**Estimated size:** **5–6 days realistic** (Step 0 came in under budget at <0.5 day; v2 estimate stands).

---

## 1. What changed v2 → v2.1

Step 0 confirmed all 5 wizards pass the EmbeddedMode mechanic with zero structural conflicts. Three new constraints (N1–N3) surfaced that the implementation needs to honour. **Q1–Q7 from v1, plus v2's Step 0 + first-run additions, all still apply.** This revision only adds the three constraints.

| # | Constraint | Source |
|---|---|---|
| N1 | OnboardingFlow.razor uses CSS visibility toggle (`d-none`), NOT `@switch`, to switch steps | Q7 (memoisation) requires Blazor to keep wizard component instances alive across step transitions |
| N2 | Embedded-mode callback signature is `OnInstanceBuilt: EventCallback<SourceInstanceConfig\|SinkInstanceConfig\|RouteConfig>`, NOT typed-per-wizard | Decouples parent flow from per-wizard model classes |
| N3 | `AddRoute.razor` gains `[Parameter] PrePopulatedSource` + `PrePopulatedSink` in EmbeddedMode | The Route step needs to render with phantom entities from Steps 3+4 that don't yet exist in `_currentConfig` |

---

## 2. Q1–Q7 + Step 0 verdicts (carried forward from v2)

| # | Question | Locked |
|---|---|---|
| Q1 | Embed existing wizards vs duplicate? | **Embed via `[Parameter] bool EmbeddedMode`** — Step 0 confirms feasibility |
| Q2 | URL? | `/onboard` |
| Q3 | Empty-state CTA? | Yes — primary CTA when empty; secondary when populated |
| Q4 | Auto-run Test Connection? | Yes; non-blocking Warning on failure |
| Q5 | Multiple sinks in v1? | No — 1:1; "+ destination" deferred |
| Q6 | Nav placement? | Both nav item AND empty-state CTAs |
| Q7 | Cache probe across Back/Next? | Memoise per field-value set; **N1 enforces the persistence pattern that makes this work** |

---

## 3. Locked architectural details

### 3.1 EmbeddedMode contract (final)

Every protocol wizard gains exactly two parameters:

```csharp
/// <summary>
/// When true, the wizard is being hosted inside the Onboarding flow.
/// Behaviour changes when EmbeddedMode is true:
///  * WizardActions footer is not rendered (parent owns Next/Back/Save).
///  * Nav.NavigateTo calls in Cancel/Save success paths are suppressed.
///  * Snackbar.Add emissions are suppressed (parent owns user messaging).
///  * SaveAsDraftAsync path is not reachable (Save button hidden).
///  * Errors surface via the wizard's WizardValidationBanner only.
///  * OnInitializedAsync still loads _currentConfig for dup-id checks
///    (cheap; the routing section is hidden anyway).
/// </summary>
[Parameter] public bool EmbeddedMode { get; set; }

/// <summary>
/// Fired by the wizard whenever its model becomes valid (CanSave() == true)
/// or transitions from valid → invalid. Receives the buildable instance
/// or null if the model is currently invalid.
///
/// The parent uses this signal to enable/disable its own Next button.
/// Fired on every field change (debounce is the parent's concern).
/// </summary>
[Parameter] public EventCallback<SourceInstanceConfig?> OnInstanceBuilt { get; set; }
// or EventCallback<SinkInstanceConfig?> for sinks
// or EventCallback<RouteConfig?> for AddRoute
```

`AddRoute.razor` additionally accepts:

```csharp
/// <summary>EmbeddedMode pre-population: the source from Step 3 of the
/// Onboarding flow. Renders in the source picker even though it isn't
/// yet in _currentConfig.Sources (bundled apply happens at the end).</summary>
[Parameter] public SourceInstanceConfig? PrePopulatedSource { get; set; }

/// <summary>EmbeddedMode pre-population: the sink from Step 4. Same rationale.</summary>
[Parameter] public SinkInstanceConfig? PrePopulatedSink { get; set; }
```

### 3.2 OnboardingFlow.razor — persistence pattern (N1)

The seven steps are all mounted simultaneously; CSS toggles visibility:

```razor
@page "/onboard"
@inject HttpClient HttpClient
@inject NavigationManager Nav

<MudContainer MaxWidth="MaxWidth.Large" Class="my-4">
    <OnboardingProgress CurrentStep="@_currentStep" TotalSteps="7" />

    @* Step 0 — Welcome (conditional: only when GatewayId is auto-provisioned) *@
    <div class="@StepVisibility(0)">
        <WelcomeStep OnNext="@(() => GoToStep(1))" OnSkip="@(() => GoToStep(1))" />
    </div>

    @* Step 1 — Source protocol picker *@
    <div class="@StepVisibility(1)">
        <ChooseSourceProtocol EmbeddedMode="true" OnProtocolChosen="@OnSourceProtocolChosen" />
    </div>

    @* Step 2 — Destination protocol picker *@
    <div class="@StepVisibility(2)">
        <ChooseDestinationProtocol EmbeddedMode="true" OnProtocolChosen="@OnSinkProtocolChosen" />
    </div>

    @* Step 3 — Source configuration (mounted lazily after Step 1 picks) *@
    @if (_sourceProtocol is not null)
    {
        <div class="@StepVisibility(3)">
            @* dynamic rendering per protocol *@
            @switch (_sourceProtocol)
            {
                case "focas2":
                    <AddFocas2Source EmbeddedMode="true" OnInstanceBuilt="@OnSourceInstanceBuilt" />
                    break;
                case "modbustcp":
                    <AddModbusSource EmbeddedMode="true" OnInstanceBuilt="@OnSourceInstanceBuilt" />
                    break;
                // ... brother-http, mtconnect, s7
            }
        </div>
    }

    @* Step 4 — Destination configuration *@
    @if (_sinkProtocol is not null)
    {
        <div class="@StepVisibility(4)">
            @switch (_sinkProtocol)
            {
                case "mqtt":
                    <AddMqttDestination EmbeddedMode="true" OnInstanceBuilt="@OnSinkInstanceBuilt" />
                    break;
                case "opcua-server":
                    <AddOpcUaServerDestination EmbeddedMode="true" OnInstanceBuilt="@OnSinkInstanceBuilt" />
                    break;
            }
        </div>
    }

    @* Step 5 — Route (pre-populated from Steps 3+4) *@
    @if (_sourceInstance is not null && _sinkInstance is not null)
    {
        <div class="@StepVisibility(5)">
            <AddRoute EmbeddedMode="true"
                      PrePopulatedSource="@_sourceInstance"
                      PrePopulatedSink="@_sinkInstance"
                      OnInstanceBuilt="@OnRouteInstanceBuilt" />
        </div>
    }

    @* Step 6 — Review + bundled apply *@
    <div class="@StepVisibility(6)">
        <ReviewAndConnect Source="@_sourceInstance"
                          Sink="@_sinkInstance"
                          Route="@_routeInstance"
                          OnApplied="@OnConnected" />
    </div>

    <OnboardingNavigation CurrentStep="@_currentStep"
                          CanGoNext="@CanGoNext()"
                          OnBack="@GoBack"
                          OnNext="@GoNext" />
</MudContainer>

@code {
    private int _currentStep = 0;
    private string? _sourceProtocol;
    private string? _sinkProtocol;
    private SourceInstanceConfig? _sourceInstance;
    private SinkInstanceConfig? _sinkInstance;
    private RouteConfig? _routeInstance;

    private string StepVisibility(int step) => _currentStep == step ? "" : "d-none";

    // ... step-transition + callback handlers ...
}
```

**Key Blazor mechanics enforced by this pattern:**
1. Steps 3+ only mount AFTER protocol pickers (Steps 1+2) — pre-pick mounting would render protocol-specific wizards with null protocols, which they can't handle.
2. Once mounted, the wizard's `@code` block (including `_testResult`, `_probeResult`, `_currentConfig`, etc.) survives every Back/Next as long as the operator doesn't change the protocol picker — Blazor sees the same component instance across re-renders.
3. Changing the source protocol in Step 1 after configuring Step 3 forces Step 3 to recreate (different component) — operator loses their step-3 state. Surface a "Going back to Step 1 will discard your source configuration. Continue?" confirm dialog before allowing the picker change.

### 3.3 The "Going back loses state" UX rule

Cancel + Back behaviours, locked:

| Action | Effect |
|--------|--------|
| **Back** from Step N → N-1 | Preserves all wizard state. Just changes visibility. |
| **Back** from Step 3 → Step 1 + change source protocol | Confirm dialog: "Going back to Step 1 with a different protocol will discard your source configuration. Continue?" |
| **Cancel** (top-right X) from any step | Confirm dialog: "Exit setup? All progress will be lost." Returns to Overview / Sources / wherever they came from. |
| **Browser refresh** / tab close | Discard everything. No autosave. (Honours ADR-0015 Rule 8.) |

### 3.4 OnInstanceBuilt firing semantics (N2)

The callback fires when:
1. The wizard initially renders with `CanSave() == true` (e.g. when loaded with prefilled values in some future flow — irrelevant in Add-mode but harmless).
2. Any field changes and the new combined state is valid.
3. Any field changes and the new combined state is invalid (fires with `null`).

The parent's `OnSourceInstanceBuilt(SourceInstanceConfig? instance)` handler:
- Stores the instance (or null) on the parent state.
- Updates `CanGoNext()` so the Next button enables / disables as the validity changes.

No debounce in the wizard — the parent can debounce if perf becomes a concern (it won't for typical typing rates).

### 3.5 ChooseSourceProtocol / ChooseDestinationProtocol — EmbeddedMode

These two picker components today navigate to `/sources/new/{protocol}` on selection. In EmbeddedMode, they fire `OnProtocolChosen(string protocolName)` and DON'T navigate. The parent flow advances the step.

```csharp
[Parameter] public bool EmbeddedMode { get; set; }
[Parameter] public EventCallback<string> OnProtocolChosen { get; set; }

private void OnCardClicked(string protocolName)
{
    if (EmbeddedMode)
    {
        await OnProtocolChosen.InvokeAsync(protocolName);
    }
    else
    {
        Nav.NavigateTo($"/sources/new/{protocolName}");
    }
}
```

### 3.6 ReviewAndConnect — bundled apply

Step 6 component renders:
- Summary card: "We're about to create: source `X` (protocol Y) → destination `Z` (protocol W) → route `R`."
- Validation preview (re-runs validation client-side as a sanity check).
- Big `[Connect]` button.

On click:
1. POST `/api/v1/onboarding/apply` with `{ source, sink, route, actor }` body.
2. Server runs `WizardConfigMerger.BuildBundledOnboardingDraft` → schema validate → typed validate → atomic apply.
3. 200 OK + `ApplyResultDto` → success screen with live tag counter (5-second poll of `/api/v1/sources/{id}` for runtime state).
4. 400 / 409 → inline error banner. Operator clicks Back to fix the offending step (banner shows which step the error belongs to via the validation path).

---

## 4. Updated deliverables list (v2 + v2.1 deltas)

| Deliverable | Status |
|---|---|
| All of v2 §5 deliverables | Carried forward unchanged |
| OnboardingFlow.razor — **visibility-toggle pattern (N1)** | Spec'd in §3.2 |
| OnInstanceBuilt EventCallback signature **(N2)** | Spec'd in §3.1, §3.4 |
| AddRoute.razor PrePopulatedSource / PrePopulatedSink params **(N3)** | Spec'd in §3.1 |
| Back-into-Step-1-with-different-protocol confirm dialog | NEW in v2.1 |
| Cancel-from-any-step confirm dialog | NEW in v2.1 |

---

## 5. Implementation steps (unchanged effort; refined sequence)

| # | Step | Effort | Track |
|---|---|---|---|
| 0 | ✅ **Done** — Step 0 reality-check pass | — | A |
| 1 | Branch `claude/connect-a-device-impl` (✅ already done) + v2.1 plan ratified | 0 day | A |
| 2 | Draft ADR-0016 — meta-wizard pattern + first-run self-provisioning + N1–N3 rules. User review before code. | 0.5 day | A |
| 3 | `ConfigurationManager.InitializeAsync` self-provisioning + diagnostic event + tests | 0.5 day | B |
| 4 | Add `EmbeddedMode` + `OnInstanceBuilt` to all 5 protocol wizards + 2 protocol pickers. Apply guards from Step 0 findings. | 1.25 day | C |
| 5 | `OnboardingFlow.razor` skeleton — visibility-toggle pattern (N1), step-transition handlers, Welcome step (Step 0 conditional) | 0.75 day | D |
| 6 | Wire Steps 3, 4, 5 with auto-run Test Connection (non-blocking Warning) + N3 pre-population for Route | 0.75 day | D |
| 7 | `WizardConfigMerger.BuildBundledOnboardingDraft` + `OnboardingApi` + `ReviewAndConnect` step + success screen with live tag counter | 1 day | E |
| 8 | Empty-state CTAs + top-level nav item + QA package launcher simplification (remove seed-copy step) + confirm dialogs (Cancel / Back-with-protocol-change) | 0.5 day | F |
| 9 | Cross-flow smoke testing (5 source protocols × 2 sink protocols = 10 combinations) + handoff doc + PR | 0.5 day | G |

**Total: 5.75 days realistic.** Step 0 saved ~0.25 day vs v2 estimate.

---

## 6. Definition of done (v2.1)

- [x] Step 0 reality-check doc landed with explicit ✓ for every wizard × axis cell — **done**
- [ ] All v2 §7 DoD items
- [ ] OnboardingFlow.razor uses visibility-toggle pattern; probe state survives Back/Next transitions (smoke-tested manually)
- [ ] OnInstanceBuilt callback fires correctly on validity transitions (covered by per-wizard tests)
- [ ] AddRoute.razor renders correctly with PrePopulatedSource + PrePopulatedSink that don't exist in `_currentConfig` yet
- [ ] Back-into-Step-1 confirm dialog appears when changing protocol after configuring Step 3
- [ ] Cancel confirm dialog appears from every step

---

## 7. Cross-references

- v2 plan (superseded by this v2.1): `docs/sessions/2026-05-27-connect-a-device-plan-v2.md`
- Step 0 reality-check: `docs/sessions/2026-05-27-connect-a-device-step0-reality-check.md`
- ADR-0015 (wizard contract): `docs/decisions/0015-wizard-contract.md`
- ADR-0014 (config-state vs runtime-state): `docs/decisions/0014-config-state-vs-runtime-state.md`
- M.2d.4 merged: master commit `b813410` (#38)

---

**v2.1 locked. Step 2 (ADR-0016 draft) starts next.**
