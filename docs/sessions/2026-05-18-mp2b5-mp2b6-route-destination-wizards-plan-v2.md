# M.2b.5 + M.2b.6 — Route + Destination Wizards (v2 amendment)

**Status:** v2 — LOCKED (ChatGPT review pass folded in)
**Date:** 2026-05-18
**Form:** **TIGHT AMENDMENT** to v1. The v1 plan ([`plan v1`](2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan.md)) and v1 mockups ([`UX mockups v1`](2026-05-18-mp2b5-mp2b6-ux-mockups.md)) remain the load-bearing references for everything not explicitly amended here.
**Combined size update:** ~1,500 LOC code + ~960 LOC tests (unchanged from v1). Test target ~1880 stays.

---

## 0. What changed v1 → v2 (delta summary)

ChatGPT review on 2026-05-18 produced a clear "proceed to v2 lock after folding" verdict. All 11 plan OPEN questions and all 7 UX OPEN questions resolved. Four substantive amendments + several UX refinements:

1. **Architectural rename** — `WizardConfigMerger.BuildNewDraft` → `BuildNewSourceDraft` (do it now before more callsites accumulate, before any new merger methods land).
2. **MQTT Test Connection scope tightened** — CONNECT + CONNACK + DISCONNECT only. NO publish.
3. **TLS scope reduced** — v1 = plain TCP + TLS toggle + username/password. Defer mTLS, cert upload, trust stores, PEM path management to a later milestone.
4. **Validation discipline** — wizards COMPOSE existing Core validators, never re-implement runtime validation semantics.

UX refinements (mockup deltas, see §4 below):
- Searchable lists with N=10 threshold
- Runtime-state coloured dots (not just text)
- "Generated from source" helper text under auto-filled fields
- Live token preview for MQTT topic template
- OPC UA Security panel collapsed by default with "Recommended" chip

---

## 1. New locked decisions

### Carry-forward from v1 (unchanged)

All locked decisions A–F (shared), R-A through R-F (Route), D-A through D-H (Destination) from v1 remain unchanged unless explicitly listed below.

### Added at v2 lock

| # | Decision | Reasoning |
|---|---|---|
| **G** | **`WizardConfigMerger.BuildNewDraft` is renamed to `BuildNewSourceDraft`** as part of M.2b.5. All call sites (Modbus + FOCAS2 wizards) updated in the same commit | Without the rename, three methods (`BuildNewDraft`, `BuildNewRouteDraft`, `BuildNewSinkDraft`) coexist with confusing asymmetric naming. The cleanup window closes once more milestones land. Doing it inside M.2b.5 keeps the diff small and reviewer-obvious. |
| **H** | **MQTT Test Connection probe performs CONNECT + CONNACK + (optional broker metadata read) + DISCONNECT only.** NO publish | Avoids broker pollution (retained messages, test topics), avoids ACL/permission edge cases, keeps probe semantics identical to Browse Controller philosophy. Health is verified at the protocol-handshake level — sufficient for "is the broker reachable + accepting our credentials". |
| **I** | **Wizards compose existing Core validators; they MUST NOT re-implement runtime validation semantics** | If Core already validates glob patterns / transform invariants / regex rules, the wizard model calls into those validators rather than re-coding them. Prevents long-term semantic drift between wizard validation and runtime validation. |
| **J** | **MQTT TLS scope in v1 = plain TCP + TLS on/off toggle + username/password authentication** | Defer mTLS, cert upload, trust stores, PEM path management, CA chain editing to a later milestone. Otherwise M.2b.6 balloons into a TLS-tooling project. |
| **K** | **OPC UA Server custom-cert-path field lives behind an `Advanced ▾` expansion panel; auto-generate is the visible default** | 90% of operators should never see the cert-path option. Surfacing it inline implies "operators need to think about this" which is the opposite of the visible default's intent. |
| **L** | **Route wizard tag typeahead is lazy-loaded on first focus, debounced 300 ms, capped at 50 suggestions** | Preloading every source's tag list at wizard init would be slow (FOCAS2's BrowseTagsAsync may return hundreds of tags; future protocols may return thousands). Lazy + debounce + cap keeps the UI snappy without sacrificing the discoverability win. |

