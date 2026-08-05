# Connect-a-device Step 0 — Reality-check pass

**Status:** Complete. EmbeddedMode is feasible across all 5 protocol wizards.
**Date:** 2026-05-27
**Branch:** `claude/connect-a-device-impl`
**Plan reference:** [v2 plan §3.1](./2026-05-27-connect-a-device-plan-v2.md)
**Methodology:** Each wizard inspected across five axes that the EmbeddedMode mechanic must handle cleanly. Findings cite line numbers; each cell rates ✓ (clean), 🟡 (conditional / mechanical guard needed), or ✗ (structural conflict).

---

## 1. The five inspection axes

| Axis | What we're checking | What "clean" looks like |
|------|--------------------|-------------------------|
| **A — `OnInitializedAsync` ownership** | Does the wizard do work in `OnInitializedAsync` that conflicts with parent-flow ownership? | Loading `_currentConfig` is acceptable as long as it can be skipped via `if (EmbeddedMode) return` and the parent passes config another way. |
| **B — Direct navigation** | Does the wizard call `Nav.NavigateTo` in code paths that EmbeddedMode must suppress? | All `NavigateTo` calls in Cancel/Save/Back paths must be behind `if (!EmbeddedMode)` guards. Parent owns step transitions. |
| **C — Snackbar ownership** | Does the wizard call `Snackbar.Add` on save/cancel/error paths? | In EmbeddedMode, the wizard should NOT emit snackbars. Errors surface via the wizard's own `WizardValidationBanner`; success is the parent's concern. |
| **D — Draft / Apply ownership** | Does the wizard POST to `/api/v1/config/drafts` or PUT to its edit endpoint? | In EmbeddedMode the wizard MUST NOT POST its own draft. It exposes `BuildSourceInstance()` / `BuildSinkInstance()` via callback; the parent owns the bundled apply. |
| **E — Probe side-effects** | Is Test Connection state contained inside the wizard component, and will it survive parent-driven Back/Next transitions? | Probe result + busy flag must live on the wizard's `@code` section (they all do today). Q7 memoisation works as long as Blazor keeps the component instance alive across step transitions — which it does when the parent's render tree references the same component. |

---

## 2. Per-wizard × per-axis grid

| Wizard | A — OnInitializedAsync | B — Navigation | C — Snackbar | D — Draft/Apply | E — Probe state |
|--------|------------------------|----------------|--------------|-----------------|-----------------|
| **AddFocas2Source** | 🟡 (line 485) | 🟡 (lines 666, 673, 730, 808) | 🟡 (lines 632, 637, 710–812 various) | 🟡 (line 789) | ✓ (Browse Controller — line-local state) |
| **AddBrotherHttpSource** | 🟡 (line 299) | 🟡 (lines 410, 444, 498, 574) | 🟡 (lines 430, 478, 484, 494, 502, 546, 559, 567, 578) | 🟡 (line 555) | ✓ (`_probeResult` at line 273) |
| **AddModbusSource** | 🟡 (line 547) | 🟡 (lines 660, 667, 790, 864) | 🟡 (lines 728, 733, 771, 777, 786, 794, 836, 849, 857, 868) | 🟡 (line 845) | ✓ (`_probeResult` at line 541) |
| **AddMqttDestination** | 🟡 (line 386) | 🟡 (lines 421, 425, 574, 651) | 🟡 (lines 494, 499, 558, 564, 573, 578, 599, 629, 638, 646, 655) | 🟡 (line 634) | ✓ (`_testResult` + `_testBusy` at lines 363–364) |
| **AddOpcUaServerDestination** | 🟡 (line 380) | 🟡 (lines 414, 418, 507, 581) | 🟡 (lines 491, 497, 506, 511, 532, 559, 568, 576, 585) | 🟡 (line 564) | ✓ (no probe — Footer comment line 334) |

**Legend:**
- ✓ = clean, no remediation needed
- 🟡 = mechanical guard (`if (!EmbeddedMode) { ... }`) suffices; no structural change
- ✗ = structural conflict requiring refactor first

**Zero ✗ cells.** All 5 wizards pass.

---

## 3. Per-axis findings

### Axis A — `OnInitializedAsync` ownership

All five wizards follow the same shape:

```csharp
protected override async Task OnInitializedAsync()
{
    if (_isEdit) { return; }                                  // Skip in Edit (router pre-loaded)
    _currentConfig = await HttpClient.GetFromJsonAsync<GatewayConfiguration>("/api/v1/config");
    _existing{Source|Sink}Ids = new HashSet<string>(...);     // For dup-id validation
}
```

**EmbeddedMode remediation:** add an `else if (EmbeddedMode) { return; }` early-exit. Parent passes the relevant config slices (existing IDs, sources list for routing-section) via parameters, OR allows each wizard to fetch independently (cheap — `/api/v1/config` is a single read of in-memory state, microseconds).

