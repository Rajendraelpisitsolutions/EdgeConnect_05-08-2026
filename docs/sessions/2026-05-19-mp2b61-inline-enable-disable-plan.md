# M.2b.6.1 — Inline Enable/Disable v1 plan

**Status:** **v1 DRAFT** — awaiting ChatGPT strategic review pass before lock
**Date:** 2026-05-19
**Form:** First-pass plan in the project's plan-trail discipline (v1 → ChatGPT review → v2 → optional reality check → v3 → implementation). v1 resolves the five open questions from the kickoff and produces a deliverables sketch concrete enough for review.

**Inputs:**
- [M.2b.6.1 kickoff](2026-05-19-mp2b6-1-inline-enable-disable-kickoff.md) — scope locked at §1, deliverables sketched at §3, open questions enumerated at §4
- [Platform principles](../platform-principles.md) — particularly **P2** (shared interaction primitives), **P4** (preserve the explainability data path), **P6** (operational product not developer tool)
- [Post-M.2b.6 roadmap v2](2026-05-19-post-mp2b6-product-roadmap-v2.md) — confirms M.2b.6.1 inserts between M.2b.6 and M.2c without disturbing the queued Tier-1 chips
- [M.2b.5/2b.6 v3 plan](2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md) §3 — premium-UX implementation discipline (the binding-contract baseline for every Studio milestone)

---

## 0. Why this v1 plan exists (one paragraph)

The kickoff already locked scope and motivation. The v1 plan's job is to **convert the five open questions in kickoff §4 into Locked decisions**, refine the deliverables sketch into something an implementer can pick up, and lock a Definition of Done so milestone exit is unambiguous. Nothing about scope changes here. If scope expands, that's a v2 amendment, not a v1 quietly-broader read.

---

## 1. Scope (re-asserted from kickoff §1, locked)

**In scope:**

- A row-level **Enable/Disable affordance** on `Sources.razor`, `Sinks.razor`, `Routes.razor` lists
- A management API verb endpoint per entity kind that flips the `Enabled` flag through the standard draft → validate → apply round-trip
- A pure planner (`EnableDisablePlanner`) mirroring `WizardConfigMerger` semantics — current config + target id + desired Enabled state → new draft `GatewayConfiguration`
- Cross-record validation: enabling/disabling that would violate Core's startup invariants returns a clear inline error naming the dependent entities
- ≥1 named test per Locked decision plus the standard happy/cross-record/idempotent matrix

**Out of scope (deferred — Locked):**

| Deferral | Goes to |
|---|---|
| Editing any field other than `Enabled` | M.2d Edit-via-Wizard |
| Bulk multi-row enable/disable | M.2e Shared List Infrastructure |
| One-click cascade (auto-disable dependents) | Explicitly NOT in any current milestone; revisit only if operator data demands it |
| Confirmation dialogs with audit-comment capture | M.2d (full edit will carry richer audit context) |
| Toggle for sub-entities (route filter rule, individual transform) | Not a current milestone |

If a Locked deferral becomes a smoke-blocker after v3, that's a v2 amendment input — not a silent in-flight scope expansion.

---

## 2. Position relative to existing architecture (no new architecture introduced)

M.2b.6.1 reuses the architectural pieces M.2b.1 / M.2b.3 / M.2b.5 / M.2b.6 already established. **No new contracts, no new lifecycle, no new pipeline behaviour.** The plan's discipline is: stay narrow.

| Reused piece | Where it comes from |
|---|---|
| Draft / validate / apply round-trip | `ConfigApi` (`/drafts`, `/drafts/{id}/validate`, `/drafts/{id}/apply`) |
| Pure planner pattern | `WizardConfigMerger.BuildNewSourceDraft` / `BuildNewRouteDraft` / `BuildNewSinkDraft` |
| Cross-record validation | Core's startup invariant checker + Management's `CrossRecordValidator` (defence-in-depth pattern from M.2b.5 Locked G) |
| Audit chain entry | Existing `ConfigDraftManager.ApplyAsync` emits a structured audit record per apply — unchanged |
| Hot reload | Existing reload pipeline picks up the diff classifier output (ADR-0009/0010) — toggle is a `Modified` classification |
| List page chrome | `MudDataGrid` in `Sources.razor` / `Sinks.razor` / `Routes.razor` — add one column, no new widget |
| Confirm surface | New small primitive (see §3, Q1 / Q5 resolution) — kept minimal so M.2e can replace with the shared confirm pattern |