---

## 2. Resolved questions (record)

The 11 plan OPEN questions from v1 §3.4, §4.4, §9 plus the 7 UX OPEN questions from mockups v1 §6 were all settled by the ChatGPT review pass on 2026-05-18.

### Plan questions (M.2b.5 Route Wizard)

| # | Verdict |
|---|---|
| R-Q1 | **Compose Core glob validator** (Locked I). No wizard-side reimplementation. |
| R-Q2 | **Unified Deadband editor** — one table with mutually-exclusive Absolute / Percent columns per row. Single cross-validation site. |
| R-Q3 | **Visual transform-order captions** — render small numbered captions ("1. Rename → 2. Filter → 3. Deadband → 4. Rate limit") on the Transforms section header so operators see the pipeline order. |
| R-Q4 | **Keep Filter and Transforms separate.** Do NOT merge into a "Tags" mega-editor. Matches the schema split; lower projection complexity. |
| R-Q5 | **StoreAndForward** default. |

### Plan questions (M.2b.6 Destination Wizard)

| # | Verdict |
|---|---|
| D-Q1 | **No publish during Test Connection** (Locked H). |
| D-Q2 | **Plain TCP + TLS toggle + username/password only in v1** (Locked J). Defer the rest. |
| D-Q3 | **Auto-generate cert is visible default; custom path behind Advanced** (Locked K). |
| D-Q4 | **MQTT tile first** in the destination picker (most-common path first). |
| D-Q5 | **Single-flight key = `host:port`** — confirmed. Mirrors `Focas2BrowseService`'s lease pattern. |
| D-Q6 | **No `EDGECONNECT_MQTT_FAKE_MODE`.** Mosquitto is trivially available locally; demo mode would be ceremony without benefit. |

### Combined cross-cutting questions

| # | Verdict |
|---|---|
| Q1 | **Combined plan doc** stays for v2 review. If implementation phases diverge during M.2b.5 → M.2b.6, split then. |
| Q2 | **"Destination" in operator-facing copy + URLs; "Sink" in code/types.** Confirmed. |
| Q3 | **Pin only the highest-risk safety copy** (banner messages, dup-IP warnings, Test Connection success/failure copy). Don't burden the codebase with constants for every label. |
| Q4 | **Rename to `BuildNewSourceDraft`** (Locked G). |
| Q5 | **Defer wizard-header refactor.** Each wizard keeps its own chrome for v1; a shared partial lands as a follow-on once 4+ wizards exist. |
| Q6 | **No demo-mode interaction tests** for these wizards — demo dispatch is downstream of route/destination wiring. |

### UX questions (mockups v1)

| # | Verdict |
|---|---|
| UX-Q1 | **Cards by default; auto-fallback to searchable virtualised list at N>10** sources/sinks. Build the search affordance from day one — retrofitting later is painful. |
| UX-Q2 | **Unified Deadband table** (resolves R-Q2 too). |
| UX-Q3 | **Live token preview** for the MQTT topic template — fetch `gatewayId` from `/api/v1/config` on init and render the resolved string live below the textbox. |
| UX-Q4 | **OPC UA "No Test Connection" caption at the bottom** (matches the draft-summary-always-last pattern). |
| UX-Q5 | **MQTT tile first** in the destination picker (resolves D-Q4 too). |
| UX-Q6 | **Keep Filter and Transforms separate** (resolves R-Q4 too). |
| UX-Q7 | **Lazy + debounced + capped typeahead** (Locked L). |

---

## 3. Refined deliverables (delta from v1 §3.2 and §4.2)

### M.2b.5 deltas

