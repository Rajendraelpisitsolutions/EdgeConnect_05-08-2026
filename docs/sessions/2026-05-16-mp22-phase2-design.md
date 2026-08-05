# M.P2.2 Phase 2 — Design (for ChatGPT review, no code yet)

**Date:** 2026-05-16
**Branch:** `claude/m-p2-2-hot-reload` (tip: `5c91273` after Phase 1)
**Status:** **DESIGN — not implementation.** Awaiting ChatGPT review before any
code lands.

This document is the reviewable blueprint for Phase 2 of M.P2.2. It
covers the dangerous parts called out at planning time:

```
supervisor lifecycle
protocol registration
runtime reconciliation
fault clearing
route restart ordering
```

The locks from ADR-0009 still hold. This doc adds one more invariant
the user reinforced at Phase-2 kickoff:

---

## 0. The non-negotiable threading invariant

**Apply must not wait on slow device I/O. Ever.**

```
ApplyDraftAsync completes after current.json is persisted.
RuntimeReloadCoordinator reconciles AFTER CurrentChanged returns.
Reload result is recorded separately as runtime outcome/fault state.
```

Why this matters: `ConfigurationManager.ApplyDraftAsync` fires
`CurrentChanged?.Invoke(...)` **synchronously inside `_mutex`**
(`ConfigurationManager.cs:397`, mutex released at line 409). If the
coordinator's handler does any blocking work inline, the apply mutex
holds for the entire reconciliation — including Modbus dial-outs,
MQTT broker reconnects, OPC UA listener restarts — and a subsequent
`ApplyAsync` queues behind it. That defeats Decision E from ADR-0009.

**Therefore the coordinator handler MUST hop off the firing thread
immediately.** The mechanic:

```csharp
private void OnCurrentChanged(object? sender, ConfigurationChangeEventArgs e)
{
    // Fire-and-forget. The handler returns in microseconds; the apply
    // mutex releases immediately; subsequent applies are not blocked.
    // The actual reconciliation runs on the threadpool, serialised by
    // _reconcileSemaphore (a SemaphoreSlim(1,1) DISTINCT from the
    // apply mutex).
    _ = Task.Run(() => ReconcileAsync(e), _shutdownCts.Token);
}
```

No `await` between `CurrentChanged` returning and `Task.Run` enqueueing.
The reconcile body runs on a threadpool thread and is serialised among
itself by `_reconcileSemaphore` (single-flight). Concurrent applies
arrive while a reconcile is in flight → second reconcile queues behind
the first; **applies themselves do not queue**.

Phase 2 does NOT add a way for callers to observe the reconcile
outcome. The Apply response stays the M.2a shape. Phase 3 adds the
`ReloadOutcomeDto` block as a separate observability surface — never as
a blocking dependency of the Apply response.

---

## 0.5. Review resolution (locked 2026-05-16)

ChatGPT review pass on the v1 draft signed off all 7 questions in §10
with 4 corrections. Locked outcomes:

| Question | Outcome |
|---|---|
| Threading model (§0) | **Accept as written** |
| `SinkRegistration.RouteId` rename | **Defer past Phase 2** — cleanup, not a hot-reload blocker |
| Stop/start order (§5) | **Accept with sink-computation correction** below |
| Failure matrix (§6) | **Accept with audit-append correction** below |
| Test list (§7) | **Accept + 3 new tests** added below |
| 30s per-instance timeout | **Accept hardcoded** for v1; configurability waits |
| `RouteDefinitionFactory.BuildOne` | **Accept** — add overload cleanly |

**Four corrections folded into this revision:**

1. **§5 `RegisterAndAuditFault` does NOT fire-and-forget the audit
   append.** Original pseudocode had `_ = AppendRuntimeFaultAsync(fault)`
   which silently drops any exception from the durable audit append.
   Corrected: the helper is renamed `RegisterAndAuditFaultAsync` and
   awaits the append. If the append itself throws, log Critical with
   the original fault detail — do NOT re-register a fault for the
   audit failure (that would loop). The fault is still in the live
   registry; the durable audit entry is the only thing lost.

2. **§5 sink teardown is plan-action-driven only.** Route changes
   never implicitly cascade into sink removes. A sink that loses one
   referencing route but is still referenced by another (or even by
   no route, post-change) is **not** touched unless its own config
   action exists in the plan. Concretely: `ComputeUnreferencedSinks`
   is replaced by a literal `plan.Actions.Where(a => a.Kind == Sink
   && a.Op in {Remove, Restart})` projection — no scan of supervised
   sinks against new-config-routes.

3. **§5 stale-reconcile protection.** Two rapid applies (`C1→C2` then
   `C2→C3`) queue two reconciles on the single-flight semaphore. When
   the C2 reconcile finally runs, its target version is no longer
   `IConfigurationManager.CurrentVersionId`. Skip it. The C3 reconcile
   that follows is the authoritative one.

4. **§5 bounded `DisposeAsync`.** Shutdown drains the reconcile
   semaphore with a 5s timeout. Beyond that, log a Warning naming the
   stuck instance (best-effort — we may not be able to tell which
   one) and exit. Prevents a stuck `Adapter.StopAsync` from hanging
   process shutdown indefinitely.

§§5, 6, 7, 10 below have been updated accordingly. Older drafts
(commit `9475cba`) are preserved in git history.

---

## 1. File-by-file changes

### Modified

| File | What changes | Approx. LOC delta |
|---|---|---|
| `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` | `_supervised` becomes a thread-safe map. Add `AddAsync`, `RemoveAsync`, `RestartAsync`. Per-instance CTS replaces the single shared `_stopCts`. Existing `StartAsync` / `StopAsync` keep their signatures but route through the new per-instance methods (so the boot-time path and the hot-reload path share machinery). | +~180 / -~30 |
| `src/ElpisEdgeConnect.Host/Adapters/SinkSupervisor.cs` | `_registrations` becomes a dictionary keyed by instance id. Add `AddAsync`, `RemoveAsync`, `RestartAsync`. Boot path unchanged externally. | +~120 / -~20 |
| `src/ElpisEdgeConnect.Host/HostStartup.cs` | After `MarkReady`, install the coordinator's `CurrentChanged` subscription. Symmetric un-subscribe before `MarkNotReady` on shutdown. | +~10 |
| `src/ElpisEdgeConnect.Host/CompositionRoot.cs` | Register `RuntimeReloadCoordinator` + `RegistrationFactory` as singletons. Pass through the existing `IConfigurationFaultRegistry`. | +~15 |
| `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs` | No behavioural change to boot-time wiring. Pass the existing fault registry into the new coordinator. | +~5 |

