# ADR-0011: Browse Controller reuses `BrowseTagsAsync` via throwaway adapter — discovery is management-plane ephemeral

**Status:** Accepted (2026-05-17)
**Date:** 2026-05-17
**Milestone:** M.2b.3
**Framing:** Discovery and probe workflows are treated as **management-plane ephemeral operations** and are intentionally isolated from the runtime supervisor pipeline.

## Context

M.2b.3 introduces a Studio "Browse Controller" feature for the FOCAS2 source wizard, surfacing real axis names + discovered tag list before the operator commits a draft. The button drives a one-shot probe of a configured controller.

Two architectural constraints shape the design:

1. **`ISourceAdapter` is LOCKED.** The file header declares "changes require blueprint revision. Adding optional members that do not break existing implementations is acceptable." The FOCAS2 adapter itself documents this: `SourceCapabilities.TestConnect` is "intentionally NOT declared even though a future management API could benefit from it — the `ISourceAdapter` contract does not yet carry a `TestConnectAsync` method. Adding the flag without the method would surface a capability the host cannot invoke. Revisit once Phase 4's management API lands the contract extension."

2. **The runtime data pipeline must remain deterministic, replayable, and isolated from interactive workflows.** ARCHITECTURE_BLUEPRINT.md §3 locks "AI in decision-support only, never in data path" — the same separation applies to discovery: the live `SourceSupervisor` lifecycle should not be entangled with operator-driven probes.

Two design paths were considered:

- **Path A:** Reuse the existing `ISourceAdapter.BrowseTagsAsync` capability via a throwaway adapter constructed and torn down per probe. No contract revision.
- **Path B:** Extend `ISourceAdapter` with a new `TestConnectAsync` method. Touches every existing source adapter (Modbus, S7, MTConnect, FOCAS2, mock).

## Decision

The Browse Controller endpoint is implemented as **Path A** — a throwaway `Focas2SourceAdapter` lifecycle. The endpoint lives in `ElpisEdgeConnect.Management`, not `ElpisEdgeConnect.Host`. The throwaway adapter is constructed directly with `new Focas2SourceAdapter(...)`, never enters DI, the supervisor, or the routing engine, and is disposed at the end of every probe.

### Sequence (load-bearing — pinned by Focas2BrowseServiceTests)

```
1. License gate                — ILicenseManager.IsModuleEnabled("source-focas2")  (Locked I)
2. Schema parse                — Focas2SourceConfiguration.FromSourceInstance(req) (Locked G)
3. Probe-override config       — retries=1, timeoutSeconds=min(8, max(1, req))     (Locked S)
4. Adapter construction        — direct new(), no DI, no SourceRegistration       (Locked H)
5. Linked CTS                  — CancelAfter(15s)                                  (Locked M)
6. ValidateConfigAsync          — fast-fail config-level errors                     (Locked N)
7. InitializeAsync → StartAsync
8. Post-Start connected check  — adapter is fail-soft; inspect health metrics
9. BrowseTagsAsync
10. finally: bounded Stop+Dispose via Task.Run(...).WaitAsync(12s)                 (Locked P)
```

### In-scope HTTP status mapping (locked by user 2026-05-17)

| Result | HTTP |
|---|---|
| `Success = true` | 200 |
| `LICENSE.MODULE_DISABLED` | 403 |
| `FOCAS2.BROWSE_IN_FLIGHT` | 409 |
| `FOCAS2.CONFIG_INVALID` | 400 |
| Controller / connect errors (`CONNECT_FAILED`, `BROWSE_TIMEOUT`, `NATIVE_LIBRARY_MISSING`, future codes) | 200 with `Success=false` |
| Unexpected service exception | 500 ProblemDetails |

The "200 with `Success=false`" choice for controller-side errors keeps the wizard's success path rendering the structured error inline, rather than routing through the JS fetch-error path.

## Reasoning

1. **`ISourceAdapter` is preserved unchanged.** Path B would require revising a locked contract for a wizard feature — a disproportionate scope. The "additive optional members" carve-out in the contract header could in principle accommodate `TestConnectAsync` later, but doing it inside M.2b.3 would have meant updating every existing source adapter (Modbus / S7 / MTConnect / FOCAS2 / mock) for a single-protocol UX feature.

2. **Same code path as the live system.** The throwaway adapter exercises `Focas2SourceConfiguration.FromSourceInstance`, `InitializeAsync`, `StartAsync`, `CheckHealthAsync`, and `BrowseTagsAsync` — exactly the methods the supervisor uses at runtime. A bug in the wizard's projection layer or in `FromSourceInstance` is caught by Browse before the operator commits the draft. Defence in depth.

3. **Endpoint lives in `Management`, not `Host`.** `Host` owns runtime supervisor lifecycle and the protocol-registration helpers in `Focas2RegistrationExtensions`. `Management` owns ephemeral probe workflows. Moving the endpoint into `Host` just to reuse adapter construction helpers would erode the runtime/management boundary. If construction-helper duplication appears (e.g. once S7 / OPC UA / EtherNet/IP browse follows), the resolution is "extract a tiny shared helper", NEVER "move the endpoint into Host".

