# M.2d.2 — Source wizards (v1 plan, brief)

**Status:** v1 — DRAFT, OPEN QUESTIONS BELOW, pending ChatGPT review pass
**Date:** 2026-05-21
**Predecessor (hard precondition):** M.2d.1 — Shared primitives, see [`2026-05-21-m2d1-shared-primitives-plan.md`](2026-05-21-m2d1-shared-primitives-plan.md) (drafted in parallel)
**Estimated size:** ~3-4 days (per roadmap v2 §3.7.2)
**Roadmap reference:** [`2026-05-21-phase2-wrapup-roadmap-v2.md`](2026-05-21-phase2-wrapup-roadmap-v2.md) §3.7.2

---

## 1. Goal

Apply the M.2d.1 shared primitives (`WizardShell`, `WizardValidationBanner`, `WizardActions`, `WizardWatchSlot`, `EditModeContext`) to the three source wizards — Focas2, Brother HTTP, Modbus TCP — so every source wizard renders on the same shell and supports both **Add** (existing) and **Edit** (NEW) flows. As a side track, **backfill the M.P2.4 Q12 Test Connection deferral** by adding a Test Connection button to the Brother HTTP wizard, and **subsume Focas2's existing Browse Controller probe** under the standardised Test Connection pattern so all three source wizards expose the same affordance.

This is one of four M.2d sub-milestones (M.2d.1 primitives → M.2d.2 sources → M.2d.3 sinks/routes → M.2d.4 cross-wizard sweep). Each sub-milestone is its own PR.

---

## 2. Hard precondition — M.2d.1 must land first

M.2d.2 references the components defined in M.2d.1 (`Components/Shared/WizardShell.razor`, `Wizards/EditModeContext.cs`, etc.). **Do not start M.2d.2 implementation until M.2d.1 is merged to master.** If M.2d.1 reality-check reshapes any of the shared component APIs, this plan re-runs its reality-check pass before implementation.

The M.2d.1 plan defines:
- The `WizardShell` slot contract (header / numbered sections / footer / save state).
- The `WizardActions` button contract (Save / Cancel / Test Connection / Browse — semantic naming TBD in M.2d.1 v3).
- The `EditModeContext` Add-vs-Edit discriminator and the hydration helper that loads an existing `SourceInstanceConfig` into a wizard model.

If any of those contracts shift, the relevant section here updates in v2.

---

## 3. Per-wizard work breakdown

### 3.1 Focas2 (`AddFocas2Source.razor`)

| Area | Change |
|---|---|
| Shell | Adopt `WizardShell` — replace the hand-rolled `MudPaper`/`MudStack` header + 5 sections + actions footer with the shared shell. Section labels unchanged. |
| Edit mode | Adopt `EditModeContext`. Route becomes `@page "/sources/new/focas2"` (Add — existing) **and** `@page "/sources/{InstanceId}/edit"` resolving to this same wizard when the source is Focas2. Hydrate `Focas2SourceWizardModel` from the existing `SourceInstanceConfig` on Edit. |
| Test Connection | **Existing Browse Controller subsumed.** The `Browse Controller` button (and its rich result panel — axes, tag count, CNC series/type) is renamed/restructured to fit the standardised Test Connection slot from M.2d.1 `WizardActions`. The underlying endpoint **stays at `/api/v1/sources/browse/focas2`** (already shipped under M.2b.3); the wizard simply calls it via the shared button. Rich result panel stays — Focas2 is the only source with a "what tags will I get?" affordance, and that data is high-value for operators. **Naming question:** does the operator-facing label remain "Browse Controller" (Focas2-specific UX) or generalise to "Test Connection" (consistent with other wizards)? Surface as v1 open question — see §9 Q-M2D2-N1. |
| Save flow | Edit mode uses an **update-existing** path (replace the `SourceInstanceConfig` in the draft, preserve any routes already wired to it). Add mode unchanged. Route id collision behaviour for Edit needs review — see §9 Q-M2D2-N3. |

