# M.P2.2 Phase 2.c — Tactical implementation plan v2

**Date:** 2026-05-16
**Branch:** `claude/m-p2-2-hot-reload` (tip: `5487955` after Phase 2.b)
**Status:** **Locked.** Implementation may proceed.
**Related docs:**
- `docs/decisions/0009-runtime-hot-reload-instance-granularity.md` (ADR-0009)
- `docs/sessions/2026-05-16-mp22-kickoff.md`
- `docs/sessions/2026-05-16-mp22-phase2-design.md` §0, §5 (the locked threading + coordinator pseudocode)
- `docs/sessions/2026-05-16-mp22-phase2a-plan.md` (supervisor refactor — shipped)
- `docs/sessions/2026-05-16-mp22-phase2b-plan.md` (RegistrationFactory — shipped)

This is the tactical plan for Phase 2.c only. Same cadence as 2.a/2.b:
draft → review → implementation. Implementation does not begin until
this plan is locked.

---

## 0. Review resolution log

ChatGPT review pass on the v1 draft approved the plan direction with
one clarification on §5.4 (the unreferenced-sinks rule).

| # | Item | Disposition |
|---|---|---|
| 1 | §5.4 — widen sink-stop rule to include orphan cleanup based on new-config references | **Accepted** with precise wording (locked in §5.4 below). |
| 2 | Add explicit "configured but dormant" sink rule | **Accepted** — added as §5.4.1. The coordinator's runtime cleanup does NOT register a `ConfigurationFault` when an orphaned sink is stopped. A new test (#27 below) pins this. |
| 3 | All other items (threading, stale-skip, awaited audit append, bounded dispose, stop/start order, wire-up, test list) | **Accepted as drafted.** |

Locked principles after review:

> **Config = operator intent. Runtime = projection of the enabled,
> referenced graph.** The coordinator stops runtime instances that
> the runtime projection no longer needs; it never deletes or mutates
> configuration records.

> Stopping a configured-but-unreferenced sink is **not** a
> `ConfigurationFault`. It's a valid dormant configured sink. The
> Studio's M.P2.1 phase 3b inventory builder already surfaces this
> as `StateConfigured` ("enabled, no route") in the existing
> precedence — no UI changes needed.

§5.4 below carries the locked rule wording verbatim.

---

## 1. Scope guardrail (locked at planning time)

> **Phase 2.c is the first wire-up phase. It may connect existing
> Phase 1, 2.a, and 2.b seams, but it must not add API/UI surfaces or
> Phase 3 `ApplyResultDto.Reload` behavior.**

Concretely:

- The coordinator is the first runtime consumer of the
  `IConfigurationManager.CurrentChanged` event. Today the event has
  no subscribers (ADR-0004 backstory).
- The coordinator orchestrates supervisors (Phase 2.a) using
  `IRegistrationFactory` (Phase 2.b) and `RuntimeReloadClassifier`
  (Phase 1).
- It writes runtime-fault audit-chain entries via the M.P2.1
  `IConfigurationManager.AppendRuntimeFaultAsync` and registers
  faults in the M.P2.1 `IConfigurationFaultRegistry`.
- It does **NOT** add a `Reload` block to `ApplyResultDto`. The
  Apply response stays the M.2a shape.
- It does **NOT** add any Studio Razor changes, API endpoints, or
  Apply-time reload observability surface.

If 2.c reveals the need for either of those, it's a Phase 3 concern
and the design surfaces the requirement without implementing it.

---

## 2. What 2.c delivers

1. `RuntimeReloadCoordinator` — the orchestrator class. Subscribes
   to `CurrentChanged`, classifies the diff, drives supervisors +
   routing engine in the locked stop/start order, registers + clears
   faults, writes runtime-fault audit entries.
2. `IRoutingEngine` gains a per-route `BuildOne` method via the
   existing `RouteDefinitionFactory` (or equivalent extension —
   see §5.4).
3. `HostStartup` wires the coordinator subscription **after**
   `MarkReady` and unsubscribes **before** `MarkNotReady`.
4. `CompositionRoot` registers `IRegistrationFactory` and
   `RuntimeReloadCoordinator` as singletons.
5. 31 new tests across `Host.Tests`. Uses real
   `IConfigurationManager` (in-memory store), real supervisors with
   mock adapters, real routing engine. The strongest tests pin the
   threading invariants, the stop/start order, and the locked
   §5.4 / §5.4.1 sink rules.

**Test target:** 1693 baseline → **1724**.

---

## 3. Out of scope + non-goals

### Out of scope (defer to Phase 3 or beyond)

- `ApplyResultDto.Reload` block. Apply response stays unchanged.
- Studio `Config.razor` "what just happened after Apply" panel.
- `IReloadOutcomeQueue` / version-keyed outcome cache.
- `docs/ops-runbook.md` / `docs/config-authoring.md` updates.
- Removing or renaming `SinkRegistration.RouteId` (Phase 2.a deferral
  still in force).
