# M.2b.6.1 — Inline Enable/Disable v3 amendment (LOCKED)

**Status:** **v3 — LOCKED.** Implementation may begin **after explicit user scope confirmation** per the standing rule. No reality-check pass — user confirmed skip per v2 §10.
**Date:** 2026-05-19
**Form:** **TIGHT AMENDMENT** to v2. v1 plan + v2 amendment remain the load-bearing references; v3 deltas are scoped to the five §9 open items + one additional operational discipline (Apply-button loading) surfaced during the v2 review.

**Predecessor documents:**
- [Kickoff](2026-05-19-mp2b6-1-inline-enable-disable-kickoff.md) — scope locked at §1
- [v1 plan](2026-05-19-mp2b61-inline-enable-disable-plan.md) — Locked A–E
- [v2 amendment](2026-05-19-mp2b61-inline-enable-disable-plan-v2.md) — Locked F, G, H, I + refinements A.1, C.1; one structural simplification (drop `EnableDisableImpactSummary.cs`)

---

## 0. What changed v2 → v3 (delta summary)

ChatGPT v2 review pass on 2026-05-19 endorsed proceeding directly to v3 lock — none of the remaining open items are architecture-risk items; they are UX-governance refinements. Six new Locked decisions land here:

| # | Theme | New Lock | Source |
|---|---|---|---|
| 1 | **Confirm-sheet primitive** → `MudDrawer` (right-anchored, 480–560px, persistent, mobile fullscreen) | Locked J | v2 §9 item 1 |
| 2 | **Status chip vs action button styling** with critical "never solid red at rest" sub-lock | Locked K | v2 §9 item 2 |
| 3 | **Keyboard shortcuts** — minimal: `Enter` primary, `Esc` close drawer; nothing else | Locked L | v2 §9 item 3 |
| 4 | **Telemetry counter** — single counter with four dimensions, no high-cardinality fields | Locked M | v2 §9 item 4 |
| 5 | **Error copy** — operational English principle, with specific phrasing | Locked N | v2 §9 item 5 |
| 6 | **Apply-button loading discipline** — loading state + lock-out duplicate submissions + drawer-stays-open until resolve | Locked O | New from v2 review |

Everything else stays. **All v1 + v2 locks (A–I plus refinements A.1, C.1) are unchanged.** Scope is unchanged. Cadence step: v3 lock → user scope confirmation → implementation.

---

## 1. Locked J — Confirm sheet renders as right-anchored `MudDrawer`

### Locked decision

> The confirm sheet renders as a **`MudDrawer` anchored to the right** (`Anchor="Anchor.End"` in MudBlazor terms), width 480–560px fixed at desktop breakpoints, **persistent** (list page remains visible and scrollable underneath), **fullscreen only at mobile breakpoints** (≤600px wide). Not a `MudDialog`. Not inline row-expansion.

### Behavioural specifics

- Opens on click of `[Enable]` or `[Disable]` action button
- Stays open while the operator reads the diff / impact / dependent list / stale-view banner
- Closes on:
  - **Cancel** button click
  - **Esc** keypress (per Locked L)
  - Successful Apply (after the snackbar resolves, drawer dismisses on the same tick)
- Does NOT close on outside-click (operationally too easy to lose context by accident)
- Does NOT close on route navigation while a request is in flight (per Locked O)

### Why MudDrawer wins (recorded for future-reader explainability per P4)

| Candidate | Why rejected |
|---|---|
| **Inline row-expansion** | Row-height thrash inside dense operational grids. Becomes unstable with v2's stale-view banner + dependency list + drain-state notice + future M.2d field-diff growth. Bad scaling. |
| **`MudDialog` (modal)** | Hides surrounding rows. Breaks "list-centric workflow." Feels heavyweight for repeated toggles in commissioning sessions. Conflicts with M.2e shared row-action rhythm. |
| **`MudDrawer` (chosen)** | List stays visible. Matches operational tooling primitive used in VS Code side panels, SCADA alarm inspectors, OT management consoles, GitHub side inspectors. Preserves context. Scales into M.2d's richer edit-diff content without re-platforming. Preserves P2. |

