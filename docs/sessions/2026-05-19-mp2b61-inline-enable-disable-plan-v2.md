# M.2b.6.1 — Inline Enable/Disable v2 amendment

**Status:** **v2 LOCKED** — ChatGPT strategic review folded in. v3 reality-check is optional given milestone size; default is to skip and proceed to v3 lock unless v2 exposes a question worth a Step-1 reality pass.
**Date:** 2026-05-19
**Form:** **TIGHT AMENDMENT** to the [v1 plan](2026-05-19-mp2b61-inline-enable-disable-plan.md). v1 remains load-bearing for everything not amended here. v2 adds operational semantics that v1 left implicit, simplifies one over-factored deliverable, and tightens documentation hygiene around the word "atomic."

**Inputs to this amendment:**
- ChatGPT strategic review pass on v1 (2026-05-19) — endorsed Q1/Q3 resolutions, scope discipline, forward-compat framing; pushed back on five operational-semantics gaps and one over-factoring concern
- [v1 plan](2026-05-19-mp2b61-inline-enable-disable-plan.md) — Locked A–E + §7 open items
- [Kickoff](2026-05-19-mp2b6-1-inline-enable-disable-kickoff.md) — scope unchanged

---

## 0. What changed v1 → v2 (delta summary)

Five operational-semantics gaps closed + one over-factored deliverable removed:

| # | Theme | New Lock | Status vs v1 |
|---|---|---|---|
| 1 | **No-op suppression** — operator clicks Enable on an already-enabled entity | Locked F (new) | Adds runtime policy v1 hinted at via "idempotency tests" but never formalised |
| 2 | **Optimistic concurrency guard** — stale-view detection between operators | Locked G (new) | Closes a real multi-operator collision gap; v1 didn't address |
| 3 | **"Atomic" wording clarification** — UI interaction atomicity ≠ distributed transactional atomicity | Locked A.1 refinement | Doc hygiene; tightens Locked A wording without changing its semantics |
| 4 | **Runtime semantics scope statement** — this milestone modifies CONFIG state only | Locked H (new) | One-sentence clarification preventing "Enabled == healthy" misreading |
| 5 | **Deep-link format** — dependent-list links use query-parameter / row-anchor form, not assume route-name path coupling | Locked C.1 refinement | Minor; prevents M.2e shared-grid friction |
| — | **Architectural simplification** — fold `EnableDisableImpactSummary` into the planner result DTO | Deliverables delta | One fewer file; one fewer abstraction layer; planner result becomes the single source of impact metadata |

Everything else in v1 stays. **Q1–Q5 resolutions (Locked A–E) are unchanged.** Scope is unchanged. Cadence is unchanged.

---

## 1. Locked F — No-op suppression (new)

**Problem v1 left open.** v1's test matrix mentioned idempotency, but never specified the *runtime policy* for "operator clicks Enable on an already-enabled entity." Without an explicit rule the implementation could end up creating empty drafts, emitting meaningless audit entries, triggering reload cycles that move no actual state, and showing success snackbars for non-operations. In a production setting an operator who double-clicks the Enable button would generate two audit records and one no-op reload — both operationally noisy.

### Locked rule

> If the target entity's current `Enabled` value already equals the desired value, the planner returns a structured **`NoOp` outcome**. No draft is created. No validation runs. No apply pipeline fires. No reload event. No audit record is written.

### API surface

The endpoint still returns **200 OK**, but with a distinct envelope shape:

```jsonc
{
  "outcome": "NoOp",
  "reason": "AlreadyInDesiredState",
  "entity": { "kind": "source", "id": "FANUC-01" },
  "currentEnabled": true
}
```

Compare to the successful-apply envelope (Locked D from v1):

```jsonc
{
  "outcome": "Applied",
  "draftId": "draft-2026-05-19-001",
  "validationOutcome": "Passed",
  "appliedAt": "2026-05-19T14:32:11Z",
  "auditRecordId": "audit-7c3a..."
}
```

