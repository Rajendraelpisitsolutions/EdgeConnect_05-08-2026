# M.2b.5 + M.2b.6 — Route + Destination Wizards (v3 amendment)

**Status:** v3 — LOCKED (Step 1 reality-check folded in; UI/UX reconfirmed by user)
**Date:** 2026-05-18
**Form:** **TIGHT AMENDMENT** to v2. v1 plan + v1 mockups + v2 amendment remain the load-bearing references; v3 deltas are scoped to the reality-check findings and the premium-UX discipline.
**Predecessor plans:**
- [v1 plan](2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan.md)
- [v1 UX mockups](2026-05-18-mp2b5-mp2b6-ux-mockups.md)
- [v2 amendment (ChatGPT review folded)](2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v2.md)

---

## 0. What changed v2 → v3 (delta summary)

Step 1 reality check on 2026-05-18 produced four findings (three clean passes + one UX decision that's already resolved by the user's prior Milestone-K direction). Three small amendments fold in here:

1. **Naming correction** — `OpcUaServerSinkConfiguration` → `OpcUaServerConfiguration` throughout the plan deliverables tables.
2. **Validator strategy locked** — eager for cheap syntactic checks; lazy via API rejection for complex cross-record/runtime rules. Wizards never duplicate Core validator logic.
3. **OPC UA Security UX locked as drawn** in v2 §4.5 — full schema visible, `Basic256Sha256` Recommended, custom cert path behind Advanced. **Premise:** Milestone K must land before product release because non-None security modes are schema-accepted but not runtime-active today.

Plus a new **§3 — Premium-UX implementation discipline** load-bearing principle pinning the project's commercial-UX commitment.

---

## 1. New locked decisions

### Carry-forward from v2 (unchanged)

All locked decisions A–F (shared), R-A through R-F (Route), D-A through D-H (Destination), and G–L (added at v2 lock) remain unchanged.

### Added at v3 lock

| # | Decision | Reasoning |
|---|---|---|
| **M** | **Naming**: the OPC UA Server sink configuration record is named `OpcUaServerConfiguration` (in `ElpisEdgeConnect.Sinks.OpcUaServer`), NOT `OpcUaServerSinkConfiguration`. All v1/v2 plan references using the wrong name are corrected. Test file names follow suit: `OpcUaServerSinkWizardModelTests` keeps the `Sink` suffix because the wizard model is sink-scoped, but it consumes the canonical `OpcUaServerConfiguration` record. | Verified by reading `src/ElpisEdgeConnect.Sinks.OpcUaServer/OpcUaServerConfiguration.cs` during Step 1. |
| **N** | **Validator-composition strategy** is split deliberately: <br/>• **Eager validation** in the wizard for cheap syntactic checks: route-id regex, required-field presence, numeric ranges, `GlobMatcher.Compile()` for glob syntax at add-pattern time. <br/>• **Lazy validation** via the `POST /api/v1/config/drafts` round-trip for complex cross-record / runtime rules: deadband mutual-exclusion, route-references-existing-source/sink, source-must-be-enabled, all `CrossRecordValidator` rules. <br/>• Wizards **MUST NOT** duplicate Core validator logic. Eager checks compose `GlobMatcher.Compile` (and any other public Core validators); they never re-implement a rule that exists in Core. | Step 1 confirmed `GlobMatcher` is public + composable. `CrossRecordValidator` enforces deadband-conflict at draft-create time. The lazy-via-API path produces a clean snackbar error when the draft is rejected — operationally acceptable for complex rules, and the unified-Deadband-table UX from v2 §4 makes the conflict structurally impossible to construct in the first place. |
| **O** | **OPC UA Security UX kept as drawn in v2 §4.5.** Full schema (None, Basic256Sha256, Aes128/Aes256 variants) visible. `Basic256Sha256` carries a "Recommended" chip. Custom-cert-path behind `Advanced ▾`. Auto-generate is the default. <br/>**Premise:** Milestone K activates Sign/Encrypt/Username runtime enforcement. M.2b.6 ships before K, but **the product release sequence must place K before public release** because non-None security modes are schema-accepted today but not yet runtime-active (`OPCUA.SECURITY_NOT_YET_IMPLEMENTED` at adapter Initialize). User-confirmed direction. | Pinned in this plan so future contributors / release managers know K is a release prerequisite. |

---

## 2. UI/UX reconfirmation record

User confirmed v2 mockup direction on 2026-05-18 with **no changes**. The premium-product UX commitments locked here:

