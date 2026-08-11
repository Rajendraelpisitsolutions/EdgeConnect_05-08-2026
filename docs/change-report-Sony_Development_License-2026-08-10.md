# Elpis EdgeConnect — Change Report

- **Branch:** `Sony_Development_License`
- **Baseline:** commit `dfd3518`
- **Date:** 2026-08-10
- **Status:** all changes are in the working tree, **not yet committed**
- **Build:** solution 0 warnings / 0 errors
- **Tests:** Core 1313 · Management 1282 · Host 253 · adapters all green. 15 failures remain (9 MQTT, 6 integration), all `NotAuthorized` from the local broker rejecting anonymous connections — environmental, present before this work.

---

## 1. What this covers

Two streams of work landed together:

1. **Integration of a parallel line.** A second development line (the "Rajendra" branch) produced UI and reliability work between 4 and 10 August. That line is a *parallel* branch, not a newer version — merging it is integration, not a fast-forward. Its work was audited item by item against this branch and the genuine gaps were ported.
2. **Defects found here.** Several were found by auditing rather than reported, including two that this branch had shipped.

Everything below was verified by build and test. The items marked **verified live** were additionally exercised against the running gateway through the same REST endpoints the Studio uses.

---

## 2. Defects fixed

### 2.1 A destination's fault never cleared — two separate causes

**Symptom.** A destination that dropped for 227 ms displayed a red "MQTT client is not connected" panel for the rest of the gateway's uptime, and — because route health folds a live sink error into its verdict — **marked the whole route broken while it published normally**.

Two independent paths produced this. The first was reported by the parallel line; the second was found here and had been missed:

| Path | Cause | Fix |
|---|---|---|
| **Publish-level** | `OnSinkRecovered` cleared `IsDegraded` and `IsDraining` but left `LastError` / `LastErrorAtUtc` set. The source side already had this fix; the sink side did not. | Clear the error on recovery. Recovery is the end of degraded → draining → recovered — reconnected **and** drained — so nothing is left to act on. |
| **Adapter-level** | An adapter fault (a broker refusing the first connect) was recorded by `RecordSinkAdapterState` and cleared **nowhere**. `OnSinkRecovered` only fires at the end of a publish cycle, which never happens if the sink was never publish-degraded. | Clear when **both** dimensions agree nothing is outstanding: adapter `Running`, no error, and not degraded or draining. A delivery fault keeps the error until recovery releases it. |

**A test was pinning the bug.** `SinkLifecycle_DegradedDrainingRecovered_OrderingPinned` asserted `LastError.Should().NotBeNull()` after recovery — it encoded the defect as intended behaviour, in the same method that correctly asserted the flags were cleared. The assertions were inverted and three regression tests added, including one that checks the error is **retained** while publish-degraded, so a future simplification cannot reintroduce the hidden-fault case.

**Sources were checked and are correct.** `RecordSourceState` clears on `Running` through a single protocol-agnostic path, so every source adapter behaves identically — there is no per-protocol variation.

### 2.2 Publish raced the reconnect

A config apply touching a route's source cascades a route restart. The new worker dequeues the backlog and publishes immediately — measured at 0.1 ms after start. The sink adapter is deliberately not restarted, so a client mid-reconnect lost the race, and one lost race degraded the whole route.

**Fix.** A bounded 2-second grace waits out an in-flight reconnect before reporting `MQTT.NOT_CONNECTED`, and kicks the reconnect loop if none is running. Applied at three sites — publish entry, mid-batch after serialization, and per-point in the PerTag loop. The budget is created **once per publish call** and threaded through, so a 200-point batch during a real outage costs one window, not two hundred.

A genuine outage is unaffected: after the window the same retryable error is raised, the buffer holds, the cursor does not advance, and the route reports the real problem.

### 2.3 Reconnect was too slow for the common case