### New

| File | Purpose | LOC est. |
|---|---|---|
| `src/ElpisEdgeConnect.Host/Adapters/RegistrationFactory.cs` | Protocol dispatcher. `BuildSource(SourceInstanceConfig src, GatewaySettings gateway, ILicenseManager?, IConfigurationFaultRegistry?) → SourceRegistration?`. Same for sinks. Extracted from the per-protocol `FromGatewayConfig` extensions so the coordinator can build a single instance without DI. | ~150 |
| `src/ElpisEdgeConnect.Host/Adapters/IRegistrationFactory.cs` | Interface for the above; lets tests substitute a fake factory that returns canned registrations. | ~25 |
| `src/ElpisEdgeConnect.Host/RuntimeReloadCoordinator.cs` | The orchestrator. Subscribes to `CurrentChanged`, classifies via `RuntimeReloadClassifier`, drives supervisors + `IRoutingEngine` in the locked stop/start order, registers/clears faults. | ~250 |
| `src/ElpisEdgeConnect.Host/Adapters/Focas2RegistrationExtensions.cs` *(MODIFIED — not new)* | Add a static `BuildSource(SourceInstanceConfig, GatewaySettings, …) → SourceRegistration?` extracted from the body of the existing `FromGatewayConfig`. The DI-time path keeps working via a thin wrapper. | +~30 each |
| Same for `MTConnectRegistrationExtensions`, `ModbusTcpRegistrationExtensions`, `S7RegistrationExtensions`, `MqttRegistrationExtensions`, `OpcUaServerRegistrationExtensions` | Same shape. | +~30 each × 5 |

**Total LOC budget:** ~700 production + ~350-450 tests. Matches the
phase target.

### Out of this phase (deferred to phase 3)

- `ApplyResultDto.Reload` block
- Studio `Config.razor` "what just happened" panel
- `IReloadOutcomeQueue` (the version-keyed outcome cache)
- `docs/ops-runbook.md` and `docs/config-authoring.md` updates

---

## 2. `SourceSupervisor` lifecycle contract

### State model

```
                    ┌────────────────────────────────────────┐
                    │ _supervised : ConcurrentDictionary<    │
                    │     string, SupervisedSource>           │
                    └────────────────────────────────────────┘

class SupervisedSource:
    Registration : SourceRegistration
    Channel      : Channel<CanonicalDataPoint>      // bounded, single-r/w
    Intake       : ISourceIntake                    // routes resolve this
    Cts          : CancellationTokenSource          // per-instance now
    PumpTask     : Task?
    Lifecycle    : Created | Initializing | Running | Stopping | Stopped | Failed
```

The supervisor remains the single owner of the channel. Each
`SupervisedSource` has its own `Cts` so the coordinator can cancel ONE
source's poll loop without disturbing the others.

### Public surface (boot + hot-reload share machinery)

| Method | Boot path | Hot-reload path | Notes |
|---|---|---|---|
| ctor(registrations, …) | yes | no | Builds initial `_supervised` map and channels; nothing started yet. |
| `StartAsync(ct)` | yes | no | For each entry in `_supervised` not yet started: `AddAndStartInternal(...)`. Idempotent. |
| `StopAsync(ct)` | yes | no | Cancels every `Cts`, awaits every `PumpTask`, calls `Adapter.StopAsync` for each. |
| **`AddAsync(SourceRegistration reg, ct)`** | no | yes | Throws if `reg.Adapter.InstanceId` already in map. Adds to map, builds channel, calls `Initialize` + `Start`, records `Running` health, launches pump. |
| **`RemoveAsync(string instanceId, ct)`** | no | yes | Idempotent on unknown id. Cancels that source's `Cts`, awaits its `PumpTask`, completes the writer, calls `Adapter.StopAsync`, removes from map. Reports `Stopped` health then `IDisposable.Dispose` if applicable. |
| **`RestartAsync(SourceRegistration newReg, ct)`** | no | yes | `RemoveAsync(newReg.Adapter.InstanceId)` + `AddAsync(newReg)`. The "channel resurrection" risk is owned here. |
| `GetIntake(id)` | yes | yes | Resolves to the LIVE channel for `id`, or null. After `RemoveAsync` the intake is gone; after `AddAsync` a fresh one exists. |

### Channel resurrection — the risk path

When `RemoveAsync(id)` runs:

1. `_supervised[id].Cts.Cancel()` — pump observes cancel, exits the
   `while (!ct.IsCancellationRequested)` loop.
2. `await _supervised[id].PumpTask` — await pump exit.
3. `_supervised[id].Channel.Writer.TryComplete()` — channel writer
   marked complete. Any reader still pulling will drain residual + see
   end-of-stream.
4. `Adapter.StopAsync(ct)` + record `Stopped` health.
5. `_supervised.Remove(id)`.

Then `AddAsync(newReg)` builds a **new channel** + a **new
`SupervisedSource`** + a **new `Intake`**. Any consumer holding a
reference to the OLD `Intake` (the route worker) still has a reference
to the completed-and-discarded channel — that's why the coordinator
**must** unregister the route before calling `AddAsync` for the source
(see §5 stop/start ordering). After `AddAsync` returns, the coordinator
re-registers the route via `RouteDefinitionFactory.Build(...)` and the
new `Intake` is wired in.

### Boot-vs-runtime symmetry

The existing boot-time `StartAsync` is rewritten to call
`AddAndStartInternal(reg, ct)` for each registration — the same
internal helper `AddAsync` calls. This means **one canonical code
path** for the "initialize + start adapter + record health + launch
pump" sequence; no risk that the boot path and the hot-reload path
drift apart silently.

### Cancellation semantics on Restart

`RestartAsync(newReg)` does not interrupt other sources. Only the
specific source instance's `Cts` is cancelled. The shared `_stopCts`
(if we keep it for the boot-time path) is touched only by
`StopAsync(everything)`.