| File | Change vs v1 |
|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/WizardConfigMerger.cs` | **+RENAME**: existing `BuildNewDraft` → `BuildNewSourceDraft`. New `BuildNewRouteDraft` lands as planned. Estimate: +60 LOC for new method, +10 LOC of mechanical rename diff across the file. |
| `src/ElpisEdgeConnect.Management/Wizards/ModbusSourceWizardModel.cs` *(consumer)* | No code change — but Razor caller in `AddModbusSource.razor` updates the call site name. |
| `src/ElpisEdgeConnect.Management/Wizards/Focas2SourceWizardModel.cs` *(consumer)* | Same — caller in `AddFocas2Source.razor` updates. |
| `tests/ElpisEdgeConnect.Management.Tests/WizardConfigMergerTests.cs` | Existing source-method tests rename to `BuildNewSourceDraft_*`. +5 new tests for `BuildNewRouteDraft` per v1 §3.3. |
| `src/ElpisEdgeConnect.Management/Wizards/RouteTransformsEditorModel.cs` | Unified Deadband row model: `Tag`, `Mode (Absolute|Percent)`, `Threshold`. Single projection that splits to either `Deadband` or `DeadbandPercent` dict at `BuildTransformProfileConfig()` time. Cross-validation enforces `Mode` is set and `Threshold` is positive. |
| `src/ElpisEdgeConnect.Management/Components/Pages/RouteWizards/AddRoute.razor` | Source picker section gains a `MudTextField` search input above the cards. Below 10 sources: cards render as drawn. ≥10 sources: list collapses into a compact searchable list. Tag typeahead in Transforms editor implements lazy-on-focus + 300ms debounce + 50-suggestion cap. |

### M.2b.6 deltas

| File | Change vs v1 |
|---|---|
| `src/ElpisEdgeConnect.Management/Api/MqttTestConnectionService.cs` | Probe sequence locked: `MqttClient.ConnectAsync` → check `Connack.ReasonCode` → optional `MqttClient.QueryServerSubscriptionAsync` (broker metadata, if MQTTnet exposes it without publish) → `DisconnectAsync`. **No `PublishAsync` call anywhere.** Pinned by a reflection / source-grep test in `MqttTestConnectionServiceTests`. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddMqttDestination.razor` | TLS section reduced: single "Use TLS" toggle (no cert upload UI). Authentication section: `None / Username+Password` radio only. mTLS option HIDDEN entirely (not "Coming in M.X" placeholder — just absent). Topic template input gains live preview row resolving `{gatewayId}` from cached `/api/v1/config` response. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddOpcUaServerDestination.razor` | Security section gains "Recommended" chip next to Basic256Sha256. The certificate-path radio "Use existing certificate at custom path" moves behind an `Advanced ▾` expansion panel; auto-generate stays as the visible default. Security section starts collapsed (default-closed `MudExpansionPanel`). |
| `tests/ElpisEdgeConnect.Management.Tests/MqttTestConnectionServiceTests.cs` | +1 test: `Probe_DoesNotInvokePublishAsync` — uses a fake `IMqttClient` and asserts the publish-call counter remains zero across the full probe lifecycle. Pins Locked H. |

### Cross-cutting

| File | Change vs v1 |
|---|---|
| **All wizard model files** | Where wizard validation overlaps with Core validators (glob patterns, regex, deadband-exclusive rule, transform invariants), the wizard model **calls into Core's validator surface** rather than re-implementing the rule. Pinned by Locked I; code review checks for parallel validation logic. |

---

## 4. UX additions (mockup deltas)

The v1 mockup ([`UX mockups v1`](2026-05-18-mp2b5-mp2b6-ux-mockups.md)) remains the load-bearing layout reference. Five additions land at v2:

### 4.1 Searchable lists from day one

Above the source-picker cards in the Route wizard (and above the destination tile list in the Destination picker), add a search input:

```
┌─ 2. Source ──────────────────────────────────────────────────────────┐
│  Pick the source whose data this route delivers.                     │
│                                                                      │
│  [ 🔍 Search by id, device name, or endpoint…__________________ ]    │
│                                                                      │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ ◉ focas-cell-A  …                                           │    │
│  └─────────────────────────────────────────────────────────────┘    │
│  [… more sources …]                                                  │
└──────────────────────────────────────────────────────────────────────┘
```

- N ≤ 10 sources: cards render as drawn in v1; search filters in-place.
- N > 10 sources: cards collapse into a single-line `MudList` (id + protocol chip + status dot only). Selecting an item expands the card inline (so context is preserved).

### 4.2 Runtime-state coloured dots

Every source/destination status indicator gets a 8px-diameter coloured dot rendered before the text label:

```
● Running   ← #16A34A green
▲ Degraded  ← #F59E0B amber (triangle for "attention" rather than circle)
● Stopped   ← #6B7280 grey
✖ Faulted   ← #DC2626 red
```

Industrial operators scan colour-coded state in <200 ms vs. ~1 s for text. Add to the existing status-text rendering, don't replace it (accessibility / colour-blindness).

### 4.3 Auto-fill helper text

Below the Route ID field, when the value is auto-suggested from the selected source:

```
Route id *  [ route-focas-cell-A_____________________________________ ]
            ⓘ Auto-generated from selected source. Editing disconnects auto-sync.
