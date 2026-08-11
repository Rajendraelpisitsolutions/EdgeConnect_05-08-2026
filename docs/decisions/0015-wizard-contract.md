# ADR-0015: Wizard contract for protocol-instance authoring surfaces

**Status:** Accepted (2026-05-27); amended (2026-05-29) to add Layer 5 — Browse + Hot-config (Rules 9 / 10 / 11 / 11.1) for the multi-protocol pilot expansion.
**Date:** 2026-05-27 (original); 2026-05-29 (Layer 5 amendment)
**Milestone:** M.2d.4 (locks the contract that M.2d.1/2/3 incrementally built); multi-protocol pilot expansion (Layer 5 amendment)
**Framing:** What is a "wizard," exactly? This ADR writes down the contract every wizard conforms to, so the next protocol's wizard is reviewed against the contract — not against six existing implementations.

## Context

Phase 2 added six wizards to the Connectivity Studio: three source-authoring wizards (Focas2, Brother HTTP, Modbus TCP), two sink-authoring wizards (MQTT, OPC UA Server), and one route-wiring wizard (AddRoute). They were authored across four sub-milestones (M.2d.1 primitives, M.2d.2 source edit-mode, M.2d.3 sink+route edit-mode) and four months. Without a written contract, the wizards drift — sink wizards still used pre-M.2d.1 manual layout while source wizards adopted `WizardShell + WizardSection`; the `WizardValidationBanner` primitive shipped in M.2d.1 was never wired by any consumer; `OnMessageClick` scroll-to-field was deferred without an owner.

M.2d.4 closes those gaps. This ADR locks the contract so future protocols (OPC UA Client, S7, Modbus RTU, MTConnect Agent, ...) extend the contract instead of re-inventing it. Without the ADR, "we have N consistent wizards" is a transient claim that decays with each new protocol.

The contract is the load-bearing M.2d.4 deliverable. The structural refactors (sink-shell adoption, banner wire-up, scroll-to-field) implement the contract; the ADR articulates what the implementation is supposed to be.

## Decision

A wizard, in this codebase, conforms to **eleven rules** organised into five layers — structure, contracts, behaviour, lifecycle, and (for browse-capable protocols) browse + hot-config. Future wizards conform to all eleven rules or amend this ADR.

### Layer 1 — Structure (component hierarchy)

**Rule 1 — Component hierarchy is locked.** Every protocol-instance authoring wizard composes from the following shared primitives:

| Primitive | Role | Source |
|---|---|---|
| `WizardShell` | Outer page-frame: header band (back arrow, icon, title, subtitle), load-state guard, body + footer slots | `src/ElpisEdgeConnect.Management/Components/Shared/WizardShell.razor` |
| `WizardSection` | Numbered card with auto-driven `"{Index}. {Title}"` heading | `WizardSection.razor` |
| `WizardValidationBanner` | Cumulative validation message surface, Error/Warning/Info severity, scroll-to-field on click | `WizardValidationBanner.razor` |
| `WizardActions` | Footer button row: Save (required), Cancel (required), Test Connection (optional), AdditionalActions slot | `WizardActions.razor` |
| `WizardWatchSlot` | Reserved embed point for M.2c Live Tag Watch — renders zero DOM today; the contract is locked | `WizardWatchSlot.razor` |

**Carve-out — Route wizard.** `AddRoute.razor` is a wiring wizard, not an authoring wizard. It pairs an existing source with one or more existing sinks; it has no protocol sections. Route uses `WizardActions` but not `WizardShell`/`WizardSection` — its layout is materially different (source picker, sink picker, filter editor, transforms editor). Future wiring wizards inherit this carve-out.

