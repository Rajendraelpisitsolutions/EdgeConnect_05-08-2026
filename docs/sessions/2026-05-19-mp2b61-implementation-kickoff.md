# M.2b.6.1 — Implementation kickoff handoff

**Status:** Plan trail locked. Implementation **not yet started.** Awaiting fresh session.
**Date:** 2026-05-19
**Form:** Session-to-session handoff per CLAUDE.md §2 discipline. Captures plan-trail completeness, environmental prerequisites, scope-confirmation gate, and the implementation guardrails carried from the v2/v3 review passes.

---

## 1. Plan trail status — LOCKED, no further amendment expected

| # | Document | Status |
|---|---|---|
| 1 | [Kickoff](2026-05-19-mp2b6-1-inline-enable-disable-kickoff.md) | LOCKED (committed at `cc26fd3` via PR #11) |
| 2 | [v1 plan](2026-05-19-mp2b61-inline-enable-disable-plan.md) | LOCKED (uncommitted — see §3 below) |
| 3 | [v2 amendment](2026-05-19-mp2b61-inline-enable-disable-plan-v2.md) | LOCKED (uncommitted) |
| 4 | [v3 amendment](2026-05-19-mp2b61-inline-enable-disable-plan-v3.md) | LOCKED (uncommitted) |
| 5 | This handoff | LOCKED on write |

> **No further amendment expected before implementation.** v3's review pass produced explicit verdict: "implementation-grade." If implementation reveals an unexpected MudBlazor primitive constraint, an unexpected reload-state timing behaviour, or a cross-record validator gap, that becomes a **v4 amendment** — not silent in-flight scope expansion.

The plan trail is the authoritative scope record. Anything the implementer is tempted to "just clean up while I'm here" that isn't in the deliverables list is out of scope by construction.

---

## 2. Required read order (do not skip; do not reorder)

The plan layers — each version assumes the prior. Reading only v3 misses load-bearing context:

1. **`docs/sessions/2026-05-19-mp2b6-1-inline-enable-disable-kickoff.md`** — motivation, the smoke-driven trigger scenario (§0), scope locked at §1, the five open questions §4
2. **`docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan.md`** (v1) — Locked A–E resolving Q1–Q5, the file-by-file deliverables sketch §4
3. **`docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v2.md`** — Locked F, G, H, I + refinements A.1, C.1; the `EnableDisableImpactSummary` structural simplification (§6); updated deliverables (§7); updated DoD (§8)
4. **`docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md`** — Locked J, K, K.1, L, M, N, O; consolidated DoD additions (§7)
5. **This handoff** — implementation-time discipline + environmental prerequisites + the scope-confirmation gate

Then, **and only then**, begin implementation work.

Cross-referenced governance documents (read as needed during implementation, not before):

- `CLAUDE.md` — §3 architectural locks, §7 working conventions (commit discipline, file-header format), §9 anti-patterns
- `docs/platform-principles.md` — P2 (shared primitives), P4 (explainability), P6 (operational product)
- `docs/decisions/0007-display-precedence-disabled-beats-faulted.md` — directly relevant to Locked H's two-column model
- `docs/decisions/0009-runtime-hot-reload-instance-granularity.md` — toggle is a `Modified` classification
- `docs/decisions/0010-coordinator-synthesizes-cross-record-recovery.md` — interaction with cross-record validator

---

## 3. Branch and commit state at handoff

Current worktree: `C:\dev\EdgeConnect\.claude\worktrees\zen-goldberg-48f89a`
Current branch: `claude/zen-goldberg-48f89a`
HEAD: `7ac6892` (M.2b.6 destination wizards merged via PR #10)

**Three uncommitted files on this branch** (the v1, v2, v3 plan docs):

```
?? docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan.md
?? docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v2.md
?? docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md
```

Plus this handoff file once written.

### Implications for the next session

- **If the implementer starts a fresh worktree off `master`**, these plan files are NOT visible there. They need to either:
  - (a) Wait for the user to commit + push these files (the plan-trail commit can land separately from the implementation PR)
  - (b) Check out branch `claude/zen-goldberg-48f89a` (or copy the files across)
- **If the implementer continues on this branch**, the plan files are already present; commit them as part of (or before) the implementation PR per the user's discretion

The user controls commit cadence per CLAUDE.md §7. **Do not commit the plan files without explicit user instruction.** Suggested commit message shape when the user authorises:

```
docs(sessions): plan trail for M.2b.6.1 — Inline Enable/Disable (v1 → v3)
```

---

## 4. Scope summary (canonical, do not paraphrase outward)

**In scope:** A row-level **Enable/Disable affordance** on `Sources.razor`, `Sinks.razor`, `Routes.razor` lists, backed by six verb-style API endpoints and a pure planner that flips the `Enabled` flag on a single named entity through the standard draft → validate → apply pipeline. Cross-record validation prevents incoherent state. Multi-operator stale-view collisions are detected. No-op double-clicks suppressed. Runtime state and config state remain operationally distinct surfaces.

**Out of scope (Locked deferrals, do not re-litigate):**

| Deferral | Goes to |
|---|---|
| Editing any field other than `Enabled` | M.2d Edit-via-Wizard |
| Bulk multi-row enable/disable | M.2e Shared List Infrastructure |
| One-click cascade (auto-disable dependents) | Explicitly NOT in any current milestone |
| Confirmation dialogs with audit-comment capture | M.2d |
| Toggle for sub-entities (filter rule, individual transform) | Not a current milestone |
| Retrofit stale-view protection to existing Add wizards | M.2d kickoff input |
| Fleet-mode multi-gateway transactional apply | Milestone K / M.2k |
| Full keyboard workflow across grids | M.2e |

> Anything the implementer touches that isn't in the in-scope sentence above is silent scope expansion. If it feels necessary mid-implementation, **stop and produce a v4 amendment** — do not just absorb it into the implementation PR.

---

## 5. Planner purity and four-layer separation (LOAD-BEARING)

This is the single most important implementation discipline carried from the v3 review pass. The temptation during implementation will be to make the planner "smart" — emitting metrics, reading runtime state, formatting copy. **Resist that temptation.**

The layering is locked:

```
┌─────────────────────┬────────────────────────────────────────────────────┐
│ Layer               │ Responsibility                                     │
├─────────────────────┼────────────────────────────────────────────────────┤
│ planner             │ Deterministic config reasoning. Pure.              │
│                     │ Input: current config + (kind, id, desiredEnabled) │
│                     │ Output: EnableDisablePlanResult record             │
│                     │ NO I/O, NO metrics, NO UI copy, NO runtime reads,  │
│                     │ NO side effects, NO logging.                       │
├─────────────────────┼────────────────────────────────────────────────────┤
│ API endpoint        │ Orchestration + ordering per Locked G:             │
│                     │   StaleView → NoOp → CrossRecord → Validate → Apply│
│                     │ Response-envelope mapping (Applied / NoOp /        │
│                     │   Conflict).                                       │
│                     │ Telemetry counter emitted HERE (Locked M).         │
│                     │ HTTP status code selection.                        │
├─────────────────────┼────────────────────────────────────────────────────┤
│ drawer / model      │ Operator interaction state machine.                │
│                     │ Owns snackbar text, stale-view banner pivot,       │
│                     │ loading lock per Locked O, focus-query deep-link   │
│                     │ rendering per Locked C.1, drain-state UI per       │
│                     │ Locked I.                                          │
├─────────────────────┼────────────────────────────────────────────────────┤
│ telemetry           │ Boundary concern.                                  │
│                     │ One Counter<long>, four dimensions (Locked M).     │
│                     │ Lives at the API boundary, not inside any layer    │
│                     │ above.                                             │
└─────────────────────┴────────────────────────────────────────────────────┘
```

### Concrete protection: planner-purity guard test

Add to `EnableDisablePlannerTests.cs` an explicit test asserting the planner's assembly graph does NOT include `Microsoft.Extensions.Logging`, `System.Diagnostics.Metrics`, `IDiagnosticsService`, or `Microsoft.AspNetCore.Http` symbols from a planner-only call. The test imports the planner's namespace + dependencies and fails if any of these boundary types are reachable. This is defence-in-depth on top of the discipline.

### Concrete protection: planner file header

The planner's file header (CLAUDE.md §7 mandates this for every source file in Core; we honour it here for Management as a discipline) MUST include:

```
// File: Wizards/EnableDisablePlanner.cs
// Purpose: Pure planner for Enable/Disable inline toggle (M.2b.6.1).
//          Produces an EnableDisablePlanResult from current configuration
//          + (entity kind, instance id, desired Enabled state).
//
//          LAYER DISCIPLINE (Locked, v3 review):
//             * planner = deterministic config reasoning. No I/O, no
//                         metrics, no UI copy, no runtime-state reads,
//                         no side effects, no logging.
//             * API     = orchestration + ordering + telemetry.
//             * model   = operator interaction state.
//             * telemetry = boundary concern at API layer.
//          If you find yourself wanting the planner to read runtime
//          state, format user-facing copy, or emit a metric — STOP.
//          That work belongs in the API or model layer.
//
// Reference: docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md
//            docs/sessions/2026-05-19-mp2b61-implementation-kickoff.md §5
// ============================================================================
```

---

## 6. ADR deliverables (both must land)

Both ADRs are part of the milestone DoD. Implementer drafts both during implementation; user reviews before merge.

| ADR | Subject | Source |
|---|---|---|
| **ADR-0013** *(next sequential)* | **Inline Enable/Disable refuses cascade — explicit dependency list, no auto-disable of dependents** | v1 Locked C, escalated for ADR-worthiness because the no-cascade discipline generalises beyond M.2b.6.1 to any future bulk/multi-state operation |
| **ADR-0014** *(next sequential after 0013)* | **Configuration state and runtime state are operationally distinct surfaces — never collapsed** | v2 Locked H + Locked I, escalated because the principle anchors M.2c Runtime Tap, M.2d Edit-via-Wizard, degraded-state overlays, and the future Operational Explainability milestone |

ADR sequence check at handoff time: `docs/decisions/` currently ends at `0012-focas2-demo-mode.md`. **Next sequential numbers are 0013 and 0014.** If a parallel session lands an ADR between handoff and implementation, renumber accordingly.

### ADR content discipline

Each ADR ≤ 100 lines. Sections: **Context** (the operational gap or temptation the lock prevents), **Decision** (the rule in one paragraph), **Reasoning** (why this lock and not the alternatives), **Consequences** (what changes for future milestones / what is now off the table).

Cross-reference the v1/v2/v3 plan files in the ADR's reference footer so the ADRs don't drift from their source-of-truth.

---

## 7. Smoke passes (both required before milestone close)

Both passes happen in the Studio at **127.0.0.1:5080** (per memory note `reference_studio_url.md`).

### Smoke 1 — Fresh-gateway scenario (the kickoff §0 trigger)

Replays the operator-facing scenario that motivated the milestone:

1. Start from a gateway with `current.json` containing zero sources / zero routes / zero sinks (or use `EDGECONNECT_FOCAS2_FAKE_MODE=1` to skip needing real hardware)
2. Add a source via the Source wizard with **"Do not wire yet"** — source lands `Enabled=false`
3. Add a destination via the Destination wizard with **"Do not wire yet"** — sink lands `Enabled=false`
4. Add a route via the Route wizard — route lands `Enabled=true`
5. Routes page now shows route disabled (because Core's startup invariant blocked it)
6. Operator clicks `[Enable]` on the source row → confirm drawer opens → operator confirms → snackbar `"Source 'X' enabled."` — drawer dismisses
7. Operator clicks `[Enable]` on the sink row → same flow → snackbar success
8. Operator clicks `[Enable]` on the route row → drawer shows route impact panel (per Locked B) → confirm → success
9. Verify hot reload picked up all three changes (status chips transition to Healthy as adapters come up)

**Pass criteria:** zero JSON editing required to recover the disabled-after-creation scenario. Three button clicks + confirmations.

### Smoke 2 — Two-tab stale-view scenario (Locked G validation)

Verifies the stale-view detection introduced by Locked G end-to-end:

1. Open Studio in Tab A, navigate to `/sources`. Note the polled state of a source (say `Enabled=true`).
2. Open Studio in Tab B (separate browser window / private window), navigate to `/sources`. Disable the source via the action button. Tab A's row has NOT yet repolled.
3. In Tab A, click `[Disable]` on the same source (Tab A still believes it's enabled).
4. **Expected:** Tab A's request returns 409 `CONFIG.STALE_VIEW`. Confirm drawer pivots to the stale-view banner per Locked O. The primary button text becomes **Refresh**.
5. Click Refresh. Tab A's list re-polls, the row now shows `Enabled=false`, the drawer dismisses.
6. **Expected:** no audit-record id was generated for Tab A's stale request. No metric incremented in the `applied` outcome bucket; one increment in the `stale_view` outcome bucket.

**Pass criteria:** the operationally misleading inverted-stale case is caught and the operator is told their view was stale — not that they performed a no-op.

---

## 8. Implementation gate — re-confirm scope at session start

The user's standing rule ("Confirm scope with me before writing any code") applies to the implementation session, not just the planning sessions. When the implementer picks up this handoff:

1. Read the plan trail (§2 above) end-to-end
2. Read this handoff in full
3. **Re-state scope summary in their own words to the user** and explicitly ask: "May I proceed with implementation as scoped?"
4. **Wait for affirmative confirmation.** No code, no file edits, no `dotnet new`, no test scaffolding before the user's explicit OK
5. Only then begin work, following the consolidated deliverables list (v1 §4 + v2 §7)

This is a hard gate, not a soft suggestion. Bypassing it would invalidate three days of plan-trail governance work.

---

## 9. Environment prerequisites (checked at session start)

Per CLAUDE.md §8 development-environment notes:

- **.NET 8 SDK** must be installed. Verify via `dotnet --list-sdks`. If missing, install from `https://dotnet.microsoft.com/download` before starting.
- **Mosquitto MQTT broker** running on `localhost:1883` (anonymous). Needed for the existing MQTT integration tests to stay green. M.2b.6.1 does NOT introduce new MQTT-touching code, but the baseline test suite must remain green before AND after implementation.
- **Studio binding** at `127.0.0.1:5080` (per memory `reference_studio_url.md`).
- **Baseline build/test green check** before any code change — implementer runs:
  ```bash
  dotnet build ElpisEdgeConnect.sln
  dotnet test --filter "Category!=Flaky"
  ```
  Both must produce 0 warnings, 0 errors, all tests green. If they don't, the implementer flags it to the user **before** modifying anything — broken baseline is a pause-and-report event, not a "fix while I'm here" event.

---

## 10. Anti-silent-scope-expansion principle

Locked from the v3 review pass:

> The implementation must not silently expand scope. Any tradeoff surfaced during implementation that isn't covered by the v1/v2/v3 plan trail produces a **v4 amendment file**, not a quiet absorption into the implementation PR.

Examples of what would be silent scope expansion (do NOT do these without a v4):

- "I'll just add a `Pause` action while I'm building Enable/Disable" — no. Pause is its own concept; M.2b.6.1 ships Enable/Disable only.
- "I'll also fix this unrelated minor bug in `Sources.razor`" — no. Surface it via a separate task chip or a follow-up PR.
- "The drawer would feel better as a dialog after all" — no. v3 Locked J is final; if implementation reveals a MudDrawer constraint, that's a v4 amendment input.
- "I noticed the existing Add wizards lack stale-view protection — let me fix that too" — no. v2 explicitly flagged this as M.2d's scope.
- "I'll add a tooltip explaining the difference between Status chip and Action button" — borderline; if it's literally one MudTooltip line per page, it's fine. If it grows to a HelpOverlay component, it's scope expansion.

When in doubt: **pause, surface, ask.** The user has been clear that silent simplification AND silent expansion are both anti-patterns.

---

## 11. Quick reference: deliverable files (cross-reference back to plans)

For the implementer's quick lookup. Authoritative list is v1 §4 + v2 §7 deltas.

### Production code

| File | New / Edit | Locked decisions touched |
|---|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/EnableDisablePlanner.cs` | new | F (no-op), A.1 (atomicity wording in header), Layer discipline (§5 above) |
| `src/ElpisEdgeConnect.Management/Api/EnableDisableApi.cs` | new | D (verb endpoints), G (stale-view + ordering), M (telemetry) |
| `src/ElpisEdgeConnect.Management/Contracts/EnableDisableRequestDto.cs` | new | G (`expectedConfigurationVersion`) |
| `src/ElpisEdgeConnect.Management/Contracts/EnableDisableResponseDto.cs` | new | F (NoOp), G (Conflict), D (Applied) — discriminated by `outcome` |
| `src/ElpisEdgeConnect.Management/Components/Shared/EnableDisableConfirmDrawer.razor` | new | J (MudDrawer), N (copy), O (loading discipline) |
| `src/ElpisEdgeConnect.Management/Components/Shared/EnableDisableConfirmDrawerModel.cs` | new | F (skip-on-noop), G (refresh pivot), I (drain-state), O (loading) |
| `src/ElpisEdgeConnect.Management/Components/Pages/Sources.razor` | edit | E (action column), H (two-column coexistence), K + K.1 (styling), C.1 (focus query) |
| `src/ElpisEdgeConnect.Management/Components/Pages/Sinks.razor` | edit | Same |
| `src/ElpisEdgeConnect.Management/Components/Pages/Routes.razor` | edit | Same; Locked B (impact panel) for route disable |

### Tests (~60 total)

See v2 §7 + v3 §1–6 test-delta tables. Highlights:

- `EnableDisablePlannerTests` — purity guard test (§5 above), no-op outcomes, cross-record refusals, idempotency, impact summary correctness
- `EnableDisableApiTests` — six endpoints, ordering test (StaleView before NoOp), four-dimensional metric assertion, status-code mapping
- `EnableDisableConfirmDrawerModelTests` — loading discipline (Locked O), stale-view pivot, drain-state guard, error-copy match
- List-page POCO tests — action button styling, focus-query read, drain-state disabled button

### ADRs

- `docs/decisions/0013-inline-enable-disable-refuses-cascade.md`
- `docs/decisions/0014-config-state-vs-runtime-state.md`

### Handoff at milestone close

- `docs/sessions/<YYYY-MM-DD>-mp2b61-close-handoff.md` capturing: what shipped, smoke pass results, follow-up flags (M.2d stale-view retrofit, etc.)

---

## 12. End-of-handoff

This document is the implementation session's entry point. Following the plan trail + this handoff + the standing scope-confirmation rule, the implementation should land cleanly inside the kickoff envelope (~600 LOC + ~150 LOC tests + 2 ADRs + 2 smoke passes).

**The plan trail is closed. The implementation gate is the user's explicit go-ahead. Until that arrives, no code.**

---

**Handoff written 2026-05-19. Next session — read the plan trail in §2 order, then re-prompt the user for scope confirmation per §8.**