- Smoke testing against the live demo gateway — happens in Phase 3.

### Non-goals locked

> The coordinator is **not** a service-locator-style ambient context.
> All dependencies flow in via the constructor. The coordinator is
> a singleton with mutable in-flight state (the reconcile semaphore
> + shutdown CTS); it is not stateless like `RegistrationFactory`.

> The coordinator **never** blocks `IConfigurationManager._mutex`.
> The `CurrentChanged` handler returns in microseconds via
> `_ = Task.Run(...)`; reconciliation runs on the threadpool
> serialised by a SEPARATE `SemaphoreSlim(1,1)`. This is the
> non-negotiable threading invariant from Phase 2 design §0.

> The coordinator **never** runs reconciliation against a stale
> configuration version. A version check at the top of
> `ReconcileAsync` skips reconciles whose target version is no
> longer current.

---

## 4. Files (new + modified)

### New

| File | Purpose | LOC est. |
|---|---|---|
| `src/ElpisEdgeConnect.Host/RuntimeReloadCoordinator.cs` | The orchestrator. Subscribes, classifies, drives supervisors + routing engine, manages faults. | ~280 |
| `tests/ElpisEdgeConnect.Host.Tests/RuntimeReloadCoordinatorTests.cs` | 30 integration tests | ~700 |

### Modified

| File | Change | LOC delta |
|---|---|---|
| `src/ElpisEdgeConnect.Core/Routing/IRoutingEngine.cs` | No change — `UnregisterRouteAsync` shipped in Phase 1. | 0 |
| `src/ElpisEdgeConnect.Core/Routing/RouteDefinitionFactory.cs` *(probable; §5.4)* | Add `BuildOne(RouteConfig, …) → RouteDefinition?` if not already extractable. The Phase 1 `RuntimeReloadClassifier` produces per-route plan actions; the coordinator needs to build a single route's definition from those. | +~50 |
| `src/ElpisEdgeConnect.Host/HostStartup.cs` | Construct + start the coordinator after `MarkReady`; stop + dispose before `MarkNotReady` on shutdown. No new `StartupPhase` enum value — coordinator wire-up rides inside the existing `MarkReady` phase's tail. | +~25 |
| `src/ElpisEdgeConnect.Host/CompositionRoot.cs` | Register `IRegistrationFactory` → `RegistrationFactory` (singleton); register `RuntimeReloadCoordinator` (singleton). | +~10 |

**Total budget:** ~360-380 production + ~700 tests. Largest single
commit of the milestone — orchestration code is verbose. Test count
30 is the largest set of any phase too.

---

## 5. `RuntimeReloadCoordinator` design

### 5.1 Construction + lifecycle surface

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
    private readonly ILogger<RuntimeReloadCoordinator> _logger;

    private readonly SemaphoreSlim _reconcileSemaphore = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private int _subscribed;   // 0 = not subscribed, 1 = subscribed
    private int _disposed;     // 0 = alive, 1 = disposed

    // Per-instance reconcile-step timeout. Matches the Phase 2.a
    // SupervisorStopInternal ceiling and the ISinkAdapter graceful-
    // stop contract.
    private const int PerInstanceTimeoutMs = 30_000;

    // Bounded drain on DisposeAsync (correction #4 from Phase 2 design v2).
    private const int DisposeDrainTimeoutMs = 5_000;

    public RuntimeReloadCoordinator(
        IConfigurationManager configManager,
        SourceSupervisor sourceSupervisor,
        SinkSupervisor sinkSupervisor,
        IRoutingEngine routingEngine,
        RouteDefinitionFactory routeDefFactory,
        IConfigurationFaultRegistry faultRegistry,
        RuntimeDiagnosticsCollector diagnostics,
        IRegistrationFactory registrationFactory,
        IServiceProvider serviceProvider,
        ILogger<RuntimeReloadCoordinator> logger,
        ILicenseManager? license = null)
    {
        // Null-checks omitted for brevity in the plan; all dependencies
        // are validated.
        _configManager = configManager;
        _sourceSupervisor = sourceSupervisor;
        // ...
    }

    /// <summary>
    /// Subscribe to <see cref="IConfigurationManager.CurrentChanged"/>.
    /// Called by HostStartup AFTER MarkReady — the gateway must not
    /// react to a CurrentChanged event mid-boot.
    /// </summary>
    public void Subscribe()
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref _subscribed, 1, 0) != 0)
        {
            return;
        }
        _configManager.CurrentChanged += OnCurrentChanged;
        _logger.LogInformation("RuntimeReloadCoordinator: subscribed to CurrentChanged.");
    }

    /// <summary>
    /// Unsubscribe. Called by HostStartup BEFORE MarkNotReady on
    /// shutdown. Idempotent.
    /// </summary>
    public void Unsubscribe()
    {
        if (Interlocked.Exchange(ref _subscribed, 0) == 0)
        {
            return;
        }
        _configManager.CurrentChanged -= OnCurrentChanged;
        _logger.LogInformation("RuntimeReloadCoordinator: unsubscribed.");
    }
}
```

### 5.2 The `CurrentChanged` handler — the threading invariant

> **NON-NEGOTIABLE:** the handler MUST return in microseconds.
> `IConfigurationManager.ApplyDraftAsync` fires `CurrentChanged?
> .Invoke(...)` synchronously inside `_mutex` (`ConfigurationManager
> .cs:397`). Any blocking work inline would hold the apply mutex
> through device I/O.

```csharp
private void OnCurrentChanged(object? sender, ConfigurationChangeEventArgs e)
{
    // Fire-and-forget hop to the threadpool. No await. No work in
    // this method that could throw. The apply mutex releases the
    // moment Invoke returns.
    _ = Task.Run(() => ReconcileSafelyAsync(e), _shutdownCts.Token);
}
```

### 5.3 The reconcile body

```csharp
private async Task ReconcileSafelyAsync(ConfigurationChangeEventArgs e)
{
    // Outer safety net. Last-resort catch for any uncaught
    // exception in ReconcileAsync — prevents a worker-thread silent
    // death.
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
        _logger.LogCritical(ex,
            "RuntimeReloadCoordinator: unhandled reconcile failure for {Version}.",
            e.NewVersionId);
    }
}