```

Once the operator hand-edits, the helper disappears (dirty bit). The same pattern can extend to the wizard-instance-id auto-suggest issue flagged in M.2b.3 Q10 — defer that to its own consistency-pass milestone.

### 4.4 Live MQTT topic-template preview

Below the topic-template `MudTextField`, render a monospace preview row that resolves `{gatewayId}` from the gateway's actual config:

```
Topic template
[ eremos/{gatewayId}/cnc/{sourceId}/{tagName}________________________ ]
Preview:  eremos/factory-gateway/cnc/focas-cell-A/axes/x/absolute
          ↑ resolved against gatewayId="factory-gateway" (gateway.json)
```

- `gatewayId` is real (from `/api/v1/config`).
- `sourceId` and `tagName` are placeholders (rendered in `#6B7280` text) since the route hasn't picked them yet at this point in the wizard.
- Re-renders live as the operator edits the template.

### 4.5 OPC UA Security panel default-closed + "Recommended" chip

Security section starts collapsed. When expanded:

```
┌─ 3. Security ──────────────────────────── ▾ ────────────────────────┐
│  Allowed security policies                                           │
│  ☑ None (insecure — dev only)                                        │
│  ☑ Basic256Sha256                    [ Recommended ]                 │
│  ☐ Aes128_Sha256_RsaOaep                                             │
│  ☐ Aes256_Sha256_RsaPss                                              │
│                                                                      │
│  Server certificate  ◉ Auto-generate on first start (recommended)    │
│                                                                      │
│  ▸ Advanced — Custom certificate path                                │
│                                                                      │
│  ☑ Allow anonymous access                                            │
│  ☐ Require username / password                                       │
└──────────────────────────────────────────────────────────────────────┘
```

The `Recommended` chip is `Color.Success Variant.Filled Size.Small`. Operators understand the visual shortcut faster than reading per-row helper text.

---

## 5. Implementation risk mitigations (folded in)

The ChatGPT review highlighted three risks worth pinning in v2:

| Risk | Mitigation |
|---|---|
| **Tag typeahead may become slow** for FOCAS2 (and future protocols with thousands of tags) | Locked L: lazy-on-focus + 300ms debounce + 50-suggestion cap. Implementation note: the suggestions come from the source's known tag set (Modbus declared tags / FOCAS2 BrowseTagsAsync cache). Render a "Showing top 50 of N matches" caption when the result is capped. |
| **`WizardConfigMerger` is approaching policy-engine territory** with three overloads | Acceptable for v1. Revisit extraction-to-policy-engine when: (a) bulk-import lands, (b) edit-wizards land, or (c) clone-operation lands. Not now. Locked-decision file header in the merger calls this out so future contributors know the threshold. |
| **OPC UA Security panel density** may intimidate operators | Locked K + §4.5: collapsed by default, sane pre-checks, "Recommended" chip on Basic256Sha256. Custom cert path hidden behind Advanced. |