### 3.2 Brother HTTP (`AddBrotherHttpSource.razor`)

| Area | Change |
|---|---|
| Shell | Adopt `WizardShell`. Section labels unchanged. |
| Edit mode | Adopt `EditModeContext`. Route becomes `@page "/sources/new/brother-http"` and `@page "/sources/{InstanceId}/edit"` (when the resolved source is `brother-http`). Hydrate `BrotherHttpSourceWizardModel` from the existing `SourceInstanceConfig`. |
| **Test Connection (NEW)** | **This is the M.P2.4 Q12 backfill.** Add a Test Connection button via M.2d.1 `WizardActions`. POSTs the current wizard state's `SourceInstanceConfig` to a **NEW endpoint** `/api/v1/sources/browse/brother-http`. The endpoint fires a single `GET <BaseUrl>/HTTPD_MCNINFO` with a short timeout (≤8s, matching Focas2's probe budget); success means HTTP 200 with parseable body. Returns a probe DTO modelled on `Focas2BrowseResultDto` (Success flag + ErrorCode + ErrorMessage + ProbeId + Warnings). NO full 6-endpoint probe in v1 — that's the "Probe Endpoints" follow-up mentioned in M.P2.4 handoff §6. |
| Save flow | Edit mode replaces existing, Add mode unchanged. |

### 3.3 Modbus TCP (`AddModbusSource.razor`)

| Area | Change |
|---|---|
| Shell | Adopt `WizardShell`. Section labels unchanged. |
| Edit mode | Adopt `EditModeContext`. Route becomes `@page "/sources/new/modbus"` and `@page "/sources/{InstanceId}/edit"` (when the resolved source is `modbus`). Hydrate `ModbusSourceWizardModel` from existing `SourceInstanceConfig` — including the per-tag list (which has the most state of any source wizard model). |
| **Test Connection (NEW)** | Add a Test Connection button via M.2d.1 `WizardActions`. POSTs to a **NEW endpoint** `/api/v1/sources/browse/modbus`. What "probe" means for Modbus is a **v1 open question** — see §9 Q-M2D2-N2. Recommended v1 default: TCP connect + a single read of Holding Register 0 against the configured unit-id, with a ≤8s timeout. Returns a probe DTO modelled on Focas2's. |
| Save flow | Edit mode replaces existing, Add mode unchanged. Per-tag list edit semantics (add / remove / reorder) carry through to Edit mode without change — operators can rework the tag list in Edit just like Add. |

---

## 4. Test Connection contract — common shape across all three source probes

The MQTT Test Connection probe (`/api/v1/sinks/test-connection/mqtt`) and the Focas2 Browse Controller probe (`/api/v1/sources/browse/focas2`) already model the pattern. M.2d.2 unifies the URL prefix at `/api/v1/sources/browse/{protocolName}` (Focas2 stays put; Brother + Modbus land alongside).

**Request shape:** canonical `SourceInstanceConfig` (matches Focas2 today).

**Response DTO (common fields):**

| Field | Purpose |
|---|---|
| `Success: bool` | Truth flag. |
| `ErrorCode: string?` | `BROTHER.PROBE_*` / `MODBUS.PROBE_*` / `FOCAS2.*` namespace; `LICENSE.MODULE_DISABLED` and `*.PROBE_BUSY` shared. |
| `ErrorMessage: string?` | Operator-facing one-line. |
| `ProbeId: string` | Correlation id, surfaced in the wizard for support tickets. |
| `ElapsedMs: long` | Probe round-trip duration. |
| `Warnings: IReadOnlyList<string>` | Non-fatal observations. |

**Status code mapping (per probe, matches existing Focas2 + MQTT contract):**