| # | Confirmed |
|---|---|
| 1 | Source picker renders as **selectable cards** (≤10 sources). Auto-fallback to searchable virtualised list at >10. |
| 2 | Searchable filter input above both source and destination lists, **from day one** (never retrofit). |
| 3 | **Unified Deadband table** — one row per tag with mutually-exclusive Absolute / Percent columns. |
| 4 | **Filter and Transforms remain separate sections** — no merge into a "Tags" mega-editor. |
| 5 | **Visual transform-order captions** ("1. Rename → 2. Filter → 3. Deadband → 4. Rate limit") on the Transforms section header. |
| 6 | **Lazy tag typeahead** with 300 ms debounce + 50-suggestion cap. |
| 7 | **MQTT Test Connection** success and failure states render: `ProbeId`, `ElapsedMs`, structured `ErrorCode`, plain-English remediation hints. |
| 8 | **MQTT topic-template live preview** resolving `{gatewayId}` from `/api/v1/config`. |
| 9 | **OPC UA "No Test Connection" caption** placed at the bottom of the wizard (matches draft-summary-always-last pattern). |
| 10 | **State dots (●▲) + text labels** in every status renderer — not text-only, not dots-only. Colour-blind safe. |
| 11 | **Draft Summary panels mandatory** on every wizard. |

These are not aspirational — they are LOCKED implementation requirements.

---

## 3. Premium-UX implementation discipline (load-bearing principle)

> **Do not degrade the UI into plain forms just to ship faster.**
>
> The goal is not only working configuration. The goal is a polished, commercially competitive Studio UX. If a Razor implementation detail makes the v2 mockup hard to preserve, **pause and report the tradeoff** instead of silently simplifying.

This principle applies to every Razor edit in M.2b.5 and M.2b.6. Concretely:

1. **No "v1 = barebones; we'll polish in v2" shortcuts.** The polish IS v1. If a section is hard, surface the tradeoff and let the user decide; never ship the simpler shape unilaterally.
2. **The mockup is a contract, not a suggestion.** ASCII layouts in v1 §1 / §2 plus v2 §4 additions specify the section order, the inline element placement, the validation states, the helper-text presence, and the empty-state copy. Razor implementation matches all of those.
3. **When MudBlazor doesn't expose a needed primitive cleanly**, the implementer's options are (in order):
   - Compose existing `MudBlazor` widgets (preferred).
   - Render raw HTML/CSS inside a `MudPaper` for a small custom region.
   - **Pause and report** — never silently fall back to a flat `MudTextField` row when the mockup specifies a richer treatment.
4. **Validation feedback always lands inline next to the offending field**, never solely in a snackbar at submit time. Snackbar feedback is for after-the-fact actions (Save succeeded / API rejected); inline feedback is for "this field has a problem right now."
5. **Status renderings always carry both state dot + text label.** A `Color.Success` chip alone is not enough. A green dot before "Running" both is.
6. **Auto-suggest semantics: dirty bit on first hand-edit, helper text disappears the moment the operator types.** Never overwrite operator input.
7. **Smart defaults are SPECIFIC values, not blank fields.** Route ID auto-fills from selected source. Buffer defaults to StoreAndForward + 10000 points + the memory-estimate caption. Demo/dev paths fill in plausible values so the operator just clicks Save.

**Pause-point trigger.** If during implementation the Razor implementer encounters any of:

- A MudBlazor widget doesn't compose to match the mockup layout
- An inline-validation state is structurally hard to render
- A helper-text or empty-state copy seems redundant or removable
- A section is taking 3x the estimated LOC

→ **STOP and report.** The user-decided tradeoff lands as a v4 amendment, NOT a silent UI simplification.

---

## 4. Step 1 reality-check record (2026-05-18)

| # | Check | Verdict | Detail |
|---|---|---|---|
| 1 | `Core/Pipeline/Steps/GlobMatcher.cs` exists, is public, exposes `Compile(string)` + `IsMatch(string)`; throws `ArgumentException` on empty/whitespace patterns. | ✅ Clean | Composable for wizard's add-pattern validation. Locked N. |
| 2 | `Core/Configuration/CrossRecordValidator.cs` already enforces deadband mutual exclusion + threshold ranges + rate-limit positivity; error code `CoreErrors.PipelineDeadbandConflict`. | ✅ Clean | Lazy validation via API rejection covers complex rules. The unified-Deadband-table UX makes the conflict impossible to construct (single Mode radio per row). |
| 3 | MQTTnet 4.x exposes `ConnectAsync` / `DisconnectAsync` / `PublishAsync` as separate `IMqttClient` methods. | ✅ Clean | CONNECT-without-publish is the literal API. Locked H pinning via grep test is straightforward. |
| 4a | OPC UA Server sink config record is `OpcUaServerConfiguration` (NOT `OpcUaServerSinkConfiguration`). Namespace `ElpisEdgeConnect.Sinks.OpcUaServer`. Fields: `EndpointUrl`, `ApplicationUri`, `ApplicationName`, `Namespace` (`OpcUaNamespaceConfig`), `Security` (`OpcUaSecurityConfig`), `MaxSessions`. | ⚠️ Naming fix | Locked M corrects the v1/v2 plan references. |
| 4b | OPC UA Server security: MVP honors `None + Anonymous`; other modes are schema-accepted but rejected at adapter Initialize with `OPCUA.SECURITY_NOT_YET_IMPLEMENTED` until Milestone K. | ⚠️ UX/Release decision | User-confirmed direction: full schema in the wizard; release blocked on K. Locked O. |

