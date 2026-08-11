# M.2b.5 + M.2b.6 — Route Wizard + Destination Wizard

**Status:** v1 — DRAFT, awaiting ChatGPT review pass before lock
**Date:** 2026-05-18
**Predecessor:** M.2b.3 (FOCAS2 wizard) + M.2b.3.1 (FOCAS2 demo mode) — both via [PR #5](https://github.com/elpisitsolutions/EdgeConnect/pull/5) + [PR #6](https://github.com/elpisitsolutions/EdgeConnect/pull/6).
**Shape:** **Two milestones, one plan doc.** M.2b.5 (Route) ships first; M.2b.6 (Destination) follows. Each is its own PR. Combined plan because the wizards share the POCO-test pattern, the `WizardConfigMerger` extension shape, and the Razor chrome.
**Combined size estimate:** ~1,200 LOC code + ~600 LOC tests across the two milestones.
**Test baseline:** 1831 (post-M.2b.3.1) → expected ~1880 after both milestones (+~50).

---

## 1. Goal

Operators today can add Modbus and FOCAS2 sources via Studio wizards (M.2b.1 + M.2b.3) but must hand-edit JSON to create routes and destinations. The buttons on `/routes` and `/sinks` are hardcoded `Disabled="true"` placeholders.

M.2b.5 ships the **Route Wizard** at `/routes/new`. M.2b.6 ships the **Destination Wizard** at `/destinations/new`. Both follow the established source-wizard cadence: POCO view-model + Razor page + new `WizardConfigMerger` method + POCO-level tests (no bUnit, matching project precedent).

### Architectural pins (locked across both)

1. **`WizardConfigMerger` is the single safety chokepoint.** Dup-instance-id and dup-route-id guards live in the pure merger, not in Razor. New merger methods follow the existing `BuildNewDraft(config, newSource, wiring)` shape.
2. **Wizards always create — never edit.** Editing existing entities continues via the Config-page JSON flow. Mirrors the M.2b.1 / M.2b.3 source-wizard pattern; preserves the "wizards land in known-shape config; edits go through the audit chain" separation.
3. **POCO view-model + Razor shell + POCO unit tests.** Established in M.2b.1 (`ModbusSourceWizardModel`) and reinforced by M.2b.3.1 (`LayoutChromeModel`, `Focas2DupIpWarningCopy`). No bUnit; copy strings pinned by test.

---

## 2. Shared locked decisions

| # | Decision | Reasoning |
|---|---|---|
| A | **Two separate milestones (M.2b.5, M.2b.6); one shared plan doc** | User-confirmed. Smaller PRs for review. Shared plan because the patterns + merger extensions are paired. |
| B | **`WizardConfigMerger` gains two new methods**: `BuildNewRouteDraft(currentConfig, newRoute)` and `BuildNewSinkDraft(currentConfig, newSink, routeWiring)` | Symmetric with the existing `BuildNewDraft(...)` for sources. Pure functions; dup-id guards inside. Existing source overload stays unchanged. |
| C | **No "wire to existing route" branch on the destination wizard** | Same safety rationale as M.2b.1: silently overwriting `RouteConfig.SinkInstanceIds` on a live route is operationally dangerous. Sink wizard offers `NotWired` OR `NewRoute` — same shape as the source wizard's `RouteWiring`. |
| D | **Wizard tests are POCO-only**; copy strings pinned by test | Matches `ModbusSourceWizardModelTests` / `LayoutChromeModelTests` precedent. |
| E | **Source-protocol-picker pattern reused for the destination picker** | `SourceProtocolPickerModel` (M.2b.3 extraction) gets a sibling: `DestinationProtocolPickerModel`. Two tiles in v1: MQTT (Available), OPC UA Server (Available). Future HTTP/TCP tiles ship as Pending until their respective wizards land. |
| F | **Studio page button flips** | `Routes.razor` line 33-42 and `Sinks.razor` line 35-44 currently have `Disabled="true"` placeholders. Each milestone flips its respective button to clickable. |

---

## 3. M.2b.5 — Route Wizard (with Filter + Transforms editors)

### 3.1 Locked decisions

| # | Decision | Reasoning |
|---|---|---|
| R-A | **Single Razor page at `/routes/new`** | No protocol picker — there's only one "route" shape. Six sections: Identity, Source, Destinations, Buffer, Filter, Transforms, Delivery. |
| R-B | **Filter editor** = two glob-pattern list editors (Include / Exclude), MudList add-row pattern | `TagFilterConfig` has just two fields; UI is straightforward. Default `Include = ["*"]` preserved as the initial state. |
| R-C | **Transforms editor in v1 covers**: TagMapping, Deadband (absolute), DeadbandPercent, RateLimitMs. **EnrichmentTags is OMITTED** | `TransformProfileConfig.EnrichmentTags` is explicitly DORMANT per its XML doc — "values in this map have no runtime effect" until C1.5/C3. Surfacing an editor for a no-op field would mislead operators. Surface when activation milestone lands. |
| R-D | **No transforms-pipeline preview** in v1 | Showing "what a tag becomes after the pipeline runs" requires routing-engine-aware preview state. Out of scope; defer to v2. |
| R-E | **Destination picker is multi-select from existing sinks** | A route fans out to one-or-more sinks (`RouteConfig.SinkInstanceIds: IReadOnlyList<string>`). Wizard renders existing sinks as checkboxes; ≥1 required. |
| R-F | **`RouteWizardModel` is POCO; `BuildRouteConfig()` projects to `RouteConfig`** | Mirrors `ModbusSourceWizardModel.BuildSourceInstance()` pattern. Per-section sub-models for Filter and Transforms keep the projection composable. |

### 3.2 Deliverables

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/RouteWizardModel.cs` *(new, ~250 LOC)* | POCO with Identity / Source / Sinks / Buffer / Filter / Transforms / Delivery fields. `BuildRouteConfig()` returns a fully-populated `RouteConfig`. Defensive guards on RouteId regex + empty SinkInstanceIds. |
| `src/ElpisEdgeConnect.Management/Wizards/RouteFilterEditorModel.cs` *(new, ~80 LOC)* | Sub-model: `IList<string> Include`, `IList<string> Exclude`. `BuildTagFilterConfig()` projects to `TagFilterConfig`. Glob-pattern validation on add. |
| `src/ElpisEdgeConnect.Management/Wizards/RouteTransformsEditorModel.cs` *(new, ~150 LOC)* | Sub-model: 4 dictionaries (TagMapping/Deadband/DeadbandPercent/RateLimitMs) as `IList<KeyValueRow>` for Razor binding. `BuildTransformProfileConfig()` collapses empty dicts to null. Cross-validation: a tag may not appear in both `Deadband` and `DeadbandPercent` (mirror Core's invariant). |
| `src/ElpisEdgeConnect.Management/Wizards/WizardConfigMerger.cs` *(edit, +60 LOC)* | New method `BuildNewRouteDraft(GatewayConfiguration currentConfig, RouteConfig newRoute)`. Pure function. Guards: dup route-id, source must exist, all sink-ids must exist, source must be enabled. Returns new draft `GatewayConfiguration with { Routes = currentConfig.Routes + newRoute }`. |
| `src/ElpisEdgeConnect.Management/Components/Pages/RouteWizards/AddRoute.razor` *(new, ~600 LOC)* | `@page "/routes/new"`. Sections: (1) Identity, (2) Source dropdown, (3) Destinations multi-checkbox, (4) Buffer policy (mode + max depth), (5) Filter editor (Include/Exclude lists), (6) Transforms editor (4 expansion panels), (7) Delivery policy, (8) Draft summary, Save/Cancel. Loads current config on init for source/sink lookup. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Routes.razor` *(edit, ~3 LOC)* | Flip "Add Route" button from `Disabled="true"` to `Href="/routes/new"`. Drop tooltip. |
| `tests/ElpisEdgeConnect.Management.Tests/RouteWizardModelTests.cs` *(new, ~250 LOC)* | ~15 tests: defaults, `BuildRouteConfig` produces correct shape, sub-models project correctly, glob-pattern validation, deadband cross-validation, blank RouteId rejection, empty SinkInstanceIds rejection. |
| `tests/ElpisEdgeConnect.Management.Tests/WizardConfigMergerTests.cs` *(edit, ~80 LOC)* | Extend existing test class with ~5 tests for `BuildNewRouteDraft`: happy path, dup-route-id rejection, phantom-source rejection, phantom-sink rejection, disabled-source rejection. |

### 3.3 Tests (~20 new methods)

`RouteWizardModelTests`:
1. `Defaults_HaveSensibleValues` — empty Include defaults to `["*"]`; Exclude null; buffer mode StoreAndForward; max depth 10000.
2. `BuildRouteConfig_RoundtripsAllFields` — set every field, project, assert.
3. `Filter_GlobPatterns_AreAccepted` — `"*"`, `"axes/*"`, `"spindle/speed"`, `"axes/?_position"`.
4. `Filter_EmptyIncludeList_ProjectsToDefaultAsterisk` — empty `Include` collapses to `["*"]`.
5. `Transforms_AllEmptyDicts_CollapseToNullSubBlocks` — wizard with no transforms produces a `TransformProfileConfig` where all sub-dicts are null OR a `null` profile entirely.
6. `Transforms_TagMappingRow_RoundtripsViaProjection` — one mapping pair → typed dict.
7. `Transforms_Deadband_AndDeadbandPercent_OnSameTag_Rejected` — defensive cross-validation at projection time.
8. `Transforms_RateLimitMs_NonPositive_Rejected`.
9. `RouteId_RegexEnforced` — matches `^[A-Za-z0-9][A-Za-z0-9._-]*$`.
10. `RouteId_Blank_ProjectionThrows`.
11. `SinkInstanceIds_Empty_ProjectionThrows` — at least one sink required.
12. `Delivery_DefaultsToAtLeastOnce` — pin the default mode.
13. `EnrichmentTags_NotExposed_InV1` — model has no field for it; serialisation roundtrip confirms the field is absent.
14. (Filter editor model tests) — Include/Exclude add/remove preserve order; duplicates dedup'd.
15. (Transforms editor model tests) — kv-row addition; row removal; validation aggregation across sub-blocks.

`WizardConfigMergerTests` (route additions):
16. `BuildNewRouteDraft_HappyPath_AppendsRoute`.
17. `BuildNewRouteDraft_DupRouteId_Rejected`.
18. `BuildNewRouteDraft_UnknownSourceId_Rejected`.
19. `BuildNewRouteDraft_UnknownSinkId_Rejected_EvenIfOneSinkExists` — fanout validation must check ALL sinks.
20. `BuildNewRouteDraft_SourceDisabled_Rejected` — Core invariant.

### 3.4 OPEN for ChatGPT review

| # | Question |
|---|---|
| R-Q1 | **Filter glob validation** — should the wizard validate glob syntax at add-time, or accept any string and rely on the runtime to reject bad patterns? Validation in the wizard is safer; runtime-only is simpler. |
| R-Q2 | **Transforms UX** — 4 dictionary editors in 4 expansion panels is a LOT of UI. Should we collapse Deadband + DeadbandPercent into one "Deadband" tabbed editor (absolute vs percentage tabs)? Less visual noise; one cross-validation site. |
| R-Q3 | **Transforms ordering visibility** — Core's transform pipeline runs in a fixed order (tag-mapping → filter → deadband → rate-limit → enrichment). Should the wizard surface this order visually (numbered sections), or trust the operator to read the doc? |
| R-Q4 | **Filter+Transforms as single editor vs separate sections** — both manipulate per-tag behaviour. Single Tags section with "rename / filter / deadband / rate-limit" sub-tabs would be more cohesive. Trade-off: doesn't match the underlying schema's split, harder to project back. |
| R-Q5 | **Buffer mode default** — `StoreAndForward` matches M.2b.1/M.2b.3 source-wizard defaults. Confirm vs `InMemory` for the route wizard? |

---

## 4. M.2b.6 — Destination Wizard (with MQTT Test Connection)

### 4.1 Locked decisions

| # | Decision | Reasoning |
|---|---|---|
| D-A | **Protocol-picker page at `/destinations/new`**, mirrors `ChooseSourceProtocol.razor` | Reuses the `SourceProtocolPickerModel` pattern via a new sibling `DestinationProtocolPickerModel`. Two tiles in v1: MQTT (Available), OPC UA Server (Available). HTTP/TCP placeholders if/when their sinks ship. |
| D-B | **Per-protocol Razor pages**: `/destinations/new/mqtt`, `/destinations/new/opcua` | Same shape as `/sources/new/modbus`, `/sources/new/focas2`. |
| D-C | **`MqttSinkWizardModel` + `OpcUaServerSinkWizardModel`** POCOs | Per-protocol view models. Each has `BuildSinkInstance()` returning canonical `SinkInstanceConfig`. |
| D-D | **`WizardConfigMerger.BuildNewSinkDraft(currentConfig, newSink, wiring)`** | Parallel to `BuildNewDraft(...)` for sources. `RouteWiring` reused unchanged. Dup-sink-id rejection. |
| D-E | **MQTT Test Connection in v1** (user-confirmed) | New endpoint `POST /api/v1/sinks/test-connection/mqtt`. Throwaway `MqttClient` does CONNECT + DISCONNECT against the configured broker. ~5s timeout. License-gated. Per-IpPort single-flight (mirrors Browse Controller's per-IpPort lease). ADR-0011's "discovery is management-plane ephemeral" principle applies symmetrically (this is verification, not data path). |
| D-F | **NO OPC UA Server "Test Connection"** | OPC UA Server is an ACCEPTOR (binds a port; clients connect to it). The wizard's equivalent verification is "can we bind?" which requires actually starting the server, which has side effects. Defer to v2; document the asymmetry in the wizard caption. |
| D-G | **Sink-creating-disabled-without-route** mirrors source-wizard logic | When `RouteWiring.NotWired`, the sink is created with `Enabled = false` so Core's startup validator doesn't fault on `CONFIG.SINK_WITHOUT_ROUTE`. `WizardConfigMerger.BuildNewSinkDraft` enforces this defence-in-depth. |
| D-H | **License-gate on Test Connection probe** uses `sink-mqtt` module key | Mirrors `source-focas2` for Browse Controller. |

### 4.2 Deliverables

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/DestinationProtocolPickerModel.cs` *(new, ~80 LOC)* | Sibling of `SourceProtocolPickerModel`. Tile catalogue: MQTT (Available), OPC UA Server (Available). |
| `src/ElpisEdgeConnect.Management/Wizards/MqttSinkWizardModel.cs` *(new, ~180 LOC)* | POCO matching `MqttSinkConfiguration` fields. `BuildSinkInstance()` returns canonical `SinkInstanceConfig` with `protocolName = "mqtt"`. |
| `src/ElpisEdgeConnect.Management/Wizards/OpcUaServerSinkWizardModel.cs` *(new, ~180 LOC)* | POCO matching the OPC UA Server config fields. `BuildSinkInstance()` returns canonical `SinkInstanceConfig` with `protocolName = "opcua-server"`. |
| `src/ElpisEdgeConnect.Management/Wizards/WizardConfigMerger.cs` *(edit, +50 LOC)* | New method `BuildNewSinkDraft(currentConfig, newSink, wiring)`. Pure function. Guards: dup sink-id, dup route-id (if wiring is NewRoute), source-ref-exists (if wiring is NewRoute), sink must be `Enabled = false` if `NotWired`. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/ChooseDestinationProtocol.razor` *(new, ~80 LOC)* | `@page "/destinations/new"`. Renders from `DestinationProtocolPickerModel`. Mirrors `ChooseSourceProtocol.razor`. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddMqttDestination.razor` *(new, ~500 LOC)* | `@page "/destinations/new/mqtt"`. Sections: Identity, Connection (broker host/port/credentials/TLS), Topic policy (mode: PerTag vs Batch, topic template), Test Connection, Routing (NotWired or NewRoute), Draft summary. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddOpcUaServerDestination.razor` *(new, ~400 LOC)* | `@page "/destinations/new/opcua"`. Sections: Identity, Server config (port, certificate path, namespace URI), Tags exposed, Routing, Draft summary. No Test Connection section. |
| `src/ElpisEdgeConnect.Management/Api/MqttTestConnectionApi.cs` *(new, ~80 LOC)* | `POST /api/v1/sinks/test-connection/mqtt`. License-gated. Delegates to `MqttTestConnectionService`. Status mapping mirrors `Focas2BrowseApi`: 200/400/403/409/500. |
| `src/ElpisEdgeConnect.Management/Api/MqttTestConnectionResultDto.cs` *(new, ~40 LOC)* | `ProbeId`, `Success`, `BrokerVersion`, `ErrorCode`, `ErrorMessage`, `Warnings`, `ElapsedMs`. |
| `src/ElpisEdgeConnect.Management/Api/MqttTestConnectionService.cs` *(new, ~150 LOC)* | Owns the probe sequence: license gate → single-flight per `host:port` → throwaway `MqttClient.ConnectAsync(...)` with bounded timeout → `DisconnectAsync` → return result. Production ctor: real factory; internal test ctor: injectable factory delegate. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Sinks.razor` *(edit, ~3 LOC)* | Flip "Add Destination" button from `Disabled="true"` to `Href="/destinations/new"`. |
| `tests/ElpisEdgeConnect.Management.Tests/MqttSinkWizardModelTests.cs` *(new, ~150 LOC)* | ~8 tests — same shape as `Focas2SourceWizardModelTests`. |
| `tests/ElpisEdgeConnect.Management.Tests/OpcUaServerSinkWizardModelTests.cs` *(new, ~150 LOC)* | ~8 tests. |
| `tests/ElpisEdgeConnect.Management.Tests/DestinationProtocolPickerModelTests.cs` *(new, ~50 LOC)* | ~3 tests, mirroring `SourceProtocolPickerModelTests`. |
| `tests/ElpisEdgeConnect.Management.Tests/MqttTestConnectionServiceTests.cs` *(new, ~180 LOC)* | ~6 tests: happy path, license-disabled, connect-fail, timeout, single-flight busy, probe-id surfaced. Mirrors `Focas2BrowseServiceTests`. |
| `tests/ElpisEdgeConnect.Management.Tests/WizardConfigMergerTests.cs` *(edit, ~100 LOC)* | +6 tests for `BuildNewSinkDraft`: happy path, dup-sink-id, NotWired forces sink Enabled=false, NewRoute appends route, NewRoute with unknown source rejected, NewRoute dup-route-id rejected. |

### 4.3 Tests (~30 new methods)

(See §4.2 file table for per-file counts. Detailed test names per file follow the patterns established in M.2b.3 and M.2b.3.1.)

### 4.4 OPEN for ChatGPT review

| # | Question |
|---|---|
| D-Q1 | **MQTT Test Connection — should the probe try a publish** to a test topic (e.g. `eremos/_test/<probeId>`) and verify round-trip, or just stop at successful CONNECT? Round-trip is stronger verification but pollutes the broker with a one-off topic. |
| D-Q2 | **TLS configuration UI** — MQTT supports plain TCP, TLS, mutual-TLS. Should the wizard cover all three in v1, or just plain TCP (most common dev/demo) with a "use JSON for TLS" note? |
| D-Q3 | **OPC UA Server certificate management** — server cert is typically auto-generated on first start. Wizard exposes path-to-cert or trusts the auto-generate flow? |
| D-Q4 | **Destination protocol-picker tile order** — MQTT first (more common) or OPC UA Server first (alphabetical)? Affects the tile that gets default focus on the picker page. |
| D-Q5 | **Per-broker single-flight key** — current MQTT broker hostname:port is the natural key, mirroring Focas2BrowseService's `IpAddress:Port`. Confirm? |
| D-Q6 | **Symmetry with FOCAS2 fake mode** — should there be an `EDGECONNECT_MQTT_FAKE_MODE` for MQTT sink demo mode? Probably no — MQTT is easy to stand up locally (Mosquitto is one apt-get away). Confirm. |

---

## 5. Out of scope (both milestones)

- **Editing existing routes/destinations via wizard.** Editing remains JSON-only via `/config`.
- **Bulk import of routes/destinations.** CSV / template-library is a future milestone (mirrors M.2c source-tag-template work).
- **Filter/Transforms preview** of "what a tag becomes after the pipeline" — requires runtime-aware preview state.
- **OPC UA Server Test Connection** — see Locked D-F.
- **TLS/mTLS UI** for MQTT in v1 if R-Q2 verdict is "plain only". TLS edits go through Config-page JSON.
- **Demo mode for sinks** — Mosquitto is trivially available locally; no parallel to FOCAS2 demo mode.

---

## 6. Sequence of work

### 6.1 M.2b.5 (Route Wizard)

1. **Reality check.** Read `RouteDefinitionFactory.BuildOne` to confirm cross-record validation flow + confirm `RouteConfig` projection roundtrips cleanly. Read `TagFilterConfig` glob validator (if any) in Core's filter engine. Read `TransformProfileConfig` cross-validation rules (deadband+deadbandPercent on same tag).
2. Write `RouteFilterEditorModel` + `RouteTransformsEditorModel` + their tests.
3. **Internal gate** — Management.Tests green.
4. Write `RouteWizardModel` + tests.
5. **Internal gate.**
6. Extend `WizardConfigMerger` with `BuildNewRouteDraft` + tests.
7. **Internal gate.**
8. Write `AddRoute.razor`.
9. Flip `Routes.razor` button.
10. **Full regression gate.** Solution build + test sweep. Target ~1855.
11. Manual smoke against the demo gateway (route a FOCAS2 source to an MQTT sink).
12. Commit + PR.

### 6.2 M.2b.6 (Destination Wizard)

1. **Reality check.** Read `MqttSinkConfiguration` + the OPC UA Server sink config. Read existing sink registration extensions to confirm license-module keys. Verify MQTT client library is loadable without a running broker (so the Test Connection probe doesn't require broker presence to construct, only to actually connect).
2. Write `DestinationProtocolPickerModel` + tests.
3. Write `MqttSinkWizardModel` + tests.
4. Write `OpcUaServerSinkWizardModel` + tests.
5. **Internal gate.**
6. Extend `WizardConfigMerger` with `BuildNewSinkDraft` + tests.
7. Write `MqttTestConnectionResultDto` + `MqttTestConnectionService` + tests.
8. Write `MqttTestConnectionApi` endpoint.
9. **Internal gate.**
10. Write `ChooseDestinationProtocol.razor`, `AddMqttDestination.razor`, `AddOpcUaServerDestination.razor`.
11. Flip `Sinks.razor` button.
12. **Full regression gate.** Target ~1880.
13. Manual smoke against demo gateway (add MQTT sink + wire route).
14. Commit + PR.

---

## 7. Risks & mitigations

| Risk | Mitigation |
|---|---|
| `WizardConfigMerger` becomes a god-class with three overloads | Acceptable for v1; each method is pure, well-scoped, and shares only the `GatewayConfiguration` input. If a fourth overload appears (e.g. bulk-import), revisit. |
| Transforms editor UI is overwhelming | R-Q2 + R-Q4 review-pass: collapse Deadband+DeadbandPercent into tabs; consider merging Filter + Transforms into one Tags section. |
| MQTT Test Connection requires a real client library — and one might not be loadable in the test environment | Service has a factory seam; tests inject a fake `IMqttClient` factory delegate (mirrors how `Focas2BrowseService` accepts `Func<string, ISourceAdapter>`). |
| OPC UA Server wizard fields might not match the existing config shape | Reality check step 1 of M.2b.6 surfaces any drift before writing the wizard. |
| Per-protocol Razor pages duplicate too much | If duplication is bad, extract shared identity/routing sections into Razor partials. Don't preempt; refactor only after second wizard is in. |
| Filter glob validation accepts patterns the runtime rejects | Pin the validator behaviour by sharing the Core filter-pattern validator (if it exists) between Core and the wizard model. Reality check step 1 confirms. |

---

## 8. Definition of done

### M.2b.5

1. `dotnet build` 0 warnings, 0 errors.
2. Full sweep at ~1855.
3. `/routes` "Add Route" button clickable → lands at `/routes/new`.
4. `/routes/new` produces draft via `/api/v1/config/drafts` indistinguishable from a hand-authored route.
5. Manual smoke: create route via wizard, Apply, verify route enters Running state with the demo gateway.
6. `WizardConfigMerger.BuildNewDraft` (source) unchanged — verify by `git diff`.

### M.2b.6

1. `dotnet build` 0 warnings, 0 errors.
2. Full sweep at ~1880.
3. `/sinks` "Add Destination" button clickable → lands at `/destinations/new`.
4. MQTT wizard → Test Connection against `localhost:1883` (with Mosquitto running) returns success with ProbeId + ElapsedMs.
5. MQTT wizard → Test Connection against unreachable broker returns `MQTT.CONNECT_FAILED` (or equivalent).
6. OPC UA Server wizard produces valid draft; no Test Connection section.
7. Saving a sink draft + applying produces a Running sink that can receive traffic from a route.

---

## 9. Combined OPEN questions for ChatGPT review

(In addition to per-milestone OPEN questions above.)

| # | Question |
|---|---|
| Q1 | **Plan-doc shape** — one combined plan (this doc) or two separate plan docs? One is cohesive; two are cleaner per-milestone review surfaces. Lean: keep combined for v1 review, then split if implementation phases diverge significantly. |
| Q2 | **Naming**: "Destination" (operator term) or "Sink" (engineering term) in the wizard URLs and copy? Sinks.razor / sinks.cs uses "Destination" in operator-facing tooltips; codebase uses "Sink" internally. Wizard URL is `/destinations/new/...` to match the operator term; codebase internals stay `Sink*`. Confirm? |
| Q3 | **Razor copy strings** — both wizards add significant new copy. Pin every visible string via POCO constants (like LayoutChromeModel did for the banner) to keep them testable? Or accept Razor-inline copy and rely on manual smoke? |
| Q4 | **`WizardConfigMerger` source signature drift** — the existing `BuildNewDraft(config, source, wiring)` is now joined by `BuildNewRouteDraft(config, route)` and `BuildNewSinkDraft(config, sink, wiring)`. Should we rename the source method to `BuildNewSourceDraft` for symmetry, accepting the M.2b.1 / M.2b.3 callers need updating? Or leave the source method as-is for compatibility? |
| Q5 | **Studio chrome** — both wizards add visual mass to the source-wizard family. Should we introduce a `/sources/new`, `/routes/new`, `/destinations/new` consistency check (e.g. a shared "wizard header" partial)? Out of scope for v1; flag for follow-on. |
| Q6 | **Route wizard vs M.2b.3.1 demo mode** — when demo mode is on, the route wizard still works exactly the same way (demo mode only affects FOCAS2 source dispatch). Test specifically that demo mode does NOT change route-wizard or destination-wizard behaviour? Probably no — demo-mode dispatch is downstream of these wizards. |

---

## 10. Scope summary

### M.2b.5 (Route Wizard)
- ~250 LOC `RouteWizardModel`
- ~80 LOC `RouteFilterEditorModel`
- ~150 LOC `RouteTransformsEditorModel`
- ~60 LOC `WizardConfigMerger` extension
- ~600 LOC `AddRoute.razor`
- ~3 LOC `Routes.razor` button flip
- ~250 LOC wizard tests (~15 tests)
- ~80 LOC merger tests (~5 tests)

### M.2b.6 (Destination Wizard)
- ~80 LOC `DestinationProtocolPickerModel`
- ~180 LOC `MqttSinkWizardModel`
- ~180 LOC `OpcUaServerSinkWizardModel`
- ~50 LOC `WizardConfigMerger` extension
- ~80 LOC `ChooseDestinationProtocol.razor`
- ~500 LOC `AddMqttDestination.razor`
- ~400 LOC `AddOpcUaServerDestination.razor`
- ~80 LOC `MqttTestConnectionApi` + DTO
- ~150 LOC `MqttTestConnectionService`
- ~3 LOC `Sinks.razor` button flip
- ~150 LOC `MqttSinkWizardModelTests` (~8 tests)
- ~150 LOC `OpcUaServerSinkWizardModelTests` (~8 tests)
- ~50 LOC `DestinationProtocolPickerModelTests` (~3 tests)
- ~180 LOC `MqttTestConnectionServiceTests` (~6 tests)
- ~100 LOC `WizardConfigMergerTests` extensions (~6 tests)

Combined: ~1,500 LOC code + ~960 LOC tests across both milestones. Targets: M.2b.5 ~1855, M.2b.6 ~1880.

---

**End of M.2b.5 + M.2b.6 v1 plan. Awaiting ChatGPT review pass before lock. After v2 lock, implementation order: M.2b.5 first (Route Wizard), then M.2b.6 (Destination Wizard). Each is its own PR; M.2b.6's PR is stacked on M.2b.5's branch.**