### Per-adapter isolation invariant

If a `RemoveAsync` throws (rare — `Adapter.StopAsync` misbehaves), the
exception escapes to the coordinator. The map is left in a known state:
either the entry was removed before the throw, or it wasn't. The
coordinator's `try/catch` per action treats this as a per-instance
reconcile failure and registers a fault.

---

## 3. `SinkSupervisor` lifecycle contract

Mirror of `SourceSupervisor`, with three differences:

1. **No channels.** Sinks don't have intakes. The routing engine drives
   `PublishAsync` on the sink adapter directly via `FanoutDispatcher`.
2. **1-to-many with routes.** A sink instance can be referenced by N
   routes (per ADR-0002 phase 3b `SinkListItemDto.RouteIds`). The
   supervisor's contract is per-INSTANCE, not per-route — it owns
   each sink adapter once regardless of how many routes use it.
3. **`SinkRegistration.RouteId` is now a misnomer.** The existing
   field still names ONE route id, but that's the route that "first
   wired in" the sink at boot time. **For phase 2 we accept this
   sub-optimal naming** rather than expand it to `RouteIds` — the
   coordinator handles the "which routes reference this sink" set
   externally by walking `config.Routes`. Deferring the
   `SinkRegistration` field rename (which would ripple into every
   protocol extension + tests) keeps phase 2 small.

   **Why this is safe today:** the `RouteId` field on `SinkRegistration`
   is only used as a tag on diagnostics events. With phase 3b the
   inventory builder walks config directly, so this tag is no longer
   load-bearing for the UI's route↔sink mapping. The route-removal
   case where a sink moves from N routes to N-1 routes does NOT need
   to update this field — the route ids the operator sees come from
   `config.Routes`, not from `SinkRegistration.RouteId`.

### Public surface

| Method | Boot | Hot-reload | Notes |
|---|---|---|---|
| ctor / `StartAsync` / `StopAsync` | yes | no | Unchanged externally; internally route through `AddAndStartInternal`. |
| **`AddAsync(SinkRegistration reg, ct)`** | no | yes | Throws on duplicate id. `Initialize` + `Start` + record health. |
| **`RemoveAsync(string instanceId, ct)`** | no | yes | Idempotent. `Adapter.StopAsync` + record health + remove. |
| **`RestartAsync(SinkRegistration newReg, ct)`** | no | yes | `Remove` + `Add`. |
| `Registrations` | yes | yes | Returns the current live registration list. Read snapshot — coordinator iterates this to compute "is this sink still referenced by any route". |

### Reference-count semantics

The supervisor itself does **not** track "how many routes reference
this sink". That's the coordinator's job, derived from
`newConfig.Routes`. The supervisor's `RemoveAsync` only runs when the
coordinator decides the sink is truly unreferenced (or being
restarted because the sink config itself changed).

---

## 4. `RegistrationFactory` extraction design

### Problem statement

Today, every protocol's `*RegistrationExtensions.AddXxxFromGatewayConfig`
method walks `gatewayConfig.Sources` (or `.Sinks`), filters by
`ProtocolName`, translates JSON → typed config, and appends a
`SourceRegistration` to DI. It does THREE things:

1. **Filter** by protocol name + enabled flag.
2. **Translate** `SourceInstanceConfig` → `ModbusTcpSourceConfiguration`
   (or equivalent typed config).
3. **Construct** the adapter + assemble the `SourceRegistration`.

The hot-reload coordinator needs step (3) for **one instance at a
time**, without going through DI (the container is sealed). The
extraction:

### Contract

```csharp
public interface IRegistrationFactory
{
    // Returns null when:
    //   * The protocol module is license-disabled.
    //   * The source has no resolved route id (cross-record fault
    //     registered into the registry).
    //   * The source's ProtocolName is unrecognised.
    SourceRegistration? BuildSource(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry,
        IServiceProvider sp);                // for ILoggerFactory, IGatewayIdentity

    SinkRegistration? BuildSink(
        SinkInstanceConfig sink,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelectorForSink,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry,
        IServiceProvider sp);
}
```

### Where the protocol-specific logic lives

Each `*RegistrationExtensions` class gains a static method matching
the contract:

```csharp
// In ModbusTcpRegistrationExtensions.cs
public static SourceRegistration? BuildSource(
    SourceInstanceConfig src,
    GatewaySettings gateway,
    Func<string, string?> routeIdSelector,
    ILicenseManager? license,
    IConfigurationFaultRegistry? faultRegistry,
    IServiceProvider sp)
{
    // Same body as the per-instance branch of FromGatewayConfig:
    //   1. License check → return null on disabled module.
    //   2. routeIdSelector(src.InstanceId) → register fault + null
    //      on missing route.
    //   3. ModbusTcpSourceConfiguration.FromSourceInstance(src) with
    //      { GatewayId = gateway.GatewayId }
    //   4. Construct ModbusTcpSourceAdapter + return SourceRegistration.
}
```

The DI extension method `AddModbusTcpSourcesFromGatewayConfig` is
rewritten to:

```csharp
public static IServiceCollection AddModbusTcpSourcesFromGatewayConfig(
    this IServiceCollection services,
    GatewayConfiguration gatewayConfig,
    Func<string, string?> routeIdSelector,
    ILicenseManager? license,
    IConfigurationFaultRegistry? faultRegistry)
{
    foreach (var src in gatewayConfig.Sources)
    {
        if (!src.Enabled || src.ProtocolName != ModbusTcpSourceConfiguration.ProtocolNameConstant)
            continue;

        services.AddSingleton<SourceRegistration>(sp =>
            BuildSource(src, gatewayConfig.Gateway, routeIdSelector, license, faultRegistry, sp)
                ?? throw new InvalidOperationException(
                       $"Modbus source '{src.InstanceId}' could not be built; check fault registry."));
        ReplaceSourceRegistrationEnumerable(services);
    }
    return services;
}
```

The boot path stays mechanically identical from the operator's
perspective — same error codes registered, same skip semantics. The
ONE difference: the DI factory body now delegates to the static
`BuildSource` instead of building inline. The `throw` is defensive —
if `BuildSource` returns null for a filtered source (license
disabled, etc.), the boot path already filtered that case BEFORE the
DI factory ran, so the throw is unreachable in practice. It exists
as a safety net.