private async Task ReconcileAsync(ConfigurationChangeEventArgs e)
{
    // ── Stale-version skip ──────────────────────────────────────
    // Two rapid applies (C1→C2 then C2→C3) queue two reconciles.
    // By the time the C2 reconcile finally starts, its target version
    // is no longer current — skip; the C3 reconcile queued behind
    // is the authoritative one.
    var current = await _configManager.GetCurrentAsync(_shutdownCts.Token);
    if (!current.VersionId.Equals(e.NewVersionId))
    {
        _logger.LogInformation(
            "Skipping stale reconcile for {Stale}; current is {Current}.",
            e.NewVersionId, current.VersionId);
        return;
    }

    var plan = RuntimeReloadClassifier.Classify(e.NewConfiguration, e.Changes);
    if (plan.IsNoOp) return;

    var ct = _shutdownCts.Token;
    var newConfig = e.NewConfiguration;

    // ── Phase A: teardown (routes → sources → sinks) ────────────

    // A1. Stop routes flagged by the plan (Remove or Restart teardown half).
    foreach (var action in plan.Actions
        .Where(a => a.Kind == ConfigurationEntityKind.Route &&
                    a.Op is ReloadOp.Remove or ReloadOp.Restart))
    {
        await TryWithFaultAsync(ConfigurationFaultKind.Route, action.EntityId, async () =>
        {
            await _routingEngine.UnregisterRouteAsync(action.EntityId, ct);
            _diagnostics.RemoveRoute(action.EntityId);
        });
    }

    // A2. Stop sources flagged by the plan.
    foreach (var action in plan.Actions
        .Where(a => a.Kind == ConfigurationEntityKind.Source &&
                    a.Op is ReloadOp.Remove or ReloadOp.Restart))
    {
        await TryWithFaultAsync(ConfigurationFaultKind.Source, action.EntityId, () =>
            _sourceSupervisor.RemoveAsync(action.EntityId, ct));
    }

    // A3. Stop sinks. Computed from NEW config: a sink is stopped if
    //     it's gone-from-config (plan Remove) OR plan-Restart (will
    //     re-add) OR no longer referenced by any enabled route in
    //     newConfig (orphan cleanup, per the locked rule below).
    var sinksToStop = ComputeUnreferencedSinks(plan, newConfig);
    foreach (var sinkId in sinksToStop)
    {
        await TryWithFaultAsync(ConfigurationFaultKind.Sink, sinkId, () =>
            _sinkSupervisor.RemoveAsync(sinkId, ct));
    }

    // ── Phase B: bring-up (sources → sinks → routes) ────────────

    // B1. Start new + restart sources.
    foreach (var action in plan.Actions
        .Where(a => a.Kind == ConfigurationEntityKind.Source &&
                    a.Op is ReloadOp.Add or ReloadOp.Restart))
    {
        await TryWithFaultAsync(ConfigurationFaultKind.Source, action.EntityId, async () =>
        {
            var src = (SourceInstanceConfig)action.NewConfig!;
            if (!src.Enabled) return;  // disabled = not the factory's problem
            var reg = _registrationFactory.BuildSource(
                src, newConfig.Gateway,
                _ => ResolveRouteForSource(src.InstanceId, newConfig),
                _license, _faultRegistry, _serviceProvider);
            if (reg is null) return;   // factory skipped (license/route/protocol)
            await _sourceSupervisor.AddAsync(reg, ct);
            _faultRegistry.ClearFor(ConfigurationFaultKind.Source, src.InstanceId);
        });
    }

    // B2. Start new + restart sinks.
    foreach (var action in plan.Actions
        .Where(a => a.Kind == ConfigurationEntityKind.Sink &&
                    a.Op is ReloadOp.Add or ReloadOp.Restart))
    {
        await TryWithFaultAsync(ConfigurationFaultKind.Sink, action.EntityId, async () =>
        {
            var sink = (SinkInstanceConfig)action.NewConfig!;
            if (!sink.Enabled) return;
            var reg = _registrationFactory.BuildSink(
                sink, newConfig.Gateway,
                _ => ResolveFirstRouteForSink(sink.InstanceId, newConfig),
                _license, _faultRegistry, _serviceProvider);
            if (reg is null) return;
            await _sinkSupervisor.AddAsync(reg, ct);
            _faultRegistry.ClearFor(ConfigurationFaultKind.Sink, sink.InstanceId);
        });
    }

    // B3. Bring up routes (Add + Restart bring-up half).
    foreach (var action in plan.Actions
        .Where(a => a.Kind == ConfigurationEntityKind.Route &&
                    a.Op is ReloadOp.Add or ReloadOp.Restart))
    {
        await TryWithFaultAsync(ConfigurationFaultKind.Route, action.EntityId, async () =>
        {
            var rc = (RouteConfig)action.NewConfig!;
            if (!rc.Enabled) return;
            var def = _routeDefFactory.BuildOne(rc, newConfig,
                _sourceSupervisor, _sinkSupervisor.Registrations, _faultRegistry);
            if (def is null) return;
            _diagnostics.EnsureRoute(rc.RouteId);
            await _routingEngine.RegisterRouteAsync(def, ct);
            await _routingEngine.StartRouteAsync(rc.RouteId, ct);
            _faultRegistry.ClearFor(ConfigurationFaultKind.Route, rc.RouteId);
        });
    }
}
```

### 5.4 Unreferenced-sinks computation (locked rule)

**Locked wording (verbatim from review pass):**

> The coordinator stops runtime sink instances that are unreferenced
> by any enabled route in the NEW configuration, regardless of
> whether the sink config record itself changed. This is runtime
> cleanup only; it does not delete or mutate the sink configuration.

Concretely, a sink is stopped if **any** of these is true:

 1. The plan has `Op == Remove` for the sink (sink gone from `newConfig.Sinks`).
 2. The plan has `Op == Restart` for the sink (the bring-up half B2 re-adds).
 3. The sink is currently supervised AND no enabled route in
    `newConfig.Routes` references it — even if the sink's own
    config record is unchanged. This is the orphan cleanup case
    that the locked wording specifically captures.

A sink with `Op == Restart` is **always** restarted, even if other
routes still reference it — operator intent: the sink's own config
changed.

A sink with `Op == Add` is **always** added in B2, regardless of
whether any route currently references it.

The orphan-cleanup case (#3) catches the scenario the review
specifically called out:

```
Old config:
  route-1 -> sink-a