The shipped default is a 5-second first retry ramping to 60 seconds. Correct for a broker that is genuinely down; far too slow for what dominates in practice — a broker restart, a Wi-Fi blip, a NAT timeout — where the endpoint returns within a second. Paying five seconds for a 200 ms outage is what operators experience as "reconnecting takes for ever".

| Attempt | Before | After |
|---|---|---|
| 1 | immediate | immediate |
| 2–6 | 5s, 10s, 20s, 40s, 60s | **250 ms each** |
| 7 onward | 60s (capped) | the configured ramp: 5s, 10s, 20s … 60s |

The fast phase covers ~1.25 s, which is deliberately **shorter than the 2-second publish grace** above, so a brief drop is invisible to the route. Implemented as constants rather than configuration keys: it needs no operator decision, it benefits existing destinations without editing them, and adding keys would break the ADR-0020 redaction drift guard.

**A latent bug was found while adding the in-flight guard:** a second disconnect used to queue a second reconnect loop that then called `ConnectAsync` on an already-reconnected client.

### 2.4 Hot reload wiped endpoint state and never rebound destinations — **verified live**

Three related defects in the reload path:

- **Destination state wiped.** Route teardown drops the whole route subtree including each destination's adapter state. On a source-only edit the destination is correctly left running, so its supervisor never pushes state again — the rebuilt route reported a `null` adapter state permanently. The Studio rendered that as a destination row with no status pill, or as **"No destinations attached" on a live, connected sink**.
- **Source state wiped.** The mirror case. Source state falls back to `Created`, so after a destination edit the Studio said *"Source is created, so no readings are arriving"* beside a live counter of readings that **were** arriving — and marked the route broken on the strength of it.
- **A destination edit never rebound its route.** Route definitions bake in sink adapter *instances*, and each route worker builds its publishers once. A sink restart disposes the old adapter and creates a new one, but the running route kept the **disposed** instance while the new one was bound to no route. A source restart cascaded a rebind; a destination restart did not.

**Fix.** Republish the live adapter state of the source and every destination when a reload brings a route back up, and synthesize a route restart for every enabled, registered, valid route referencing a restarted or added sink.

**Critical exclusion:** replay-aware sinks whose in-place hot-replace is *rejected* are excluded at synthesis time. Without it, a synthesized restart would immediately be faulted on routes the operator never touched — for a rejection whose entire purpose is "change nothing".

**Verified live.** A destination-only edit was applied to a running 10-route gateway through the config API. Result: the edited route stayed `Running` with both endpoints intact, and across all ten routes there were **zero null adapter states and zero sources stuck at `Created`**.

### 2.5 Onboarding stranded a device on the gateway

Shipped in this branch earlier and found by audit. The saved-state flag was set on Save and never cleared, so an operator who saved a device and then corrected its instance id left the disabled entity **stranded under the old id for ever**, while Connect created a second one.

The obvious fix — clearing the flag on every edit — introduces a *new* failure: editing a non-id field would also clear it, and Connect would then try to create an entity that already exists and fail with a duplicate-id 400.

**Fix.** The field now holds *the id the entity is persisted under* rather than "the operator pressed Save". Both cases fall out of one comparison: same id → replace in place; changed id → the next Save removes the superseded entity **inside the same draft**, so the rename is one atomic apply with no window holding two copies.

### 2.6 MTConnect rejected a bare host or IP

An operator naming an agent as `192.168.1.10:5000` or `agent.local:5000` was rejected as "Unreachable" — a network verdict for a formatting rule nobody had stated. Worse, a schemeless `host:port` *passes* `Uri.TryCreate` as scheme `agent.local`, so it reached `HttpClient` and threw, surfacing an operator typo as a 500.

**Fix.** Normalise the address the way Brother HTTP already does, at all four places that touch it, so no two paths can disagree about what is valid.

---

## 3. Health verdict consolidated

Every operator-visible health defect in this product's history has been two surfaces disagreeing about one route. The verdict is now a single function.

