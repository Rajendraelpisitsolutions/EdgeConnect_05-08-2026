# ADR-0014: Configuration state and runtime state are operationally distinct surfaces — never collapsed

**Status:** Accepted (2026-05-19)
**Date:** 2026-05-19
**Milestone:** M.2b.6.1 (locked as part of this milestone; generalises to M.2c Runtime Tap, M.2d Edit-via-Wizard, future Operational Explainability)
**Framing:** "Is this thing supposed to be running?" (configuration state) and "IS it running?" (runtime state) are two independent operational questions. The Studio surfaces both. No UI element ever collapses them into a single chip / toggle / state machine.

## Context

M.2b.6.1 introduced a row-level Enable/Disable button on the Sources / Sinks / Routes list pages. The list pages already carried a **Status column** representing runtime adapter health (`Running`, `Degraded`, `Stopped`, `Faulted`, `Draining`, ...).

A naive implementation would render a single MudSwitch in the Status column, conflating the two concepts. v2 §4 surfaced the trap:

- Config `Enabled=true` + Runtime healthy → green chip ✓
- Config `Enabled=true` + Runtime faulted → red chip (the most common remediation gesture: operator clicks Disable on a faulted, config-enabled row)
- Config `Enabled=false` + Runtime stopped → grey chip ✓
- Config `Enabled=false` + Runtime still draining → amber transitional chip (mid-shutdown; toggle must be guarded against thrash)

A single state widget cannot represent all four cases coherently. Industrial OT operators reason about these as two distinct concerns — this is how PLC and SCADA tooling have worked for 30 years — and collapsing them would hide important operational information.

The principle generalises beyond M.2b.6.1:

- **M.2c Runtime Tap** will surface live event streams. The Tap is the runtime-state surface; the Config page is the config-state surface. They must not be sutured together.
- **M.2d Edit-via-Wizard** will edit config fields. The edit drawer must NOT also drive runtime state.
- **A future Operational Explainability surface** depends on the two surfaces remaining independently queryable: "show me everything that's config-enabled but runtime-faulted" only works if the two are distinct columns.

## Decision

**Configuration state and runtime state are rendered, queried, and reasoned about as two distinct surfaces.** Specifically:

1. The list pages carry **two coexisting columns**:
   - **Status column** — informational, non-interactive, soft-fill chip, runtime telemetry.
   - **Action column** — mutating control, MudButton, configuration state. `Enable` when config-disabled; `Disable` when config-enabled.

2. The two columns **may diverge intentionally**; the divergence is a feature, not a bug. A faulted-but-config-enabled row showing a red Status chip and an outlined `[Disable]` action button is the most common remediation gesture surface.

3. The drain-state transition is the only case where the two surfaces interact: while a sink/route is mid-disable, the Action button renders disabled with a `"Drain in progress. Wait for stop to complete."` tooltip (Locked I). This is NOT a collapse — the Action column still owns the toggle, the Status column still owns the runtime state; the Action button is just temporarily uninteractable.

4. **Future surfaces that touch either dimension MUST honour this separation.** M.2c Runtime Tap's event stream is runtime-only. M.2d Edit-via-Wizard's edit drawer is config-only. Any proposed surface that would render or mutate both in a single widget requires an ADR amending this one.

## Reasoning

1. **Operator mental model.** Industrial operators have spent decades learning the two-column model from PLC, SCADA, DCS tooling. Importing the model gives EdgeConnect a UX baseline that doesn't need explaining.

2. **Audit chain meaning.** Config-state changes write audit entries; runtime-state observations do not. Collapsing the two would either (a) emit audit entries for every runtime transition (overwhelming the chain) or (b) hide config-state changes inside runtime-state telemetry (loses explainability). Both regress P4.

3. **Diagnostic value.** "Show me everything config-enabled but runtime-faulted" is the canonical commissioning query. It's only answerable if the two dimensions are independently queryable.

4. **Forward-compat with Runtime Tap (M.2c).** The Tap is a platform capability (ADR-pending, roadmap v2 Locked A). It carries live event streams. The Tap's consumers (Watch UI, MQTT payload inspector, OPC UA node viewer) ALL render runtime state. None render config state. Keeping the two distinct surfaces today means M.2c doesn't need to redesign the list pages.

5. **Anti-pattern protection.** A future contributor seeing a faulted-but-config-enabled row and thinking "the Disable button should be greyed out because the adapter is already broken" would collapse the two surfaces and lose the remediation gesture (disabling a faulted adapter is exactly the action the operator wants to take). The two-column model prevents this misreading.

## Consequences

- **List pages render two columns.** `Sources.razor` / `Sinks.razor` / `Routes.razor` all carry both Status (existing) and Action (M.2b.6.1, new). Removing or merging them requires ADR amendment.

- **The Action button's `Enabled` flag is driven by config state, not runtime state.** A faulted-but-config-enabled row shows `[Disable]` enabled. A drain-state row shows the disabled button with tooltip; this is a transitional UI guard, not a state-collapse.

- **Runtime Tap (M.2c) inherits this separation.** The Tap surfaces runtime events only. The Config page (existing) surfaces config history only. The Studio header / nav reinforces "configuration" vs "runtime" as the top-level information architecture.

- **Edit-via-Wizard (M.2d) inherits this separation.** The edit drawer mutates config fields. It does NOT also drive runtime state (no "restart adapter" button inside the edit drawer; that's a separate operator action with its own audit semantics, deferred to a separate milestone).

- **Future Operational Explainability** can compose the two: "for each config change, render the runtime-state transitions that followed." This is additive, not collapsing — the two surfaces remain primary.

- **Any UI proposal that would render both in a single widget** (toggle that flips config AND restarts, chip that shows config-and-runtime as one color) is OUT OF SCOPE and requires an ADR amending this one.

## Out-of-scope follow-ups

- **Restart-adapter button.** A future milestone (M.2c+ TBD) may add a "Restart" action that mutates runtime state without changing config. That's a separate Action column row item and a separate ADR.

- **Combined "Disabled in config" badge on the Status column.** Operators sometimes ask "why is this row stopped?" — a small "config-disabled" badge on the Status chip could answer that. Deferred; if added, must not be a clickable control (config mutation remains in the Action column).

- **"Show only config-faults / runtime-faults" filters.** Belongs to M.2e Shared List Infrastructure's filtering primitive.

## References

- M.2b.6.1 v2 amendment: [`docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v2.md`](../sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v2.md) §4 Locked H + §4.x Locked I
- M.2b.6.1 v3 amendment: [`docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md`](../sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md) §2 Locked K (styling discipline pinning the two surfaces visually)
- Implementation handoff: [`docs/sessions/2026-05-19-mp2b61-implementation-kickoff.md`](../sessions/2026-05-19-mp2b61-implementation-kickoff.md) §6
- ADR-0007 — Display precedence: Disabled beats Faulted (companion runtime-state ordering rule)
- ADR-0009 — Runtime hot-reload instance granularity (reload classifier sees config-state changes; runtime-state stays separate)
- ADR-0010 — Coordinator synthesises cross-record recovery (runtime-state surface synthesis)
- Platform principles P4 (explainability data path) — depends on the two surfaces remaining independently queryable
- Post-M.2b.6 roadmap v2 Locked A (Runtime Tap as a platform capability) — explicit consumer of this ADR