**Anti-coupling locks:**
- `WizardShell` does NOT wrap children in section structure (sections are `WizardSection`'s job).
- `WizardSection` does NOT wrap children in a layout grid (the wizard chooses MudGrid / table / chart / etc.).
- `WizardValidationBanner` does NOT own field anchors (the razor template owns the DOM IDs).

### Layer 2 — Contracts (validators, anchors, edit-mode)

**Rule 2 — Per-instance validators follow the `ModbusTagValidator` shape.** A protocol with a collection of per-instance items (tag rows, NodeIds, topic templates, etc.) ships a **static class** with the signature:

```csharp
public static class FooValidator
{
    public static void Validate(FooItem item, string pathPrefix, List<ValidationIssue> errors);
}
```

Three call sites compose the validator: the adapter's `ValidateConfigAsync`, the wizard model's per-row validation method, and any CSV/import flow. **Zero rule duplication** — when the adapter learns a new datatype, every caller inherits the rule for free.

**Carve-out — protocols without per-instance items.** Wizards whose configuration is purely scalar (FOCAS2 has scalar connection params + a fixed-catalog datapoint list; Brother HTTP same; MQTT has scalar broker config + a scalar topic template; OPC UA Server has scalar endpoint config) do NOT need a per-instance validator. The wizard model validates inline; the adapter's `ValidateConfigAsync` validates the same scalars; no shared validator exists because there is no collection to iterate. ADR-0015 explicitly does NOT require these wizards to invent a fictitious per-instance type.

**Open extension.** Static class is the v1 convention. If a future case needs DI injection (e.g., a validator that calls a real network probe), lift the class behind a thin `IFooValidator` interface and update callers. Until then, static is the canonical shape.

**Rule 3 — Field-anchor naming is `field-{kebab-case-path}`.** When a wizard model emits a `WizardValidationMessage` with a non-null `FieldAnchor`, the value follows the convention:

- **Format:** kebab-case, hierarchical, dot-delimited. Examples: `field-instance-id`, `field-connection.host`, `field-security.cert-path`, `field-tag-definitions.3.byte-order`.
- **DOM contract:** the corresponding razor template renders the field with `id="field-{anchor}"`. For MudBlazor inputs that don't accept a raw `id` prop, use `UserAttributes`:

  ```razor
  <MudTextField @bind-Value="_model.Host"
                Label="Host *"
                UserAttributes="@(new Dictionary<string, object?> { ["id"] = "field-connection.host" })" />
  ```

- **Ownership matrix:**

  | Side | Responsibility |
  |---|---|
  | Wizard model (`*.cs`) | Sets `FieldAnchor` to kebab-case path matching the template's DOM id |
  | Razor template (`*.razor`) | Declares `id="field-{anchor}"` on each validatable input |
  | Per-instance validator | Returns `pathPrefix` in kebab-case (e.g. `"tag-definitions.3"`); the wizard model concatenates with field name |

- **Why kebab-case:** CSS-selector friendly, matches existing `data-testid` convention, prevents C# property-name leakage into the DOM contract (renaming `Host` to `BrokerHost` in the model doesn't break the field ID).

**Rule 4 — `EditModeContext` discriminates Add from Edit.** A wizard renders identically in both modes except:

- **Add mode:** InstanceId field is editable; routing/wiring section visible (where applicable); Save button reads "Save as draft".
- **Edit mode:** InstanceId field is disabled (renaming requires delete + recreate); routing/wiring section hidden; Save button reads "Save changes"; an info banner shows `"Editing runtime configuration — version {versionId}"`; PUT direct-apply path with optimistic concurrency; 409 surfaces `StaleEditWarningBanner`.

Loading an existing config into the wizard model is `HydrateFromExisting(existingConfig)`'s job — pure mapping, no DI, no HTTP. The wizard razor calls `HydrateFromExisting` once in `OnParametersSet` guarded by a `_hydrated` flag.

### Layer 3 — Behaviour (validation, probe, banner)

**Rule 5 — Validation banner severity, ordering, and empty-state are locked.**

- **Severity mapping.** `WizardValidationSeverity.Error` → `MudAlert Severity="Error"`; `Warning` → `Warning`; `Info` → `Info`. Same outline/icon vocabulary across wizards.
- **Ordering.** Messages are sorted: Errors first, then Warnings, then Info. Within a severity, sorted lexicographically by `Path` (deterministic).
- **Empty-state — NO success state.** When the message list is empty, the banner renders zero DOM. There is no "All good ✓" green banner. **Absence is the success signal.** Future contributors might reasonably propose an explicit success state to make validation feel more responsive — the ADR documents why that's wrong: signal-to-noise (banners that always show carry no information), false confidence (a green banner on a stale-validated form misleads), passive surfaces (the lack of a problem is the default).
- **Auto-clear on fix.** When the operator fixes a field, the corresponding message disappears from the banner on the next render. No "Re-validate" button. Driven by `WizardModel.Validate()` being called on every field change.
- **Save-gating.** `CanSave()` returns true iff zero Error-severity messages exist. Warnings DO NOT block Save. Info NEVER blocks.
- **Scroll-to-field on click.** Clicking a message with a non-null `FieldAnchor` invokes JS interop: `document.getElementById('field-{anchor}').scrollIntoView({ behavior: 'smooth', block: 'center' })` followed by `.focus()`. Messages without a `FieldAnchor` render as non-clickable text.

**Rule 6 — Test Connection is a read-only probe.** When a wizard offers a Test Connection button, it satisfies all four criteria:

- **Idempotent.** Multiple clicks produce the same result; no state mutation.
- **No side effects on the running adapter.** The probe opens a separate connection / socket / handle, does its check, closes. The running adapter's state is unaffected.
- **No side effects on the wizard's draft.** The probe reads the wizard's current edited values; it does not write back, save, or modify any persisted state.
- **Uses edited values, not running values.** The probe verifies what the operator is about to save, not what's running.

Results surface as an inline panel (typically a `MudAlert` Severity-mapped to outcome) — NEVER as a snackbar (snackbars dismiss; the probe result is reference information the operator may want to consult while editing).

**Carve-out — OPC UA Server.** OpcUaServer does not offer a Test Connection probe because binding to the OPC UA endpoint has side effects on running adapter state (acceptor design). This is documented behaviour, not an oversight.

**Test Connection label convention.** All probes use the label `"Test Connection"` except FOCAS2, which uses `"Browse Controller"` — a product name borrowed from FANUC's documentation. The product-name label is intentional and documented; future FOCAS-aligned protocols may inherit it.

### Layer 4 — Lifecycle (save flow, persistence)

**Rule 7 — Save commits to a draft, never directly to the running config (Add mode); commits via PUT direct-apply in Edit mode.**

- **Add mode** (`POST /api/v1/config/drafts`). The wizard's output becomes a draft. The Configuration page is where the operator validates + applies the draft. The wizard NEVER bypasses the draft → validate → apply → rollback flow.
- **Edit mode** (`PUT /api/v1/sources/{id}` / `PUT /api/v1/sinks/{id}` / `PUT /api/v1/routes/{id}`). Direct-apply with optimistic concurrency. The endpoint internally creates and applies a draft in one transaction; the operator does not visit the Configuration page. 409 conflicts surface `StaleEditWarningBanner`.

The two flows are intentionally different — Add is exploratory (the operator is constructing something new; the draft is a working artifact); Edit is a focused mutation (the operator knows what they want to change; round-tripping through the Configuration page adds friction without value).

**Rule 8 — Persistence boundary: the wizard is in-memory only.** The wizard's intermediate state lives in the Blazor circuit. No auto-save. No `localStorage` persistence. No "resume where you left off." The Save button is the only commit gesture; Cancel and tab-close discard.

This is locked behaviour. Rationale: anti-thrash (auto-save would surface half-typed fields as validation errors), anti-confusion (the draft IS the persistence model — surfacing two persistence layers conflates them), explainability (a draft has an audit-chain entry; an auto-saved local state does not).

### Layer 5 — Browse + Hot-config (added 2026-05-29)

> Layer 5 applies to wizards whose source protocol exposes an interactive **browse service** — currently OPC UA Client (uses UA `Browse` / `BrowseNext`) and EtherNet/IP (uses libplctag `@tags` + `@udt/<id>`). Protocols without a browse service (e.g., a future MELSEC native wizard) are exempt; their wizard documents the absence via an Info alert per Rule 6's carve-out pattern.

**Rule 9 — Browse capability.** Wizards for protocols that expose a browse service MUST surface a "Connect & Browse" button in the tag-selection section. The button MUST call the wizard's `IBrowseService` implementation (per-protocol) and render results in the shared `TagBrowseTreeView` component. Browse implementations MAY be lazy (children fetched on node expansion).

- **Shared abstractions** (introduced by the multi-protocol pilot expansion):
  - `ElpisEdgeConnect.Core.Browse.ITagBrowseService` — protocol-agnostic browse contract
  - `ElpisEdgeConnect.Core.Browse.BrowseResult` / `BrowseNode` / `BrowseNodeKind` — protocol-agnostic data shapes
  - `ElpisEdgeConnect.Management.Components.Shared.TagBrowseTreeView` — lazy-load MudTreeView wrapper with multi-select + checkbox column + "Add selected" / "Add all under this node" actions

- **Inheritance.** Protocols whose physical contract does not support browse (e.g., MELSEC native — operator-defined tag lists only) are exempt. Their wizard documents the absence in an Info alert mirroring the Rule 6 carve-out used by OPC UA Server.

- **Failure UX.** A failed browse (network failure, auth failure, browse-not-supported on the server) surfaces in the wizard's validation banner per Rule 5, NOT as a modal or silent failure.

**Rule 10 — Auto-load action.** Wizards that implement Rule 9 MUST also surface an "Add all" / "Auto-load" action that bulk-imports browsed tags into the source.

- **Confirmation gate.** If the resulting count exceeds 500 (operator-configurable max-tag-count safety cap), the wizard prompts: "Add N tags to this source? (max recommended: 500)" with Confirm / Cancel.
- **Partial failure.** Bulk-add MAY surface per-tag errors in the wizard's validation banner; the source is not left in a half-populated state — bulk-add is transactional at the wizard model level (either all succeed and the model commits, or none commit and errors render).
- **Scope.** Operator selects a tree node; the action recursively adds all leaf Variable nodes under that node. Folder nodes themselves do not become tags.

**Rule 11 — Hot-config invariant.** Edit-mode changes to the tag list, polling rate, or subscription tuning on a browse-capable wizard MUST go through `ISourceAdapter.ReconfigureAsync` rather than full Stop+Initialize+Start.

- **Default fallback.** Adapters that don't implement true hot-reconfigure rely on the `ISourceAdapter.ReconfigureAsync` default-implementation member (which IS Stop+Initialize+Start). Existing non-browse adapters inherit this default safely; new browse-capable adapters (OPC UA Client, EtherNet/IP) override with their true hot-reconfigure path.
- **Wizard UX.** Wizard surfaces "reconfigure in progress" via the standard busy spinner during the async `ReconfigureAsync` call.
- **Concurrency.** A second `ReconfigureAsync` while the first is in flight produces a protocol-specific error code (e.g. `OPCUA.RECONFIGURE_IN_PROGRESS`). The wizard surfaces the error via the standard error snackbar with retry guidance.

**Rule 11.1 — Reconfigure validation precedence.** New configurations MUST pass full validation (`ValidateConfigAsync`) BEFORE the adapter's active set changes. A reconfigure that fails validation leaves the adapter in its previous running state with no operator-visible disruption beyond the wizard's error report.

This is the load-bearing invariant for safe edit-mode. Without it, a partially-applied bad config could leave the adapter in an unrecoverable state. With it, validation-then-swap is atomic from the operator's perspective.

## Reasoning

1. **Operator interchangeability.** An operator who has commissioned a FOCAS2 source should be able to commission a Modbus source without re-learning the wizard. The structural and behavioural rules above make this true — the protocol-specific sections vary; the surface around them does not.

2. **Future-protocol extension cost.** Adding a new protocol's wizard should cost N hours, not N days. With the contract written down, the new wizard is a fill-in-the-blanks composition over the shared primitives. Without it, every new protocol re-debates layout, validation surface, save-flow, edit-mode discrimination.

3. **Drift prevention.** A contract that lives only in code drifts. M.2d.1 shipped `WizardValidationBanner` as a primitive; no wizard consumed it for four months. With the ADR, "is the banner wired?" is a PR review question; without it, it's a "we'll get to it" that decays.

4. **Forward-compat with Live Tag Watch (M.2c).** `WizardWatchSlot` is the contractual embed point for M.2c. Wizards that conform to the structure today inherit the Watch capability tomorrow without rework. ADR-0015 locks the slot's contract so M.2c can ship without re-litigating where Watch lives.

5. **ADR-0014 alignment.** ADR-0014 locks "config state and runtime state are distinct surfaces." Rule 6 (Test Connection is read-only, no draft mutation) and Rule 7 (Save flows are config-only) honour ADR-0014 — the wizard touches config, the runtime is observed, never mutated by the wizard.

6. **Why ADR-driven implementation.** The v2.1 plan deliberately drafts this ADR before refactoring sinks. The ADR is the spec; the refactor implements the spec. Writing the spec first forces clarity (every rule is articulated before any code changes); writing it after risks the spec rationalising whatever the implementation happened to do.

## Consequences

- **All five protocol wizards** (Focas2, Brother, Modbus, MQTT, OpcUa) use `WizardShell + WizardSection`. MQTT and OpcUa adopt these in M.2d.4 (Steps 4–5).

- **All five protocol wizards wire `WizardValidationBanner`** with field-anchor `id` attributes per Rule 3. Wired in M.2d.4 (Steps 4–6).

- **`WizardValidationBanner.OnMessageClick` implements scroll-to-field** via `IJSRuntime` JS interop. Implemented in M.2d.4 Step 3.

- **The cross-wizard consistency audit checklist** (`tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md`) is the ongoing drift-detection artifact. Every PR that touches a wizard re-checks the relevant cells. If drift becomes a recurring problem, the checklist is upgraded to an automated test suite.

- **Route is permanently carved out** of `WizardShell` adoption. The carve-out is rule-1-explicit and is the only exception. Future wiring wizards inherit the carve-out without needing a new ADR.

- **Future protocol-instance validators** (e.g., when adding S7 or MTConnect Agent, if those wizards introduce per-tag collections) follow Rule 2's static-class shape. The first time a validator needs DI, this ADR is amended.

## How to add a new wizard — 5-step operational guide

The following is the contract's operational surface. A new wizard for a hypothetical protocol "Foo" follows these steps:

1. **Define the wizard model** at `src/ElpisEdgeConnect.Management/Wizards/FooSourceWizardModel.cs`:
   - Add public properties for each field the wizard binds (`Host`, `Port`, `Username`, ...).
   - Add `Validate()` method that returns `List<WizardValidationMessage>` ordered Error → Warning → Info, with kebab-case `FieldAnchor` per Rule 3.
   - Add `HydrateFromExisting(SourceInstanceConfig existing)` static factory for edit mode.
   - Add `BuildSourceInstance()` for the save flow.
   - If the protocol has per-instance items, compose a `FooItemValidator` static class per Rule 2.

2. **Define the wizard razor** at `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddFooSource.razor`:
   - Wrap content in `<WizardShell Title="Add Foo source" Icon="..." BackHref="/sources/new">`.
   - Number each section: `<WizardSection Index="1" Title="Identity">...</WizardSection>`.
   - Add `<WizardValidationBanner Messages="_model.Validate()" OnMessageClick="OnValidationClick" />` after the header / before sections.
   - Add `id="field-{anchor}"` to each validatable input via `UserAttributes`.
   - Footer: `<WizardActions OnSave="OnSave" OnCancel="OnCancel" OnTestConnection="OnTestConnection" CanSave="_model.CanSave()" />`.
   - Accept `[Parameter] EditMode` + `[Parameter] HydratedConfig`; in edit mode, hydrate once in `OnParametersSet`.

3. **Register the route** by adding a `@page "/sources/foo/new"` directive to the razor file, and updating `SourceEditRouter.razor`'s protocol dispatch + `RegisteredSourceProtocols` set.

4. **Add a picker card** to `ChooseSourceProtocol.razor`: a `MudCard` with the protocol's name, icon, brief description, and `Href="/sources/foo/new"`.

5. **Wire the per-instance validator** (if Rule 2 applies). Adapter's `ValidateConfigAsync` and the wizard model's per-row validator both call `FooItemValidator.Validate(item, pathPrefix, errors)`.

A new wizard that completes these five steps satisfies the ADR-0015 contract. Code review against this ADR rather than against six other wizards.

## Cross-references

- ADR-0002: Configuration is the inventory truth — wizard saves write to config, the inventory of intent.
- ADR-0008: "Destinations" not "Sinks" in operator-facing UI — wizard copy and route labels honour this.
- ADR-0014: Configuration state and runtime state are distinct surfaces — wizard touches config only; probe reads runtime without mutating it.
- M.2d.1 shared primitives plan: `docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md`
- M.2d.4 v2.1 plan: `docs/sessions/2026-05-27-m2d4-cross-wizard-sweep-plan-v2.1.md`
- **Multi-protocol pilot expansion v2.1 (Layer 5 amendment source): `docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md` §1.3 / §4**
- **Multi-protocol pilot expansion kickoff: `docs/sessions/2026-05-29-multi-protocol-pilot-expansion-kickoff.md`**
- Cross-wizard consistency audit checklist: `tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md`
- Canonical per-instance validator example: `src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusTagValidator.cs`