- `Success=true` → 200
- `LICENSE.MODULE_DISABLED` → 403
- `*.PROBE_BUSY` (single-flight rejection) → 409
- `*.CONFIG_INVALID` → 400
- Controller / broker / device reachability errors → **200 with `Success=false`** so the wizard renders the structured error inline rather than hitting the fetch error path. (Critical UX rule already locked under Focas2.)

**Per-protocol probe semantics (v1):**

| Protocol | What "probe" does |
|---|---|
| Focas2 | Existing throwaway-adapter Initialize/Start/BrowseTags cycle (Locked S — single attempt, ≤8s timeout). Rich axes + tag count + CNC identity response. **No change in v1.** |
| Brother HTTP | Single `GET {BaseUrl}/HTTPD_MCNINFO`, ≤8s timeout. Success = HTTP 200 + body parseable as the offline-marker file. Lean response — no tag enumeration, no 6-endpoint sweep. |
| Modbus TCP | **v1 open — see §9 Q-M2D2-N2.** Recommendation: TCP connect + read Holding Register 0 against unit-id, ≤8s timeout. NOT a full per-configured-tag dry run. |

**What probes MUST NOT do** (locked invariants inherited from Focas2 + MQTT probes):
- Mutate any state — no writes to controller / device / broker.
- Persist anything — no draft mutation, no log spam, no metrics leakage beyond the probe-id-correlated diagnostic record.
- Block longer than 8s.
- Run more than one concurrent probe per protocol — `*.PROBE_BUSY` rejection enforces this.

---

## 5. Edit mode — entry points and save-replace semantics

### 5.1 Entry points (NEW in M.2d.2)

| Trigger | Destination |
|---|---|
| `/sources` list → click row → `SourceDetail` page → **Edit button (NEW)** | `/sources/{instanceId}/edit` |
| Routing knows the source's `ProtocolName` from `SourceInstanceConfig.ProtocolName` and renders the matching wizard. |

The shared route `@page "/sources/{InstanceId}/edit"` lives on each protocol's wizard — when hit, the wizard:
1. Loads the existing `SourceInstanceConfig` for `InstanceId` (via `/api/v1/config` or a new convenience endpoint — reality-check in v3).
2. Verifies `ProtocolName == focas2 | brother-http | modbus`. Mismatch → 404 / redirect to the right wizard.
3. Hydrates `*SourceWizardModel` from the loaded config using `EditModeContext.HydrateFromExisting(...)`.
4. Renders the same shell + sections as Add, with `EditModeContext.Mode == Edit`. UI surfaces "Editing existing source" indicator (M.2d.1 contract).

### 5.2 Save-replace semantics

On Edit-mode Save:
- The wizard builds the updated `SourceInstanceConfig` from the model.
- The merger (new method on `WizardConfigMerger` — `BuildUpdatedSourceDraft`) replaces the matching `SourceInstanceConfig` in the draft, **preserving routes already wired to it**.
- Instance-id is **immutable in Edit mode** — operators cannot rename a source via the wizard. Renaming = delete + add (separate flow). This avoids cascading rename surprises through routes. See §9 Q-M2D2-N3 for the rationale.
- The "Routing" section behaves differently in Edit vs Add: Edit shows existing route(s) referencing this source (read-only summary + link to Route editor), no "create new route" branch unless source is currently unrouted.

### 5.3 Sources list → Edit button location

A new "Edit" button on `SourceDetail.razor` navigates to `/sources/{instanceId}/edit`. Optionally also surfaced as a row action on `Sources.razor` (the list). Inline-list Enable/Disable from M.2b.6.1 already there; Edit slots alongside.

---

## 6. Deliverables

### 6.1 Wizard file edits

