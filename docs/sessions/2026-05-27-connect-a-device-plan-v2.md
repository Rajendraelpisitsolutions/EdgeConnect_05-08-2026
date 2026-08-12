# Connect-a-device — guided onboarding flow (v2 plan, LOCKED)

**Status:** v2 — Q1–Q7 ratified, Step 0 reality check added, first-run scope folded in. Ready for implementation.
**Date:** 2026-05-27
**Supersedes:** `docs/sessions/2026-05-27-connect-a-device-plan-v1.md`
**Hard precondition satisfied:** M.2d.4 merged to master at commit `b813410`.
**Estimated size:** **5–6 days realistic.** v1's 4.5-day estimate grew by ~1 day from the first-run fold-in and the explicit Step 0 reality check.

---

## 1. Goal (unchanged from v1)

A new operator opens Studio for the first time and wants to connect a CNC / PLC / device to a sink. Today: 3 wizards × 3 Applies × 4 page-jumps. After this milestone: **one guided flow** at `/onboard` that walks pick-source → configure-source → pick-destination → configure-destination → review → one Apply. Data flows.

**v2 scope expansion (folded in from this session):**
- **Self-provisioning `current.json`** on first launch — Studio no longer crashes when the file is missing. The `ConfigurationManager.InitializeAsync` change is part of this milestone.
- **Welcome step** at the start of the guided flow when the system has no gateway identity set, captures GatewayId + GatewayName from the operator.

---

## 2. Q1–Q7 Locked Decisions

| # | Question | v2 Decision |
|---|---|---|
| Q1 | Embed existing wizards as steps, or duplicate? | **Embed via `[Parameter] bool EmbeddedMode` on the 5 protocol wizards.** Gated by Step 0 reality-check passing. |
| Q2 | URL? | **`/onboard`** |
| Q3 | Empty-state CTA? | **Yes** — primary CTA when Sources / Sinks / Routes lists are all empty; secondary action when populated. |
| Q4 | Auto-run Test Connection in guided flow? | **Yes** — auto-run where supported (FOCAS2 / Brother / Modbus / MQTT), non-blocking. Failure surfaces as Warning banner; operator can Next anyway. OpcUa skipped (no probe — Rule 6 carve-out). |
| Q5 | Multiple sinks in v1? | **No** — 1 source : 1 sink for v1. "+ another destination" deferred to v2 (separate milestone). |
| Q6 | Nav placement of `/onboard`? | **Both** — top-level nav item AND empty-state CTAs on Overview / Sources / Destinations / Routes. |
| Q7 | Cache Test Connection result across Back/Next? | **Memoise per field-value set; invalidate on field edit.** |

---

## 3. v2 Adjustments (new in this revision)

### 3.1 Step 0 — Reality-check pass before EmbeddedMode coding

User-requested addition. Before writing a single line of EmbeddedMode code, do a structured walk-through of all 5 protocol wizards across **five inspection axes**. Any wizard that fails an axis becomes a chip-or-fold-in decision before the milestone proceeds:

| Inspection axis | What to check | Failure signal |
|---|---|---|
| **`OnInitializedAsync` ownership** | Does the wizard's `OnInitializedAsync` do work that conflicts with parent-flow ownership? E.g. loads its own config, sets up its own state-machine, makes HTTP calls that should be parent-driven. | The wizard does anything in `OnInitializedAsync` that the parent flow can't reliably trigger or suppress. |
| **Direct navigation** | Does the wizard call `Nav.NavigateTo(...)` in any code path that EmbeddedMode would need to suppress? E.g. on save success, on cancel, on stale-edit. | Direct `NavigateTo` calls outside of edit-mode (which we don't embed) would break flow control. |
| **Snackbar ownership** | Does the wizard call `Snackbar.Add(...)` on save / cancel / error paths? In EmbeddedMode the parent should own the success / error messaging. | Embedded wizards emitting their own snackbars during the guided flow would surface duplicate or contradictory toasts. |
| **Draft / Apply ownership** | Does the wizard POST to `/api/v1/config/drafts` or PUT to its edit endpoint? In EmbeddedMode the parent owns the bundled apply. | Embedded wizards triggering their own draft creation would break atomicity of the bundled flow. |
| **Probe side-effects** | Does Test Connection mutate any wizard-local state that the parent can't observe? E.g. caches results in a way that survives parent-driven Back/Next transitions. | Probe state hidden behind the wizard's component boundary would prevent Q7's memoisation contract. |

**Step 0 deliverable:** `docs/sessions/2026-05-XX-connect-a-device-step0-reality-check.md` — a 5-wizard × 5-axis grid with ✓ / ✗ / 🟡 (conditional) per cell, citing line numbers. Any 🟡 or ✗ gets a remediation plan before Step 1 begins.

**If Step 0 surfaces material issues:** either (a) refactor the offending wizard concern out FIRST (own chip), or (b) abandon EmbeddedMode for that one wizard and duplicate-and-tailor (lean path for that protocol only). Decision per-wizard, surfaced to the user before continuing.

### 3.2 First-run self-provisioning — folded in

#### Current behaviour
`ConfigurationManager.InitializeAsync` throws `CORE.ConfigFileNotFound` when `config/current.json` is missing, killing Studio startup. QA workaround was to ship a `seed/current.json` in the package.

#### v2 change
`InitializeAsync` self-provisions a minimal empty-state config when `current.json` is missing:

```csharp
public async Task InitializeAsync(CancellationToken cancellationToken)
{
    // ... existing mutex + initialized-check ...

    var currentJson = await _store.ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
    if (currentJson is null)
    {
        // M.connect-a-device: self-provision an empty-state config on first
        // run instead of throwing. The operator's first action via the
        // guided flow will Apply the real first config. Diagnostic event
        // emitted so the audit chain captures the auto-provision.
        var seed = BuildAutoProvisionedSeed();
        await _store.WriteCurrentAsync(JsonSerializer.Serialize(seed, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
        _current = seed;
        _initialized = true;
        _diagnostics.Record(new DiagnosticEvent(
            "CORE.CONFIG_AUTO_PROVISIONED",
            "current.json not found at startup; auto-provisioned empty seed.",
            DiagnosticSeverity.Info));
        return;
    }
    // ... existing schema + typed validation path ...
}

private GatewayConfiguration BuildAutoProvisionedSeed()
{
    var hostnameSlug = Slugify(Environment.MachineName); // letters/digits/.-_ only
    return new GatewayConfiguration
    {
        Gateway = new GatewaySettings
        {
            GatewayId = $"gw-{hostnameSlug}",
            GatewayName = $"EdgeConnect on {Environment.MachineName}",
            LogLevel = "Information",
        },
        Sources = [],
        Sinks = [],
        Routes = [],
    };
}
```

#### Welcome step in the guided flow
The guided flow detects whether the operator is still on an auto-provisioned identity (`GatewayId` matches `gw-{hostname}` pattern) and surfaces a one-step welcome:

> **Welcome to EdgeConnect.**
> Set a name for this gateway. You can change it later under Settings.
>
> **Gateway ID:** `gw-line1-edge` *(used in MQTT topics, OPC UA browse paths, diagnostics)*
> **Display name:** `Line 1 EdgeConnect`
>
> [Skip — use auto-generated]  [Set and continue →]

If the operator has already set a custom GatewayId in a previous session, the welcome step is silently skipped.

#### QA package simplification
The `seed/current.json` and the launcher's seed-copy logic disappear from the QA package. Reset between phases becomes: delete `data\` → app self-provisions on next launch.

---

## 4. Architecture (refined from v1)

### 4.1 The flow — `/onboard`

7 steps now (welcome added as a conditional first step):

| Step | Component | Skippable? | What happens |
|---|---|---|---|
| 0 — Welcome | `WelcomeStep.razor` (new) | Yes (auto-skipped when GatewayId is custom) | GatewayId + GatewayName |
| 1 — Pick source | `ChooseSourceProtocol.razor` (existing) | No | Picker cards |
| 2 — Pick destination | `ChooseDestinationProtocol.razor` (existing) | No | Picker cards |
| 3 — Configure source | `Add{Protocol}Source.razor` (existing, **EmbeddedMode**) | No | Same fields, auto-runs Test Connection on Next |
| 4 — Configure destination | `Add{Protocol}Destination.razor` (existing, **EmbeddedMode**) | No | Same |
| 5 — Configure route | `AddRoute.razor` (existing, **EmbeddedMode**, pre-populated) | No | Source + sink pre-selected; filter `*`; defaults |
| 6 — Review & connect | `ReviewAndConnect.razor` (new) | No | Summary card + `[Connect]` button → bundled Apply → success screen with live tag counter |

### 4.2 The bundled Apply

Same as v1: new `WizardConfigMerger.BuildBundledOnboardingDraft(source, sink, route)` + new `POST /api/v1/onboarding/apply` endpoint. Atomic — validation failure surfaces in the review screen; the operator returns to fix the bad step.

### 4.3 The `EmbeddedMode` seam

Same as v1, with one **new addition** from Step 0 reality-check findings:

```csharp
/// <summary>
/// When true, the wizard is hosted inside the Onboarding flow.
/// Behaviour changes:
///  - Footer (WizardActions) is not rendered — parent owns Next/Back.
///  - Save handlers are not called — parent owns the bundled Apply.
///  - Direct navigation calls (Nav.NavigateTo) are suppressed.
///  - Snackbar emissions on save/cancel are suppressed (parent owns).
///  - OnInitializedAsync behaviour: see per-wizard Step 0 notes — some
///    wizards may need additional Embedded-mode early-exits.
/// </summary>
[Parameter] public bool EmbeddedMode { get; set; }

[Parameter] public EventCallback<{Protocol}WizardModel> OnModelChanged { get; set; }
```

---

## 5. Deliverables (v2)

| Deliverable | Type | Source from v1? |
|---|---|---|
| Step 0 reality-check doc | New doc | NEW in v2 |
| `ConfigurationManager.InitializeAsync` self-provisioning | Edit | NEW in v2 (folded first-run) |
| `WelcomeStep.razor` | New | NEW in v2 (folded first-run) |
| `OnboardingFlow.razor` | New | v1 |
| `ReviewAndConnect.razor` | New | v1 |
| `EmbeddedMode` + `OnModelChanged` on 5 wizards | Edit (small per wizard) | v1 |
| `WizardConfigMerger.BuildBundledOnboardingDraft()` | New | v1 |
| `OnboardingApi.cs` — `POST /api/v1/onboarding/apply` | New | v1 |
| Empty-state CTAs on Overview / Sources / Destinations / Routes pages | Edit (small) | v1 (Overview added in v2 per Q6) |
| Top-level nav item for `/onboard` | Edit (small) | NEW in v2 (Q6) |
| QA package launcher simplification (remove seed/current.json copy logic) | Edit (small) | NEW in v2 (folded first-run) |
| `docs/decisions/0016-onboarding-meta-wizard.md` | New ADR | v1 (v2 adds first-run + Welcome step rules) |
| Tests | New tests | v1 + new tests for self-provisioning + Welcome step skip logic |
| `docs/sessions/2026-05-XX-connect-a-device-handoff.md` | Handoff | v1 |

---

## 6. Implementation steps (10 steps, with v2 sizing)

| # | Step | Effort | Track |
|---|---|---|---|
| 0 | **Reality-check pass** across 5 wizards × 5 axes. Output → `docs/sessions/2026-05-XX-connect-a-device-step0-reality-check.md`. Each ✗ or 🟡 becomes a remediation chip-or-fold-in. | 0.5–1 day | A |
| 1 | Branch `claude/connect-a-device-impl` from master. v2 plan reviewed (this doc). | 0.25 day | A |
| 2 | Draft ADR-0016 — meta-wizard pattern + first-run self-provisioning rules. User review before code. | 0.5 day | A |
| 3 | `ConfigurationManager.InitializeAsync` self-provisioning + diagnostic event + tests. Verifies Studio launches against missing `current.json` without crashing. | 0.5 day | B |
| 4 | Add `EmbeddedMode` + `OnModelChanged` to all 5 protocol wizards. Apply Step 0 remediations per wizard. Tests verify standalone mode unchanged. | 1.5 day | C |
| 5 | `OnboardingFlow.razor` skeleton — Welcome / Pick-source / Pick-destination steps with Next/Back wiring. | 0.5 day | D |
| 6 | Wire Steps 3, 4, 5 (embedded source / sink / route wizards) — including pre-population logic for Step 5. Auto-run Test Connection with non-blocking Warning surface. | 1 day | D |
| 7 | `WizardConfigMerger.BuildBundledOnboardingDraft` + `OnboardingApi` + `ReviewAndConnect` step. Atomic apply with success-screen live tag counter. | 1 day | E |
| 8 | Empty-state CTAs + top-level nav item. QA package launcher simplification (remove seed-copy step). | 0.5 day | F |
| 9 | Cross-flow smoke testing (5 source protocols × 2 sink protocols = 10 combinations). Handoff doc. PR. | 0.5 day | G |

**Total: 6 days realistic, 7 days if Step 0 surfaces 1–2 wizard remediations.**

Track labels mark natural seams. If the user wants to ship faster, Tracks B (first-run) + F (empty-state polish) can split out as separate PRs, leaving 4–5 days for the core guided flow.

---

## 7. Definition of done

- [ ] Step 0 reality-check doc landed, with explicit ✓ for every wizard × axis cell or a tracked remediation.
- [ ] `/onboard` flow completes end-to-end from empty config → running pipeline → live tag counter, with all 5 source protocols × 2 sink protocols (10 combinations smoke-tested).
- [ ] Studio launches successfully against missing `current.json` — auto-provisions a hostname-derived seed, diagnostic event emitted.
- [ ] Welcome step appears on auto-provisioned identity; silently skipped on customised identity.
- [ ] Existing single-entity wizards behaviour unchanged when invoked at their original URLs (`EmbeddedMode=false` default — invisible to existing callers).
- [ ] Empty-state CTA visible + functional on Overview / Sources / Destinations / Routes tabs. Top-level nav item present.
- [ ] Atomic apply: validation failure at apply-time leaves config untouched (operator returns to review screen).
- [ ] Test Connection auto-runs in steps 3 and 4 (where supported); failures show Warning, don't block Next. Q7 memoisation contract honoured across Back/Next.
- [ ] ADR-0016 landed and user-approved.
- [ ] QA package launcher no longer carries `seed/current.json` or the seed-copy logic.
- [ ] Solution-wide test sweep clean, zero warnings.
- [ ] Handoff doc cross-references this plan + ADR-0016 + any chips spawned during implementation.

---

## 8. v1 → v2 change log

| # | Change | Triggered by |
|---|---|---|
| 1 | Step 0 reality-check pass before EmbeddedMode coding (5 wizards × 5 inspection axes) | User feedback — verify assumptions before depending on them |
| 2 | First-run self-provisioning folded in: `ConfigurationManager.InitializeAsync` no longer throws on missing `current.json` | User question on whether to fold or separate |
| 3 | Welcome step added as conditional Step 0 in the guided flow | Required by #2 — operator should be able to customise auto-provisioned identity |
| 4 | QA package launcher simplification (remove `seed/current.json` + copy logic) | Side-effect of #2 |
| 5 | Effort estimate: 4.5 days → 5–6 days (7 with remediations) | Items 1 + 2 + 3 |
| 6 | Track labels (A–G) added to implementation steps for optional split | Lets user defer Tracks B / F into separate PRs if they want a smaller core-flow PR |

---

## 9. Cross-references

- ADR-0014: `docs/decisions/0014-config-state-vs-runtime-state.md`
- ADR-0015: `docs/decisions/0015-wizard-contract.md` — the wizard contract this meta-wizard extends
- M.2d.4 merged commit: `b813410` (master)
- Follow-up chips: `docs/sessions/2026-05-27-followup-chips.md`
- v1 plan (superseded): `docs/sessions/2026-05-27-connect-a-device-plan-v1.md`

---

**v2 ready for implementation.** Step 0 reality-check pass is the next concrete action — output drives whether the EmbeddedMode mechanic ships cleanly or surfaces per-wizard adjustments.