---

## 5. Refined deliverables (delta from v2 §3)

### M.2b.5 deltas

(Carry-forward from v2 §3 M.2b.5 deltas — unchanged. The `BuildNewDraft` → `BuildNewSourceDraft` rename happens in this milestone per v2 Locked G.)

### M.2b.6 deltas

| File | Change vs v2 |
|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/OpcUaServerSinkWizardModel.cs` | **References `OpcUaServerConfiguration`** (NOT `OpcUaServerSinkConfiguration`). All field names match the canonical record: `EndpointUrl`, `ApplicationUri`, `ApplicationName`, `Namespace`, `Security`, `MaxSessions`. Wizard-side mode list mirrors the full schema; the `Recommended` chip on `Basic256Sha256` stays. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddOpcUaServerDestination.razor` | (Carry-forward — v2 §4.5 layout unchanged. Adds the Locked-O release-prerequisite caption: "Sign + Encrypt modes are accepted in config today but enforced at runtime starting in Milestone K. Picking a non-None mode against an MVP build will produce `OPCUA.SECURITY_NOT_YET_IMPLEMENTED` at apply time.") |
| `tests/ElpisEdgeConnect.Management.Tests/OpcUaServerSinkWizardModelTests.cs` | Roundtrip parity test uses `OpcUaServerConfiguration` (not the assumed name). All other tests unchanged. |

### Validator composition (cross-cutting Locked N)

| File | Change vs v2 |
|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/RouteFilterEditorModel.cs` | Glob-pattern validation at add-time calls `GlobMatcher.Compile(pattern)` and catches `ArgumentException` to render an inline error. Wizard never re-implements glob syntax rules. |
| `src/ElpisEdgeConnect.Management/Wizards/RouteWizardModel.cs` | Route-id regex enforced eagerly via the Core record's own regex constant (no parallel pattern). Required-field presence enforced inline. Cross-record rules (route references existing source/sink, source is enabled, dup-route-id) are enforced **lazily** via the `POST /api/v1/config/drafts` round-trip — the wizard handles rejection responses via the standard snackbar pattern. |
| `src/ElpisEdgeConnect.Management/Wizards/RouteTransformsEditorModel.cs` | Numeric-range checks (deadband ≥ 0 for Absolute, in (0, 1] for Percent, rate-limit > 0) enforced eagerly. **Cross-tag mutual exclusion (Deadband vs DeadbandPercent on same tag) is impossible by construction** in the unified Deadband table — single Mode radio per row prevents it. No duplicated logic. |

---

## 6. Sequence (unchanged from v2 §6)

Implementation order: **M.2b.5 first** (Route wizard incl. `BuildNewDraft` → `BuildNewSourceDraft` rename), **then M.2b.6 stacked on M.2b.5's branch**.

After this v3 commits, **the user has asked for one more pause** before M.2b.5 implementation starts. No code changes until that pause clears.

---

## 7. DoD additions (delta from v2 §6)

### M.2b.5 additional DoD

| # | Verification |
|---|---|
| 13 | **Locked N (eager validation)**: code review confirms `RouteFilterEditorModel` calls `GlobMatcher.Compile()`; grep for any new glob-pattern parsing code in the wizard returns zero matches. |
| 14 | **Locked N (lazy validation)**: dup-route-id rejection at the wizard happens via the `/api/v1/config/drafts` POST response, NOT via a wizard-side dup-id check that duplicates `WizardConfigMerger`'s logic. |

### M.2b.6 additional DoD

| # | Verification |
|---|---|
| 14 | **Locked M**: `git grep "OpcUaServerSinkConfiguration"` returns zero matches across `src/` + `tests/`. Only `OpcUaServerConfiguration` and `OpcUaServerSinkWizardModel` appear. |
| 15 | **Locked O**: `AddOpcUaServerDestination.razor` carries the release-prerequisite caption (or an equivalent operator-facing warning) near the Security section's non-None mode rows. |

---

## 8. Final shape

This v3 is **LOCKED**. Per user direction:

1. v3 committed.
2. **PAUSE** before M.2b.5 implementation starts.
3. After pause clears, M.2b.5 implementation proceeds with the premium-UX discipline of §3 as the binding contract.

Reality check is complete; no further investigative work is needed before implementation. If during M.2b.5 or M.2b.6 the implementer hits any §3 pause-point trigger (MudBlazor composability gap, hard-to-render inline validation, redundant-seeming copy, 3x LOC blowout), they stop and report — a v4 amendment lands before the simplification, never after.

---

**End of M.2b.5 + M.2b.6 v3 amendment. LOCKED 2026-05-18 after reality check + user UX reconfirmation. Implementation paused at user request; resumes after the user clears the pre-implementation gate.**