**Recommendation:** Let each wizard fetch independently for v1 (simpler — no new parameters needed). Optimise later if it shows in metrics.

### Axis B — Direct navigation

Every wizard has 4–5 `Nav.NavigateTo` call sites:

| Pattern | Purpose | EmbeddedMode action |
|---------|---------|---------------------|
| `Nav.NavigateTo($"/destinations/new")` (or similar) | Back-arrow / Cancel in Add mode | Suppress — parent owns Cancel (→ previous step or exit) |
| `Nav.NavigateTo($"/destinations/{id}")` | Back-arrow in Edit mode | N/A — EmbeddedMode is Add-mode only |
| `Nav.NavigateTo(Nav.Uri, forceLoad: true)` | Reload after stale-edit (409) | N/A — EmbeddedMode is Add-mode only |
| `Nav.NavigateTo($"/destinations/{id}")` | Edit-mode Save success | N/A — EmbeddedMode is Add-mode only |
| `Nav.NavigateTo("/config?new={draftId}")` | Add-mode Save success | Suppress — parent owns Save (→ next step or Apply) |

**EmbeddedMode remediation:** ALL navigation calls behind `if (!EmbeddedMode)` guards. In EmbeddedMode, the wizard either:
1. Calls a parent-supplied `EventCallback OnSaveRequested` (which the parent ignores — Save button is hidden in EmbeddedMode anyway), or
2. Emits the model via `OnModelChanged` and the parent's Next button does the rest.

**Recommendation:** Strategy 2. The wizard's Save button doesn't render in EmbeddedMode (WizardActions is hidden). Model emission via `OnModelChanged` is what the parent consumes.

### Axis C — Snackbar ownership

Snackbars are the largest source of code-touch surface — every wizard has 8–11 `Snackbar.Add` calls. They split into three categories:

| Category | Example | EmbeddedMode action |
|----------|---------|---------------------|
| **Save-success** | `Snackbar.Add($"Destination 'X' updated.", Severity.Success)` | Suppress — parent owns success messaging on the Connect-screen |
| **Save-error** | `Snackbar.Add($"Save failed: {err}", Severity.Error)` | Suppress — error surfaces in parent's review screen via `WizardValidationBanner` |
| **Probe-error** | `Snackbar.Add($"Test Connection failed: {ex.Message}", Severity.Error)` | Suppress — probe result panel already shows in the wizard; snackbar is redundant. (Even in non-embedded mode this is arguably redundant.) |

**EmbeddedMode remediation:** Wrap every `Snackbar.Add` in `if (!EmbeddedMode)`. Mechanical, ~20–30 minutes per wizard.

**Alternative considered:** route snackbars through an injected `IWizardMessageSink` that the parent supplies. Cleaner long-term, but YAGNI for v1 — the `if (!EmbeddedMode)` guard is fine.

### Axis D — Draft / Apply ownership

Every wizard has exactly one `PostAsJsonAsync("/api/v1/config/drafts", ...)` call inside `SaveAsDraftAsync`. In EmbeddedMode this call must never happen.

**EmbeddedMode remediation:** `OnSaveAsync` dispatcher updates:

```csharp
private Task OnSaveAsync() =>
    _isEdit ? SaveEditAsync()
    : EmbeddedMode ? Task.CompletedTask  // parent owns Save — this is unreachable since the Save button isn't rendered
    : SaveAsDraftAsync();
```

Since EmbeddedMode never renders the Save button, this guard is belt-and-suspenders. The real protection is that the WizardActions footer renders behind `!EmbeddedMode`.

### Axis E — Probe state

Probe state across the 4 wizards that have a probe (Focas2 / Brother / Modbus / MQTT):

| Wizard | State variables | Component-local? | Q7 memoisation viable? |
|--------|----------------|------------------|------------------------|
| Focas2 | (browse-result, line-local) | Yes | Yes |
| Brother | `_probeResult: BrotherHttpProbeResultDto?` (line 273) | Yes | Yes |
| Modbus | `_probeResult: ModbusProbeResultDto?` (line 541) | Yes | Yes |
| MQTT | `_testResult: MqttTestConnectionResultDto?` + `_testBusy: bool` (lines 363–364) | Yes | Yes |
| OpcUa | n/a (no probe, by Rule 6 carve-out) | n/a | n/a |

**Q7 memoisation contract:** "cache the last result keyed by the field values that produced it; invalidate on field edit." Today's wizards already preserve the result across re-renders (it's `private` state). The Q7 requirement holds **as long as Blazor keeps the wizard component instance alive across Back/Next transitions in the parent flow.**

