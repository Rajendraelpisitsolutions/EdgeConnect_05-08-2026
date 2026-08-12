# M.2b.3.1 — FOCAS2 demo mode (v3 amendment: gateway-startup-event surface)

**Status:** v3 — LOCKED (Step 1 reality-check folded in)
**Date:** 2026-05-18
**Predecessor plans:**
- [`v1`](2026-05-18-mp2b31-focas2-demo-mode-plan.md) — initial draft
- [`v2`](2026-05-18-mp2b31-focas2-demo-mode-plan-v2.md) — ChatGPT review folded
**Form:** **TIGHT AMENDMENT** to v2. v2 remains the load-bearing reference for everything not explicitly amended here.
**Estimated size:** ~500 LOC code (+120 over v2) + ~280 LOC tests (+30 over v2)
**Test baseline:** 1782 → expected **~1807** after M.2b.3.1 (+7 over v2's 1800)

---

## 0. What changed v2 → v3 (delta only)

Step 1 reality-check found four passes and one open issue (priority check 3 — diagnostics-event placement). The user chose **Option B: add a new `IGatewayStartupEventStore` surface in Core**, aggregated by the existing Management `DiagnosticsEventAggregator`. Reusable for future boot-time signals (license alerts, manifest warnings, future demo modes).

### v2 sections unchanged

§1 (Goal), §2 Locked A–J, §3 Resolved questions, §4 Out of scope, §5.1–5.3 deliverables, §6 Sequence (Steps 2–10), §9 DoD #1–14, §11 ADR outline (with one added paragraph below), §12 Scope summary base.

### Step 1 reality-check verdicts (record)

| Check | Verdict |
|---|---|
| 1. `Focas2DemoApi` has zero dependency path to `Focas2Interop`/`fwlib` | **PASS.** `Focas2Interop.cs` declares the static class AND the `Odb*` structs in the same file but as separate CLR types. `Focas2DemoApi` can use the structs without referencing the static class. No `[ModuleInitializer]` in the assembly. |
| 2. Loading `Sources.Focas2` does NOT trigger native DLL resolution | **PASS.** No module initializer, no `BeforeFieldInit` shenanigans. `Focas2Interop`'s static ctor fires only on first access to its members. |
| 3. Diagnostics-event placement | **AMENDED** — see §1 below. |
| 4. `Metrics["demoMode"] = true` safe for existing consumers | **PASS.** Only consumer is `Focas2BrowseService` (uses `TryGetValue`); contract is open-ended. |
| 5. License gate still applies | **PASS by construction.** Demo dispatch happens INSIDE the adapter ctor, AFTER `Focas2RegistrationExtensions.ResolveSourceRegistrationInputs` license check has already approved/rejected. Browse path is symmetric (license check in `Focas2BrowseService.BrowseAsync` runs before adapter construction). |

---

## 1. New surface — `IGatewayStartupEventStore` in Core

### 1.1 New Locked decisions

| # | Decision | Reasoning |
|---|---|---|
| **K** | **Gateway-startup boot-time signals live in a new `IGatewayStartupEventStore` interface in `ElpisEdgeConnect.Core.Diagnostics`** | Mirrors the existing pattern: Core holds stateful diagnostic stores (`RuntimeDiagnosticsCollector`, `ConfigurationFaultRegistry`, `IConfigurationFaultRegistry`); Management aggregates and projects to wire-shape DTOs. Putting the store in Core keeps the "Management is the aggregation seam" rule intact while letting Host (which doesn't reference Management) emit boot-time events. |
| **L** | **`DiagnosticsEventAggregator` gains a third source** alongside per-route events and audit entries: gateway-startup events from `IGatewayStartupEventStore` | Same `DiagnosticsEventDto` wire shape (`RouteId = null`, gateway-scoped) as audit entries. Filter semantics: gateway-startup events are skipped when caller has pinned a specific `RouteId`. |
| **M** | **`GatewayStartupEvent` is APPEND-ONLY for the process lifetime** — never deleted, never cleared at runtime | Boot-time signals describe a state the operator chose at process start; they should remain visible for the entire run. Process restart is the only "clear" path, which matches Locked F (toggle requires restart). |
| **N** | **The store is in-memory only** — not persisted to disk, not chained, not signed | Boot-time signals are process-lifetime facts. Persisting them adds complexity (corruption recovery, schema versioning) without benefit. The next boot will re-emit any still-applicable signals from the current process state. |

### 1.2 Why Core (not Management)

Project dependency direction in this repo: **Core ← Sources.X ← Host ← Management**. Host cannot reference Management. If the store lived in Management, `EdgeConnectComposition.ConfigureRuntimeAsync` (in Host) couldn't emit to it at startup. Putting the store in Core lets both Host (emit) and Management (read + project) participate without inverting the dependency graph.

This is the same pattern as `IConfigurationFaultRegistry`: stateful registry in Core, written by Host's registration extensions, read by Management's aggregator. Established precedent.

### 1.3 Files to add or edit (delta from v2)

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Core/Diagnostics/GatewayStartupEvent.cs` *(new, ~30 LOC)* | Record: `EventCode` (e.g. `"focas2.fake-mode.activated"`), `Message`, `Severity` (`"Info"` / `"Warning"` / `"Critical"`), `EmittedAtUtc`. Frozen-on-construction. |
| `src/ElpisEdgeConnect.Core/Diagnostics/IGatewayStartupEventStore.cs` *(new, ~30 LOC)* | Interface: `void Append(GatewayStartupEvent ev)`, `IReadOnlyList<GatewayStartupEvent> GetAll()`. Thread-safe contract. |
| `src/ElpisEdgeConnect.Core/Diagnostics/GatewayStartupEventStore.cs` *(new, ~40 LOC)* | Default impl. In-memory list under a lock. Append-only (Locked M). Returns a defensive copy on `GetAll()`. |
| `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs` *(edit, +10 LOC on top of v2's +25)* | Register `IGatewayStartupEventStore` as singleton. When `Focas2DemoModeOptions.IsEnabled`, append: `new GatewayStartupEvent { EventCode = "focas2.fake-mode.activated", Severity = "Critical", Message = "FOCAS2 fake mode is active — all FOCAS2 sources use a synthetic controller. To disable, clear EDGECONNECT_FOCAS2_FAKE_MODE and restart.", EmittedAtUtc = DateTime.UtcNow }`. The existing `LogCritical` + Prometheus gauge from v2 stay. |
| `src/ElpisEdgeConnect.Management/Diagnostics/DiagnosticsEventAggregator.cs` *(edit, ~25 LOC)* | Constructor gains a fourth dependency: `IGatewayStartupEventStore startupEvents`. `GetRecentEventsAsync` adds a third event-source block (gateway-scoped, skipped when `filter.RouteId` is set). New private `MapStartupEvent(GatewayStartupEvent)` projection method following the same shape as `MapAuditEntry`. |

### 1.4 Test additions (delta from v2)

| File | Change |
|---|---|
| `tests/ElpisEdgeConnect.Core.Tests/Diagnostics/GatewayStartupEventStoreTests.cs` *(new, ~80 LOC)* | ~5 tests: append + retrieve; thread-safe concurrent appends; append-only invariant (no Clear, no Remove); `GetAll` returns defensive copy; ordering preserved (FIFO). |
| `tests/ElpisEdgeConnect.Management.Tests/DiagnosticsEventAggregatorTests.cs` *(edit, ~30 LOC)* | Extend existing test class with ~2 tests: gateway-startup events appear in `GetRecentEventsAsync` when `filter.RouteId` is null; gateway-startup events are SKIPPED when `filter.RouteId` is set. The existing fixture seeds route-events and audit entries; we add a fake `IGatewayStartupEventStore` to the constructor. |

### 1.5 Updated test count

- v2 target: 1800
- v3 target: **~1807** (+5 Core store tests + 2 aggregator integration tests)

### 1.6 Updated scope summary (delta only)

Adds to v2's totals:
- ~100 LOC Core surface (`GatewayStartupEvent.cs` + `IGatewayStartupEventStore.cs` + `GatewayStartupEventStore.cs`)
- ~10 LOC Host emit (additive to v2's startup edit)
- ~25 LOC Management aggregator integration
- ~80 LOC Core store tests
- ~30 LOC aggregator test extension

Total ~245 LOC, ~110 of which are tests.

---

## 2. Updated DoD clauses (delta from v2)

The full DoD from v2 §9 still applies. Two additions:

15. **Locked K verified:** `IGatewayStartupEventStore` and `GatewayStartupEventStore` live in `ElpisEdgeConnect.Core/Diagnostics/`. DI registration happens in `EdgeConnectComposition.ConfigureRuntimeAsync` (Host), not in `AddConnectivityStudio` (Management).
16. **Locked L verified:** `DiagnosticsEventAggregatorTests` covers the gateway-startup events path; the new fixture exercises `GetRecentEventsAsync` with and without `filter.RouteId` pinning; both new tests green.
17. **Step 9 manual smoke amended:** Studio's Diagnostics page now shows the "focas2.fake-mode.activated" event (severity Critical, no route id) in the gateway-scoped list whenever demo mode is on.

---

## 3. ADR-0012 framing addition

v2 §11 has the ADR-0012 outline. Add one paragraph to the "Consequences" section:

> A new general-purpose `IGatewayStartupEventStore` is introduced in `ElpisEdgeConnect.Core/Diagnostics/` to surface boot-time process-state observations to the Studio's Diagnostics surface without abusing the audit chain (which describes config CHANGES per ADR-0006) or the per-route events (which need a route scope). The demo-mode activation is the first consumer; future use cases include license-state alerts, native-library-load warnings, and manifest-mismatch signals. The store is append-only for the process lifetime, in-memory only, and lives in Core so both `EdgeConnectComposition` (Host) can write to it and `DiagnosticsEventAggregator` (Management) can read from it without inverting the project dependency graph.

ADR cross-link: this new surface mirrors the established `IConfigurationFaultRegistry` pattern (stateful registry in Core, written by Host, read by Management).

---

## 4. Sequence amendment

v2 §6 sequence steps 2–10 remain. Add **Step 1.5** between Step 1 (now complete) and Step 2:

| 1.5 | Write the new Core surface: `GatewayStartupEvent.cs`, `IGatewayStartupEventStore.cs`, `GatewayStartupEventStore.cs` + 5 store tests. Register in `EdgeConnectComposition` (Host) but do NOT yet integrate with aggregator. | Internal gate — Core.Tests green; +5 new tests. |

Then v2 Step 6 (Management options + Studio banner edit) gains a sub-step:

| 6a | Edit `DiagnosticsEventAggregator` to accept and consume `IGatewayStartupEventStore`. Extend `DiagnosticsEventAggregatorTests` with 2 new cases. | Internal gate — Management.Tests green; +2 new tests. |

---

## 5. Risks (delta from v2)

Add one row to v2's §8 table:

| Risk | Mitigation |
|---|---|
| `DiagnosticsEventAggregator` constructor signature change breaks existing DI registration | The aggregator is registered in `AddConnectivityStudio` with `AddSingleton<IDiagnosticsEventAggregator, DiagnosticsEventAggregator>()`. Adding a 4th constructor parameter is picked up automatically by Microsoft DI as long as `IGatewayStartupEventStore` is also registered (which Host does at the same composition step). Existing tests construct the aggregator directly; they'll need the new fixture parameter — covered by §1.4. |

---

## 6. Pause-points (unchanged from v2)

v2 §10 pause-point criteria carry forward. No new pause-points; this v3 amendment is purely additive on top of v2's locked structure.

---

**End of M.2b.3.1 v3 amendment. LOCKED 2026-05-18 after Step 1 reality-check. Implementation per v2 §6 sequence + the v3 §4 insertions starting at Step 1.5.**