| File | Change |
|---|---|
| `Components/Pages/SourceWizards/AddFocas2Source.razor` | Adopt `WizardShell` + `EditModeContext`. Browse Controller routed through `WizardActions` Test Connection slot (preserving rich result panel). |
| `Components/Pages/SourceWizards/AddBrotherHttpSource.razor` | Adopt `WizardShell` + `EditModeContext`. Add Test Connection button. |
| `Components/Pages/SourceWizards/AddModbusSource.razor` | Adopt `WizardShell` + `EditModeContext`. Add Test Connection button. |
| `Components/Pages/SourceDetail.razor` | Add Edit button → `/sources/{instanceId}/edit`. |
| `Components/Pages/Sources.razor` | Optional inline Edit row action (parity with M.2b.6.1 Enable/Disable). v3 decides. |

### 6.2 Wizard model changes

| File | Change |
|---|---|
| `Wizards/Focas2SourceWizardModel.cs` | Add `HydrateFromExisting(SourceInstanceConfig)` factory or extension consumed by `EditModeContext`. |
| `Wizards/BrotherHttpSourceWizardModel.cs` | Same. |
| `Wizards/ModbusSourceWizardModel.cs` | Same — must round-trip per-tag list precisely. |
| `Wizards/WizardConfigMerger.cs` | New method `BuildUpdatedSourceDraft(GatewayConfiguration current, SourceInstanceConfig updated)` — replaces source, preserves routes. |

### 6.3 New Management API endpoints

| File | Purpose |
|---|---|
| `Api/BrotherHttpProbeApi.cs` | `MapPost("/api/v1/sources/browse/brother-http", ...)`. Fires `GET {BaseUrl}/HTTPD_MCNINFO`. Status mapping mirrors Focas2 + MQTT. |
| `Api/BrotherHttpProbeService.cs` | Probe orchestration — license gate, single-flight lease, throwaway HTTP call via `IHttpClientFactory`. |
| `Api/BrotherHttpProbeResultDto.cs` | Response DTO (matches §4 common shape). |
| `Api/ModbusProbeApi.cs` | `MapPost("/api/v1/sources/browse/modbus", ...)`. Probe semantic per §9 Q-M2D2-N2 v3 verdict. |
| `Api/ModbusProbeService.cs` | Probe orchestration. |
| `Api/ModbusProbeResultDto.cs` | Response DTO. |
| `Program.cs` (Management) | Register new probe services + map new endpoints. |

### 6.4 Tests (target: ~60 new across the sub-milestone)

| Suite | Coverage |
|---|---|
| `BrotherHttpProbeServiceTests` | License-gated, single-flight, success path, timeout, HTTP-status-code rejection. |
| `BrotherHttpProbeApiTests` | Status code mapping per §4. |
| `ModbusProbeServiceTests` | License-gated, single-flight, success path, TCP-refused / TCP-timeout / register-read-failure shaping. |
| `ModbusProbeApiTests` | Status code mapping. |
| `Focas2SourceWizardModelTests` | Round-trip — hydrate from existing config → re-emit `SourceInstanceConfig` → byte-equivalence. |
| `BrotherHttpSourceWizardModelTests` | Same round-trip. |
| `ModbusSourceWizardModelTests` | Same round-trip (per-tag list is the tricky case). |
| `WizardConfigMergerTests` | `BuildUpdatedSourceDraft` — replaces matching source, preserves routes, rejects instance-id change. |
| Edit-mode page-model tests (one per protocol) | Edit route hits the right wizard, hydration succeeds, instance-id field is immutable. |

---

## 7. Definition of done (from v2 §3.7.2)

- [ ] All three source wizards render on `WizardShell`.
- [ ] All three source wizards expose Test Connection via `WizardActions`. Focas2's rich Browse panel preserved.
- [ ] All three source wizards support Add (existing) and Edit (NEW) flows.
- [ ] Edit-mode page tests green across all three protocols.
- [ ] Brother HTTP and Modbus probe endpoints land with the same contract as Focas2 / MQTT — license-gated, single-flight, status-mapped.
- [ ] M.P2.4 Q12 deferral closed — `docs/sessions/2026-05-21-mp24-handoff.md` §6 updated.
- [ ] Cumulative test count delta ≈ +60 (per roadmap v2 §4.1 trajectory).
- [ ] 0 new warnings; `TreatWarningsAsErrors` honoured.