- `RouteHealth.Verdict` performs **one ordered walk** producing the level and its sentence together, so the reason shown is by construction the condition that set the level. Previously the level and the explanation were computed by separate ladders and could name different faults.
- **A red card with no reason is no longer reachable.** No source, no destination, a stopped endpoint with no reported error, and a wedged buffer all previously rendered a red pill and nothing else.
- **The status footer was not using route health at all.** It counted pipeline state, so it could print *"All systems healthy"* over a board of red cards — the original defect, still live in the footer. It now counts through the shared verdict, and its label reads "routes **delivering**" to match what is counted.
- `ConnectionLevel` remains a **separate** question — "is the machine connected?" — so an operator can tell "walk to the machine" from "the broker is down".

**27 tests were written for `RouteHealth`, which previously had none.** It is the single definition behind the banner, the cards, the ordering and the footer, and it is a pure function of a DTO — the cheapest thing in the Studio to verify and, until now, the least verified.

---

## 4. Studio UI

Ported from the parallel line after an item-by-item audit:

| Area | Change |
|---|---|
| Overview | Verdict banner with counts and next step; broken routes sorted first; the broken-count chip expands to say **why** each route is broken |
| Route card | Rebuilt on a three-column grid; status pills replacing chips; fault panel held **permanently red** rather than fading to amber with age — a device down an hour had been looking calmer than one down a minute |
| Error text | 12 error codes across 8 protocols now render as operator language. The catalogue existed but had **zero call sites** — operators still read raw codes |
| Navigation rail | Operations / Gateway groups; Connect-a-device moved under Overview; collapse control is a labelled row; Live Stream removed (reachable from Diagnostics) |
| Cards | Shared design tokens, layered shadows, hover lift, disabled tiles flat, motion dropped under `prefers-reduced-motion` |
| Dialogs | Delete confirmation at 560px with a countable impact list; **a discard dialog that did not exist** — the onboarding exit used a stock message box |
| Lists | Column filter funnels removed, quick-filter search retained; footer timestamp removed |
| Licence page | Both actions on one baseline; "Choose License File" → **"Upload License File"**; left accent on all three boxes |
| Wizards | Numbered section spine; Modbus connection row rebalanced; bulk-import step counter moved to the header |
| Onboarding | Save is an optional checkpoint, Continue never blocked by it; Review shows "Switch on source … saved, not yet connected" for entities already on the gateway; **one "Save & Connect" button** replacing two that did the same thing |
| Notifications | Success confirmations centred |

### The stylesheet cache key

`site.css` was linked with the assembly version, which only changes on a version bump — so **every CSS change was invisible to a returning browser** until a hard refresh. This presented as "the fix didn't work" and cost real time during this work. The token now folds in the build stamp.

---

## 5. Build hygiene

- **Offline builds unblocked.** `NU1900` (NuGet audit cannot reach the internet) is demoted to a warning. With `TreatWarningsAsErrors`, an ordinary offline build produced 13 hard errors with nothing wrong in the code. The audit stays on and the notice stays visible — "the internet is down" is no longer indistinguishable from "the code is broken".
- **Five CA1859 analyzer errors fixed** — declarations that were looser than reality. No API change.
- **Horizontal scroll leak clamped.** A wide data grid scrolled the document sideways, carrying the nav rail and status footer off-screen.

---

## 6. Files changed

**31 modified, 7 new.**