4. **`ValidateConfigAsync` before `InitializeAsync`.** Schema validation is deterministic, fast, and local; Init / Start allocate native handles and hit the network. Fast-fail cheap errors before paying connect-time cost. Step 1 reality-check (2026-05-17) confirmed `Focas2SourceAdapter.ValidateConfigAsync` is pure/config-only — does not require `Initialize` to have run.

5. **Fixed probe budget, NOT derived from `config.TimeoutSeconds`.** Runtime reconnect policy and interactive UI responsiveness are different concerns. An operator setting `TimeoutSeconds: 60` for chronically slow production conditions must not freeze Studio Browse for 65 seconds. The 15s probe budget pairs with the Locked S override (single attempt, ≤8s per-call timeout) so the budget is **genuinely enforceable** — without Locked S, `Focas2ConnectionManager.TryConnect`'s uncancellable retry loop could spend ~57s of native work even after the await is cancelled.

6. **Combined Stop+Dispose bounded at 12s, not Dispose alone at 5s.** Step 1 reality-check identified the wedge risk: `Focas2SourceAdapter.StopAsync` awaits a queued `Disconnect` work item that can be parked behind an in-flight native call; `DisposeAsync` is essentially a no-op after `StopAsync` runs. Bounding the combined cleanup matches the actual hang vector and aligns with `Focas2Thread.DisposeAsync`'s internal `Thread.Join(10s)` ceiling.

## Consequences

- **`ISourceAdapter` is unchanged.** Verified by `git diff src/ElpisEdgeConnect.Core/Adapters/ISourceAdapter.cs`.

- **`Focas2BrowseService` carries the full throwaway-adapter sequence** in `src/ElpisEdgeConnect.Management/Api/Focas2BrowseService.cs`. The production constructor builds a real `Focas2SourceAdapter`; the internal test constructor accepts a factory delegate + license-gate delegate + overridable probe/cleanup budgets so the locked invariants can be pinned without `WebApplicationFactory` or `FakeFocas2Api` wiring.

- **`SourceCapabilities.TestConnect` remains undeclared** on the FOCAS2 adapter. Consistent with the adapter's existing documentation.

- **A future `TestConnectAsync` contract extension is deferred open-endedly** — no named milestone, no commitment beyond "when the management API contract extension lands". When it eventually arrives, the existing Browse endpoint can be refactored to consume it without altering the management-plane / runtime separation enshrined here.

- **Path for future protocol browse endpoints is set.** OPC UA Client browse, S7 symbol discovery, EtherNet/IP browse, MTConnect capability introspection, and the eventual "Test Connection" feature should all follow Path A: live in `Management`, instantiate a throwaway adapter, exercise `BrowseTagsAsync` (or its protocol-specific equivalent), and never thread through `SourceSupervisor`. The "Discovery is management-plane ephemeral" principle is the architectural framing future contributors should reach for.

## Out-of-scope follow-ups

- **`TestConnectAsync` contract extension.** Deferred. When it lands, it will be a separate ADR that explicitly amends ADR-0011's Path A position.

- **Browse endpoints for other protocols.** Each future protocol wizard ships its own browse endpoint in `Management`. Cross-protocol generalisation is allowed once two or more endpoints exist and a shared shape becomes evident — premature abstraction is explicitly out of scope here.

- **Probe-result caching.** Each Browse click is a fresh round-trip. Caching introduces stale-data questions; not worth solving in v1.

- **Cross-process single-flight.** Single-flight is process-local (`ConcurrentDictionary<string, SemaphoreSlim>` in `Focas2BrowseService`). Studio is single-process; cross-process probing is not a scenario v1 supports.

## References

- M.2b.3 plan: `docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v3.md` (v3 locked, post-Step-1 reality-check)
- M.2b.3 kickoff: `docs/sessions/2026-05-17-focas2-wizard-kickoff.md`
- `ISourceAdapter` contract: `src/ElpisEdgeConnect.Core/Adapters/ISourceAdapter.cs` (locked)
- FOCAS2 adapter: `src/ElpisEdgeConnect.Sources.Focas2/Focas2SourceAdapter.cs` — declares `SourceCapabilities.Polling | Browse`, deliberately omits `TestConnect`.
- Locked decisions G/H/I/J/M/N/O/P/Q/R/S: M.2b.3 plan v3 §2.
- Status mapping: locked by user in Step 6 conversation 2026-05-17.
- ChatGPT review pass on M.2b.3 plan v1, 2026-05-17 — elevated the "management-plane ephemeral" principle to the load-bearing framing sentence and required the fixed-Browse-timeout (Locked M) and ValidateConfigAsync-first (Locked N) shapes.