### `RegistrationFactory` (the dispatcher)

```csharp
public sealed class RegistrationFactory : IRegistrationFactory
{
    public SourceRegistration? BuildSource(... src, ...)
    {
        return src.ProtocolName switch
        {
            ModbusTcpSourceConfiguration.ProtocolNameConstant
                => ModbusTcpRegistrationExtensions.BuildSource(src, gateway, ..., sp),
            Focas2SourceConfiguration.ProtocolNameConstant
                => Focas2RegistrationExtensions.BuildSource(src, gateway, ..., sp),
            MTConnectSourceConfiguration.ProtocolNameConstant
                => MTConnectRegistrationExtensions.BuildSource(src, ..., sp),
            S7SourceConfiguration.ProtocolNameConstant
                => S7RegistrationExtensions.BuildSource(src, ..., sp),
            _ => null,  // unrecognised protocol — coordinator registers a fault
        };
    }

    // BuildSink — same shape for MQTT + OPC UA Server.
}
```

### Why a dispatcher rather than virtual methods on a base class

- Adapters are compile-time, not plugin-loaded (Locked Decision #4).
  The set of protocols is closed and small (6 today).
- Each protocol's `RegistrationExtensions` is a static class — already
  the place protocol-specific build logic lives.
- A switch is honest about the surface area; an abstract base would
  invite over-engineering.

---

## 5. `RuntimeReloadCoordinator` pseudocode

```csharp
public sealed class RuntimeReloadCoordinator : IAsyncDisposable
{
    private readonly IConfigurationManager _configManager;
    private readonly SourceSupervisor _sourceSupervisor;
    private readonly SinkSupervisor _sinkSupervisor;
    private readonly IRoutingEngine _routingEngine;
    private readonly RouteDefinitionFactory _routeDefFactory;
    private readonly IConfigurationFaultRegistry _faultRegistry;
    private readonly RuntimeDiagnosticsCollector _diagnostics;
    private readonly IRegistrationFactory _registrationFactory;
    private readonly ILicenseManager? _license;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _reconcileSemaphore = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private bool _subscribed;

    public void Start()
    {
        // Called by HostStartup AFTER MarkReady.
        if (_subscribed) return;
        _configManager.CurrentChanged += OnCurrentChanged;
        _subscribed = true;
    }

    private void OnCurrentChanged(object? sender, ConfigurationChangeEventArgs e)
    {
        // CRITICAL: never await inline. The apply mutex is held by the
        // caller. Hop to the threadpool immediately. Reconciliation is
        // fire-and-forget here; serialised among itself by the semaphore.
        _ = Task.Run(() => ReconcileSafelyAsync(e), _shutdownCts.Token);
    }

    private async Task ReconcileSafelyAsync(ConfigurationChangeEventArgs e)
    {
        try
        {
            await _reconcileSemaphore.WaitAsync(_shutdownCts.Token);
            try
            {
                await ReconcileAsync(e);
            }
            finally
            {
                _reconcileSemaphore.Release();
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            // Last-resort guard: any uncaught exception in reconcile
            // would kill the worker thread silently. Log loudly; the
            // fault registry will not have the per-instance detail but
            // the audit chain will still have the original Apply.
            _logger.LogCritical(ex, "RuntimeReloadCoordinator: unhandled reconcile failure for version {Version}",
                e.NewVersionId);
        }
    }

    private async Task ReconcileAsync(ConfigurationChangeEventArgs e)
    {
        // Correction #3 (review): stale-reconcile protection. Two
        // rapid applies (C1→C2 then C2→C3) queue two reconciles on the
        // single-flight semaphore. By the time the C2 reconcile starts,
        // its target version is no longer current. Skip — the C3
        // reconcile that's queued behind it will converge against the
        // latest intent. This is simpler than collapsing the queue and
        // matches "config is operator intent" (ADR-0005): only reconcile
        // against the most-recent intent.
        var current = await _configManager.GetCurrentAsync(_shutdownCts.Token);
        if (!current.VersionId.Equals(e.NewVersionId))
        {
            _logger.LogInformation(
                "RuntimeReloadCoordinator: skipping stale reconcile for {Stale}; current is {Current}.",
                e.NewVersionId, current.VersionId);
            return;
        }

        var plan = RuntimeReloadClassifier.Classify(e.NewConfiguration, e.Changes);
        if (plan.IsNoOp) return;

        var ct = _shutdownCts.Token;

        // ─── Phase A: teardown (routes first, then unrefed instances) ───
        var routesToStop = plan.Actions
            .Where(a => a.Kind == ConfigurationEntityKind.Route &&
                        a.Op is ReloadOp.Remove or ReloadOp.Restart)
            .Select(a => a.EntityId)
            .ToList();
        foreach (var routeId in routesToStop)
        {
            await TryWithFaultAsync(ConfigurationFaultKind.Route, routeId, async () =>
            {
                await _routingEngine.UnregisterRouteAsync(routeId, ct);
                _diagnostics.RemoveRoute(routeId);
            });
        }

        // Sources next: Remove + Restart (Restart's teardown half).
        var sourcesToRemove = plan.Actions
            .Where(a => a.Kind == ConfigurationEntityKind.Source &&
                        a.Op is ReloadOp.Remove or ReloadOp.Restart)
            .Select(a => a.EntityId)
            .ToList();
        foreach (var sourceId in sourcesToRemove)
        {
            await TryWithFaultAsync(ConfigurationFaultKind.Source, sourceId, () =>
                _sourceSupervisor.RemoveAsync(sourceId, ct));
        }

        // Sinks next. Correction #2 (review): action-driven ONLY. Route
        // changes do NOT implicitly cascade into sink removes. A sink
        // that lost one referencing route but is still in newConfig.Sinks
        // stays running — operator intent is the sink's own config, not
        // the route count. The "garbage-collect unreferenced sinks" pass
        // is explicitly NOT here. v1 limitation: a Sink Restart action
        // briefly disrupts other routes that share the sink (store-and-
        // forward holds points across the gap). Documented in risk #6.
        var sinksToRemove = plan.Actions
            .Where(a => a.Kind == ConfigurationEntityKind.Sink &&
                        a.Op is ReloadOp.Remove or ReloadOp.Restart)
            .Select(a => a.EntityId)
            .ToList();
        foreach (var sinkId in sinksToRemove)
        {
            await TryWithFaultAsync(ConfigurationFaultKind.Sink, sinkId, () =>
                _sinkSupervisor.RemoveAsync(sinkId, ct));
        }

        // ─── Phase B: bring-up (sources → sinks → routes) ───
        // Sources first: Add + Restart (Restart's bring-up half).
        var sourcesToAdd = plan.Actions
            .Where(a => a.Kind == ConfigurationEntityKind.Source &&
                        a.Op is ReloadOp.Add or ReloadOp.Restart)
            .ToList();
        foreach (var action in sourcesToAdd)
        {
            await TryWithFaultAsync(ConfigurationFaultKind.Source, action.EntityId, async () =>
            {
                var src = (SourceInstanceConfig)action.NewConfig!;
                if (!src.Enabled) return;  // disabled sources don't get registered
                var routeId = ResolveRouteFor(src.InstanceId, e.NewConfiguration);
                if (routeId is null) {
                    // Cross-record fault — register and skip. Same code path
                    // boot-time uses.
                    _faultRegistry.Register(MakeNoRouteFault(src));
                    await AppendRuntimeFaultAsync(MakeNoRouteFault(src));
                    return;
                }
                var reg = _registrationFactory.BuildSource(src, e.NewConfiguration.Gateway,
                    _ => routeId, _license, _faultRegistry, _serviceProvider);
                if (reg is null) return;  // license disabled or unrecognised protocol
                await _sourceSupervisor.AddAsync(reg, ct);
                _faultRegistry.ClearFor(ConfigurationFaultKind.Source, src.InstanceId);
            });
        }

        // Sinks next: Add + Restart. Action-driven only (per correction #2).
        var sinksToAdd = plan.Actions
            .Where(a => a.Kind == ConfigurationEntityKind.Sink &&
                        a.Op is ReloadOp.Add or ReloadOp.Restart)
            .ToList();
        foreach (var action in sinksToAdd)
        {
            await TryWithFaultAsync(ConfigurationFaultKind.Sink, action.EntityId, async () =>
            {
                var sink = (SinkInstanceConfig)action.NewConfig!;
                if (!sink.Enabled) return;
                var reg = _registrationFactory.BuildSink(sink, e.NewConfiguration.Gateway,
                    _ => ResolveFirstRouteForSink(sink.InstanceId, e.NewConfiguration),
                    _license, _faultRegistry, _serviceProvider);
                if (reg is null) return;
                await _sinkSupervisor.AddAsync(reg, ct);
                _faultRegistry.ClearFor(ConfigurationFaultKind.Sink, sink.InstanceId);
            });
        }

        // Routes last: Add + Restart. The route definition factory is
        // already stateless + reusable; we ask it to build definitions
        // for the affected routes only, then RegisterRouteAsync +
        // StartRouteAsync per route.
        var routesToBring = plan.Actions
            .Where(a => a.Kind == ConfigurationEntityKind.Route &&
                        a.Op is ReloadOp.Add or ReloadOp.Restart)
            .ToList();
        foreach (var action in routesToBring)
        {
            await TryWithFaultAsync(ConfigurationFaultKind.Route, action.EntityId, async () =>
            {
                var rc = (RouteConfig)action.NewConfig!;
                if (!rc.Enabled) return;
                var def = _routeDefFactory.BuildOne(rc, e.NewConfiguration,
                    _sourceSupervisor, _sinkSupervisor.Registrations, _faultRegistry);
                if (def is null) return;
                _diagnostics.EnsureRoute(rc.RouteId);
                await _routingEngine.RegisterRouteAsync(def, ct);
                await _routingEngine.StartRouteAsync(rc.RouteId, ct);
                _faultRegistry.ClearFor(ConfigurationFaultKind.Route, rc.RouteId);
            });
        }
    }

    // The unified per-action fault-on-throw wrapper. Each per-instance
    // failure registers a ConfigurationFault, appends a system-actor
    // audit entry, and CONTINUES with remaining actions. No exceptions
    // escape ReconcileAsync — fail-soft is unconditional per ADR-0004.
    private async Task TryWithFaultAsync(
        ConfigurationFaultKind kind,
        string entityId,
        Func<Task> action)
    {
        try
        {
            // Per-instance timeout to keep one stuck adapter from
            // blocking the whole reconcile queue. 30s matches the
            // ISinkAdapter graceful-stop contract. Hardcoded per
            // review correction (configurability deferred).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            await action();
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            throw;  // bubble shutdown
        }
        catch (OperationCanceledException)
        {
            // Per-instance timeout — register fault, continue.
            await RegisterAndAuditFaultAsync(kind, entityId, "HOST.RECONCILE_TIMEOUT",
                "Reconcile step exceeded 30s; instance left in last-known state.");
        }
        catch (Exception ex)
        {
            await RegisterAndAuditFaultAsync(kind, entityId, "HOST.RECONCILE_FAILED",
                ex.Message);
        }
    }

    // Correction #1 (review): renamed from RegisterAndAuditFault. The
    // audit append is NOT fire-and-forget — runtime fault audit is
    // part of the operator trust model. Await it. If the audit append
    // itself throws, log Critical with the original fault detail and
    // continue; do NOT re-register a fault for the audit failure
    // (that would loop: every audit append failure would create a
    // fault whose audit append would also fail).
    private async Task RegisterAndAuditFaultAsync(
        ConfigurationFaultKind kind,
        string entityId,
        string errorCode,
        string message)
    {
        var fault = new ConfigurationFault
        {
            Kind = kind,
            InstanceId = entityId,
            ErrorCode = errorCode,
            Message = message,
            ObservedAtUtc = DateTime.UtcNow,
        };
        // Register is in-memory + idempotent; never throws in practice.
        _faultRegistry.Register(fault);

        // Durable audit append. Await so we surface (or log) the result
        // before the next plan action runs. Bounded by the per-instance
        // 30s timeout already in TryWithFaultAsync's CTS.
        try
        {
            await _configManager.AppendRuntimeFaultAsync(fault, _shutdownCts.Token);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            throw;  // bubble shutdown
        }
        catch (Exception ex)
        {
            // The fault remains in the live registry (Studio still sees
            // it). Only the durable audit-chain entry was lost. Operator
            // trust requires this be loud — log Critical, including the
            // full fault detail so it can be reconstructed from logs.
            _logger.LogCritical(ex,
                "Runtime fault audit append FAILED for {Kind} '{Id}' (code={Code}). " +
                "Fault is in live registry but NOT in durable audit chain. Detail: {Message}",
                kind, entityId, errorCode, message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscribed) _configManager.CurrentChanged -= OnCurrentChanged;
        _shutdownCts.Cancel();

        // Correction #4 (review): bounded drain. Wait at most 5s for the
        // in-flight reconcile to observe the cancellation and release
        // the semaphore. If a stuck Adapter.StopAsync prevents that,
        // log Warning and exit — the process is shutting down anyway;
        // hanging here serves no operator value.
        const int DisposeDrainTimeoutMs = 5_000;
        var acquired = await _reconcileSemaphore.WaitAsync(DisposeDrainTimeoutMs);
        if (!acquired)
        {
            _logger.LogWarning(
                "RuntimeReloadCoordinator: reconcile did not exit within {Timeout}ms during dispose. " +
                "Process exit will proceed; in-flight reconcile may leave supervisors in mid-state.",
                DisposeDrainTimeoutMs);
        }

        _shutdownCts.Dispose();
        _reconcileSemaphore.Dispose();
    }
}
```

### Open question for review

`ResolveRouteFor` for a source: the existing pattern (in
`*RegistrationExtensions`) returns the first enabled route whose
`SourceInstanceId == src.InstanceId`. Phase 2 keeps that — but route
remove/restart can race. **Sequencing:** route teardown runs BEFORE
source add (Phase A vs Phase B), so by the time source add looks up
the route, the new route either is in the new config (visible to
`ResolveRouteFor`) or it isn't (no route → fault registered, source
skipped). No race.