The two envelopes share an `outcome` discriminator. UI / clients dispatch on `outcome`.

### UI surface

- Snackbar text: `"Source 'FANUC-01' is already enabled."` — **info severity**, not success
- No audit-record id surfaced (because none was written)
- No row state change visually (it's already in the desired state)
- The confirm sheet does NOT open. Click → planner pre-check via lightweight client-side state comparison → if already in desired state, snackbar fires immediately; the sheet flow is skipped entirely

### Test deltas

- New: `EnableDisablePlannerTests.cs` — `EnableEnabledSource_ReturnsNoOpOutcome` (one per entity kind, three tests)
- New: `EnableDisableApiTests.cs` — `Post_AlreadyEnabled_Returns200WithNoOpEnvelope` (one per entity kind)
- New: `EnableDisableConfirmSheetModelTests.cs` — `Open_AlreadyInDesiredState_FiresSnackbarSkipsSheet`

### Why this lock is justified

- **Audit chain stays semantically meaningful.** Every audit record represents a state change. No-op operations don't pollute the chain.
- **No fake reload events.** ADR-0009/0010 reload classifier wouldn't have anything to classify; emitting a Modified entry with identical before/after is misleading.
- **Operator double-click is a normal production behaviour.** Resilience here is operational hygiene, not premature optimisation.

---

## 2. Locked G — Optimistic concurrency guard (new)

**Problem v1 left open.** Two operators sharing a Studio session against one gateway is a normal multi-operator scenario (commissioning engineer + site operator, or two engineers on a maintenance window). v1 never described what happens when:

1. Operator A opens `/sources` — page polls, sees `FANUC-01` as `Enabled=false`
2. Operator B (another tab / another machine) enables `FANUC-01`
3. Operator A's page has not yet repolled — Operator A clicks Enable on the stale row

Without a guard, Operator A's request reaches the server and **Amendment 1's no-op path handles it correctly** (current state already equals desired → NoOp). The audit chain is fine.

But the operationally interesting case is the *inverse*:

1. Operator A sees `FANUC-01` as `Enabled=true` (stale; B just disabled it)
2. Operator A clicks Disable
3. Server reads current state, sees `Enabled=false`, returns `NoOp` — "Source FANUC-01 is already disabled."
4. Operator A is confused — they thought they were disabling it; the snackbar suggests they did nothing

This is a stale-view problem, not a no-op problem. Amendment 1 alone doesn't catch it. Operator A needs to be told **their view was stale**, not that they performed a no-op.

### Locked rule

> Each Enable/Disable request includes the operator's **observed configuration version** (the version the operator's page polled most recently). If that version does not match the gateway's current configuration version, the server returns **409 with code `CONFIG.STALE_VIEW`** and the response includes the current version so the page can refresh.

### Request body (replaces v1's empty body)

```jsonc
{
  "expectedConfigurationVersion": "v-2026-05-19-014"
}
```

The version is the same `ConfigurationVersionId` the Config / Diagnostics pages already track (no new versioning concept introduced). The list-page polling response already includes it (or can be extended trivially to do so).

### 409 response

```jsonc
{
  "outcome": "Conflict",
  "error": {
    "code": "CONFIG.STALE_VIEW",
    "message": "Configuration changed since this page was last refreshed.",
    "expectedVersion": "v-2026-05-19-014",
    "currentVersion": "v-2026-05-19-015"
  }
}
```

### UI surface

- The confirm sheet does NOT apply silently into a stale baseline. If 409 returns, the sheet renders a `"Configuration changed since you opened this page. Refresh to see current state, then retry."` banner with a **Refresh** button.
- After refresh: list re-polls, row state updates to reflect current truth, operator decides whether they still want to act.
- No audit record. No draft creation. No state change.

### Order of evaluation (Locked, important)

