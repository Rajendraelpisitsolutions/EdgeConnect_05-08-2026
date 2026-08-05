# M.2b.6.1 — Inline Enable/Disable for Sources / Sinks / Routes (kickoff)

**Status:** **QUEUED** — next milestone after M.2b.6 merges
**Date:** 2026-05-19
**Form:** Kickoff / queueing note. v1 plan, ChatGPT review, v2/v3 cadence follow in the next session per the project's plan-trail discipline.

---

## 0. Why this milestone

**Smoke-driven trigger.** During M.2b.6 manual testing (2026-05-19), the user attempted the end-to-end wizard flow on a fresh gateway with no prior config:

1. Add Source → "Do not wire yet" → source lands `Enabled = false`.
2. Add Destination → "Do not wire yet" → sink lands `Enabled = false`.
3. Add Route → merger rejects because Core's startup invariant requires an enabled route to reference an enabled source.

The operator is stuck. Today the only fix is editing `gateway.json` directly to flip the `enabled` flags. That breaks the "everything via Studio UI" promise the wizard family makes.

There IS one wizard-only sequence that works (Destination first → Source with "Create new route now" picks the disabled sink → source + route land enabled, sink still disabled → manual JSON edit to flip the sink). But that's brittle and operator-hostile.

**User quote** (2026-05-19): "If Source, destination or route is disabled. How to make it enabled? I think still we didn't implement the Edit option... Now its becoming pain if we create one without other, it goes to disabled mode. Otherwise we have to follow the sequence in creating these three. It will be difficult to enforce the customer."

---

## 1. Scope (locked at kickoff)

**In scope:**

- A small **Enable/Disable toggle** on each row of the Sources / Sinks / Routes list pages.
- A management API endpoint that flips the `Enabled` flag on a single instance via the standard draft → apply round-trip.
- Cross-record validation: enabling a source/sink/route that would violate Core's startup invariant returns a clear inline error (e.g. "Cannot enable this route because its source is disabled — enable the source first").
- The minimum UX to avoid the painful sequence problem at first-gateway setup.

**Out of scope (deferred to M.2d Edit-via-Wizard):**

- Editing any field other than `Enabled`. Renaming, retyping protocol settings, changing connection details, modifying transforms — all defer to M.2d.
- Bulk enable/disable across multiple rows (deferred to M.2e Shared List Infrastructure).
- Confirmation dialogs for high-impact disables (e.g. disabling a production route). Maybe add as a Locked decision in v1 plan; keep MVP simple.

---

## 2. Position in the roadmap

Inserts between **M.2b.6** (Destination Wizard, just shipped) and **M.2c** (Live Tag Watch + Runtime Tap, was queued as "START NEXT").

```
... M.2b.6 Destination Wizard      [shipped, PR #10]
    ↓
    M.2b.6.1 Inline Enable/Disable   ⭐ NEW — START NEXT
    ↓
    M.2c     Live Tag Watch + Runtime Tap
    ↓
    M.2d     Edit-via-Wizard (full edit; supersedes M.2b.6.1's surface)
    ↓
    ...
```

Rationale for inserting here rather than rolling into M.2d:

- **Time-to-fix.** Operators hit this gap on first-gateway setup TODAY. M.2d is a multi-week milestone. The toggle is days.
- **Scope hygiene.** The full Edit-via-Wizard requires designing edit-mode for every wizard (Modbus / FOCAS2 / MQTT / OPC UA Server / Route). That's its own design problem. A narrow Enable toggle has none of that complexity — it's a single boolean field on three list pages.
- **Forward-compat.** M.2b.6.1 doesn't preclude or constrain M.2d. When M.2d lands, the toggle can either stay (quick action) or be subsumed by the full edit UI.

---

## 3. Sketched deliverables (for v1 plan to refine)

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Api/EnableDisableApi.cs` *(new)* | POST `/api/v1/sources/{id}/enable`, `/disable`; same for sinks + routes. Pure draft-creation endpoint — same flow Add wizards use; just toggles one field. |
| `src/ElpisEdgeConnect.Management/Wizards/EnableDisablePlanner.cs` *(new)* | Pure function: current config + target instance + desired Enabled state → draft GatewayConfiguration. Validates cross-record constraints (can't enable a route whose source is disabled, etc.). Mirrors `WizardConfigMerger` patterns. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Sources.razor` *(edit)* | Add a toggle column / row action. Disabled state → "Enable" button; enabled state → "Disable" button. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Sinks.razor` *(edit)* | Same. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Routes.razor` *(edit)* | Same. |
| `tests/ElpisEdgeConnect.Management.Tests/EnableDisablePlannerTests.cs` *(new)* | ~10 tests: happy paths (enable each kind), cross-record rejection (enable route over disabled source, etc.), idempotency (enabling an enabled thing is a no-op). |
| `tests/ElpisEdgeConnect.Management.Tests/EnableDisableApiTests.cs` *(new)* | ~5 tests for status mapping. |

Estimate: ~600 LOC + ~150 LOC test. One session of focused implementation.

---

## 4. Open questions for v1 plan

| # | Question |
|---|---|
| Q1 | **Snackbar feedback vs inline state.** When the operator clicks Enable on a Source, do we (a) snackbar "Draft created — Validate then Apply" mirroring the Add wizards, or (b) auto-validate-and-apply for this narrow toggle case since the change is trivial? Option (b) is more operator-friendly but breaks the draft-then-apply discipline of every other config change. |
| Q2 | **Confirmation on Disable.** Should disabling an enabled route prompt a confirmation dialog ("This will stop data flow from X to Y. Continue?")? MVP-simple = no. Operator-friendly = yes for routes only (disabling a source/sink while routes still reference them is already cross-record-rejected). |
| Q3 | **Cascade on Disable.** If the operator disables a source, do its dependent routes also auto-disable? Today: no — Core's startup validator would refuse the draft. The toggle's planner could either (a) refuse with a clear error ("disable the dependent routes first") or (b) offer a "Disable source and its 2 dependent routes" cascade button. (a) is safer; (b) is more operator-friendly. Probably (a) for MVP. |
| Q4 | **Endpoint naming.** REST nouns vs verbs: `PATCH /sources/{id}` with body `{enabled: true}` vs `POST /sources/{id}/enable`. Current Management API leans toward verb-based endpoints (e.g. drafts have `/validate` and `/apply`). Verb-based probably wins for consistency. |
| Q5 | **List-page UX for the toggle.** MudSwitch inline in the row vs MudIconButton in the action column vs a small menu? Mockup needed at v1 plan time. |

---

## 5. Cadence (next session)

1. v1 plan — covers §1-4 above with locked decisions + DoD checklist.
2. ChatGPT review pass on v1.
3. v2 amendment folding ChatGPT feedback.
4. Optional Step 1 reality check (probably skip — scope is small enough that surprises are unlikely).
5. v3 lock.
6. Implementation.

---

**End of M.2b.6.1 kickoff. v1 plan starts in next session.**