---

## 6. Failure handling matrix

Per ADR-0004/0005 the unconditional rule is **fail-soft, per-instance**.
What follows is the explicit "what happens when X throws":

| Throw site | Catch in coordinator? | Fault registered? | Audit entry? | Effect on remaining actions |
|---|---|---|---|---|
| `RoutingEngine.UnregisterRouteAsync` throws | yes | `Route` kind, `HOST.RECONCILE_FAILED` | yes | continue |
| `SourceSupervisor.RemoveAsync` throws | yes | `Source` kind, `HOST.RECONCILE_FAILED` | yes | continue |
| `SinkSupervisor.RemoveAsync` throws | yes | `Sink` kind, `HOST.RECONCILE_FAILED` | yes | continue |
| `Adapter.InitializeAsync` throws (e.g., Modbus dial fails) — surfaces from `SourceSupervisor.AddAsync` | yes | `Source` kind, propagates the adapter's `ErrorCode` if `AdapterException`, else `HOST.RECONCILE_FAILED` | yes | continue |
| `Adapter.StartAsync` throws | yes | same as above | yes | continue |
| Per-instance 30s timeout | yes | `Source/Sink/Route` kind, `HOST.RECONCILE_TIMEOUT` | yes | continue |
| `IRegistrationFactory.BuildSource` returns null (license disabled) | n/a | none (license-disable is INTENT, not fault) | no | source not added; continue |
| `IRegistrationFactory.BuildSource` returns null (cross-record missing route) | n/a (factory itself registers the fault per ADR-0004 mechanical pattern) | yes (factory) | yes (factory triggers via faultRegistry) | continue |
| `RouteDefinitionFactory.BuildOne` returns null (route references missing source/sink) | n/a (factory registers fault) | yes | yes | route not registered; continue |
| Apply mutex thread observes `CurrentChanged` throw | **CANNOT HAPPEN** — handler is `_ = Task.Run(...)`, no synchronous body | n/a | n/a | n/a |
| `ConfigurationManager.AppendRuntimeFaultAsync` itself throws | **awaited** by `RegisterAndAuditFaultAsync` (correction #1); logged at **Critical** level with the full fault detail; **NOT re-registered as a fault** (would loop) | no (the call that threw IS the audit attempt) | no (durable audit entry for this fault is the thing we just lost) | continue |
| Uncaught exception escapes `ReconcileAsync` | `ReconcileSafelyAsync` last-resort catch logs Critical | no | no | next reconcile runs normally; the current version's reconcile is lost — operator sees the Apply succeeded but supervisor state is unchanged |
| Stale version observed at `ReconcileAsync` start (correction #3) | n/a — pre-action skip; Information-level log | no | no | the queued-behind reconcile for the latest version runs as normal |
| `DisposeAsync` reconcile-drain timeout (correction #4) | yes — 5s `WaitAsync(int)` returns false | no | no | process exit continues; Warning logged |

### What this guarantees

- A single bad instance never blocks others.
- A storm of failures cannot crash the gateway.
- The audit chain always reflects what the operator did (Apply
  succeeded); the registry reflects what the runtime observed.
- Shutdown cancellation propagates through every wrapper.

### What this does NOT guarantee (deferred)

- "Apply succeeded but every instance faulted" surfaces in
  `/diagnostics/configuration-faults` (M.P2.1 phase 3b is the
  read surface), but there's no consolidated "rollback now"
  button. That's Phase 3+ territory.
- Successful re-init of a stale-fault instance clears the registry
  entry — but if the audit chain append for the original fault was
  the one that threw, the operator sees the live registry as healthy
  while the audit chain has no record of the fault ever existing.
  Acceptable: the audit chain is the rare path; the registry is the
  user-visible truth.

---

## 7. Tests list (before coding)

### Phase 1 builders + classifier (already shipped, 26 tests)

No changes; existing coverage stands.

### Phase 2 — new tests (~80 estimated)

**`SourceSupervisor` per-instance lifecycle (15-20 tests)**

1. `AddAsync_NewInstance_StartsAdapterAndPump`
2. `AddAsync_DuplicateId_Throws`
3. `RemoveAsync_RunningInstance_StopsAndCompletesChannel`
4. `RemoveAsync_UnknownId_IsSilentNoOp`
5. `RemoveAsync_DoesNotAffectOtherInstances`
6. `RestartAsync_ConstructsNewChannel` *(channel resurrection pin)*
7. `RestartAsync_NewIntakeNotEqualToOldIntake` *(must observe the new channel)*
8. `RestartAsync_AdapterInitThrows_LeavesInstanceRemoved` *(failure mode: failure during Add half of Restart leaves nothing supervised)*
9. `RemoveAsync_DuringActivePump_NoExceptionEscapes`
10. `BootStartAsync_StillWorks_AfterRefactor` *(regression: boot path unchanged)*
11. `BootStopAsync_StillStopsEverything` *(regression)*
12. `AddAsync_AdapterInitializeAsyncThrows_PropagatesAsAdapterException`
13. `AddAsync_AdapterInitializeAsyncThrowsGenericException_WrapsAsHostError` *(per-instance isolation)*
14. `GetIntake_AfterRemove_ReturnsNull`
15. `GetIntake_AfterRestart_ReturnsNewChannel`
16. `DisposeAsync_DrainsEverything` *(regression on the existing dispose path)*
17. `Concurrent_AddAndRemove_DifferentInstances_Succeeds` *(thread safety on `_supervised`)*

**`SinkSupervisor` per-instance lifecycle (10-12 tests)**

Mirror set of the above, minus the channel-resurrection cases (sinks
have no channels). Add:
- `RemoveAsync_DoesNotStopSinkReferencedByOtherRoute` *(invariant: supervisor only removes when coordinator decides to remove; ref-counting is coordinator's job, but the supervisor's `RemoveAsync` shouldn't peek at the config — it just trusts the caller. This test pins that contract.)*

**`RegistrationFactory` (10-12 tests)**

1. `BuildSource_Modbus_ProducesValidRegistration`
2. `BuildSource_Focas2_ProducesValidRegistration`
3. `BuildSource_S7_ProducesValidRegistration`
4. `BuildSource_MTConnect_ProducesValidRegistration`
5. `BuildSource_UnrecognisedProtocol_ReturnsNull`
6. `BuildSource_LicenseDisabledModule_ReturnsNull` *(no fault registered — license is intent, not fault)*
7. `BuildSource_NoRouteForSource_RegistersFaultAndReturnsNull`
8. `BuildSink_Mqtt_ProducesValidRegistration`
9. `BuildSink_OpcUaServer_ProducesValidRegistration`
10. `BuildSink_UnrecognisedProtocol_ReturnsNull`
11. `BuildSource_AdapterCtorThrows_PropagatesExceptionToCaller` *(coordinator's TryWithFault catches; factory doesn't swallow)*

**`RuntimeReloadCoordinator` integration (25-30 tests)**

Each test substitutes `IConfigurationManager` (with the real
implementation backed by `InMemoryConfigurationStore`), real supervisors
(with mock adapters), real routing engine, and the real
`IConfigurationFaultRegistry`.

1. `CurrentChanged_HandlerReturnsImmediately` *(critical: assert handler hops off thread; mutex stays free)*
2. `CurrentChanged_DoesNotBlockSubsequentApply` *(stress: 10 applies back-to-back, all return promptly even though reconciles take seconds)*
3. `Reconcile_AddSource_BringsUpSupervisorEntryAndRoute`
4. `Reconcile_RemoveSource_TearsDownSupervisorAndRoute`
5. `Reconcile_RestartSource_ReplacesAdapterAndChannel`
6. `Reconcile_AddSink_BringsUpSupervisorEntry`
7. `Reconcile_RemoveSink_TearsDownOnlyWhenUnreferenced` *(N=2 routes → 1 route: sink stays. 1 route → 0 routes: sink removed.)*
8. `Reconcile_AddRoute_RegistersAndStartsRoute`
9. `Reconcile_RemoveRoute_StopsAndUnregistersRoute`
10. `Reconcile_RestartRoute_TopologyChangedSource_RebuildsCleanly`
11. `Reconcile_OrderInvariant_RoutesStopBeforeSources`
12. `Reconcile_OrderInvariant_RoutesStartAfterSourcesAndSinks`
13. `Reconcile_ModifiedSource_BecomesStopThenStart` *(no in-place reconfigure path)*
14. `Reconcile_AdapterInitThrows_RegistersFaultAndContinues`
15. `Reconcile_TwoNearSimultaneousApplies_AreSerialised` *(only one reconcile runs at a time)*
16. `Reconcile_ApplyDuringReconcile_DoesNotBlockApplyResponse` *(apply returns promptly while reconcile is mid-flight)*
17. `Reconcile_SuccessfulReInit_ClearsRegistryEntry`
18. `Reconcile_FailedReInit_RegistersFault_AndAuditEntry`
19. `Reconcile_RemovedInstance_ClearsRegistryEntry_ForThatId` *(the source was Faulted, then operator removed it; registry should be empty for that id)*
20. `Reconcile_PerInstanceTimeout_OneStuckInstanceDoesNotBlockOthers`
21. `Reconcile_GatewaySettingsOnly_IsNoOp` *(no supervisor work)*
22. `Reconcile_DisabledInstance_GetsRemovedFromSupervisor` *(operator flipped Enabled=false → equivalent to Remove)*
23. `Reconcile_AddDisabledInstance_DoesNotStartIt`
24. `Reconcile_ShutdownDuringReconcile_ExitsCleanly` *(cancellation propagates)*
25. `Reconcile_LastResortCatch_LogsCritical_DoesNotKillGateway`
26. `Reconcile_AppendRuntimeFaultAsyncThrows_OtherActionsContinue`
27. **`Reconcile_StaleQueuedVersion_IsSkipped`** *(review correction #3 — version check at start)*
28. **`Reconcile_AuditAppendFailure_IsLogged_AndOtherActionsContinue`** *(review correction #1 — Critical log, no re-register loop, plan continues)*
29. **`DisposeAsync_ReconcileHung_DoesNotHangForever`** *(review correction #4 — 5s bounded drain, Warning log, exit cleanly)*

**`HostStartup` wire-up (3-5 tests)**

1. `Coordinator_Subscribed_AfterMarkReady_NotBefore`
2. `Coordinator_Unsubscribed_OnShutdown`
3. `Coordinator_NoCurrentChanged_DuringBoot` *(initial load doesn't fire CurrentChanged; coordinator never runs at boot)*

### Total estimate

~63-78 new tests across the four new test files (review pass added 3).
Baseline 1642 → ~1705-1720. Within the kickoff doc's 1696-1721 target.

---

## 8. Risks the review pass should examine

### Architectural

1. **`SinkRegistration.RouteId` staying as misnomer.** **RESOLVED:**
   review pass said "accept for Phase 2, rename later." Cleanup, not
   a hot-reload blocker. The diagnostics emit path tags health events
   with the FIRST route's id; M.P2.1 phase 3b's inventory builder
   doesn't rely on this field for route↔sink mapping (walks
   `config.Routes` directly). One subtle behaviour: a `Degraded`
   event on a sink serving routes [X, Y, Z] is tagged with X only.
   Acceptable for v1; revisit when renaming.

2. **Per-instance 30s timeout.** **RESOLVED:** review pass locked
   hardcoded 30s for v1. Configurability deferred. Modbus's slow-
   network case is mitigated by the boot-time fault path (which uses
   the same 30s ceiling implicitly) — operationally, if a Modbus dial
   exceeds 30s the gateway is in trouble regardless.

3. **`RouteDefinitionFactory.BuildOne`** doesn't exist yet — the
   current `Build` operates on the whole `GatewayConfiguration`.
   **RESOLVED:** review pass said "add overload cleanly." The new
   per-route overload reuses the existing `Build`'s body for a single
   `RouteConfig`. Verify during Phase 2.c implementation that the
   refactor doesn't change boot-time `Build(config, …)` behaviour.

### Threading

4. **`_reconcileSemaphore` vs `_shutdownCts` on dispose.**
   **RESOLVED:** review pass added the 5s bounded drain (correction
   #4). DisposeAsync logs Warning + exits if the in-flight reconcile
   can't finish in 5s. Test pinned at #29 in §7.

5. **`_ = AppendRuntimeFaultAsync(fault)`** — fire-and-forget on the
   audit append. **RESOLVED:** review pass mandated `await`
   (correction #1). The helper is renamed `RegisterAndAuditFaultAsync`
   and catches the audit-append exception locally — logs Critical,
   does not re-register. Test pinned at #28 in §7.

### Operational

6. **MQTT/OPC UA Server connection churn.** Spelled out in the
   kickoff doc — store-and-forward holds points across the gap, but
   external SCADA clients see a disconnect/reconnect on every sink
   restart. **Acceptable for v1** per review pass — alternative is a
   much larger Phase 2. Document in Phase 3 ops-runbook.

---

## 9. Critical files to confirm during review

These are the files whose current behaviour Phase 2 MUST NOT regress.
The review pass should sanity-check that the proposed changes don't
violate the invariants pinned in their headers:

- `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` — header
  locks "channels pre-created at construction" + "per-adapter
  isolation." Phase 2 EXTENDS this (channels per-instance) but the
  invariants must still hold.
- `src/ElpisEdgeConnect.Host/Adapters/SinkSupervisor.cs` — header
  locks "per-adapter isolation" + "ONE adapter-state event per state
  change." Phase 2 EXTENDS — the Remove/Add/Restart cycle must emit
  the same event sequence.
- `src/ElpisEdgeConnect.Core/Configuration/ConfigurationManager.cs`
  — `ApplyDraftAsync` emits `CurrentChanged` inside the mutex. Phase
  2 MUST hop off the firing thread to avoid blocking the mutex on
  device I/O. (Pinned in §0.)
- `src/ElpisEdgeConnect.Core/Diagnostics/IConfigurationFaultRegistry.cs`
  — `Register` is REPLACES per (Kind, InstanceId). `ClearFor` is the
  inverse. Phase 2 uses both per the success/failure paths above.
- `src/ElpisEdgeConnect.Host/HostStartup.cs` — locked startup phase
  order. Phase 2 adds the coordinator subscription AFTER `MarkReady`,
  unsubscribe BEFORE `MarkNotReady`. No new `StartupPhase` enum value.

---

## 10. Review resolution log

ChatGPT review pass, 2026-05-16. All 7 questions answered; design
revised to incorporate 4 corrections. Tracked in §0.5 (top of doc)
for visibility.

| Q | Question | Outcome | Where applied |
|---|---|---|---|
| 1 | Threading model | Accept | unchanged (§0) |
| 2 | `SinkRegistration.RouteId` misnomer | Accept defer | risk #1 closed (§8) |
| 3 | Stop/start order | Accept with sink correction | §5 sink teardown rewritten to action-driven only (correction #2) |
| 4 | Failure matrix audit row | Accept with fix | §5 helper renamed + awaited (correction #1); §6 row rewritten |
| 5 | Test list | Accept + 3 added | §7 tests #27-29 added |
| 6 | 30s timeout | Accept hardcoded | risk #2 closed (§8); §5 unchanged |
| 7 | `RouteDefinitionFactory.BuildOne` | Accept | risk #3 closed (§8); phase 2.c will add overload |

Additional corrections beyond the 7 questions:

- **Correction #3 (stale-reconcile):** version check at start of
  `ReconcileAsync`. Skip if `e.NewVersionId !=
  IConfigurationManager.CurrentVersionId`. Test #27.
- **Correction #4 (DisposeAsync timeout):** 5s bounded drain on
  the reconcile semaphore. Warning log + exit on timeout. Test #29.

### Implementation phases (locked)

```
Phase 2.a — Supervisor refactor + tests (SourceSupervisor, SinkSupervisor)
Phase 2.b — RegistrationFactory + extension extractions + tests
Phase 2.c — RuntimeReloadCoordinator + HostStartup wire-up + tests
```

All three on `claude/m-p2-2-hot-reload`, committed sequentially. Phase
3 (Management surfaces + smoke + docs) follows on the same branch.

If anything novel surfaces mid-phase (especially in 2.a's channel-
resurrection or 2.c's audit-append handling), pause for a follow-up
review before continuing.

---

**End of Phase 2 design v2 — review locked, ready for implementation.**