New config:
  route-1 removed
  sink-a still exists in config but no enabled route references it

Classifier emits: Op == Remove for route-1; no action for sink-a.

Coordinator:
  A1: stops route-1.
  A3: observes sink-a is now unreferenced; calls
      _sinkSupervisor.RemoveAsync("sink-a").
  Result: sink-a is stopped at runtime; sink-a STAYS in
  config.Sinks (operator intent is preserved); the Studio's
  inventory builder shows sink-a as StateConfigured
  ("enabled, no route") per M.P2.1 phase 3b precedence.
```

If we didn't compute unreferenced-sinks from new config, sink-a
would keep running with no routes feeding it — a wasted MQTT broker
connection / OPC UA listener / etc.

### 5.4.1 Dormant-sink rule — stopping is NOT a ConfigurationFault

**Locked:** if a sink is configured + enabled but unreferenced in
the new config, stopping it is **not** a `ConfigurationFault`. It
is a valid dormant configured sink.

Implementation guarantee: the orphan-cleanup `foreach` loop wraps
`_sinkSupervisor.RemoveAsync` in `TryWithFaultAsync` (same as every
other coordinator action). `TryWithFaultAsync` only registers a
fault if the wrapped action **throws**. On the happy path —
`RemoveAsync` succeeds, returning normally — no fault is
registered, no audit-chain entry is written, the registry stays
clean. The supervisor records `Stopped` health for the sink
(per Phase 2.a's `StopInternal`); the inventory builder maps that
to no live snapshot + sink-still-in-config → `StateConfigured`.

Test #27 below asserts this end-to-end: after orphan cleanup
stops a sink, `_faultRegistry.GetFaults()` contains no entry for
that sink id, AND the supervisor reports `Stopped`. The two
together prove the runtime-cleanup-not-fault invariant.

**Future contributors note:** if a follow-up phase introduces a
"sink is configured but unreferenced" warning surface (e.g., the
Studio's Diagnostics page), it must NOT route through
`IConfigurationFaultRegistry`. The right channel for that
operator hint is a separate diagnostics observation (Phase 3+
concern).

### 5.4.2 Stale-fault clearing (observation, not locked here)

A subtle interaction observed during this plan: when the
coordinator stops a sink via orphan cleanup, any pre-existing
fault for that sink in the registry stays in place. The Studio's
inventory builder displays `Faulted` higher in the precedence
than `Configured` (ADR-0007), so a sink that had an old
broker-unreachable fault and is now stopped + dormant would still
render as `Faulted` until the operator fixes the underlying
config OR brings the sink back up via a route addition.

This is **acceptable for v1**:
- The fault is historically accurate (the sink did fail before).
- ADR-0005 locks ClearFor to fire on **successful re-init**, not
  on teardown. Loosening that rule to "ClearFor on teardown
  without re-add" is a separate decision worth its own ADR
  discussion (changes the fault-registry semantics).

If operationally painful in practice, Phase 3 (or a follow-up
phase) can introduce a "stale fault dismissal" mechanism. For
2.c, leave the registry behavior unchanged from Phase 2.a/2.b.

Tests #14, #15, #16, #27 pin the locked rules in §5.4 and §5.4.1.
No test pins §5.4.2 — it's an observation, not a rule yet.

```csharp
private List<string> ComputeUnreferencedSinks(
    ConfigurationReloadPlan plan,
    GatewayConfiguration newConfig)
{
    var result = new List<string>();
    var planSet = plan.Actions
        .Where(a => a.Kind == ConfigurationEntityKind.Sink &&
                    a.Op is ReloadOp.Remove or ReloadOp.Restart)
        .Select(a => a.EntityId)
        .ToHashSet(StringComparer.Ordinal);

    // (1) + (2) — plan-driven Remove/Restart.
    result.AddRange(planSet);

    // (3) — orphan cleanup. Compute the set of sink ids still
    // referenced by any enabled route in the new config.
    var stillReferenced = new HashSet<string>(StringComparer.Ordinal);
    foreach (var route in newConfig.Routes)
    {
        if (!route.Enabled) continue;
        foreach (var sinkId in route.SinkInstanceIds)
        {
            stillReferenced.Add(sinkId);
        }
    }

    // Any currently-supervised sink not in stillReferenced and not
    // already in planSet (i.e., we wouldn't double-stop it).
    foreach (var reg in _sinkSupervisor.Registrations)
    {
        var id = reg.Adapter.InstanceId;
        if (stillReferenced.Contains(id)) continue;
        if (planSet.Contains(id)) continue;  // already in result
        result.Add(id);
    }

    return result;
}
```

**Defensive guarantee:** sinks in `Op == Restart` are added to
`result` from `planSet`, BUT B2 (bring-up) re-adds them via
`AddAsync`. The supervisor's `RemoveAsync` + `AddAsync` pair handles
the restart cleanly; references from other routes are managed at
the route-definition level (which gets rebuilt for restarted routes
in B3).

### 5.5 Per-instance fault wrapper — awaited audit append

> **Locked from Phase 2 design v2 correction #1:** the audit append
> is **awaited**, not fire-and-forget. The wrapper catches the
> append's own exception locally, logs Critical, and does NOT
> re-register a fault (would loop).

```csharp
private async Task TryWithFaultAsync(
    ConfigurationFaultKind kind,
    string entityId,
    Func<Task> action)
{
    try
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(PerInstanceTimeoutMs));
        await action();
    }
    catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
    {
        throw;  // bubble shutdown
    }
    catch (OperationCanceledException)
    {
        await RegisterAndAuditFaultAsync(kind, entityId,
            "HOST.RECONCILE_TIMEOUT",
            $"Reconcile step exceeded {PerInstanceTimeoutMs}ms; instance left in last-known state.");
    }
    catch (Exception ex)
    {
        await RegisterAndAuditFaultAsync(kind, entityId,
            "HOST.RECONCILE_FAILED",
            ex.Message);
    }
}

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
    _faultRegistry.Register(fault);

    // AWAITED — durability matters. If the append throws, log
    // Critical with the full fault detail; do NOT re-register
    // (would loop). The fault stays in the live registry; only
    // the durable audit-chain entry is lost.
    try
    {
        await _configManager.AppendRuntimeFaultAsync(fault, _shutdownCts.Token);
    }
    catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogCritical(ex,
            "Runtime fault audit append FAILED for {Kind} '{Id}' (code={Code}). " +
            "Fault is in live registry but NOT in durable audit chain. Detail: {Message}",
            kind, entityId, errorCode, message);
    }
}
```

### 5.6 Bounded `DisposeAsync`

> **Locked from Phase 2 design v2 correction #4:** wait at most 5s
> for the in-flight reconcile to release the semaphore. Beyond that,
> log Warning and exit cleanly. Prevents a stuck adapter from
> hanging process shutdown indefinitely.

```csharp
public async ValueTask DisposeAsync()
{
    if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
    {
        return;
    }

    Unsubscribe();
    _shutdownCts.Cancel();

    // Bounded drain.
    var acquired = await _reconcileSemaphore.WaitAsync(DisposeDrainTimeoutMs);
    if (!acquired)
    {
        _logger.LogWarning(
            "RuntimeReloadCoordinator: reconcile did not exit within {Timeout}ms during dispose. " +
            "Process exit will proceed; in-flight reconcile may leave supervisors in mid-state.",
            DisposeDrainTimeoutMs);
    }
    else
    {
        _reconcileSemaphore.Release();
    }

    _shutdownCts.Dispose();
    _reconcileSemaphore.Dispose();
}