This is the smallest milestone in the post-M.2b.6 sequence by design.

---

## 3. Locked decisions (the v1 plan's actual contribution)

These are the resolutions to kickoff §4 questions Q1–Q5. Each shows the alternatives considered, the chosen path, and the reasoning that will be tested by ChatGPT review.

### Locked A — Q1 resolution: confirm-sheet + atomic validate+apply, NOT plain snackbar, NOT silent auto-apply

**Alternatives considered:**

- (a) **Snackbar after draft creation, operator opens Config page to Validate then Apply.** Mirrors the Add wizards exactly. Three clicks per toggle, leaves the list page mid-task. Maximally conservative — but operator-hostile for a single-boolean change.
- (b) **One-click silent auto-apply.** Operator presses Enable, the row state flips, snackbar shows "Done." Minimum friction. But hides the diff, hides the apply outcome (validation errors silently surface as a row that didn't flip), and violates the spirit of the explicit-confirmation pattern that anti-pattern #10 protects.
- (c) **Confirm sheet: one inline panel shows the trivial diff (`Enabled: false → true`), dependency context if any, and a single primary action that creates the draft, validates it, and applies it atomically.** Operator stays on the list page. Snackbar surfaces the result (success or structured validation error) plus the audit-record id for traceability.

**Locked: (c).** Reasoning:

- **Anti-pattern #10 preserved.** A draft IS created; validation IS run; apply IS the apply pipeline. The audit chain entry is identical to a wizard apply. The "atomic" framing is a UI condensation, not a flow bypass.
- **P4 preserved.** The structured audit record carries the operator id, the affected entity, and the before/after value. The Operational Explainability future surface (roadmap v2 Locked D) can render "why was X enabled? operator action at T" verbatim.
- **P6 honoured.** One operator gesture per toggle. Not three.
- **Distinct from Add wizards** because Add wizards mutate 30–80 fields per draft and the Draft Summary panel is the right discipline; this milestone mutates 1 boolean and the Draft Summary panel would be operator-hostile chrome around a one-line diff.
- **Reversibility.** Rollback via the existing audit-history page is one click. If the operator regrets the toggle, the remedy is one navigation away.

### Locked B — Q2 resolution: confirm sheet shows extra warning for routes; no separate dialog

**Alternatives considered:**

- (a) **No confirmation anywhere.** Click → it happens. Brittle when an operator misclicks a production route in a busy list.
- (b) **Confirm dialog only for routes.** Adds a modal layer over the route-disable case. Inconsistent surface across entity kinds — the same operator gesture means different things on different pages.
- (c) **Single confirm-sheet surface (Locked A) with contextual warning text.** Sources/sinks see only the diff. Routes being disabled see an additional `"This will stop data flow from {source} → {sink}"` warning rendered in the sheet's diff panel.

**Locked: (c).** Reasoning:

- **P2 satisfied.** One confirm surface across all three pages; pages CONFIGURE the warning text, they do not invent new modals.
- **Operationally distinguishes high-impact disables** (route off = data flow off) from low-impact toggles (source enable when no route yet wired) without proliferating UI primitives.
- **Forward-compat with M.2d.** When edit-wizard lands, this same sheet can extend to show a per-field diff list for richer changes; the warning slot remains for high-impact field changes.

### Locked C — Q3 resolution: no cascade; refuse with structured dependency-list error

**Alternatives considered:**

- (a) **Refuse with clear error listing dependents.** Operator manually disables dependents first. Safe; transparent; no hidden multi-state change.
- (b) **Offer one-click cascade button** ("Disable source and its 2 dependent routes"). Operator-friendlier; opaque from an audit-trail perspective unless the cascade emits N audit entries.
- (c) **Refuse with dependency list + deep links to each dependent row.** Same as (a) but the error message is actionable — operator clicks a link, lands on the dependent row, disables it, returns and retries.

**Locked: (c).** Reasoning:

- **Anti-pattern #9 honoured** — no silent multi-state change.
- **P4 preserved** — every state change has its own audit record; the chain remains 1:1 with operator gestures, no synthetic multi-record commits.
- **Operator pain bounded.** Most operators will hit this for 1–3 dependents at most on a fresh gateway; the deep links make the manual chain quick. Bulk multi-select belongs in M.2e and will be the proper relief valve when N gets large.
- **(b) is rejected explicitly** to keep M.2b.6.1 a one-boolean milestone. Once a "cascade" feature exists, every future related milestone has to consider whether to extend it. Defer until M.2e or until operator data shows we need it.

**Concrete error shape** (rendered in the confirm sheet's warning slot, NOT in a snackbar — the operator needs to read it):

> Cannot disable source `FANUC-01` because these enabled routes depend on it:
>
> • Route [`spindle-to-eremos`](/routes/spindle-to-eremos) — disable first
> • Route [`alarms-to-eremos`](/routes/alarms-to-eremos) — disable first
>
> Disable the routes above, then return here.

### Locked D — Q4 resolution: verb endpoints (`/enable`, `/disable`), consistent with existing API

**Alternatives considered:**

- (a) **`POST /api/v1/sources/{id}/enable` + `POST /api/v1/sources/{id}/disable`** (and parallel for sinks/routes). Two endpoints per entity kind, six total. Verb-explicit. Audit log self-documenting. Matches existing convention (`/drafts/{id}/validate`, `/drafts/{id}/apply`, `/drafts/{id}/rollback`).
- (b) **`POST /api/v1/sources/{id}/state` with body `{enabled: true|false}`.** One endpoint per entity kind, three total. Extensible to future states (paused, in-maintenance) without new endpoints. But premature extensibility; no current state model beyond enabled.
- (c) **`PATCH /api/v1/sources/{id}` with body `{enabled: ...}`.** Most RESTful. But would collide with M.2d's future full-edit PATCH semantics — better to leave PATCH unclaimed for M.2d.

**Locked: (a).** Reasoning:

- **Consistency with existing verb-style endpoints.** Operators reading audit logs see `/sources/abc/enable` and understand it without parsing a JSON body.
- **Preserves PATCH for M.2d.** When edit-wizard lands, `PATCH /api/v1/sources/{id}` will be the natural shape for arbitrary-field updates. Holding it now would invite double-claiming later.
- **Planner dispatches on the verb** without parsing a body — code path stays simple.

**Endpoint matrix (Locked):**

| Method | Path | Effect |
|---|---|---|
| POST | `/api/v1/sources/{id}/enable` | Plans + drafts + validates + applies `Enabled = true` on the named source |
| POST | `/api/v1/sources/{id}/disable` | Symmetric, `Enabled = false` |
| POST | `/api/v1/sinks/{id}/enable` | Sink analogue |
| POST | `/api/v1/sinks/{id}/disable` | Sink analogue |
| POST | `/api/v1/routes/{id}/enable` | Route analogue |
| POST | `/api/v1/routes/{id}/disable` | Route analogue |

**Request body:** empty.
**Response shape (200 OK):**

```jsonc
{
  "draftId": "draft-2026-05-19-001",
  "validationOutcome": "Passed",
  "appliedAt": "2026-05-19T14:32:11Z",
  "auditRecordId": "audit-7c3a..."
}
```

**Response shape (409 Conflict — cross-record refusal):**

```jsonc
{
  "error": {
    "code": "CONFIG.CROSS_RECORD_REFUSED",
    "message": "Cannot disable source 'FANUC-01' because enabled routes reference it.",
    "dependents": [
      { "kind": "route", "id": "spindle-to-eremos", "name": "Spindle to EREMOS" },
      { "kind": "route", "id": "alarms-to-eremos", "name": "Alarms to EREMOS" }
    ]
  }
}
```

Status codes are explicit: 200 (applied), 409 (cross-record refused), 404 (entity not found), 422 (validation failed for a non-cross-record reason — surfaces as the existing validation-error envelope). No 500 paths in normal operation.

### Locked E — Q5 resolution: status column + trailing-cell action button; confirm sheet on click

**Alternatives considered:**

- (a) **MudSwitch inline in a new "Enabled" column.** Single-click toggle, no separate button. But MudSwitch's click semantics conflict with confirm-sheet flow — the switch visually commits before the operator confirms.
- (b) **3-dot row menu with "Enable"/"Disable" items.** Tidy; consistent with future M.2e action menus. But hides what is currently the primary remediation surface during first-gateway setup. Discoverability fails.
- (c) **Existing status chip column** (already shown today) **+ a new trailing action button** in the row. Text varies: "Enable" (when disabled, primary green) or "Disable" (when enabled, neutral with red hover). Click opens confirm sheet.

**Locked: (c).** Reasoning:

- **Discoverable** — visible primary affordance during first-gateway setup, exactly when operators need it most.
- **P2 satisfied** — the action button is a standard MudButton, not a custom widget. M.2e will replace it with the shared list-row-action primitive when that arrives; until then this is a one-line column.
- **Aligned with Add Source button styling** in the existing Sources.razor header (`Variant.Filled` for primary, `text-transform:none` for the typographic baseline).
- **Forward-compat with M.2d.** When edit-wizard lands, the trailing-cell area gains a second button ("Edit") next to "Enable/Disable". M.2e bulk ops will move both into a shared row-action surface.

**Visual sketch:**

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Instance ID  Protocol   Route        State    Points    Last point   Action  │
├──────────────────────────────────────────────────────────────────────────────┤
│ FANUC-01     FOCAS2     spindle-to…  ●Enab.   18,432    2s ago      [Disable]│
│ FANUC-02     FOCAS2     —            ○Disab.  —         —           [Enable ]│
│ HAAS-01      MTConnect  haas-to-er…  ●Enab.   2,103     5s ago      [Disable]│
└──────────────────────────────────────────────────────────────────────────────┘
```

Confirm sheet (when operator clicks `[Disable]` on a route, for example):

```
┌─ Confirm: Disable route 'spindle-to-eremos' ─────────────────────────┐
│                                                                       │
│  Diff                                                                 │
│     Enabled: true → false                                             │
│                                                                       │
│  ⚠  Impact                                                            │
│     Data flow from source 'FANUC-01' to sink 'mqtt-eremos' will       │
│     stop until this route is re-enabled.                              │
│                                                                       │
│  Audit                                                                │
│     A draft will be created, validated, and applied as one operation. │
│     Rollback via the audit history page if needed.                    │
│                                                                       │
│                                          [ Cancel ]  [ Disable route ]│
└───────────────────────────────────────────────────────────────────────┘
```

Sources/sinks see the same surface minus the Impact section (or with a neutral Impact for "Sink is enabled but no enabled routes reference it — disabling has no immediate runtime effect").

---

## 4. Refined deliverables (file-by-file)

Refines kickoff §3 with full names and ownership boundaries. ~600 LOC implementation + ~150 LOC tests, as estimated at kickoff — refining doesn't expand that envelope.

### Production code

| File | Status | Purpose |
|---|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/EnableDisablePlanner.cs` | **new** | Pure planner. `BuildEnableDisableDraft(current, kind, id, desiredEnabled)` → `(GatewayConfiguration draft, IReadOnlyList<DependencyRef> blockers)`. Mirrors `WizardConfigMerger` purity + record-with idioms. Defence-in-depth cross-record check identical to `CrossRecordValidator`. |
| `src/ElpisEdgeConnect.Management/Wizards/EnableDisableImpactSummary.cs` | **new** | Pure helper. Given a current config + target entity + desired Enabled state → structured `ImpactSummary` (diff lines + impact note). Consumed by confirm sheet. Pure so component tests are trivial. |
| `src/ElpisEdgeConnect.Management/Api/EnableDisableApi.cs` | **new** | Six endpoint registrations per Locked D. Each calls `EnableDisablePlanner` → `ConfigDraftManager.CreateAsync` → `.ValidateAsync` → `.ApplyAsync` in sequence. Maps planner blockers to 409, validation failures to 422. |
| `src/ElpisEdgeConnect.Management/Contracts/EnableDisableResponseDto.cs` | **new** | Response envelope (success + 409 dependent-list shape). |
| `src/ElpisEdgeConnect.Management/Components/Shared/EnableDisableConfirmSheet.razor` | **new** | POCO-model + Razor shell pattern (per P2 — same shape as `ReloadOutcomePanelModel`). Renders the diff + impact + audit sections. Calls the relevant API endpoint and surfaces snackbar + result. |
| `src/ElpisEdgeConnect.Management/Components/Shared/EnableDisableConfirmSheetModel.cs` | **new** | POCO view-model. Holds entity kind, id, current state, desired state, computed impact, validation result. Pure — unit-testable without bUnit. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Sources.razor` | **edit** | Add an `Action` column to the MudDataGrid. CellTemplate renders the Enable/Disable button. Click opens the confirm sheet. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Sinks.razor` | **edit** | Same. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Routes.razor` | **edit** | Same — with the additional Impact section content in the sheet for route-disable. |

### Tests

| File | Status | Coverage target |
|---|---|---|
| `tests/ElpisEdgeConnect.Management.Tests/Wizards/EnableDisablePlannerTests.cs` | **new** | ~14 tests: enable each kind, disable each kind, idempotency (enable an already-enabled instance), cross-record refusals (route → disabled source, route → disabled sink, enable route while source disabled, enable route while sink disabled), unknown id → 404 semantic, planner purity (input config not mutated). |
| `tests/ElpisEdgeConnect.Management.Tests/Wizards/EnableDisableImpactSummaryTests.cs` | **new** | ~6 tests: route disable shows data-flow warning, sink disable with no dependents shows neutral, source disable with no dependents shows neutral, idempotent no-op marked as such, dependent list ordering deterministic. |
| `tests/ElpisEdgeConnect.Management.Tests/Api/EnableDisableApiTests.cs` | **new** | ~10 tests: status-code mapping (200/409/422/404), response-envelope shape, audit-record id propagation, six-endpoint surface registered, empty body accepted, unknown-id 404. |
| `tests/ElpisEdgeConnect.Management.Tests/Components/EnableDisableConfirmSheetModelTests.cs` | **new** | ~6 tests: opens in pending state, transitions to error state on planner blocker, transitions to success on apply, snackbar text per outcome, cancel resets state, no-op for already-in-desired-state. |

**Total: ~36 tests.** Kickoff estimate was ~15; refinement found that the planner and impact-summary deserve separate test files and that the model layer warrants its own focused tests per P2's POCO discipline. Still well under any single-milestone test-budget concern.

### Documentation

| File | Status | Purpose |
|---|---|---|
| `docs/sessions/<date>-mp2b61-…-handoff.md` | new at milestone close | Standard handoff file per CLAUDE.md §2's session-handoff discipline. |
| `docs/decisions/<NN>-inline-enable-disable-no-cascade.md` | new ADR | Locks Locked C (no cascade) so a future milestone doesn't relitigate. Other locks (verb endpoints, confirm sheet) are stylistic — ADR-worthy only if they generalise beyond M.2b.6.1, which the cascade decision does. |

---

## 5. Premium-UX implementation discipline application

Per M.2b.5/6 v3 plan §3, every Studio milestone re-applies the premium-UX discipline. Re-stated as a checklist for this milestone:

- ✅ **No degradation to plain forms.** The confirm sheet is the shared interaction primitive — not a `confirm()` JS alert.
- ✅ **Pause-and-report on tradeoffs.** Every Q1–Q5 resolution above lists alternatives + reasoning. ChatGPT review's job is to challenge the locked choice, not to discover it.
- ✅ **Operator can finish the task without leaving the list page** (Locked A's confirm sheet, Locked E's inline button).
- ✅ **Audit chain entry is unchanged from any other config apply** — operators get the same explainability surface for toggle actions as for wizard actions.
- ✅ **Forward-compat with M.2d / M.2e** is explicit, not implicit — see each Locked's "forward-compat" notes.

---

## 6. Definition of Done (milestone exit checklist)

Maps to PHASE1_EXECUTION_PLAN §10 discipline; M.2b.6.1 is small enough that the checklist fits in one section.

- [ ] All six API endpoints registered and returning correct status codes per Locked D
- [ ] `EnableDisablePlanner` purity proven by a test that captures the input config object reference and asserts equality after the call
- [ ] Cross-record refusal returns 409 with a `dependents` list that names every blocker (not just the first)
- [ ] Confirm sheet surfaces 409 dependent list as deep-linkable rows
- [ ] Snackbar on success shows audit-record id
- [ ] Hot reload picks up the toggle as a `Modified` classification (ADR-0009 path); existing reload-classifier tests pass unchanged
- [ ] No new test categories — toggle tests join the existing Management test pyramid
- [ ] `dotnet build` 0 warnings, 0 errors
- [ ] `dotnet test --filter "Category!=Flaky"` green across all 7+ projects
- [ ] Sources / Sinks / Routes pages render the action button in dense + non-dense MudDataGrid modes
- [ ] ADR landed for Locked C (no cascade)
- [ ] Smoke pass: replay the kickoff §0 scenario (fresh gateway → wizard sequence → enable each disabled entity via the toggle) end-to-end via Studio at 127.0.0.1:5080

---

## 7. Items deliberately left open for v2

Items I am NOT locking in v1, because ChatGPT review may push back productively:

1. **Confirm sheet — MudDrawer vs MudDialog vs inline expansion under the row.** v1 leaves the rendering primitive open; spec says "a confirm sheet" without committing to which MudBlazor primitive. ChatGPT might prefer one for accessibility reasons.
2. **Status chip styling delta.** Today the status column shows `●Enab.` / `○Disab.`. After this milestone the chip needs to be visually distinct enough from the action button that operators don't double-tap. v2 should lock the chip styling guidance.
3. **Keyboard shortcut affordance.** Per P2 + P6, operators in production lists want a keyboard path. v1 does not commit to one. ChatGPT may want a default keybinding (e.g. `e` to focus enable column on the highlighted row).
4. **Telemetry counters.** Should the apply pipeline emit a metric for `config.toggle.enabled.{kind}` and `config.toggle.disabled.{kind}`? P4 likes structured emissions; v1 leaves this open in case it duplicates the existing apply-counter.
5. **The exact wording of the cross-record error.** v1 sketched copy. v2 should refine for India + Middle East English baseline (P5).

---

## 8. Cadence (next steps)

1. **v1 plan landed (this file)** — 2026-05-19
2. **ChatGPT strategic review pass** — user runs the v1 plan through ChatGPT, returns feedback
3. **v2 amendment file** — `2026-05-2N-mp2b61-inline-enable-disable-plan-v2.md`, folds review feedback into Locked A–E or relaxes the §7 open items
4. **Optional reality-check pass** — scope is small (~700 LOC total), so kickoff §5 marked this as probably skippable. Decide at v2 lock.
5. **v3 lock** — final plan, deliverables sketch frozen, ready to implement
6. **Implementation** — single focused session per kickoff's "~600 LOC + ~150 LOC test, one session" estimate
7. **Handoff doc + milestone close** — standard discipline

---

**End of v1 plan. Awaiting ChatGPT strategic review pass before v2 amendment. No code is written until v3 lock and explicit scope confirmation per the user's standing instruction.**