### Test deltas

- New: list-page POCO test — `OpenConfirmSheet_RendersRightAnchoredDrawer` (one per page; verifies the `Anchor` + `Variant` parameters)
- Existing `EnableDisableConfirmSheetModelTests` unchanged (the drawer is the shell; the model is primitive-agnostic)

---

## 2. Locked K — Status chip vs action button styling discipline

This lock matters more after v2's Locked H (two coexisting columns) because the visual distinction between an **informational chip** (Status) and a **mutating control** (Action) carries operational signal: misreading them in a busy grid is the entire failure mode this lock prevents.

### Locked rules

#### Status chip (existing surface; styling discipline reaffirmed)

| Attribute | Lock |
|---|---|
| Purpose | Informational only — runtime state, passive telemetry surface |
| Clickability | **Non-clickable** (chip is not a control) |
| Shape | Compact, low elevation, soft fill (MudBlazor `Variant.Outlined` or low-opacity filled) |
| Color semantics | Green (Healthy), Amber (Degraded), Red (Faulted), Grey (Stopped) — all using `.soft` opacity (~12–16% fill alpha) |
| Icon-led | Lead with a small status icon, text secondary |
| Text | "Healthy" / "Degraded" / "Faulted" / "Stopped" — never the operator's intended action verb |

#### Action button (new surface from Locked E + Locked H)

| Attribute | Lock |
|---|---|
| Purpose | Mutating control — config-state action |
| Shape | Always a `MudButton` (outlined or filled), larger click target than chip |
| Text | Explicit verb: `Enable` or `Disable` (never status-state words) |
| Enable button | `Variant.Filled`, `Color.Primary` |
| Disable button | `Variant.Outlined`, `Color.Default` at rest |
| Disable button on hover/focus | Red accent border + red text (destructive affordance only on intent surface) |
| Disable button on the confirm drawer's primary action | Solid red (destructive confirm — the operator is committing) |

#### **Critical sub-lock (K.1) — Never solid red at rest**

> The Disable button at row resting state is **NEVER** solid red (`Color.Error` filled). Solid red is reserved for hover/focus on the row button AND for the drawer's primary commit action. Operational grids with many enabled rows would become alert-color noise if every Disable rendered red — operators tune out destructive coloring, and real destructive prompts lose impact.

### Visual delta from v2 (refined)

```
Status column                Action column
─────────────────────────    ──────────────────
●  Healthy   (green soft)    [Disable] (outlined neutral; red on hover)
○  Stopped   (grey soft)     [Enable]  (filled primary)
▲  Degraded  (amber soft)    [Disable] (outlined neutral; red on hover)
✕  Faulted   (red soft)      [Disable] (outlined neutral; red on hover)
```

### Test deltas

- New: list-page POCO test per page — `ActionButton_RestingDisable_NotSolidRed`
- New: list-page POCO test per page — `ActionButton_HoverDisable_ShowsRedAccent`
- New: drawer test — `DrawerPrimary_DisableCommit_RendersSolidRed`

---

## 3. Locked L — Keyboard shortcuts kept intentionally minimal

### Locked decision

> Inside the confirm drawer ONLY: **`Enter`** activates the primary action (Apply); **`Esc`** closes the drawer. **No other shortcuts** are added in this milestone — no row-level hotkeys, no `e`-to-open, no grid-level shortcut routing, no focus navigation gestures.

### Behaviour specifics

- `Enter` is bound to the drawer's primary action **only when the drawer is open and the primary action is enabled** (i.e. not in loading state per Locked O, not on a stale-view banner — in stale-view state, `Enter` triggers the Refresh button instead)
- `Esc` closes the drawer **only when no request is in flight** (per Locked O the drawer cannot close during a pending Apply)
- Both shortcuts are scoped to the drawer's DOM subtree; they do NOT leak to the list page underneath

### Why this minimum

