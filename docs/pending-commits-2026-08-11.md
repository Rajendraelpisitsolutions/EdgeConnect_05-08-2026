# Pending items for commit

- **Branch:** `Sony_Development_License` (mirrored to `Edge-Connect_v2.1.0.1a` and `Rajendra_Development`)
- **Baseline:** all three branches in sync at commit `21c39f2`
- **Date:** 2026-08-11
- **Scope:** 22 modified files, 8 new files — none committed
- **Build:** solution 0 warnings / 0 errors
- **Tests:** Core 1343 · Host 258 · Management 1309 · S7 213 · MELSEC 273 · EtherNet/IP 90 · Modbus 290 · Sparkplug 581 · others green. The only failures are the 15 pre-existing environmental ones (9 MQTT, 6 integration) caused by the local broker rejecting anonymous connections.

---

## Recommended commit order

**A first.** It is the only item here that changes a data-retention contract in Core. If the Modbus RTU work (still open, see §"Not in this batch") later needs reverting or bisecting, A must not be tangled up with a day of unrelated UI work in one undifferentiated pile.

---

## A · Orphan cursor pinned the buffer tail forever

**The most serious item in this batch.** Unbounded disk growth that no retention setting could reclaim.

| File | |
|---|---|
| `src/ElpisEdgeConnect.Core/Buffer/IMessageBuffer.cs` | modified |
| `src/ElpisEdgeConnect.Core/Buffer/SqliteRouteStore.cs` | modified |
| `src/ElpisEdgeConnect.Core/Buffer/SqliteBuffer.cs` | modified |
| `src/ElpisEdgeConnect.Core/Buffer/InMemoryBuffer.cs` | modified |
| `src/ElpisEdgeConnect.Core/Buffer/SinkCursorTracker.cs` | modified |
| `src/ElpisEdgeConnect.Core/Routing/RouteWorker.cs` | modified |
| `tests/ElpisEdgeConnect.Core.Tests/Buffer/SqliteBufferOrphanCursorPruneTests.cs` | **new** |

Cursors were loaded with no filter against the route's configured sinks, and `DeregisterSinkAsync` had **zero production callers** — so a cursor, once written, was never removed. One orphan pinned `tail_sequence` permanently, and because it lived in the table it was resurrected on every open, which is why a restart never cleared it. A route delivering perfectly reported a phantom backlog and a red "nothing has ever been delivered".

**6 tests, each verified to fail without the fix.** The load-bearing one is the reopen test: it is red not only with no `DELETE`, but also for a cosmetic fix that only deregisters in memory.

**Known behaviour change to record in the message:** a sink temporarily removed from config and re-added later now loses its backlog at the first worker start after removal. That is the intended meaning of "no longer configured", but it is a genuine data-discard event — which is why the prune emits a warning naming the sink, the pinned sequence and the undelivered count.

**Known limit:** SQLite `DELETE` frees pages for reuse but does not truncate. Growth is bounded; the file stays at its high-water mark until an explicit `VACUUM`.

---

## B · Duplicate device endpoints now warn

| File | |
|---|---|
| `src/ElpisEdgeConnect.Core/Configuration/CrossRecordValidator.cs` | modified |
| `src/ElpisEdgeConnect.Core/Errors/CoreErrors.cs` | modified |
| `src/ElpisEdgeConnect.Core/Configuration/SourceEndpointIdentity.cs` | **new** |
| `tests/ElpisEdgeConnect.Core.Tests/Configuration/CrossRecordValidatorDuplicateEndpointTests.cs` | **new** |

Two enabled sources pointed at one device endpoint passed validation silently, quietly halving a controller's connection budget. **Warning, not error** — the pattern is legitimately used (different register ranges at different poll rates), and the existing FOCAS2 startup advisory already set that precedent.

**Needs a decision at some point:** a protocol-knowledge table now lives in Core, in tension with the protocol-agnostic lock. It holds no assembly references — only JSON key names as data, with precedent in `LicenseEditionCatalog` — and is isolated in one file with the intended exit documented. Worth an ADR.

---

## C · Reconnect backoff jitter

`src/ElpisEdgeConnect.Sources.S7/S7ConnectionManager.cs` · `.Melsec/MelsecConnectionManager.cs` · `.EthernetIp/EthernetIpConnectionManager.cs`

Devices recovering from one event retried in lockstep and exceeded the controller's connection limit — the recovery attempt causing the next failure. Equal jitter (50–100% of the capped delay), following FOCAS2's existing implementation.