private void ThrowIfDisposed()
{
    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
```

---

## 6. `RouteDefinitionFactory.BuildOne`

The coordinator needs to build a `RouteDefinition` for a single
route in B3. The current `RouteDefinitionFactory.Build` operates
on the whole `GatewayConfiguration`. Two options:

**Option A — add a public `BuildOne(RouteConfig, GatewayConfiguration,
SourceSupervisor, IEnumerable<SinkRegistration>, IConfigurationFaultRegistry?)
→ RouteDefinition?` overload.** Preferred. The existing `Build`
method can be refactored to call `BuildOne` per route. Keeps the
boot-time behavior bit-identical.

**Option B — fully reuse the existing `Build` method by calling it
on a one-route projection of the config.** Less clean (creates a
throwaway `GatewayConfiguration` for every per-route reconcile);
not recommended.

§4 budgets Option A at ~+50 LOC. Step 1 of implementation verifies
the existing `Build` body can be refactored cleanly without changing
boot-time behavior.

---

## 7. `HostStartup` + `CompositionRoot` wire-up

### 7.1 `CompositionRoot.cs`

```csharp
// Phase 2.b: factory is stateless, registered as singleton.
services.AddSingleton<IRegistrationFactory, RegistrationFactory>();

// Phase 2.c: the coordinator is a singleton; HostStartup drives
// its Subscribe/Unsubscribe lifecycle.
services.AddSingleton<RuntimeReloadCoordinator>();
```

### 7.2 `HostStartup.cs` — subscribe after MarkReady

```csharp
// ... existing phases through MarkReady ...

_observer.OnStartupPhase(StartupPhase.MarkReady);
_readiness.MarkReady();

// M.P2.2 phase 2.c: subscribe the coordinator AFTER MarkReady so
// the gateway doesn't react to a CurrentChanged event mid-boot.
// CurrentChanged itself doesn't fire during InitializeAsync (per
// the IConfigurationManager contract), so this is belt-and-braces.
_reloadCoordinator.Subscribe();

_observer.OnStartupPhase(StartupPhase.StartMetricsEndpoint);
// ... rest of startup unchanged ...
```

### 7.3 `HostStartup.cs` — unsubscribe before MarkNotReady

```csharp
// ... existing shutdown phases ...

// Unsubscribe BEFORE marking not-ready. Any CurrentChanged event
// after this point is silently ignored.
_reloadCoordinator.Unsubscribe();

_observer.OnShutdownPhase(StartupPhase.MarkReady);
_readiness.MarkNotReady();

// ... rest of shutdown unchanged ...
```

### 7.4 `HostStartup.cs` — dispose during shutdown

The coordinator is a singleton; DI disposes it when the host scope
ends. `DisposeAsync` handles the bounded drain per §5.6. No
explicit dispose call in `HostStartup`.

### 7.5 No new `StartupPhase` enum value

Wire-up rides inside the existing `MarkReady` phase block — keeps
the locked startup phase order unchanged. Subscribe is the LAST
line of the MarkReady phase block.

---

## 8. Implementation order (with regression gates)

| Step | Files touched | Why this order |
|---|---|---|
| 1 | `RouteDefinitionFactory.cs` — add `BuildOne` overload; refactor `Build` to call it per route. | Smallest change; verifies boot-time behavior holds before adding the coordinator. |
| 2 | **Full sweep — must still be 1693.** | First regression gate. |
| 3 | `RuntimeReloadCoordinator.cs` — full implementation. | The big chunk. |
| 4 | `CompositionRoot.cs` + `HostStartup.cs` — wire-up. | Now the coordinator is reachable but no tests touch it yet. |
| 5 | **Full sweep — must still be 1693.** | Second regression gate. Boot still works, no CurrentChanged event has fired during the test suite (only happens on Apply, which the test suite doesn't routinely do at the coordinator-test scope — except integration tests, which we monitor). |
| 6 | `RuntimeReloadCoordinatorTests.cs` — write all 31 tests. | Most complex test surface in the milestone. |
| 7 | **Final sweep — expect 1724.** | Final gate. |
| 8 | Single commit. |  |

**Step 2 + Step 5 are the critical regression gates.** Either failing
stops implementation pending review.

---

## 9. Test list (31 named — final count after writing)

`RuntimeReloadCoordinatorTests` lives in
`tests/ElpisEdgeConnect.Host.Tests/RuntimeReloadCoordinatorTests.cs`.
Each test uses real `IConfigurationManager` (with `InMemoryConfigurationStore`),
real supervisors with mock adapters, real routing engine, real
`IConfigurationFaultRegistry`, real `RuntimeDiagnosticsCollector`.

### Threading invariants (5)

1. `CurrentChanged_HandlerReturnsImmediately` — assert the handler completes in <10ms even when reconciliation will take longer (use barrier in mock adapter to slow reconcile).
2. `CurrentChanged_DoesNotBlockSubsequentApply` — stress: 10 applies back-to-back, all return in <100ms total even though reconciles take seconds. Pin the apply-mutex-not-blocked invariant.
3. `Reconcile_TwoNearSimultaneousApplies_AreSerialised` — fire two `CurrentChanged` events back-to-back; reconciles run sequentially via the reconcile semaphore.
4. `Reconcile_StaleQueuedVersion_IsSkipped` — apply C1→C2 then C2→C3 rapidly; the C2 reconcile starts after C3 has landed; assert it's skipped with the locked Information-level log.
5. `Reconcile_ApplyDuringReconcile_ApplyResponseDoesNotWait` — start a long-running reconcile; fire a new Apply; the Apply returns immediately while reconcile is still in flight.

### Plan-driven actions (8)

6. `Reconcile_AddSource_BringsUpSupervisorEntryAndRoute`
7. `Reconcile_RemoveSource_TearsDownSupervisorAndRoute`
8. `Reconcile_RestartSource_StopsOldAndStartsNew`
9. `Reconcile_AddSink_BringsUpSupervisorEntry`
10. `Reconcile_RemoveSink_StopsSupervisorEntry`
11. `Reconcile_RestartSink_StopsAndRestarts_RegardlessOfOtherRouteReferences`
12. `Reconcile_AddRoute_RegistersAndStartsRoute`
13. `Reconcile_RemoveRoute_StopsAndUnregistersRoute`

### Unreferenced-sinks computation + dormant rule (4)

14. `Reconcile_SinkBecomesUnreferenced_ViaRouteRemoval_StopsSink` *(the "orphan cleanup" case — Route R is removed, Sink S has no plan action, S becomes unreferenced, coordinator stops S)*
15. `Reconcile_SinkStillReferencedByAnotherRoute_NotStopped` *(N=2 → N=1; sink stays running)*
16. `Reconcile_SinkRestart_RestartsEvenIfStillReferenced` *(locked: Restart always proceeds regardless of N)*
17. **`Reconcile_OrphanSinkCleanup_DoesNotRegisterFault`** *(locked from §5.4.1: stopping a configured-but-unreferenced sink is NOT a fault. After the coordinator stops sink-a via orphan cleanup, `_faultRegistry.GetFaults()` contains no entry for sink-a AND the supervisor reports Stopped. The sink stays in `config.Sinks` — operator intent preserved.)*

### Stop/start ordering (3)

18. `Reconcile_OrderInvariant_RoutesStopBeforeSources` *(observable via health-event ordering: route Stopped event precedes source Stopped event)*
19. `Reconcile_OrderInvariant_RoutesStartAfterSourcesAndSinks` *(source Running and sink Running precede route Running on a fresh add)*
20. `Reconcile_RestartSource_RouteIsRebuiltAfterAddCompletes` *(channel-resurrection contract from Phase 2.a — the route's new RouteDefinition references the new intake)*

### Fault handling (4)

21. `Reconcile_AdapterInitThrows_RegistersFault_AndAuditEntry` *(awaited audit append, locked from correction #1)*
22. `Reconcile_AdapterInitThrows_OtherInstancesContinue` *(per-instance isolation)*
23. `Reconcile_SuccessfulReInit_ClearsRegistryEntry` *(the operator fixed a bad source; coordinator clears the fault)*
24. `Reconcile_PerInstanceTimeout_RegistersFault_AndContinues` *(`HOST.RECONCILE_TIMEOUT`; the 30s ceiling fires for a stuck adapter; other instances proceed)*

### Robustness (3)

25. `Reconcile_AuditAppendFailure_IsLogged_AndOtherActionsContinue` *(correction #1 — the audit-append wrapper catches its own exception, logs Critical, does not re-register; remaining plan actions proceed)*
26. `Reconcile_LastResortCatch_LogsCritical_DoesNotKillGateway` *(an uncaught exception in ReconcileAsync is caught by ReconcileSafelyAsync; the gateway continues)*
27. `DisposeAsync_ReconcileHung_DoesNotHangForever` *(correction #4 — DisposeAsync waits 5s on the reconcile semaphore; logs Warning; exits cleanly)*

### Wire-up sanity (3)

28. `Coordinator_Subscribed_AfterMarkReady_NotBefore`
29. `Coordinator_Unsubscribed_OnShutdown`
30. `Coordinator_NoCurrentChanged_DuringBoot` *(initial load doesn't fire CurrentChanged; coordinator never runs at boot)*

### Optional adds during writing

- `Reconcile_GatewaySettingsOnly_IsNoOp` *(classifier already returns empty plan; coordinator should no-op)* — likely already implicitly covered.
- `Reconcile_DisabledInstance_GetsRemovedFromSupervisor` *(operator flipped Enabled=false → equivalent to Remove)* — possibly covered by #7.

**Final target:** **31 tests** (the v2 plan locks #17 as the dormant-rule pin, plus #17b added during writing for the orphan-cleanup-with-stale-pre-existing-fault case). Baseline 1693 → **1724**.

---

## 10. Risks & mitigations

| Risk | Mitigation |
|---|---|
| `Task.Run` fire-and-forget could swallow exceptions silently | `ReconcileSafelyAsync` last-resort catch logs Critical. Test #25 pins this. |
| Apply mutex blocked by inline reconcile work | Pinned by tests #1 + #2 + #5. Strongly. |
| Stale reconcile clobbers later state | Stale-version skip at top of `ReconcileAsync`; test #4 pins. |
| Coordinator timeout doesn't catch stuck adapter | 30s per-instance ceiling via `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter`. Test #23 pins via mock adapter that blocks. |
| `DisposeAsync` hangs forever waiting for stuck reconcile | 5s drain ceiling. Test #26 pins. |
| Stop-order regression — routes don't stop first | Health-event ordering tests #17 + #18. |
| Orphan-sink computation removes sinks still in use | Test #15 pins "still referenced → not stopped". |
| Sink restart skipped because still referenced | Test #16 pins "Restart always proceeds regardless of N". |
| Audit append failure cascades | Locally caught; logged Critical. Test #24 pins. |
| `RouteDefinitionFactory.Build` boot-path regression after `BuildOne` extraction | Step 2 full sweep is the gate. If 1693 doesn't hold, stop and re-think the refactor. |
| `CurrentChanged` fires during InitializeAsync (would race subscribe timing) | The `IConfigurationManager` contract says it doesn't; defensive log if it does. Plus subscribe-after-MarkReady belt-and-braces. |
| Memory leak: subscribed coordinator never disposed | DI singleton; HostStartup disposes the host scope on shutdown which disposes the coordinator. DisposeAsync is idempotent + Unsubscribe is in DisposeAsync. |

---

## 11. Definition of done

1. `dotnet build ElpisEdgeConnect.sln --nologo` is **0 warnings, 0 errors**.
2. `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky" --no-build --nologo` passes — total **1724**.
3. All 31 named tests above exist and pass.
4. Step 2 and Step 5 regression gates both passed at 1693.
5. `IRegistrationFactory` + `RuntimeReloadCoordinator` are registered in DI.
6. `HostStartup` subscribes the coordinator after `MarkReady` and unsubscribes before `MarkNotReady` on shutdown.
7. No `ApplyResultDto.Reload` block. No Razor changes. No new API endpoints. No `IReloadOutcomeQueue`. No smoke-test script. (Guardrail enforcement.)
8. The locked threading invariant is pinned by tests #1, #2, #5.
9. The stop/start order is pinned by tests #17, #18.
10. The "unreferenced sinks from new config" rule is pinned by tests #14, #15, #16.

---

## 12. Pause-point criteria

Report back and pause before continuing if any of:

- Step 2 (post-`BuildOne` refactor) regression gate fails.
- Step 5 (post-coordinator wire-up, no new tests) regression gate fails — would mean something in the wire-up regressed an existing test.
- A test in the threading-invariant group reveals a real apply-mutex blocking scenario I didn't account for.
- `RouteDefinitionFactory.Build` can't be refactored cleanly into a `BuildOne` per-route call — would need a different extraction strategy.
- Orphan-sink cleanup interacts badly with `Op == Restart` on a still-referenced sink (e.g., the restart half tries to re-add a sink that was already in the "stop list"). The pseudocode above handles this; tests #15 + #16 are the gates.

---

## 13. Estimated session length

~2-3 hours for implementation + tests. Largest single commit of the
milestone. The coordinator pseudocode is essentially locked by this
plan; the heavy lifting is in the 31 tests, which exercise real
concurrency and ordering.

---

**End of Phase 2.c v2 plan. Locked. Implementation may proceed.**