**Blazor behaviour:** if `OnboardingFlow.razor` switches between steps by changing the render tree (e.g. `@switch (_currentStep) { case 3: <AddModbusSource ... /> }`), Blazor will dispose + recreate the component on each switch — and the probe state will be lost.

**Solution:** keep all wizard instances mounted in the render tree, using CSS visibility to show only the current step. Effectively:

```razor
<div class="@(_currentStep == 3 ? "" : "d-none")">
    <AddModbusSource EmbeddedMode="true" OnModelChanged="..." />
</div>
<div class="@(_currentStep == 4 ? "" : "d-none")">
    <AddMqttDestination EmbeddedMode="true" OnModelChanged="..." />
</div>
```

This is a standard Blazor "multi-step wizard with persistent state" pattern. Component instances stay alive; their `private` state survives Back/Next. **This is a v2 plan addition** — the implementation step that wires the embedded wizards (Step 5 in v2 plan §6) must use this visibility-toggle pattern, not `@switch`.

---

## 4. Verdict

**All 5 wizards pass Step 0. EmbeddedMode is feasible without structural refactoring.** Every 🟡 cell is resolved by a mechanical `if (!EmbeddedMode)` guard or an `if (EmbeddedMode) return` early-exit. No wizard needs to be redesigned.

**Effort estimate refinement:** v2 plan budgeted 1.5 days for Step 4 (the EmbeddedMode addition to all 5 wizards). Step 0 confirms this is realistic — roughly 15 minutes per wizard for navigation guards + 30 minutes for snackbar guards + 15 minutes for OnInitializedAsync + 5 minutes for the draft-API guard ≈ 65 min × 5 wizards ≈ 5.5 hours = 0.75 day. Plus tests ≈ 1.25 days. Half-day under budget.

**No remediation chips needed.** No wizard exhibits the structural concerns that v2 §3.1 anticipated as potential blockers.

---

## 5. New constraints surfaced for v2

These weren't in v2 plan §3.1 but emerged during Step 0; folding into v2.1 if you ratify:

### N1 — Multi-step persistence pattern

`OnboardingFlow.razor` must use **visibility-toggle (CSS `display:none`)**, NOT `@switch`, to switch between steps. Otherwise Blazor disposes + recreates the embedded wizards on each step transition, losing probe state + form values. Specific to Q7 memoisation.

### N2 — `OnModelChanged` callback signature

The wizard model is private to each protocol wizard. The parent flow needs to receive it. Two options:

**Option A** — typed callback per wizard:
```csharp
[Parameter] public EventCallback<MqttSinkWizardModel> OnModelChanged { get; set; }
```
Parent has 5 typed handlers (one per protocol).

**Option B** — generic "instance built" callback:
```csharp
[Parameter] public EventCallback<SinkInstanceConfig> OnInstanceBuilt { get; set; }
```
Parent has 1 handler that receives the already-built `SinkInstanceConfig`.

**Recommendation:** **Option B**. The parent flow doesn't care about the wizard's internal model; it cares about the buildable result. Wizard fires `OnInstanceBuilt` whenever the model becomes valid (and re-fires on every change). Parent's `Next` button enables when `OnInstanceBuilt` has fired at least once with a non-null result.

### N3 — `_currentConfig` access in EmbeddedMode

Each wizard loads `_currentConfig` independently for dup-ID validation + routing-section source list. In EmbeddedMode, the source-wizard's `_currentConfig` may not yet include the sink the operator is about to add (because they haven't picked it yet). This is fine — the routing section is hidden in EmbeddedMode anyway (parent owns route step). But the dup-ID check on the SOURCE wizard's instance-id remains useful and works as-is.

For the SINK wizard, same — dup-ID check works (the sink doesn't yet exist).

For the ROUTE wizard, it needs the source-instance-id (from step 3) + sink-instance-id (from step 4) **before** they're applied. v2 plan §3.1 already calls for pre-population from the previous steps; we need to make sure the route wizard's source/sink picker doesn't require those entries to ALREADY exist in `_currentConfig` (they don't yet — bundled apply happens at the end).

**Remediation:** EmbeddedMode flag on AddRoute.razor includes an additional `PrePopulatedSource: SourceInstanceConfig` and `PrePopulatedSink: SinkInstanceConfig` parameter. The wizard renders these as if they existed in `_currentConfig`. This is a small addition (~30 min) that the v2 plan implicitly assumed but didn't make explicit.

---

## 6. Conclusion

EmbeddedMode passes Step 0 across all 5 wizards with no ✗ cells and no structural surprises. v2 implementation can proceed with the constraints above (N1–N3) folded into the v2.1 plan if user ratifies. Total remaining effort estimate unchanged from v2: **5–6 days realistic.**

**Next step:** v2.1 plan revision (incorporates N1–N3) → user ratification → Step 1 (branch + ADR-0016 draft).