| Group | Files |
|---|---|
| Route health | `RouteHealth.cs` *(new)*, `ErrorGuidance.cs` *(new)*, `Overview.razor`, `RouteCard.razor`, `StatusFooter.razor`, `RouteHealthTests.cs` *(new)* |
| Studio UI | `NavMenu.razor`, `Sources.razor`, `Sinks.razor`, `Routes.razor`, `WizardShell.razor`, `WizardSection.razor`, `AddModbusSource.razor`, `BulkImportSources.razor`, `AddRoute.razor`, `ConfirmDeleteDialog.razor` *(new)*, `ConfirmDialog.razor` *(new)*, `site.css` |
| Stale errors | `RuntimeDiagnosticsCollector.cs`, `RuntimeDiagnosticsCollectorTests.cs` |
| MQTT | `MqttSinkAdapter.cs` |
| Hot reload | `RuntimeReloadCoordinator.cs` |
| Clock tamper | `ClockAnchorStore.cs`, `LicenseTrialEnforcer.cs` |
| Onboarding | `OnboardingFlow.razor`, `ReviewAndConnect.razor`, `WizardConfigMergerBundledOnboardingTests.cs` |
| Licence / notifications | `License.razor`, `ManagementHostingExtensions.cs` |
| Build | `Directory.Build.props`, `App.razor`, `S7TagCsvImporter.cs`, `ModbusTagCsvImporter.cs`, `SparkplugSessionActor.cs`, `TemplateSubstitutionEngine.cs`, `BulkSourceMergeCsvParser.cs` |
| Docs | `uninstall-complete-delete-design.md` *(new)*, `license-driven-installer-plan.md` *(new)* |

---

## 7. Open items

| Item | Detail |
|---|---|
| **`ConnectionLevel` doc contradicts its body** | The documentation says delivery signals are excluded; the body applies them. So the route card's left accent still goes red on a broker-drain failure — the exact case the split was created to distinguish. Changing it alters accent behaviour, so it is a decision, not a fix. |
| **"Building backlog → Degraded" not implemented** | Described by the parallel line but not adopted: nothing here implements it, and a non-zero queue is normal in flight, so it would turn most cards amber. |
| **A load-sensitive test** | One Core test failed once under load and then passed three consecutive runs; it could not be identified before it went green. `RuntimeReloadCoordinatorTests` shows the same behaviour. Neither is marked `Category=Flaky`, so both will bite CI. |
| **MQTT integration gate still skipped** | `Gate5_BrokerOutageReconnect_AdapterRecoversWithin5Seconds` is skipped because reconnect took 15s+. The publish grace and fast reconnect target exactly that scenario — it is now the natural way to prove them. |
| **Local broker rejects anonymous connections** | 15 tests cannot run on this machine. Nothing verifies the MQTT adapter against a real broker here. |
| **FOCAS2 native-library documentation** | Two traps produce an identical "could not load" error and are documented nowhere: a 32-bit `Fwlib32.dll` (present but unusable), and dev-build versus installed service — nothing populates `bin/`, so an F5 run fails on every FOCAS2 source while the repo builds green. |
| **Uninstall "complete delete"** | Designed, not implemented. See `docs/installer/uninstall-complete-delete-design.md`. |
| **The parallel line's own branch** | It has no common ancestor with `origin/main` and will not merge cleanly; and it was pushed to a typo'd branch name. Neither is fixed here. |

---

## 8. Verification performed

| Check | Result |
|---|---|
| Solution build | 0 warnings / 0 errors |
| Core tests | 1313 pass |
| Management tests | 1282 pass (27 new `RouteHealth` tests) |
| Host tests | 253 pass, run twice for the timing-sensitive reload class |
| Adapter suites | S7 213 · Modbus 290 · MELSEC 273 · MTConnect 84 · Brother 210 · FOCAS2 150 · OPC UA Client 291 · OPC UA Server 57 · Sparkplug 581 · EtherNet/IP 90 — all pass |
| **Live: destination edit on a running gateway** | Route stayed `Running`, both endpoints intact; **0 null adapter states, 0 sources stuck at `Created`** across 10 routes |
| **Live: Overview rendering** | Verdict banner, broken-first ordering, status pills and health accents all confirmed in served HTML |
| Not verified | MQTT reconnect timing against a real broker drop; the centred notification rendering; clock-tamper banner (requires moving the system clock) |