---

## 8. Step-by-step implementation sequence

1. **Confirm M.2d.1 has landed on master.** If not, stop — this plan is blocked.
2. **Reality-check v3 pass on this plan** — verifies M.2d.1 component contracts haven't shifted, surfaces any §9 open questions to user.
3. **Brother HTTP probe service + endpoint** (no wizard changes yet). New `BrotherHttpProbeApi` + `BrotherHttpProbeService` + DTO. Pin status-mapping tests. **Discrete commit.**
4. **Modbus probe service + endpoint** (no wizard changes yet). Same shape as Brother. Pin status-mapping tests. **Discrete commit.**
5. **`WizardConfigMerger.BuildUpdatedSourceDraft`** + merger tests. **Discrete commit.**
6. **Edit-mode hydration helpers** on the three wizard models + round-trip tests. **Discrete commit.**
7. **`AddFocas2Source.razor`** — adopt WizardShell, route Browse Controller through Test Connection slot, add `/sources/{InstanceId}/edit` route + page-model test. **Discrete commit.**
8. **`AddBrotherHttpSource.razor`** — adopt WizardShell, add Test Connection button wired to new endpoint, add Edit route + page-model test. **Discrete commit.**
9. **`AddModbusSource.razor`** — adopt WizardShell, add Test Connection button wired to new endpoint, add Edit route + page-model test. **Discrete commit.**
10. **`SourceDetail.razor`** Edit button + optional `Sources.razor` row action. **Discrete commit.**
11. **Manual verification** end-to-end in Studio: Add a Brother source → Test Connection passes against demo-mode fixture → Save → Edit existing source → instance-id immutable, other fields editable → Save → diff confirms route preserved. Repeat for Focas2 + Modbus.
12. **Close M.P2.4 §6 Q12 deferral** in the handoff doc.

---

## 9. Open questions (for v2 ratification / v3 reality-check)

### Carried verbatim from roadmap v2 §5.2

- **Q26** — Does the existing `/api/v1/sources/focas2/probe` endpoint shape generalize to Brother, or does Brother need its own probe-endpoint contract? Reality-check before M.2d.2 starts.
  - **Reality-check note (this plan):** the actual endpoint is `/api/v1/sources/browse/focas2`, not `/api/v1/sources/focas2/probe`. The shape (canonical `SourceInstanceConfig` request → `Focas2BrowseResultDto` response with structured status mapping) does generalise — §4 promotes it to the shared contract. Roadmap §5.2 Q26 wording should be corrected in a v2.X amendment.

### New v1-specific open questions

- **Q-M2D2-N1 — Focas2 Test Connection labelling.** Should the operator-facing button label remain "Browse Controller" (Focas2-specific, preserves operator familiarity, matches the rich axes/tag-count response panel) or generalise to "Test Connection" (consistent vocabulary across all three source wizards)? Recommendation: keep "Browse Controller" as the Focas2 label since the affordance does more than test connectivity — it discovers tags. The shared `WizardActions` slot accepts a label override.
- **Q-M2D2-N2 — Modbus probe semantic.** What does "probe" actually do for Modbus TCP in v1?
  - (a) TCP connect only — fastest, but doesn't validate the device is actually a Modbus server.
  - (b) TCP connect + single read of Holding Register 0 against the configured unit-id (recommendation). Validates the slave responds.
  - (c) TCP connect + read of one configured tag from the wizard's tag list. Closer to a real-world dry run but couples probe to per-tag configuration completeness.
  - Recommend (b); resolve in v2.