- Keyboard systems inside data grids become unexpectedly expensive: focus ownership, accessibility interactions, row virtualisation edge cases, future M.2e shortcut-collision risk
- The drawer's accessibility controls (`Esc` + tab navigation + focus trap) are sufficient for keyboard-first operators today
- Full keyboard workflow belongs in M.2e Shared List Infrastructure where it can be designed cohesively across every list-page action

### Test deltas

- New: drawer test — `EnterKey_PrimaryEnabled_FiresApply`
- New: drawer test — `EscKey_NoPendingRequest_ClosesDrawer`
- New: drawer test — `EnterKey_StaleViewBanner_FiresRefresh`
- New: drawer test — `EscKey_RequestPending_DoesNotClose` (the Locked O guard)

---

## 4. Locked M — Telemetry counter shape

### Locked metric

```text
management_enable_disable_operations_total
```

A single `System.Diagnostics.Metrics.Counter<long>` (the same metrics primitive Phase 1 standardised on per CLAUDE.md §6). Incremented once per Enable/Disable API request, regardless of outcome.

### Dimensions (Locked)

| Dimension | Allowed values |
|---|---|
| `entity_kind` | `source` / `sink` / `route` |
| `requested_action` | `enable` / `disable` |
| `outcome` | `applied` / `noop` / `stale_view` / `cross_record_refused` / `validation_refused` |
| `initiated_from` | `sources_page` / `sinks_page` / `routes_page` |

### What does NOT go in the dimensions (Locked, to prevent high-cardinality explosion)

- ❌ Entity instance id
- ❌ Route names / source names / sink names
- ❌ Tenant id (multi-tenancy not in scope yet anyway)
- ❌ Operator id (lives in the audit chain, not in metrics)
- ❌ Error message text
- ❌ Timestamps (Prometheus already handles time)

### Why one counter, not per-outcome counters

- Prometheus aggregations over `outcome` dimension answer "what's our no-op rate?" / "what's our stale-view rate?" without metric proliferation
- Operationally simpler to alert on (one rule per concern, dimensioned)
- Matches existing Phase 1 metric shapes (`pipeline_steps_total`, `sink_delivery_attempts_total`)

### Cardinality bound

`3 × 2 × 5 × 3 = 90` distinct time series. Well within any Prometheus/operational metric budget.

### Test deltas

- New: `EnableDisableApiTests.cs` — `Post_EmitsCounterWithFourDimensions` (per outcome path; six tests covering each outcome × at least one entity-kind path)
- New: cardinality guard test — asserts the registered counter declares exactly the four dimensions listed above (catches accidental cardinality additions during future maintenance)

---

## 5. Locked N — Error copy: operational English principle

### Locked principle

> All operator-facing strings in this milestone follow **operational English**: short sentences, concrete verbs, no platform jargon. The target reader is an OT operator whose primary language may not be English — the copy should be unambiguous in translation and read naturally to a non-native speaker.

### Locked strings (final form for v3)

These supersede the sketched copy from v1 + v2:

| Surface | v3 final copy |
|---|---|
| Cross-record refusal (route → disabled source) | `Cannot disable source 'FANUC-01'. These routes use it:` then bulleted list `• Route 'spindle-to-eremos' — disable first` |
| Cross-record refusal (route → disabled sink) | `Cannot enable route 'spindle-to-eremos'. Its destination 'mqtt-eremos' is disabled. Enable the destination first.` |
| Stale-view banner | `Configuration changed. Refresh and try again.` |
| Drain-in-progress tooltip | `Drain in progress. Wait for stop to complete.` |
| No-op snackbar (Enable) | `Source 'FANUC-01' is already enabled.` |
| No-op snackbar (Disable) | `Source 'FANUC-01' is already disabled.` |
| Applied snackbar (success) | `Source 'FANUC-01' disabled.` |
| Route disable impact (in drawer) | `Data flow from 'FANUC-01' to 'mqtt-eremos' will stop until the route is enabled again.` |
| Validation refused (non-cross-record) | Existing validation-error envelope copy — no changes; already operational tone |

### Banned phrasings (explicit anti-patterns)