**Found in passing, not fixed:** `ModbusTransactionExecutor.ComputeRetryDelay` has a comment claiming "bounded linear-plus-jitter delay" over a body with no randomisation at all. The comment misleads anyone who greps for "jitter".

---

## D · Route/endpoint mismatch now fails closed

| File | |
|---|---|
| `src/ElpisEdgeConnect.Management/Components/Pages/Onboarding/OnboardingFlow.razor` | modified |
| `src/ElpisEdgeConnect.Management/Components/Pages/Onboarding/ReviewAndConnect.razor` | modified |
| `src/ElpisEdgeConnect.Management/Wizards/OnboardingRouteWiring.cs` | **new** |
| `tests/ElpisEdgeConnect.Management.Tests/OnboardingRouteWiringTests.cs` | **new** |

A route wired before an endpoint was renamed kept the stale id, and the apply refused at the last step with a message naming a parameter rather than a step. Three independent gates now make a disagreeing route impossible to POST, and two parity tests pin the rule against the merger in both directions so they cannot drift.

> **Note before committing:** these two razor files also carry the earlier Save / Save & Connect work. That code has not been touched since it was asked to be left alone, but committing these files brings it along. Confirm that is intended.

---

## E · Hot-reload regression tests

`tests/ElpisEdgeConnect.Host.Tests/RuntimeReloadCoordinatorTests.cs`

Five tests for the endpoint-republish and sink→route rebind fixes already shipped in `0a30f8b`. Each was reasoned about against **both halves removed separately**, and the quiet-source case is the one that discriminates — a chatty source papers over the bug, because its poll loop re-pushes state on the next non-empty batch.

---

## F · Filter alignment

`src/ElpisEdgeConnect.Management/Components/Pages/Diagnostics.razor` · `Components/Pages/RouteWizards/AddRoute.razor` · `wwwroot/css/site.css`

Diagnostics filters stretched the full width on a 12-column grid; the route wizard's two strips sat mid-row. Both now use container-driven layout. The recurring trap is recorded in the CSS: MudBlazor's own wrapper carries the `flex-grow`, so sizing passed through a component's `Class` lands one level too deep and silently does nothing.

---

## G · EtherNet/IP datatype and S7 address message

`Wizards/EthernetIpSourceWizardModel.cs` · `Components/Pages/SourceWizards/AddEthernetIpSource.razor` · `Api/S7AddressValidationService.cs` · `Wizards/S7SourceWizardModel.cs`

Datatype now follows the tag name (suggest, never coerce — matching the S7 wizard's existing convention), and an empty cell is no longer reported as a typo. The S7 address error now says what an address *is* rather than listing five unexplained examples.

---

## H · Notification position

`src/ElpisEdgeConnect.Management/Hosting/ManagementHostingExtensions.cs`

Success confirmations centred, one global setting covering every save path.

---

## I · Documentation

`docs/change-report-Sony_Development_License-2026-08-10.md` · `docs/installer/uninstall-complete-delete-design.md` · `docs/installer/license-driven-installer-plan.md`

---

## Caveats that apply across the batch

| | |
|---|---|
| **`site.css` spans groups** | It carries the route-card, health, licence and filter rules together. It lands whole in whichever commit takes it; splitting one stylesheet across commits produces boundaries that do not build in isolation. |
| **`RuntimeReloadCoordinatorTests` is load-sensitive** | It failed three separate times today — a different test each time, always passing in isolation — and is **not** marked `Category=Flaky`. It will bite CI harder than this machine. Worth tagging. |
| **Isolation not proven per commit** | The batch builds and tests green as a whole. Individual commits have not each been built in isolation. |

---

## Not in this batch — open, awaiting a decision

| Item | Why it is not here |
|---|---|
| **D-02 · Modbus RTU publishing fabricated values as `Quality: Good`** | Critical data integrity. Needs a decision between adding a validation layer over the library's response and changing the library, and needs reproducing against the repo's own simulator first to separate a loopback from a cached replay. A wrong fix in a Modbus driver corrupts data silently. |
| **D-01 · S7 Port field accepted then ignored** | Two valid fixes: honour the port if the driver genuinely supports it, or make the field read-only with a note. Product decision. |
| **D-03 / D-04 · Modbus error reporting** | Belong in the same work package as D-02. |
| **D-07 · `pipeline.pointsIn` always zero** | Decision needed: wire the pipeline up, or hide the panel. |
| **E-04 · SMTP password stored in plain text** | Security issue, machine environment rather than repository code. |