- **Q-M2D2-N3 — Edit mode instance-id immutability + route cascading.** If an operator could rename a source's instance-id in Edit, every `RouteConfig.SourceInstanceId` referencing it would need a cascading rewrite. Recommendation: **instance-id immutable in Edit** (locked above in §5.2). Rename = delete + add. Confirm in v2.
- **Q-M2D2-N4 — Edit-mode loading endpoint.** Does `/api/v1/config` return enough state to hydrate the wizard, or should a new convenience endpoint `/api/v1/sources/{instanceId}` exist? `SourcesApi.cs` already exists — reality-check whether it has a `GET /{instanceId}` route or only the list endpoint.
- **Q-M2D2-N5 — Edit-mode draft seeding.** When opening Edit, do we start from the on-disk `current.json` or from any pending draft for the same source? Mixed answer: probably start from `current.json` (the source as it actually runs); show a warning banner if a pending draft already modifies the same source. v3 reality-check.
- **Q-M2D2-N6 — Brother probe DTO reuse.** Should `BrotherHttpProbeResultDto`, `ModbusProbeResultDto`, and `Focas2BrowseResultDto` extract a shared `SourceProbeResultDto` base, or stay independent record types? Recommendation: independent for v1 (Focas2's rich axes/tag-count payload doesn't generalise; forcing it into a base risks awkward optional-field bloat). M.2d.4 cross-wizard sweep can revisit.
- **Q-M2D2-N7 — Brother probe single-flight scope.** Is `*.PROBE_BUSY` keyed per protocol globally (one Brother probe in flight at a time, system-wide) or per-instance (one Brother probe per `InstanceId` at a time)? Focas2's existing lease is per-call (effectively global). Recommend per-protocol-global for v1 — matches Focas2 — and revisit if the customer hits contention.

These are reality-check items — not architectural decisions. They get resolved during the v3 pass without re-opening v2.

---

## 10. Cross-references

- Roadmap: [`2026-05-21-phase2-wrapup-roadmap-v2.md`](2026-05-21-phase2-wrapup-roadmap-v2.md) §3.7.2 (M.2d.2 scope), §5.2 Q26 (Brother probe contract), §4.1 (test trajectory)
- Roadmap amendments: [`2026-05-21-phase2-wrapup-roadmap-v2.3.md`](2026-05-21-phase2-wrapup-roadmap-v2.3.md) §1.1 (no new shared abstractions outside M.2d.1's contract), §1.2 (terminology freeze)
- Sibling plans (drafted in parallel):
  - [`2026-05-21-m2d1-shared-primitives-plan.md`](2026-05-21-m2d1-shared-primitives-plan.md) — HARD PRECONDITION
  - [`2026-05-21-m2d3-sink-route-editors-plan.md`](2026-05-21-m2d3-sink-route-editors-plan.md)
  - [`2026-05-21-m2d4-cross-wizard-sweep-plan.md`](2026-05-21-m2d4-cross-wizard-sweep-plan.md)
- M.P2.4 (Brother) handoff: [`2026-05-21-mp24-handoff.md`](2026-05-21-mp24-handoff.md) §6 (Q12 Test Connection deferral — backfilled here)
- M.2b.3 (Focas2) v3 plan: [`2026-05-17-mp2b3-focas2-wizard-plan-v3.md`](2026-05-17-mp2b3-focas2-wizard-plan-v3.md) — current Browse Controller contract
- Existing probe contracts:
  - `src/ElpisEdgeConnect.Management/Api/Focas2BrowseApi.cs` (`POST /api/v1/sources/browse/focas2`)
  - `src/ElpisEdgeConnect.Management/Api/MqttTestConnectionApi.cs` (`POST /api/v1/sinks/test-connection/mqtt`)
- Architecture: `docs/ARCHITECTURE_BLUEPRINT.md` Appendix A locked decisions #4 (modular assemblies), #10 (per-adapter isolation), #18 (3-way diagnostics)
- Reference plan structure: [`2026-05-20-mp24-brother-http-plan.md`](2026-05-20-mp24-brother-http-plan.md)

---

**End of v1 plan trail. OPEN QUESTIONS — ready for ChatGPT review pass before v2.**