---

## 6. Sequence + DoD (unchanged from v1 §6 and §8)

Implementation sequence stays: **M.2b.5 first** (Route wizard incl. rename), **then M.2b.6 stacked on M.2b.5's branch**. Per-milestone DoD from v1 §8 holds. v2 adds these DoD clauses:

### M.2b.5 additional DoD

| # | Verification |
|---|---|
| 7 | **Locked G**: `git grep "BuildNewDraft(" -- ':!*v1*'` returns zero matches. Only `BuildNewSourceDraft`, `BuildNewRouteDraft` (and later `BuildNewSinkDraft`) exist. |
| 8 | **Locked I**: code review confirms `RouteWizardModel` glob-pattern check uses Core's filter validator (no local re-implementation). |
| 9 | **Locked L**: `AddRoute.razor` tag-typeahead JS / Blazor code path uses lazy-on-focus + debounce + cap. Verified by inspection. |
| 10 | **§4.1 search**: source picker renders search input regardless of source count. |
| 11 | **§4.2 dots**: runtime-state coloured dots render in source picker + destination checklist. |
| 12 | **§4.3 auto-fill helper**: Route ID helper text renders only when value is auto-suggested AND not user-edited. |

### M.2b.6 additional DoD

| # | Verification |
|---|---|
| 8 | **Locked H**: `MqttTestConnectionService` has zero `PublishAsync` invocations (grep + reflection test). |
| 9 | **Locked J**: TLS section in `AddMqttDestination.razor` has no mTLS / cert-upload UI elements. Authentication is `None / Username+Password` only. |
| 10 | **Locked K**: `AddOpcUaServerDestination.razor` custom-cert-path radio is inside an `MudExpansionPanel` titled "Advanced". |
| 11 | **§4.4 token preview**: MQTT topic template preview row renders below the input and updates live as the operator types. |
| 12 | **§4.5 OPC UA "Recommended" chip**: Basic256Sha256 row has a Color.Success Filled chip next to it. |
| 13 | **§4.2 dots**: destination tiles + Test Connection result panel use coloured state dots. |

---

## 7. Pause-points

(Carried from v1 §9 — no v2 amendments needed. ChatGPT review surfaced no new pause conditions.)

---

## 8. Final shape

This v2 is **LOCKED**. Next per cadence: **optional Step 1 reality check** (read `MqttSinkConfiguration` + OPC UA Server config + Core validator surfaces to confirm Locked I is achievable in one PR), **then implement M.2b.5**, **then implement M.2b.6**.

Critical pre-implementation reality-check questions:

1. **Core validator surface for glob patterns** — is there a reusable `IGlobPatternValidator` (or equivalent) in Core's filter engine that the wizard can call? Or do we need to extract one?
2. **Core deadband cross-validation** — does the configuration manager already enforce "a tag cannot be in both `Deadband` and `DeadbandPercent`"? If yes, the wizard composes it. If no, where should the rule live?
3. **MQTT library** — does the existing `MqttSink` use `MQTTnet`? Does that library expose CONNECT-without-publish cleanly? Test Connection design hinges on this.
4. **OPC UA Server config** — what's the actual configuration record's name and namespace? v1 plan assumed `OpcUaServerSinkConfiguration`; need to verify.

If Step 1 surfaces any of these as blockers, v3 amendment lands before implementation. Otherwise, implementation starts directly from v2.

---

**End of M.2b.5 + M.2b.6 v2 amendment. LOCKED 2026-05-18 after ChatGPT review pass. Optional Step 1 reality check next; then implementation, M.2b.5 → M.2b.6.**