| Avoid | Use instead |
|---|---|
| "dependency graph" | "These routes use it" |
| "conflicting state" | name the conflict directly |
| "synchronization" | "refresh" / "reload" |
| "resource contention" | name the resource explicitly |
| "stale baseline" / "stale view" (operator-facing) | "Configuration changed" |
| "validation failed" | name the specific validation explicitly |

Internal code identifiers (`CONFIG.STALE_VIEW`, `EnableDisablePlanResult.NoOp`, etc.) retain platform terminology — those are for developers, not operators.

### Test deltas

- New: `EnableDisableConfirmSheetModelTests` — `ErrorCopy_MatchesLockedStrings` (asserts each Locked-N string is rendered exactly when its trigger fires)
- Localisation note: copy lives in the component where it's used (no resource-file extraction yet); when localisation lands as a future milestone, the Locked-N strings are the canonical English baseline to translate from.

---

## 6. Locked O — Apply-button loading discipline

### Locked rules

When the operator clicks the primary action (Apply Enable / Apply Disable) inside the drawer:

1. **Primary button enters loading state immediately** (MudBlazor `Loading` parameter set to true; spinner replaces button text; button remains visible but non-interactive)
2. **Cancel button is disabled during the request** (so the operator cannot dismiss mid-flight and lose the snackbar / outcome feedback)
3. **Drawer cannot close until the request resolves** (no `Esc`, no outside-click, no successful-navigation close path — request completion is the only exit)
4. **Duplicate submissions are impossible** — the button's onclick handler is a single-fire pattern; subsequent clicks while loading are ignored (defence-in-depth on top of the button's disabled state)
5. **Request timeout** — bounded by the existing `HttpClient` default timeout (already configured in Studio's DI); on timeout the drawer renders a `Request timed out. The change may or may not have applied. Refresh to confirm.` banner and re-enables Cancel + primary

### Why each rule

- (1) prevents stale "click me" affordance from suggesting the operator should try again
- (2) prevents the dual-snackbar / abandoned-request race (operator presses Cancel, then a delayed 200 OK applies the change anyway, then they see a "success" snackbar for a change they tried to cancel)
- (3) ensures the operator sees the result of every intentional action — no orphan applies
- (4) plain defence — even if the framework's disabled-state implementation has a frame of timing slop, the handler refuses to re-enter
- (5) makes the rare timeout case operationally recoverable rather than a silent ambiguous state

### Interaction with Locked G stale-view

If a request returns 409 `CONFIG.STALE_VIEW`:

- Loading state ends, Cancel re-enables
- Drawer pivots to the stale-view banner UI per v2 §2
- Primary button text changes to **Refresh** (`Enter` now triggers Refresh, per Locked L's stale-view branch)
- After Refresh resolves and list re-polls, the drawer closes; the operator decides whether to reopen with the now-current state

### Interaction with Locked F no-op

Locked F's planner pre-check fires **before** the drawer opens. So the loading discipline never engages on a no-op — the operator sees the snackbar info-severity message and the drawer never opens. The Apply button never enters loading because there is no Apply.

### Test deltas

- New: drawer test — `PrimaryClick_DrawerLocksUntilResolve` (asserts Cancel is disabled, Esc is no-op, primary shows spinner)
- New: drawer test — `PrimaryClick_DuplicateClick_IgnoredWhileLoading`
- New: drawer test — `TimeoutResponse_ShowsRecoveryBanner`
- New: drawer test — `StaleView409_DrawerPivots_PrimaryBecomesRefresh`

---

## 7. Consolidated Definition of Done (delta against v2 §8)

New rows added; v1 + v2 rows retained unless noted.

### v3 additions

- [ ] Confirm sheet implemented as right-anchored `MudDrawer` per Locked J; persistent variant on desktop, fullscreen on mobile breakpoint
- [ ] Status column chip uses soft-fill informational styling; chip is non-clickable
- [ ] Action button text is the verb (`Enable` / `Disable`); never status text
- [ ] Disable button at rest is `Variant.Outlined` `Color.Default`; red appears only on hover/focus and on drawer's commit action (Locked K.1 verified by visual regression or styling assertion)
- [ ] `Enter` activates primary action inside drawer; `Esc` closes drawer; no other shortcuts registered
- [ ] `Esc` is a no-op while a request is in flight (Locked O guard)
- [ ] `management_enable_disable_operations_total` counter registered with exactly four dimensions per Locked M; cardinality guard test passes
- [ ] All operator-facing strings match Locked-N table exactly; no banned phrasings present
- [ ] Drawer primary button enters loading state on click; Cancel disables; duplicate clicks ignored
- [ ] Drawer cannot close until request resolves (success / 409 / 422 / timeout)
- [ ] Timeout path renders Locked-O recovery banner
- [ ] Stale-view 409 path pivots primary to Refresh per Locked O + Locked G interaction

### v2 carry-forward (still required)

(All v2 §8 DoD rows remain in effect — listed there, not duplicated here.)

### v1 carry-forward (still required)

(All v1 §6 DoD rows remain in effect — listed there, not duplicated here.)

---

## 8. Closing the §9 open items

| Item | Resolution |
|---|---|
| 1. Confirm-sheet primitive | Locked J — MudDrawer right-anchored |
| 2. Status chip vs action button contrast | Locked K + K.1 |
| 3. Keyboard shortcuts | Locked L — minimal |
| 4. Telemetry counters | Locked M — one counter, four dimensions |
| 5. Error copy refinement | Locked N — operational English with explicit table |
| (Surfaced during v2 review) Apply-button discipline | Locked O |

All v1 + v2 open items now closed. v3 has **no remaining open items**.

---

## 9. Scope confirmation gate

Per the user's standing rule ("Confirm scope with me before writing any code"), the v3 lock does NOT trigger implementation automatically. The next step is:

> **User explicitly confirms scope.** The implementer (this session or a future one) does not touch code until that confirmation arrives.

When confirmation lands:

1. Implementer reads v1 + v2 + v3 in sequence (they layer; reading only v3 misses load-bearing context)
2. Implementer also reads the kickoff §0 motivation so smoke testing is grounded
3. Files created and edits made strictly per the consolidated deliverables list (v1 §4 + v2 §7 deltas; no v3 deliverable changes — v3 is pure governance refinement)
4. Test suite expands from the v1 baseline through v2 and v3 deltas — final count ~47 + v3 additions = approximately 60 tests total across planner, API, model, and component layers
5. ADRs land per v2 §8: `<NN>-inline-enable-disable-no-cascade.md` and `<NN>-config-state-vs-runtime-state.md`
6. Smoke pass replays kickoff §0 AND the two-tab stale-view scenario before milestone close
7. Handoff doc lands per CLAUDE.md §2 session-handoff discipline

---

## 10. Final cadence record

1. ✅ Kickoff — 2026-05-19
2. ✅ v1 plan — 2026-05-19 (Q1–Q5 resolved, Locked A–E)
3. ✅ ChatGPT review pass on v1 — 2026-05-19
4. ✅ v2 amendment — 2026-05-19 (Locked F, G, H, I, A.1, C.1; `ImpactSummary` simplified into planner result)
5. ✅ ChatGPT review pass on v2 — 2026-05-19
6. ⏭ Step-1 reality check — **SKIPPED** by mutual recommendation (v2 stayed inside the kickoff envelope; no novel architecture)
7. ✅ **v3 lock (this file)** — 2026-05-19 (Locked J, K, K.1, L, M, N, O)
8. ⏳ **Awaiting explicit user scope confirmation**
9. ⏳ Implementation (single focused session per kickoff §3 envelope; refined to ~60 tests + ADR pair + smoke passes)
10. ⏳ Handoff + milestone close

---

**End of v3 amendment. v3 LOCKED 2026-05-19. M.2b.6.1 plan trail is complete. No further amendment expected before implementation; if implementation surfaces unforeseen tradeoffs they land as a v4 amendment, not as silent in-flight scope expansion.**
