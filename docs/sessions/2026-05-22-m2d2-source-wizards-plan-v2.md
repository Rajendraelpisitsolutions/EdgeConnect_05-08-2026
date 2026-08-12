# M.2d.2 — Source wizards (v2 plan, post-review)

**Status:** v2 — REVIEW-INCORPORATED, ready for v3 reality-check before implementation
**Date:** 2026-05-22
**Predecessor (hard precondition):** M.2d.1 — Shared primitives, merged in `1dfd1d1` (see [`2026-05-21-m2d1-shared-primitives-plan-v2.md`](2026-05-21-m2d1-shared-primitives-plan-v2.md))
**v1 source:** [`2026-05-21-m2d2-source-wizards-plan.md`](2026-05-21-m2d2-source-wizards-plan.md) (255 lines, 7 open questions Q-M2D2-N1..N7)
**Estimated size:** ~4 days (per roadmap v2 §3.7.2, adjusted up from v1's 3-4 days for the resolver + concurrency work)
**Roadmap reference:** [`2026-05-21-phase2-wrapup-roadmap-v2.md`](2026-05-21-phase2-wrapup-roadmap-v2.md) §3.7.2

---

## 0. What changed from v1

Two ChatGPT review passes incorporated. Ten amendments — six required, four optional-but-strongly-recommended.

### From pass 1 (REQUIRED)

| # | v1 problem | v2 fix |
|---|---|---|
| R1 | Three protocol wizards each declaring `@page "/sources/{InstanceId}/edit"` — Blazor route ambiguity | New `SourceEditRouter.razor` resolver page; protocol wizards keep only `/sources/new/<protocol>` (§5.1) |
| R2 | Edit-mode hydration "probably start from current.json" was under-specified | Locked precedence: draft-if-exists, else current; banner copy locked (§5.3) |
| R3 | `BuildUpdatedSourceDraft` "replace matching source" — no collision detection | Activate `DraftMetadataDto.ExpectedCurrentVersionId` reservation; server returns 409 on stale save (§5.5) |
| R4 | Q-M2D2-N7 said "per-protocol global lease, matches Focas2" — Focas2 is **already target-keyed**; v1 was factually wrong | Brother key = `BaseUrl`; Modbus key = `{IpAddress}:{Port}:{UnitId}`; Focas2 unchanged (§4) |
| R5 | Q-M2D2-N2 recommended Holding Register 0 — unsafe across vendors | Modbus probe ladder: TCP connect → first configured tag → configurable test address (HR1 / FC03 default) (§4.3) |
| R6 | (Direct corollary of R1) duplicate edit routes on protocol wizards | Removed; routes are `/sources/new/<protocol>` only |

### From pass 1 (STRONGLY RECOMMENDED)

| # | Concern | v2 resolution |
|---|---|---|
| S1 | "No-op edit MUST NOT restart" needs new invariant | Already owned by `RuntimeReloadClassifier` + ADRs 0009/0010. Wizard trusts the classifier and proves it with one integration test (§5.6) |
| S2 | Probe DTOs may need protocol-specific extensions | Locked: shared status/transport fields; protocol-private result panels (Focas2 axes, future OPC UA namespace tree, etc.) are independent (§4.5) |
| S3 | Test count 60 → realistic 80-100 | v2 target: **~85-100** new tests (§6.4) |

### From pass 2 (POST-REVISED-DIRECTION)

| # | Concern | v2 resolution |
|---|---|---|
| P1 | `SourceEditRouter` needs explicit handling for unknown / unlicensed / wizard-missing protocols | Four-branch UX matrix (§5.2) |
| P2 | Modbus probe diagnostic metadata for field support | `FunctionCodeUsed`, `AddressTested`, `UnitIdTested`, `EndpointTested`-style fields locked in `ModbusProbeResultDto` (§4.4) |
| P3 | Explicit `InstanceId` vs `DisplayName` mutability table | `SourceInstanceConfig.DeviceName` already exists. No schema change; explicit mutability table in §5.4 |
| P4 | M.2d.3 sinks/routes must mirror the resolver pattern, not adopt v1's duplicate-route approach | Cross-reference + v2-precondition note for M.2d.3 (§10) |

### Closed open questions

| Q | v2 resolution |
|---|---|
| Q-M2D2-N2 (Modbus probe semantic) | TCP connect → first configured tag → configurable test address (HR1, FC03). See §4.3. |
| Q-M2D2-N5 (Edit draft-vs-current) | Draft if exists, else current. Banner copy locked. See §5.3. |
| Q-M2D2-N7 (probe single-flight scope) | Target-keyed per protocol (existing Focas2 + MQTT behaviour). See §4.2. |

### Locked in v2 (v3 verifies, doesn't relitigate)

- **Q-M2D2-N1** — keep "Browse Controller" label (§3.1)
- **Q-M2D2-N3** — instance-id immutable (§5.4)
- **Q-M2D2-N6** — independent probe DTOs (§4.5)

These are settled — v3 only confirms the implementation matches.

### Resolved by v3 reality-check (2026-05-22)

- **Q-M2D2-N4** — `SourcesApi.cs:69` already exposes `GET /api/v1/sources/{sourceInstanceId}`. `SourceEditRouter.razor` consumes the existing endpoint; no new API surface needed.
- **`ILicenseManager.IsModuleEnabled(string moduleKey)`** confirmed present in `ILicenseManager.cs:47`; `ILicenseGate` wraps it. §5.2 "license module disabled" branch has a real API to call.
- **`SourceInstanceConfig.InstanceId`** confirmed init-only (`required string InstanceId { get; init; }`); the immutability locked in §5.4 is enforceable by the type system, not just by wizard UI.

---

## 1. Goal

Apply the M.2d.1 shared primitives (`WizardShell`, `WizardActions`, `WizardValidationBanner`, `WizardWatchSlot`, `WizardSection`, `WizardSaveState`, `EditModeContext`) to the three source wizards — Focas2, Brother HTTP, Modbus TCP — so every source wizard renders on the same shell and supports both **Add** (existing) and **Edit** (NEW) flows.

Edit-mode entry is via a new resolver page `SourceEditRouter.razor` which dispatches by `ProtocolName`, with explicit UX for unknown / unlicensed / wizard-missing protocols.

As side tracks: **backfill the M.P2.4 Q12 Test Connection deferral** by adding a Test Connection button to the Brother HTTP wizard, **add Modbus Test Connection** with a safe escalation-ladder probe, and **subsume Focas2's existing Browse Controller probe** under the standardised Test Connection contract.

This is one of four M.2d sub-milestones (M.2d.1 primitives → **M.2d.2 sources** → M.2d.3 sinks/routes → M.2d.4 cross-wizard sweep). M.2d.3 will mirror the resolver pattern from §5 of this plan.

---

## 2. Hard precondition — M.2d.1 (already merged)

M.2d.1 landed in commit `1dfd1d1`. The primitives M.2d.2 consumes:

- `Components/Shared/WizardShell.razor` — slot contract (header, sections, footer, save state)
- `Components/Shared/WizardSection.razor` — numbered-section slot
- `Components/Shared/WizardActions.razor` — Save / Cancel / Test Connection / Browse slots
- `Components/Shared/WizardValidationBanner.razor` — error/warning surface
- `Components/Shared/WizardWatchSlot.razor` — secondary-content slot
- `Components/Shared/WizardSaveState.cs` — save-progress model
- `Wizards/EditModeContext.cs` — Add-vs-Edit discriminator + hydration helper

No M.2d.1 reality-check needed for v2 — the contracts the v1 plan referenced all shipped intact, plus `WizardSection` and `WizardSaveState` as bonus structuring primitives.

---

## 3. Per-wizard work breakdown

### 3.1 Focas2 (`AddFocas2Source.razor`)

| Area | Change |
|---|---|
| Shell | Adopt `WizardShell` + `WizardSection`. Section labels unchanged. |
| Edit mode | **No new `@page` directive.** Add mode keeps `@page "/sources/new/focas2"`. Edit mode is reached via `SourceEditRouter.razor` (§5), which renders this component with `EditModeContext.Mode = Edit` and a pre-hydrated `Focas2SourceWizardModel`. |
| Test Connection | **Existing Browse Controller subsumed.** The Browse Controller button (with its rich axes / tag count / CNC identity panel) is rendered via `WizardActions.TestConnectionSlot`. Underlying endpoint `/api/v1/sources/browse/focas2` unchanged. **Label remains "Browse Controller"** (Q-M2D2-N1 v2 resolution — Focas2's affordance does more than test connectivity, it discovers tags; the slot accepts label override). |
| Save flow | Edit mode → `WizardConfigMerger.BuildUpdatedSourceDraft` (replace source body, preserve routes, optimistic-concurrency guarded — §5.5). Add mode unchanged. |

### 3.2 Brother HTTP (`AddBrotherHttpSource.razor`)

| Area | Change |
|---|---|
| Shell | Adopt `WizardShell` + `WizardSection`. Section labels unchanged. |
| Edit mode | Same router pattern as Focas2. No new `@page` on this component. |
| **Test Connection (NEW)** | **M.P2.4 Q12 backfill.** Test Connection button via `WizardActions.TestConnectionSlot`. POSTs the current wizard's `SourceInstanceConfig` to new endpoint `/api/v1/sources/browse/brother-http`. Endpoint fires a single `GET {BaseUrl}/HTTPD_MCNINFO`, ≤8s timeout, success = HTTP 200 + parseable body. Returns `BrotherHttpProbeResultDto` (common shape, §4). NO 6-endpoint sweep in v1. |
| Save flow | Same merger path as Focas2. |

### 3.3 Modbus TCP (`AddModbusSource.razor`)

| Area | Change |
|---|---|
| Shell | Adopt `WizardShell` + `WizardSection`. Section labels unchanged. |
| Edit mode | Same router pattern. No new `@page`. Per-tag list edit semantics carry through to Edit without change. |
| **Test Connection (NEW)** | Test Connection button via `WizardActions.TestConnectionSlot`. POSTs to new `/api/v1/sources/browse/modbus`. **Probe ladder per §4.3** (TCP connect → first configured tag → configurable test address, FC03/HR1 default). Returns `ModbusProbeResultDto` with diagnostic fields (§4.4). |
| Save flow | Same merger path. |

---

## 4. Test Connection contract — common shape

### 4.1 URL and request shape

Unified prefix: `POST /api/v1/sources/browse/{protocolName}`. Focas2 already lives there; Brother and Modbus land alongside.

Request body: canonical `SourceInstanceConfig`.

### 4.2 Single-flight lease (target-keyed)

**Reality-check finding:** `Focas2BrowseService.cs:162` and `MqttTestConnectionService.cs:158` already key leases on the **target identity**, not the protocol globally. v2 applies the same pattern to Brother and Modbus.

| Protocol | Lease key |
|---|---|
| Focas2 | `"{IpAddress}:{Port}"` (unchanged — already shipped) |
| Brother HTTP | `BaseUrl` (normalised: lowercase host, trailing-slash stripped) |
| Modbus TCP | `"{IpAddress}:{Port}:{UnitId}"` |
| MQTT (sink) | `"{BrokerHost}:{BrokerPort}"` (unchanged — already shipped) |

Two engineers commissioning two different PLCs in parallel must not contend on each other's probes. Lease is held only for the duration of a single probe call.

### 4.3 Modbus probe ladder (Q-M2D2-N2 resolution)

The Holding Register 0 default proposed in v1 is **rejected** — many vendors offset addressing, some don't expose register 0, some restrict function codes, and false negatives generate support tickets.

v2 escalation ladder:

1. **TCP connect** to `{IpAddress}:{Port}`, ≤2s timeout. Failure → `MODBUS.PROBE_TCP_REFUSED` / `MODBUS.PROBE_TCP_TIMEOUT`.
2. **If the wizard model already has tags configured**, probe the **first configured tag** using its function code and address. Validates real-world readability of an operator-chosen address.
3. **Else**, probe a **configurable test address**, defaulting to:
   - Function code: `FC03` (Read Holding Registers)
   - Address: `1` (not 0 — many PLCs reject address 0)
   - Quantity: `1` register

   **Where the configurable values live:** wizard-only **transient probe parameters**, NOT persisted into `SourceInstanceConfig`. The wizard holds an in-memory `ModbusProbeOverrides { FunctionCode, Address, Quantity }` struct that the operator can tweak via a small "Probe options" disclosure in the Test Connection slot when the default probe fails. The struct is sent as part of the probe request body but **never round-tripped into the saved source config**. If a customer needs a vendor-specific probe address persistently, that's a future enhancement to `SourceInstanceConfig` requiring its own design pass — not in v1.

All steps ≤8s end-to-end. Probe must never write to the device.

### 4.4 Response DTO (common fields + protocol-specific extensions)

Common fields (all protocols):

| Field | Purpose |
|---|---|
| `Success: bool` | Truth flag. |
| `ErrorCode: string?` | `BROTHER.PROBE_*` / `MODBUS.PROBE_*` / `FOCAS2.*` namespace; `LICENSE.MODULE_DISABLED` and `*.PROBE_BUSY` shared. |
| `ErrorMessage: string?` | Operator-facing one-line. |
| `ProbeId: string` | Correlation id for support tickets. |
| `ElapsedMs: long` | Round-trip duration. |
| `Warnings: IReadOnlyList<string>` | Non-fatal observations. |

**Modbus-specific diagnostic fields** (P2 review pass, support-troubleshooting value):

| Field | Purpose |
|---|---|
| `FunctionCodeUsed: byte?` | Debugging vendor function-code quirks (FC03 vs FC04 vs FC01). |
| `AddressTested: ushort?` | Support diagnostics — "what address did probe actually try?" |
| `UnitIdTested: byte?` | Multi-drop visibility — confirms the slave id used. |
| `ProbeStepReached: ModbusProbeStep` enum | `TcpConnect` / `FirstConfiguredTag` / `FallbackTestAddress` — tells the operator how far the ladder got. |

**Brother-specific diagnostic fields:**

| Field | Purpose |
|---|---|
| `EndpointTested: string?` | Echoes the full URL probed (e.g. `http://.../HTTPD_MCNINFO`). |
| `HttpStatusObserved: int?` | Raw HTTP status (200 / 401 / 404 / etc.) for diagnosing non-success. |

**Focas2** — keeps its existing rich payload (axes, tag count, CNC series/type). No change.

### 4.5 Protocol-specific payloads — extensibility note

The shared contract standardises **status/transport fields only**. Each protocol's probe-result DTO may carry **protocol-private** result fields (Focas2's axes panel, future OPC UA namespace tree, future S7 DB list). v1 does NOT introduce a shared base record — forcing one risks awkward optional-field bloat. M.2d.4 cross-wizard sweep can revisit if a real common axis emerges.

### 4.6 Status code mapping (locked)

- `Success = true` → 200
- `LICENSE.MODULE_DISABLED` → 403
- `*.PROBE_BUSY` (lease contention) → 409
- `*.CONFIG_INVALID` → 400
- Reachability / device / endpoint errors → **200 with `Success = false`** so the wizard renders the structured error inline rather than triggering the fetch error path. (Existing Focas2 + MQTT invariant.)

### 4.7 Probe-side invariants (locked)

- **No state mutation** — no writes to controller / device / broker / draft / runtime.
- **No persistence** beyond the probe-id-correlated diagnostic record.
- **≤8s total** including all retries.
- **Single in-flight probe per lease key**; second concurrent call gets `*.PROBE_BUSY` 409.

---

## 5. Edit mode

### 5.1 SourceEditRouter — single resolver page

**v1 problem:** three protocol wizards each declaring `@page "/sources/{InstanceId}/edit"` creates Blazor route ambiguity and breaks silently when new protocols arrive.

**v2 design:**

```
/sources/{instanceId}/edit  →  SourceEditRouter.razor
                                ├─ loads SourceInstanceConfig for instanceId
                                ├─ determines hydration source (draft vs current — §5.3)
                                ├─ checks protocol module availability (§5.2)
                                └─ renders the correct protocol wizard component
                                   with EditModeContext.Mode = Edit + hydrated model
```

Protocol wizards keep ONLY their Add-mode route:

- `AddFocas2Source.razor` → `@page "/sources/new/focas2"`
- `AddBrotherHttpSource.razor` → `@page "/sources/new/brother-http"`
- `AddModbusSource.razor` → `@page "/sources/new/modbus"`

Edit-mode rendering happens via component composition inside the router, not via additional `@page` directives. This pattern extends cleanly to future protocols (OPC UA, S7, MTConnect editing, etc.) without route collisions.

### 5.2 Unsupported-protocol UX matrix (NEW from pass 2)

The router resolves `ProtocolName` and renders one of four states:

| State | Condition | UX |
|---|---|---|
| **Render wizard** | Protocol known to this Studio build AND license module enabled AND wizard component registered | Hydrate model, render protocol wizard with `EditModeContext.Mode = Edit` |
| **License module disabled** | Protocol known but `LicenseGate.IsModuleEnabled(protocolName) == false` | `LicenseModuleDisabledPanel` — read-only summary + "Enable {Protocol} module in licensing" CTA. Uses the same `LICENSE.MODULE_DISABLED` error shape as probes. |
| **Unknown protocol** | `ProtocolName` does not match any registered source adapter (e.g. config authored by a newer Studio version) | `UnsupportedProtocolPanel` — read-only JSON/YAML view of the `SourceInstanceConfig` + "Open raw config" escape hatch (links to `/config` page). |
| **Wizard missing** | Protocol registered but no Razor wizard component (future protocols where the adapter ships before the wizard) | `WizardNotAvailablePanel` — "Editing this source is not available in this Studio version. Use the raw config editor." Same escape hatch as Unknown. |

The escape hatch is critical for forward-compatibility: an operator must always be able to inspect (and, via raw-config editor, modify) a source, even if Studio doesn't have a typed wizard for its protocol.

### 5.3 Draft-vs-current hydration precedence + banner copy (Q-M2D2-N5 resolution)

**v1 problem:** "probably start from current.json" left operationally ambiguous behaviour when a pending draft already modifies the same source. Split-brain UX risk: operator's pending edits silently overwritten.

**v2 locked rule:**

| Situation | Hydration source | Banner |
|---|---|---|
| Pending draft modifies this source | **Draft** | **Warning banner** (amber): "Editing pending draft configuration — changes from draft `{draftId}` are loaded. Discard the draft to edit the running configuration instead." |
| No pending draft, OR pending draft doesn't touch this source | **Current (`current.json`)** | **Info banner** (neutral): "Editing runtime configuration — version `{versionId}`." |

The visual distinction is intentional: amber means "you're editing on top of unapplied changes" — operators must immediately understand the implication.

When a draft exists but does NOT touch this source, we still hydrate from current — the draft is only relevant if it specifically modifies this `InstanceId`.

### 5.4 Field mutability table (NEW from pass 2)

**Reality-check finding:** `SourceInstanceConfig` already has the mutability split. `InstanceId` is the immutable identity; `DeviceName` is the mutable operator-friendly label. No schema change needed — just explicit documentation.

| Field | Mutable in Edit? | Notes |
|---|---|---|
| `InstanceId` | **No** | Renaming = delete + add. Avoids cascading `RouteConfig.SourceInstanceId` rewrites. |
| `ProtocolName` | **No** | Cannot change protocol on an existing source. Delete + re-add as a different protocol. |
| `DeviceName` | Yes | Operator-friendly label. Free-text edit. |
| `DeviceId` | Yes | Physical-device identifier. Operators rename PLCs over time. |
| `DeviceClass` | Yes | Editable — affects MQTT topic structure (already covered by reload classifier). |
| `Enabled` | Yes | Already settable inline from M.2b.6.1. |
| `Tags` | Yes | Free-form classification tags. |
| `Connection.*` | Yes | All protocol-specific connection fields. Reload classifier decides reconcile/restart granularity. |
| `Polling.*` | Yes | Polling cadence / batch size / etc. |

Wizard surfaces immutable fields as **disabled inputs with tooltip "Cannot be changed in Edit. Delete and re-add to rename."** rather than hiding them — operators need to see the values even when they can't change them.

### 5.5 Save-replace semantics + optimistic concurrency (R3 from pass 1)

**v1 problem:** "Replace matching `SourceInstanceConfig` in the draft" with no collision detection. Two operators editing the same source → last save silently wins. Catastrophic for large route graphs.

**Reality-check finding:** `DraftMetadataDto.ExpectedCurrentVersionId` is **already reserved** in the contract for exactly this purpose ("optimistic-concurrency token for safe multi-operator workflows"). `IConfigurationManager.CurrentVersionId` is already queryable. M.2d.2 activates the reservation.

**Locked invariant — Edit mode never modifies routes.**

`BuildUpdatedSourceDraft` writes ONLY the `SourceInstanceConfig` body. `RouteConfig` entries are read-only from this code path. This holds even when:

- The source is disabled (`Enabled = false`) in Edit — routes referencing the disabled source are preserved verbatim.
- Connection details change (e.g. Focas2 IP or Modbus UnitId) — routes are unaware of source connection internals.
- `DeviceClass` changes — even though it affects MQTT topic structure, routes are untouched; the reload classifier handles the downstream effect at runtime.
- The source's tag list changes (Modbus per-tag) — routes filter by tag-pattern, not by exact tag membership.

Route changes flow exclusively through the Route wizard (M.2d.3). Anything that wants to delete a route as a side-effect of source deletion goes through a separate "delete source" flow, not Edit. This is enforced in the merger with a contract test: feed an `updated: SourceInstanceConfig` whose `InstanceId` matches a routed source and assert the post-merge `RouteConfig[]` is byte-identical to pre-merge.

**Flow:**

1. **Edit hydration** — Router captures `BaseVersionId = currentManager.CurrentVersionId` when loading the source. Stored in `EditModeContext.BaseVersionId`.
2. **Wizard save** — POST body includes `BaseVersionId`.
3. **Server-side `WizardConfigMerger.BuildUpdatedSourceDraft`** — checks `BaseVersionId == currentManager.CurrentVersionId`:
   - Match → proceed: replace `SourceInstanceConfig`, **leave all `RouteConfig` entries untouched** (invariant above), return new draft.
   - Mismatch → return `409 Conflict` with `ConfigVersionMismatchDto { BaseVersionId, CurrentVersionId, ChangedSinceUtc }`.
4. **Wizard on 409** — render `StaleEditWarningBanner`: *"Configuration was updated by another session at {ChangedSinceUtc}. Reload to see the latest state — your edits will be discarded."* Single button: **Reload**. No automatic merge in v1 — too risky.

This is **minimum-viable collision detection.** No ETags everywhere, no merge engine, no document snapshots. Activates a contract field already designed for the purpose. ~1 DTO field, 1 server guard, 1 wizard banner, ~3 tests.

### 5.6 No-op edits and the reload classifier (S1 from pass 1)

**Reality-check finding:** `RuntimeReloadClassifier` (Core) + `RuntimeReloadCoordinator` (Host) already own the "what kind of reload does this change require" decision — ADRs 0009 + 0010 lock the contract, `RuntimeReloadClassifierTests` covers it.

The wizard layer **does not need its own no-op-no-restart invariant**. It needs to **trust the classifier and prove it through the Edit path** with one integration test:

```
EditMode_CosmeticOnlyChange_DoesNotRestartSource
  Arrange: existing focas2 source running, BaseVersionId captured
  Act: open Edit, change DeviceName only, save through full Apply path
  Assert: ReloadOutcomeDto.AffectedInstances does NOT include this InstanceId
  Assert: source runtime state stays Started throughout
```

If the assertion ever fails, the bug is in `RuntimeReloadClassifier`, not the wizard — that's the right architectural placement.

### 5.7 Entry points

| Trigger | Destination |
|---|---|
| `/sources` list → click row → `SourceDetail` page → **Edit button (NEW)** | `/sources/{instanceId}/edit` (resolves via `SourceEditRouter`) |
| `/sources` list → **inline Edit row action (NEW)** | Same destination |

Both entry points are locked **in** for v2 (v1 left the inline row action as v3-decides). Operators expect parity with the Enable/Disable inline action from M.2b.6.1.

---

## 6. Deliverables

### 6.1 Wizard file edits

| File | Change |
|---|---|
| `Components/Pages/SourceWizards/AddFocas2Source.razor` | Adopt `WizardShell` + `WizardSection` + `EditModeContext`. Browse Controller wired through `WizardActions.TestConnectionSlot`. **Remove any `@page "/sources/{InstanceId}/edit"` directive** if present. |
| `Components/Pages/SourceWizards/AddBrotherHttpSource.razor` | Same. Add Test Connection button. |
| `Components/Pages/SourceWizards/AddModbusSource.razor` | Same. Add Test Connection button. |
| **`Components/Pages/SourceEditRouter.razor` (NEW)** | Resolver page. `@page "/sources/{InstanceId}/edit"` lives here and only here. Dispatch matrix per §5.2. |
| `Components/Pages/SourceDetail.razor` | Add Edit button → `/sources/{instanceId}/edit`. |
| `Components/Pages/Sources.razor` | Inline Edit row action (parity with M.2b.6.1). |

### 6.2 New shared panels (under `Components/Shared/`)

| File | Purpose |
|---|---|
| `LicenseModuleDisabledPanel.razor` | Renders the "license module disabled" state from §5.2. Reusable for any future feature gated on `LicenseGate.IsModuleEnabled`. |
| `UnsupportedProtocolPanel.razor` | Read-only JSON/YAML view + raw-config CTA. |
| `WizardNotAvailablePanel.razor` | "Editing this source is not available in this Studio version." |
| `StaleEditWarningBanner.razor` | The 409 collision banner from §5.5. |

### 6.3 Wizard model changes

| File | Change |
|---|---|
| `Wizards/Focas2SourceWizardModel.cs` | Add `HydrateFromExisting(SourceInstanceConfig)` factory consumed by `EditModeContext`. |
| `Wizards/BrotherHttpSourceWizardModel.cs` | Same. |
| `Wizards/ModbusSourceWizardModel.cs` | Same. Must round-trip per-tag list precisely. |
| `Wizards/EditModeContext.cs` | Add `BaseVersionId: string?` field. |
| `Wizards/WizardConfigMerger.cs` | New method `BuildUpdatedSourceDraft(GatewayConfiguration current, SourceInstanceConfig updated, string baseVersionId)` — replaces source, preserves routes, version-checks. |

### 6.4 New Management API endpoints

| File | Purpose |
|---|---|
| `Api/BrotherHttpProbeApi.cs` | `MapPost("/api/v1/sources/browse/brother-http", ...)`. Status mapping per §4.6. |
| `Api/BrotherHttpProbeService.cs` | Probe orchestration — license gate, target-keyed single-flight (BaseUrl), throwaway HTTP call. |
| `Api/BrotherHttpProbeResultDto.cs` | Response DTO (common fields + `EndpointTested`, `HttpStatusObserved`). |
| `Api/ModbusProbeApi.cs` | `MapPost("/api/v1/sources/browse/modbus", ...)`. Status mapping per §4.6. |
| `Api/ModbusProbeService.cs` | Probe orchestration — target-keyed single-flight (`IP:Port:UnitId`), ladder per §4.3. |
| `Api/ModbusProbeResultDto.cs` | Response DTO (common fields + `FunctionCodeUsed`, `AddressTested`, `UnitIdTested`, `ProbeStepReached`). |
| `Api/ModbusProbeStep.cs` | Enum `TcpConnect / FirstConfiguredTag / FallbackTestAddress`. |
| `Contracts/Config/ConfigVersionMismatchDto.cs` | 409 collision response shape. |
| `Program.cs` (Management) | Register new probe services + map new endpoints. |

### 6.5 Test target: **~85-100 new tests** (S3 from pass 1)

| Suite | Coverage |
|---|---|
| `BrotherHttpProbeServiceTests` | License-gated, single-flight (target-keyed), success, timeout, HTTP non-success. |
| `BrotherHttpProbeApiTests` | Status code mapping per §4.6. |
| `ModbusProbeServiceTests` | License-gated, single-flight, ladder steps (TCP-only, first-tag, fallback), TCP-refused/timeout, FC mismatch, register-read-failure. |
| `ModbusProbeApiTests` | Status code mapping; diagnostic-field population for each ladder step. |
| `Focas2SourceWizardModelTests` | Round-trip: hydrate from existing → re-emit `SourceInstanceConfig` → byte-equivalence. |
| `BrotherHttpSourceWizardModelTests` | Same round-trip. |
| `ModbusSourceWizardModelTests` | Same round-trip (per-tag list is the tricky case). |
| `SourceEditRouterTests` | Each of the four §5.2 states renders correctly; draft-vs-current hydration per §5.3; banner copy correct. |
| `WizardConfigMergerTests` | `BuildUpdatedSourceDraft` — replaces matching source; preserves routes; rejects InstanceId change; **rejects stale `BaseVersionId` with 409**. |
| `OptimisticConcurrencyTests` | Two simulated edits against the same source — first save wins, second gets 409 + reload banner. |
| `EditModeIntegrationTests` | One test per protocol: open Edit → modify → save → diff confirms only intended fields changed. |
| `EditMode_CosmeticOnlyChange_DoesNotRestartSource` | The §5.6 classifier-trust integration test. |

---

## 7. Definition of done

- [ ] All three source wizards render on `WizardShell` + `WizardSection`.
- [ ] All three source wizards expose Test Connection via `WizardActions.TestConnectionSlot`. Focas2's rich Browse panel preserved with "Browse Controller" label override.
- [ ] All three source wizards support Add (existing) and Edit (NEW) flows.
- [ ] `SourceEditRouter.razor` handles all four §5.2 states (render / license-disabled / unknown / wizard-missing) with verified UX.
- [ ] Edit hydration precedence: draft-when-touched, else current; banner copy locked per §5.3.
- [ ] Field mutability per §5.4 enforced in UI (disabled inputs with tooltip for immutable fields).
- [ ] Optimistic concurrency: `BaseVersionId` round-trip; 409 on stale; reload banner.
- [ ] **Edit mode never modifies routes** (§5.5 invariant) — `WizardConfigMergerTests` contract test asserts `RouteConfig[]` byte-identical pre-/post-merge.
- [ ] Brother HTTP + Modbus probe endpoints land with target-keyed lease, license gate, status mapping per §4.6.
- [ ] Modbus probe ladder works through all three steps and surfaces diagnostic fields.
- [ ] M.P2.4 Q12 deferral closed — `docs/sessions/2026-05-21-mp24-handoff.md` §6 updated.
- [ ] `EditMode_CosmeticOnlyChange_DoesNotRestartSource` integration test green.
- [ ] Cumulative test count delta ≈ **+85-100**.
- [ ] 0 new warnings; `TreatWarningsAsErrors` honoured.
- [ ] M.2d.3 plan-trail updated with mirror-router cross-reference (§10).

---

## 8. Step-by-step implementation sequence

1. ~~**Reality-check v3 pass on this plan**~~ — **COMPLETED 2026-05-22.** Findings folded into §0 and §9. `SourceInstanceConfig.InstanceId` confirmed init-only; `ILicenseManager.IsModuleEnabled` confirmed present; `SourcesApi.cs:69` already serves `GET /{sourceInstanceId}`.
2. **`ConfigVersionMismatchDto` + server-side `BuildUpdatedSourceDraft` version check** — pure backend, easy to test in isolation. **Discrete commit.**
3. **Brother HTTP probe service + endpoint** — target-keyed lease, BaseUrl-keyed, status mapping. **Discrete commit.**
4. **Modbus probe service + endpoint** — ladder per §4.3, diagnostic fields per §4.4. **Discrete commit.**
5. **Shared panels** (`LicenseModuleDisabledPanel`, `UnsupportedProtocolPanel`, `WizardNotAvailablePanel`, `StaleEditWarningBanner`) — all pure presentational. **Discrete commit.**
6. **`SourceEditRouter.razor`** — resolver logic, four-state dispatch, hydration precedence, banner picker. Includes `SourceEditRouterTests`. **Discrete commit.**
7. **Edit-mode hydration helpers** on the three wizard models + round-trip tests. **Discrete commit.**
8. **`AddFocas2Source.razor`** — adopt `WizardShell`, route Browse Controller through Test Connection slot, remove any stray edit `@page`. **Discrete commit.**
9. **`AddBrotherHttpSource.razor`** — same shell adoption, add Test Connection. **Discrete commit.**
10. **`AddModbusSource.razor`** — same shell adoption, add Test Connection. **Discrete commit.**
11. **`SourceDetail.razor` + `Sources.razor`** Edit buttons. **Discrete commit.**
12. **`EditMode_CosmeticOnlyChange_DoesNotRestartSource`** integration test. **Discrete commit.**
13. **Manual end-to-end verification in Studio** — Add → Edit → Test Connection → optimistic-concurrency collision → unsupported-protocol panel for each of the four §5.2 states. Document smoke run.
14. **Close M.P2.4 §6 Q12 deferral** in the handoff doc. Update M.2d.3 plan with mirror-router note.

---

## 9. Open questions (v3 reality-check only)

All architectural questions from v1 are closed (Q-M2D2-N2, N5, N7). Remaining items are implementation-level:

- **Q-M2D2-N1 — Focas2 Test Connection label** — **v2 resolution: keep "Browse Controller"**. The slot accepts label override. Operator familiarity preserved.
- **Q-M2D2-N3 — Edit mode instance-id immutability** — **v2 resolution: LOCKED immutable** (§5.4). Rename = delete + add. Confirmed by mutability table.
- **Q-M2D2-N4 — Edit-mode loading endpoint** — **RESOLVED 2026-05-22 v3 reality-check:** `SourcesApi.cs:69` already exposes `MapGet("/{sourceInstanceId}", ...)`. No new endpoint needed. `SourceEditRouter.razor` consumes the existing API.
- **Q-M2D2-N6 — Probe DTO inheritance** — **v2 resolution: independent record types**. Common transport fields are duplicated by intention; protocol-specific fields stay close to their owning protocol. M.2d.4 cross-wizard sweep may revisit if a real shared axis emerges.

---

## 10. Cross-references

- Roadmap: [`2026-05-21-phase2-wrapup-roadmap-v2.md`](2026-05-21-phase2-wrapup-roadmap-v2.md) §3.7.2 (M.2d.2 scope), §5.2 Q26 (Brother probe contract — corrected here), §4.1 (test trajectory adjusted to ~85-100 in v2)
- Roadmap amendments: [`2026-05-21-phase2-wrapup-roadmap-v2.3.md`](2026-05-21-phase2-wrapup-roadmap-v2.3.md) §1.1 (no new shared abstractions outside M.2d.1's contract), §1.2 (terminology freeze)
- v1 source: [`2026-05-21-m2d2-source-wizards-plan.md`](2026-05-21-m2d2-source-wizards-plan.md)
- Sibling M.2d plans:
  - [`2026-05-21-m2d1-shared-primitives-plan-v2.md`](2026-05-21-m2d1-shared-primitives-plan-v2.md) — MERGED in `1dfd1d1`
  - [`2026-05-21-m2d3-sink-route-editors-plan.md`](2026-05-21-m2d3-sink-route-editors-plan.md) — **v2 PRECONDITION:** must adopt mirror-router pattern (`SinkEditRouter.razor`, `RouteEditRouter.razor`) per §5.1-§5.2 of this plan
  - [`2026-05-21-m2d4-cross-wizard-sweep-plan.md`](2026-05-21-m2d4-cross-wizard-sweep-plan.md)
- M.P2.4 (Brother) handoff: [`2026-05-21-mp24-handoff.md`](2026-05-21-mp24-handoff.md) §6 (Q12 Test Connection deferral — backfilled here)
- M.2b.3 (Focas2) v3 plan: [`2026-05-17-mp2b3-focas2-wizard-plan-v3.md`](2026-05-17-mp2b3-focas2-wizard-plan-v3.md) — current Browse Controller contract
- Reload-classifier architecture:
  - `src/ElpisEdgeConnect.Core/Configuration/RuntimeReloadClassifier.cs`
  - `src/ElpisEdgeConnect.Host/RuntimeReloadCoordinator.cs`
  - ADR [`0009-runtime-hot-reload-instance-granularity.md`](../decisions/0009-runtime-hot-reload-instance-granularity.md)
  - ADR [`0010-coordinator-synthesizes-cross-record-recovery.md`](../decisions/0010-coordinator-synthesizes-cross-record-recovery.md)
- Existing probe contracts (templates):
  - `src/ElpisEdgeConnect.Management/Api/Focas2BrowseService.cs` (target-keyed lease pattern)
  - `src/ElpisEdgeConnect.Management/Api/MqttTestConnectionService.cs` (same)
- Platform principles: `docs/platform-principles.md` P1 (Runtime Tap observational), P2 (shared interaction primitives), P4 (preserve explainability data path)
- Architecture: `docs/ARCHITECTURE_BLUEPRINT.md` Appendix A locked decisions #4 (modular assemblies), #10 (per-adapter isolation), #18 (3-way diagnostics)

---

**End of v2 plan. All architectural items closed. v3 reality-check completed 2026-05-22 — see §0 "Resolved by v3 reality-check" and §9 Q-M2D2-N4 resolution. Plan is implementation-ready.**
