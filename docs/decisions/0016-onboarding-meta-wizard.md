# ADR-0016: Onboarding meta-wizard for first-run + multi-entity authoring flows

**Status:** Proposed (2026-05-27)
**Date:** 2026-05-27
**Milestone:** Connect-a-device (post M.2d.4)
**Framing:** A new operator's first interaction with EdgeConnect is bootstrap, not edit. The single-entity wizards (ADR-0015) are the right surface for editing one source / sink / route in isolation. They are the *wrong* surface for the bootstrap case where the operator needs all three to exist and be wired together to see anything happen. This ADR introduces a new wizard kind — the **meta-wizard** — that composes existing wizards into a single guided flow with atomic apply, and codifies what self-provisioning behaviour the system surfaces when no `current.json` exists.

## Context

ADR-0015 locked the contract for protocol-instance authoring wizards. Six wizards (3 source, 2 sink, 1 route) conform. The contract works well for the established case: an operator with a populated configuration who wants to add or edit one entity.

Two operational gaps remain.

**Gap 1 — bootstrap.** A new operator opening Studio for the first time confronts:
1. `current.json` missing → `ConfigurationManager.InitializeAsync` throws `CORE.ConfigFileNotFound` → Studio crashes before any UI renders.
2. Even with config provisioned, the first-pipeline ceremony is 3 wizards × 3 drafts × 3 Applies × 4 page-jumps. The operator can't see data flow until all three entities are configured, applied, and runtime-connected.

The first problem broke QA distribution during M.2d.4 smoke-testing — we shipped a seed `current.json` in the QA package as a workaround.

The second problem surfaced as direct user feedback during the M.2d.4 sweep: *"setting up is causing major frustration."* The single-entity wizard contract is correct; what's missing is a composing surface above it.

**Gap 2 — multi-entity atomicity.** The current draft model creates one draft per wizard save. The operator applies each draft separately. There is no surface for "apply these N entities as a single transaction" — which is exactly what bootstrap needs (the route can't reference a source that doesn't exist yet, so partial-apply produces fault states the operator has to clean up).

This ADR addresses both gaps with one architectural addition: the **meta-wizard**.

## Decision

A meta-wizard is a wizard-shaped surface that **composes other wizards as steps** and delivers a **single atomic apply** at the end. The first concrete meta-wizard is **Connect-a-device** at `/onboard`. This ADR codifies six rules organised across structure, composition, runtime, and lifecycle layers.

### Layer 1 — Structure

**Rule 1 — Meta-wizard composition.** A meta-wizard composes existing protocol-instance wizards as embedded steps. Each embedded step is a Razor component that:
- Implements ADR-0015 in standalone mode (unchanged at its existing route).
- Accepts a `[Parameter] bool EmbeddedMode` that suppresses the wizard's owned footer, navigation, snackbars, and draft-creation paths.
- Emits its buildable result via `[Parameter] EventCallback<TInstance?> OnInstanceBuilt` where `TInstance` is `SourceInstanceConfig`, `SinkInstanceConfig`, or `RouteConfig`.

The meta-wizard owns:
- Step navigation (Next, Back, Cancel).
- Overall progress indicator.
- The Apply ceremony at the end.
- All operator-facing messaging (snackbars, success screen, error banners).

The meta-wizard does NOT:
- Re-implement field rendering or validation that already exists in the embedded wizards.
- Bypass any embedded wizard's validation rules.
- Mutate the running configuration directly (uses the bundled-apply endpoint).

**Rule 2 — Persistence across step transitions.** A meta-wizard MUST use **CSS visibility toggle** (`display: none`), not `@switch`, to switch between steps. Blazor disposes a component when it leaves the render tree; visibility-toggle keeps every step component mounted so its `private` state (probe results, form values, validation messages) survives Back / Next transitions.

The only exception: changing a *protocol selection* in an earlier step DOES invalidate any later step that depended on that choice — the later step is unmounted and recreated. The meta-wizard MUST surface a confirm dialog before allowing such a change.

### Layer 2 — Composition contract

**Rule 3 — `EmbeddedMode` contract.** When `EmbeddedMode = true` on a protocol-instance wizard:

| Concern | Behaviour |
|---|---|
| Footer (WizardActions) | Not rendered. |
| `Nav.NavigateTo` in Cancel / Save paths | Suppressed (guarded by `if (!EmbeddedMode)`). |
| `Snackbar.Add` emissions | Suppressed. Errors surface via the wizard's own `WizardValidationBanner` only. |
| Draft creation (`POST /api/v1/config/drafts`) | Not reachable (Save button is hidden; the dispatcher won't be invoked). |
| `OnInitializedAsync` loading `_currentConfig` | Still runs (cheap; needed for dup-id validation). The routing section that references `_currentConfig.Sources` / `Sinks` is already hidden in EmbeddedMode (it's only shown in Add mode + when wiring choice = "newRoute", which is meaningless when a meta-wizard owns the route step). |
| Test Connection probe state | Lives on the wizard's `@code` block (unchanged from standalone). Survives Back / Next transitions per Rule 2. |
| Validation banner | Rendered normally — the operator still sees inline validation errors. |

**Rule 4 — `OnInstanceBuilt` semantics.** The callback fires whenever the wizard's `CanSave()` evaluation changes:
- Fires with the buildable `TInstance` when the model becomes valid.
- Fires with `null` when the model transitions from valid → invalid.
- May fire repeatedly while the operator types; no debounce in the wizard. The meta-wizard debounces if performance demands it.

The meta-wizard uses this signal to enable / disable its Next button. The wizard does NOT call `OnInstanceBuilt` from `OnInitializedAsync`; only from field-change handlers.

**Carve-out — `AddRoute.razor`.** The route wizard's source / sink pickers normally read from `_currentConfig.Sources` / `Sinks`. In a meta-wizard composition, the source and sink for the route haven't been applied yet (the bundled apply happens AFTER the route is configured). `AddRoute.razor` therefore additionally accepts:

```csharp
[Parameter] public SourceInstanceConfig? PrePopulatedSource { get; set; }
[Parameter] public SinkInstanceConfig? PrePopulatedSink { get; set; }
```

When these are set, the wizard renders them as if they existed in `_currentConfig`. This is documented as an EmbeddedMode-only behaviour and is invisible in standalone mode (parameters default to null; standalone code paths are unchanged).

### Layer 3 — Runtime first-run

**Rule 5 — First-run self-provisioning.** When `ConfigurationManager.InitializeAsync` finds no `current.json`, it MUST self-provision a minimal empty-state config and continue startup — NOT throw `CORE.ConfigFileNotFound`.

The provisioned seed:

```json
{
  "Gateway": {
    "GatewayId": "gw-{hostname-slug}",
    "GatewayName": "EdgeConnect on {hostname}",
    "LogLevel": "Information"
  },
  "Sources": [],
  "Sinks": [],
  "Routes": []
}
```

The seed is written via the normal atomic-write path so it becomes the system's "v1 applied" config. A diagnostic event `CORE.CONFIG_AUTO_PROVISIONED` is emitted (Info severity) so the audit chain captures the auto-provision as a first-version event.

The operator can override the auto-generated `GatewayId` / `GatewayName` via the meta-wizard's optional first step (rendered conditionally when the current identity still matches the auto-provision pattern).

**Rule 5.1 — Why not just ship a seed config in the installer?** Self-provisioning makes the binary self-sufficient. No "did you remember to copy the seed file?" footgun. No machine-specific GatewayId baked into a template. No installer mutating a checked-in artifact. The behaviour aligns with the "the app works out of the box" expectation operators have learned from every other piece of software they install.

### Layer 4 — Lifecycle

**Rule 6 — Atomic bundled apply.** A meta-wizard apply is a single transaction:
1. The meta-wizard collects `TInstance` objects from each embedded step via `OnInstanceBuilt`.
2. POSTs the collection to a meta-wizard-specific endpoint (e.g. `POST /api/v1/onboarding/apply` for Connect-a-device).
3. Server-side: builds the bundled draft via a new merger method (e.g. `WizardConfigMerger.BuildBundledOnboardingDraft(source, sink, route)`), runs schema + typed + cross-record validation, applies atomically via the normal `IConfigurationManager.ApplyAsync` path.
4. Success: the operator's review screen transitions to a success screen with live runtime state.
5. Failure (validation or apply): the operator returns to the offending step (identified by the validation path) to fix.

The bundled-apply endpoint MUST go through the same `IConfigurationManager.ApplyAsync` that single-entity Apply uses — no parallel path. This preserves ADR-0014 (config vs runtime state distinction), ADR-0015 (save-flow contract), and the audit-chain invariant (every applied config has exactly one history entry).

## Reasoning

1. **One bootstrap experience, two surfaces.** The single-entity wizards remain the right surface for `edit one thing later`. The meta-wizard is the right surface for `connect a new device from scratch`. Trying to make one surface serve both produces friction on whichever case is less common — and the bootstrap case (rare per-operator but high-stakes-for-first-impression) is exactly where polish matters most.

2. **The "embed, don't duplicate" choice.** Step 0 of the Connect-a-device milestone walked all 5 wizards across 5 axes (OnInitializedAsync, Nav, Snackbar, Draft/Apply, probe state) and confirmed every axis is resolvable by mechanical `if (!EmbeddedMode)` guards — no structural refactor needed. Duplicating each wizard would have cost 5× the code surface and create a forever-divergence risk. The embed mechanic costs ~1.25 days; duplication would cost ~5 days plus permanent maintenance overhead.

3. **Visibility-toggle vs `@switch` (Rule 2).** Blazor's component lifecycle disposes any component that leaves the render tree. Probe state lives in the wizard's `@code` block as `private` fields. If we used `@switch` to switch steps, the operator clicking Back from Step 5 to Step 3 would discard their probe result and force them to re-run Test Connection. Visibility-toggle keeps the component instance alive; probe state survives. Q7's memoisation contract is enforceable as a consequence of Rule 2 — not via additional plumbing.

4. **Self-provisioning aligns with ADR-0014.** Config state is "is this thing supposed to be running?" An empty self-provisioned config says: no sources, no sinks, no routes — the gateway has identity but no configured intent yet. That is an entirely coherent state, distinct from a missing-file state. Throwing on missing-file conflated the two; self-provisioning separates them cleanly. The auto-provisioned `GatewayId` is auditable (the diagnostic event names it as auto-generated) so an operator who needs a stable production identifier knows to set their own before going live.

5. **Bundled apply through the same `ApplyAsync` keeps invariants intact.** ADR-0014 + ADR-0015 don't need amendment because the meta-wizard's apply goes through the existing primitives. Schema validation, typed validation, cross-record validation, history entry, atomic write — all unchanged. The new endpoint is a thin wrapper around `BuildBundledOnboardingDraft` + `ApplyAsync`.

6. **The Route pre-population carve-out is a design integrity check.** If `AddRoute.razor` couldn't accept phantom source / sink references, that would mean the route wizard fundamentally couldn't compose with other wizards in a flow — which would make ADR-0015's "wizards are protocol-instance authoring surfaces" claim weaker. The pre-population parameters are a small explicit acknowledgement that wiring wizards (per ADR-0015's Route carve-out) need a slightly different composition surface. They land as additive parameters, not breaking changes.

## Consequences

### Code

- **5 protocol wizards** (Focas2 / Brother / Modbus / MQTT / OpcUa) gain `[Parameter] bool EmbeddedMode` + `[Parameter] EventCallback<TInstance?> OnInstanceBuilt`. ~15 min per wizard for navigation guards + ~30 min per wizard for snackbar guards + per-wizard remediations from Step 0. Total ~1.25 days.
- **2 protocol pickers** (`ChooseSourceProtocol.razor`, `ChooseDestinationProtocol.razor`) gain `EmbeddedMode` + `OnProtocolChosen` callback.
- **`AddRoute.razor`** additionally gains `PrePopulatedSource` + `PrePopulatedSink` parameters per the route carve-out.
- **`OnboardingFlow.razor`** (new) — the first meta-wizard. ~1.5 days.
- **`WelcomeStep.razor`** (new) — conditional first step for auto-provisioned identity.
- **`ReviewAndConnect.razor`** (new) — Step 6 of OnboardingFlow.
- **`OnboardingApi.cs`** (new) — `POST /api/v1/onboarding/apply` endpoint.
- **`WizardConfigMerger.BuildBundledOnboardingDraft`** (new) — pure function that composes source + sink + route into a bundled draft.
- **`ConfigurationManager.InitializeAsync`** — self-provisioning branch added.

### UX

- Empty-state Sources / Destinations / Routes pages surface `[Connect a device →]` as the primary CTA.
- Top-level nav gains a "Connect" entry.
- First launch on a machine with no `current.json` no longer crashes; Studio loads, the operator lands on Overview, and the empty-state CTA leads them to `/onboard`.

### Future surfaces

- **More meta-wizards** are now a documented pattern. Future use cases might include: an "Import from another gateway" meta-wizard (composes file-pick + identity-mapping + verification steps + atomic apply); a "Migrate adapter" meta-wizard (composes new-adapter-config + traffic-shadow + cutover + decommission steps). Each follows Rules 1–6.

- **The Configuration page's Apply ceremony** is unchanged. Operators editing individual entities still go through draft → validate → apply on the Config page. The meta-wizard is additive, not replacement.

- **The single-entity wizards stay untouched in their existing routes.** `EmbeddedMode = false` is the default; existing standalone behaviour is invisible to existing callers.

### Carve-outs documented

- **OpcUa Server destination** has no Test Connection probe (ADR-0015 Rule 6). The auto-run Test Connection in meta-wizard Step 4 (Q4 verdict) skips OpcUa silently — the operator sees no probe panel, no warning, just Next.
- **The Route wizard's source/sink picker** accepts phantom entries via `PrePopulatedSource` / `PrePopulatedSink` in EmbeddedMode only (Rule 3 carve-out). Standalone mode is unchanged.

## Cross-references

- ADR-0014: Config state and runtime state are distinct surfaces.
- ADR-0015: Wizard contract for protocol-instance authoring surfaces. **This ADR extends ADR-0015** — does not replace it.
- M.2d.4 commit `b813410` — landed the wizard primitives this meta-wizard composes.
- Connect-a-device plan trail:
  - `docs/sessions/2026-05-27-connect-a-device-plan-v1.md`
  - `docs/sessions/2026-05-27-connect-a-device-plan-v2.md`
  - `docs/sessions/2026-05-27-connect-a-device-plan-v2.1.md` (locked)
- Step 0 reality-check: `docs/sessions/2026-05-27-connect-a-device-step0-reality-check.md`
- Follow-up chips: `docs/sessions/2026-05-27-followup-chips.md`