The server evaluates checks in this order:

1. **Stale view check** (`expectedConfigurationVersion`) — if mismatch, return 409 `CONFIG.STALE_VIEW` and stop
2. **No-op check** (Locked F) — if current state == desired state, return 200 `NoOp` and stop
3. **Cross-record refusal** (Locked C from v1) — if planner returns blockers, return 409 `CONFIG.CROSS_RECORD_REFUSED`
4. **Validation** — if validator rejects the draft, return 422
5. **Apply** — return 200 `Applied`

Order matters: stale-view BEFORE no-op so Operator A's inverted-stale case (above) gets the correct refresh prompt rather than a misleading no-op.

### Test deltas

- New: `EnableDisableApiTests.cs` — `Post_StaleVersion_Returns409StaleView` (one per entity kind, three tests)
- New: `EnableDisableApiTests.cs` — `StaleAndNoOp_StaleVersionTakesPrecedence` (one test, asserts ordering)
- New: `EnableDisableConfirmSheetModelTests.cs` — `Apply_StaleViewError_ShowsRefreshBanner`
- New: integration test against a 2-tab scenario (mock'd) confirming the refresh path works end-to-end

### Wider implication (flagged, not addressed in M.2b.6.1)

Existing Add wizards (`AddFocas2Source`, the new MQTT/OPC UA destination wizards from PR #10) do not currently enforce stale-view protection. They probably should — multi-operator collision during a long-form wizard session is the same problem at larger scale. **M.2b.6.1 does NOT retrofit this to existing wizards.** The retrofit is in scope for M.2d Edit-via-Wizard (which already extends every wizard's surface area). Flag noted for the M.2d kickoff.

### Why this lock is justified

- Multi-operator commissioning is the dominant production deployment pattern, not the exception
- The version concept is already in the system (`ConfigurationVersionId`) — no new architecture
- 409 STALE_VIEW is the standard ETag pattern, operators familiar with web tooling recognise the semantics
- Adds ~15 LOC to the planner + ~10 LOC to each list page's polling handler; cost is trivial

---

## 3. Locked A.1 — "Atomic" wording clarification (refinement of v1 Locked A)

**Problem v1 left open.** v1 used "atomic" loosely in Locked A ("creates the draft, validates it, and applies it atomically"). A future reader could read this as a distributed transactional guarantee: rollback semantics, all-or-nothing runtime mutation, transactional reload.

That is NOT what's meant. Clarifying.

### Locked clarification

> The word **"atomic"** in Locked A refers to the **operator interaction model**, not transactional atomicity. From the operator's perspective: one click → one outcome (Applied, NoOp, Conflict, RefusedByValidation, or RefusedByCrossRecord). From the system's perspective: the draft/validate/apply steps run sequentially via the existing `ConfigDraftManager` pipeline; each step has its own failure semantics, and if `ApplyAsync` succeeds the runtime reload is asynchronous and observed via existing reload-outcome surfaces (per ADR-0009/0010).

### What this means in practice

- If `ApplyAsync` succeeds but runtime reload partially faults, the **configuration state is durably changed** — the toggle did "happen" from a config perspective. Runtime health is a separate concern (see Locked H below).
- There is **no rollback hook** if reload misbehaves. The operator's remedy is the existing audit-history rollback page.
- Validate + apply do NOT execute under a distributed lock or transaction. They share a single in-process critical section already enforced by `ConfigDraftManager`, which is sufficient for single-gateway deployments. Fleet-mode multi-gateway extension is out of scope for this milestone (and probably for M.2b.6.1 forever — fleet ops live in Milestone K and beyond).

### Documentation hygiene only

This is not a behaviour change. It is a wording fix in the plan, the code-comment template for `EnableDisablePlanner.cs`, and in the file headers per CLAUDE.md §7 Documentation. The implementer should write the planner's file header comment with this exact clarification so any reader 6 months from now doesn't mistake atomicity for transactionality.

---

## 4. Locked H — Configuration vs runtime semantics scope statement (new)

**Problem v1 left open.** The v1 visual sketch in Locked E showed a "State" column with chips like `●Enab.` / `○Disab.`. That column today already exists on `Sources.razor` / `Sinks.razor` / `Routes.razor` and represents **runtime health**, not configuration state. An entity with `Enabled=true` in config can be operationally `Unhealthy` (adapter init failed, sink connection broken, license suspended a module).

Without a clarifying rule, operators may mentally collapse "Enabled" and "Healthy" into one concept, and a future contributor may "fix" the supposed inconsistency by making the toggle button drive the runtime-state column — collapsing two distinct operational signals into one.

### Locked rule

> **M.2b.6.1 modifies configuration state only.** Runtime operational health remains represented by the existing status chip + diagnostics surfaces (and the future Runtime Tap from M.2c). The list-page row carries two coexisting columns:
>
> - **Status column** (existing): runtime health chip — green when healthy, amber when degraded, red when faulted, grey when disabled-and-not-running
> - **Action column** (M.2b.6.1, new): configuration-state action button — "Enable" when config says disabled, "Disable" when config says enabled
>
> The chip and the button can diverge intentionally:
>
> - Config `Enabled=true` + Runtime healthy → chip green, button shows "Disable"
> - Config `Enabled=true` + Runtime faulted → chip red, button shows "Disable" (operator can still disable a faulted entity; that is in fact the most common remediation gesture)
> - Config `Enabled=false` + Runtime not running → chip grey, button shows "Enable"
> - Config `Enabled=false` + Runtime still draining (transition state) → chip amber/grey transitional, button shows "Enable" but disabled while drain is in flight (Locked I, see below for the drain-state UI lock)

### Implication for Sources.razor / Sinks.razor / Routes.razor visual update

The State column is **NOT renamed and NOT changed semantically**. The Action column is appended **after** State. v1's sketch (which left State ambiguous) is superseded by this two-column model.

Updated visual sketch:

```
┌─────────────────────────────────────────────────────────────────────────────────────┐
│ Instance ID  Protocol   Route        Status      Points    Last point   Action      │
├─────────────────────────────────────────────────────────────────────────────────────┤
│ FANUC-01     FOCAS2     spindle-to…  ● Healthy   18,432    2s ago      [Disable]    │
│ FANUC-02     FOCAS2     —            ○ Stopped   —         —           [Enable ]    │
│ HAAS-01      MTConnect  haas-to-er…  ▲ Degraded  2,103     5m ago      [Disable]    │
│ MAZAK-01     FOCAS2     mazak-to-…   ✕ Faulted   —         —           [Disable]    │
└─────────────────────────────────────────────────────────────────────────────────────┘
```

### Locked I (sub-lock of H) — drain-state button disable

While a sink/source is in mid-disable drain (Core's existing graceful-stop semantics), the Action button should render as disabled with a tooltip `"Drain in progress — wait for stop to complete."` This prevents an Enable-Disable-Enable thrash from re-enabling a half-stopped adapter mid-shutdown. Drain duration is bounded by the adapter's stop timeout (already configured in Core).

### Test deltas

- New: `EnableDisableConfirmSheetModelTests.cs` — `Apply_FaultedEntity_DisableStillAllowed` (verifying that runtime faulted state doesn't block a disable apply)
- New: list-page POCO test (per page) — `Render_DrainInProgress_ButtonRendersDisabledWithTooltip`

### Why this lock is justified

- Industrial operators reason about config-state and runtime-state as two distinct concerns (this is how PLC and SCADA tooling has worked for 30 years)
- Collapsing them would hide important operational information (a healthy-looking row with config `Enabled=false` would mislead operators)
- Preserves P4 explainability: the two columns together answer "is this thing supposed to be running, and IS it running?"

---

## 5. Locked C.1 — Deep-link format refinement (refinement of v1 Locked C)

**Problem v1 left open.** v1 Locked C's error message sketch used direct paths like `[link to /routes/spindle-to-eremos](...)`. Today route instance ids ARE stable URL fragments — so this works — but the form pre-commits to a path shape that M.2e Shared List Infrastructure may want to change (e.g. moving to `/routes?focus=<id>` for row-anchor scrolling, or to a row-anchor `#row-<id>` form for in-page navigation).

### Locked refinement

> Dependent-list deep links in the cross-record refusal panel use a **focus-query form** rather than a raw path: `/routes?focus=spindle-to-eremos` (parallel for sources / sinks). The list page reads the `focus` query parameter, scrolls to the matching row, and applies a brief highlight pulse. If the row is filtered out by the user's active search/filter, the page clears filters first.

### Why this matters

- M.2e will build the shared list-row-focus primitive once, and every page that links to a list row will use the same query parameter
- Without this refinement, every page that links to a list row would invent its own focus mechanism — exactly the P2 anti-pattern (page-by-page chrome)
- Cost in M.2b.6.1: ~5 LOC per list page to read the query parameter and call a scroll-into-view + add a highlight CSS class

### Test deltas

- Existing list-page tests gain one assertion: opening `/sources?focus=FANUC-01` triggers the highlight code path on the FANUC-01 row
- Cross-record refusal panel test asserts the link href uses `?focus=` form, not a raw path

### Bounded scope

This milestone implements **read** of the `focus` parameter on the three list pages. M.2e takes over the **write** side and generalises the primitive. The Locked C.1 form is forward-compat with M.2e's eventual API.

---

## 6. Architectural simplification — drop `EnableDisableImpactSummary.cs`

**ChatGPT review's structural pushback** — accepted. The standalone `EnableDisableImpactSummary.cs` was one abstraction layer too many for a single-boolean milestone.

### What changes

The planner's return type extends from a tuple `(GatewayConfiguration draft, IReadOnlyList<DependencyRef> blockers)` to a single result record:

```csharp
public sealed record EnableDisablePlanResult(
    EnableDisablePlanOutcome Outcome,
    GatewayConfiguration? Draft,
    IReadOnlyList<DependencyRef> Blockers,
    ImpactSummary Impact);

public enum EnableDisablePlanOutcome { Apply, NoOp, CrossRecordRefused }

public sealed record ImpactSummary(
    string DiffSummary,           // "Enabled: false → true"
    string? ImpactWarning,        // "Data flow from FANUC-01 to mqtt-eremos will stop" or null
    IReadOnlyList<DependencyRef> Dependents);
```

`ImpactSummary` lives inside `EnableDisablePlanner.cs` (a sibling record, not a sibling file). The confirm sheet consumes the planner's `Impact` field directly. No separate helper file, no separate test file.

### Deliverable list delta (against v1 §4)

| File | v1 status | v2 status |
|---|---|---|
| `Wizards/EnableDisableImpactSummary.cs` | new | **REMOVED** — folded into planner result |
| `Wizards/EnableDisablePlanner.cs` | new | new, with `EnableDisablePlanResult` + `ImpactSummary` records inside |
| `tests/.../EnableDisableImpactSummaryTests.cs` | new (~6 tests) | **REMOVED** — assertions absorbed into planner tests (planner test count goes from ~14 to ~18) |

### Why the simplification is justified

- One file fewer to grep through six months later when explaining the toggle to a new contributor
- Planner already owns the cross-record validation (the same data needed for ImpactSummary) — co-locating Impact with Blockers and Outcome avoids re-computing dependency state
- The confirm sheet's model would have been a thin wrapper around two planner calls — collapsing to one call is simpler
- Future extension (e.g. if M.2d edit-wizard wants impact summaries for non-Enabled-field changes) builds its own `EditImpactSummary` then; reusing M.2b.6.1's helper for a different shape of change would be speculative coupling

---

## 7. Updated deliverables list (consolidated against v1 §4)

### Production code (delta against v1)

| File | Status | Note |
|---|---|---|
| `Wizards/EnableDisablePlanner.cs` | new | Now returns `EnableDisablePlanResult` per Amendment 6. Header comment includes Locked A.1 atomicity clarification. |
| ~~`Wizards/EnableDisableImpactSummary.cs`~~ | **dropped** | Per Amendment 6 |
| `Api/EnableDisableApi.cs` | new | Six endpoints. Now reads `expectedConfigurationVersion` from request body per Locked G; evaluates checks in the Locked-G order. |
| `Contracts/EnableDisableRequestDto.cs` | **new** | Replaces v1's "empty body" assumption. Carries `expectedConfigurationVersion`. |
| `Contracts/EnableDisableResponseDto.cs` | new | Discriminated by `outcome` (Applied / NoOp / Conflict). |
| `Components/Shared/EnableDisableConfirmSheet.razor` | new | Renders Applied / Conflict / StaleView paths. |
| `Components/Shared/EnableDisableConfirmSheetModel.cs` | new | POCO. Owns Locked H drain-state UI logic, Locked F no-op short-circuit, Locked G stale-view refresh banner. |
| `Components/Pages/Sources.razor` | edit | Action column appended after Status column (Locked H two-column model). Reads `focus` query param per Locked C.1. |
| `Components/Pages/Sinks.razor` | edit | Same. |
| `Components/Pages/Routes.razor` | edit | Same; confirm sheet shows the Impact section per v1 Locked B. |

### Tests (delta against v1)

| File | Status | Test count change |
|---|---|---|
| `Wizards/EnableDisablePlannerTests.cs` | new | ~14 → ~18 (NoOp + Impact assertions absorbed) |
| ~~`Wizards/EnableDisableImpactSummaryTests.cs`~~ | **dropped** | −6 |
| `Api/EnableDisableApiTests.cs` | new | ~10 → ~16 (NoOp + StaleView + ordering) |
| `Components/EnableDisableConfirmSheetModelTests.cs` | new | ~6 → ~10 (drain-state + stale-view-banner + already-in-state-skip-sheet) |
| New: list-page focus-query integration tests | new | +3 (one per page) |

**Total: ~47 tests** (was ~36 in v1). Kickoff estimated ~15; the refinement budget on testing is well-earned by the operational-semantics surface — and still inside any single-milestone budget concern.

---

## 8. Updated Definition of Done (delta against v1 §6)

New rows added; existing rows unchanged unless noted.

- [ ] **(new)** Planner returns `EnableDisablePlanResult` with `Outcome ∈ {Apply, NoOp, CrossRecordRefused}`; no separate impact-summary type exists in the codebase
- [ ] **(new)** API evaluates checks in the Locked-G order (StaleView → NoOp → CrossRecord → Validate → Apply); ordering test passes
- [ ] **(new)** 200 OK response envelope is discriminated by `outcome`; Applied / NoOp / Conflict shapes all carry their respective payloads per v2 §1 + §2
- [ ] **(new)** Stale-view 409 returns `CONFIG.STALE_VIEW` with `expectedVersion` + `currentVersion` fields
- [ ] **(new)** Confirm sheet does NOT open when the planner pre-check determines no-op; snackbar fires info-severity directly
- [ ] **(new)** Confirm sheet renders refresh banner on `CONFIG.STALE_VIEW`; refresh button re-polls the list and clears the sheet
- [ ] **(new)** Action column appended after Status column on all three list pages; both columns coexist per Locked H
- [ ] **(new)** Drain-state button rendered as disabled with the Locked-I tooltip during mid-disable; thrash prevention test passes
- [ ] **(new)** Dependent-list deep links use `?focus=<id>` form; the three list pages read the query parameter and apply a highlight-pulse class
- [ ] **(new)** No occurrence of standalone `EnableDisableImpactSummary` symbol or file in the codebase (grep guard in DoD)
- [ ] **(retained)** All six API endpoints registered per Locked D (v1)
- [ ] **(retained)** `EnableDisablePlanner` purity proven (input config reference equality post-call)
- [ ] **(retained)** Hot reload picks up the toggle as a `Modified` classification (ADR-0009)
- [ ] **(retained)** `dotnet build` 0 warnings / 0 errors
- [ ] **(retained)** `dotnet test --filter "Category!=Flaky"` green
- [ ] **(retained)** ADR for Locked C (no cascade) landed
- [ ] **(new)** ADR for Locked H (config vs runtime semantics) — earned promotion because it generalises beyond M.2b.6.1; M.2c Runtime Tap and M.2d Edit-via-Wizard will both rely on this distinction
- [ ] **(retained)** Smoke pass: kickoff §0 scenario end-to-end via Studio at 127.0.0.1:5080
- [ ] **(new)** Smoke pass: two-tab stale-view scenario — Tab A sees stale row, Tab A's click returns 409, refresh recovers cleanly

---

## 9. Updated open items for v3 (delta against v1 §7)

Items v2 has NOT locked, leaving them open for the optional reality-check pass OR for v3 closing:

1. **Confirm sheet — MudDrawer vs MudDialog vs inline expansion under the row.** Still open. v2 added more content to the sheet (refresh banner, drain-state guard, focus-deep-link rendering); the choice of MudBlazor primitive should be re-evaluated against this richer content.
2. **Status chip styling delta.** v2 amplifies this — the Status column now visibly contrasts with the Action column. The chip-vs-button visual distinction is more important after v2 than after v1. Lock the styling guidance during implementation.
3. **Keyboard shortcut affordance.** Still open. Possibly `e` to open the confirm sheet for the focused row; cancel/apply via Enter/Esc.
4. **Telemetry counters.** v2 implicitly resolves part of this — `outcome` is now structured (`Applied | NoOp | Conflict`) and an apply-counter naturally dimensions on `outcome` + entity kind. Still worth a one-line decision in v3.
5. **Exact wording of cross-record / stale-view error copy.** v2 sketched copy. v3 should refine for the P5 India + Middle East English baseline.

### Items v2 explicitly leaves out of scope (flag for follow-up milestones)

| Item | Where it goes |
|---|---|
| Retrofit stale-view protection to existing Add wizards | M.2d Edit-via-Wizard kickoff input |
| Fleet-mode multi-gateway transactional apply | Milestone K and/or fleet management (M.2k); explicitly out of forever-scope for M.2b.6.1 |
| Audit-comment capture per toggle | M.2d (its richer edit context is the right home) |

---

## 10. Cadence (revised)

1. ✅ v1 plan landed — 2026-05-19
2. ✅ ChatGPT strategic review pass — 2026-05-19
3. ✅ **v2 amendment landed (this file)** — 2026-05-19
4. **Optional Step-1 reality check** — recommend SKIP. v2 expanded the operational-semantics surface but stayed inside the kickoff's "one boolean milestone" envelope. The new locks (no-op, stale-view, config-vs-runtime separation, focus-query deep links) are all built on already-existing system concepts (`ConfigurationVersionId`, status chip column, draft pipeline). No novel architecture introduced. Reality-check value is low.
5. **v3 lock** — confirm SKIP on #4, address §9 open items (1–5), produce v3 file.
6. **Implementation** — after v3 lock + explicit scope confirmation from user per the standing rule.
7. **Handoff doc + milestone close** — per project discipline.

---

**End of v2 amendment. v2 LOCKED 2026-05-19 after ChatGPT strategic review. Next: confirm Step-1 skip and produce v3 with §9 open items resolved; or, if the user wants the reality check, run it first.**
