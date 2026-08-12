// ============================================================================
// File: RuntimeReloadCoordinator.cs
// Purpose: The M.P2.2 hot-reload orchestrator. Subscribes to
//          IConfigurationManager.CurrentChanged, classifies the diff,
//          and drives Phase 2.a supervisors + Phase 2.b RegistrationFactory
//          + the routing engine to converge runtime state on each applied
//          configuration.
//
//          First runtime consumer of CurrentChanged — before M.P2.2 the
//          event had no subscribers. Gateway boots without coordinator
//          activity (CurrentChanged doesn't fire during InitializeAsync);
//          coordinator subscribes AFTER MarkReady, unsubscribes BEFORE
//          MarkNotReady on shutdown.
//
// Reference: docs/sessions/2026-05-16-mp22-phase2c-plan.md
//            docs/decisions/0009-runtime-hot-reload-instance-granularity.md
// Milestone: M.P2.2 phase 2.c
//
// LOCKED design rules (per the phase 2.c plan v2):
//
//   * **Non-negotiable threading invariant.** The CurrentChanged handler
//     hops off the firing thread via `_ = Task.Run(...)` IMMEDIATELY.
//     The handler must return in microseconds. ConfigurationManager
//     fires CurrentChanged synchronously inside _mutex — any blocking
//     work in the handler would hold the apply mutex through device I/O.
//
//   * **Reconcile semaphore distinct from apply mutex.** Reconciliations
//     run on the threadpool, single-flighted by _reconcileSemaphore.
//     Concurrent applies arrive while a reconcile is in flight → the
//     next reconcile queues; APPLIES THEMSELVES do not queue.
//
//   * **Stale-reconcile skip.** If by the time a reconcile reaches the
//     head of the semaphore queue its target version is no longer the
//     ConfigurationManager's CurrentVersionId, skip — the later
//     reconcile is the authoritative one (ADR-0005: only reconcile
//     against the most-recent intent).
//
//   * **Stop/start order** (ADR-0009 Decision 2):
//        Teardown:  routes → unrefed sources → unrefed sinks
//        Bring-up:  sources → sinks → routes
//
//   * **Unreferenced sinks computed from NEW config** (phase 2.c plan v2
//     §5.4 — locked verbatim):
//        "The coordinator stops runtime sink instances that are
//        unreferenced by any enabled route in the NEW configuration,
//        regardless of whether the sink config record itself changed.
//        This is runtime cleanup only; it does not delete or mutate
//        the sink configuration."
//
//   * **Orphan-sink cleanup is NOT a fault** (phase 2.c plan §5.4.1):
//     stopping a configured-but-unreferenced sink is a valid dormant-
//     sink transition. TryWithFaultAsync registers a fault only on
//     throw; the happy-path RemoveAsync returns normally and no fault
//     is written.
//
//   * **Awaited runtime-fault audit append** (phase 2 design v2
//     correction #1): RegisterAndAuditFaultAsync awaits
//     AppendRuntimeFaultAsync. If the audit append itself throws, log
//     Critical with full fault detail and continue — do NOT re-register
//     a fault for the audit failure (would loop).
//
//   * **Bounded DisposeAsync** (phase 2 design v2 correction #4): wait
//     at most 5s on the reconcile semaphore during dispose. Beyond
//     that, log Warning and exit cleanly — prevents a stuck adapter
//     from hanging process shutdown indefinitely.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Host.Adapters;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// M.P2.2 hot-reload orchestrator. Subscribes to
/// <see cref="IConfigurationManager.CurrentChanged"/>, classifies the diff
/// via <see cref="RuntimeReloadClassifier"/>, and drives supervisors +
/// routing engine to converge runtime state on the applied configuration.
/// </summary>
public sealed class RuntimeReloadCoordinator : IAsyncDisposable
{
    /// <summary>
    /// Per-instance reconcile-step timeout. Matches the Phase 2.a
    /// SourceSupervisor / SinkSupervisor StopInternal ceiling and the
    /// ISinkAdapter graceful-stop contract.
    /// </summary>
    public const int PerInstanceTimeoutMs = 30_000;

    /// <summary>
    /// Bounded drain on <see cref="DisposeAsync"/>. Wait at most this
    /// many ms for in-flight reconciliation to release the semaphore;
    /// beyond that, log Warning and exit (correction #4).
    /// </summary>
    public const int DisposeDrainTimeoutMs = 5_000;

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
    private readonly IReloadOutcomeRegistry? _outcomeRegistry;

    // K1.3 R8 replay-sink hot-replace guard: classifies whether an INCOMING sink config is replay-aware
    // (no adapter instantiation). Null in K1.3 (no replay-aware sink protocol until K2's Sparkplug sink) —
    // the incoming side is then inert; the live side is always checked from the supervised adapter type.
    private readonly ISinkReplayCapabilityClassifier? _replayClassifier;

    /// <summary>
    /// Single-flight reconcile gate. **Distinct from
    /// <c>ConfigurationManager._mutex</c>.** Holding this semaphore
    /// across device I/O is fine — applies are not blocked.
    /// </summary>
    private readonly SemaphoreSlim _reconcileSemaphore = new(1, 1);

    private readonly CancellationTokenSource _shutdownCts = new();
    private int _subscribed;   // 0 = not subscribed, 1 = subscribed
    private int _disposed;     // 0 = alive, 1 = disposed

    /// <summary>Construct the coordinator with all dependencies resolved by DI.</summary>
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
        ILicenseManager? license = null,
        IReloadOutcomeRegistry? outcomeRegistry = null,
        ISinkReplayCapabilityClassifier? replayClassifier = null)
    {
        ArgumentNullException.ThrowIfNull(configManager);
        ArgumentNullException.ThrowIfNull(sourceSupervisor);
        ArgumentNullException.ThrowIfNull(sinkSupervisor);
        ArgumentNullException.ThrowIfNull(routingEngine);
        ArgumentNullException.ThrowIfNull(routeDefFactory);
        ArgumentNullException.ThrowIfNull(faultRegistry);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(registrationFactory);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _configManager = configManager;
        _sourceSupervisor = sourceSupervisor;
        _sinkSupervisor = sinkSupervisor;
        _routingEngine = routingEngine;
        _routeDefFactory = routeDefFactory;
        _faultRegistry = faultRegistry;
        _diagnostics = diagnostics;
        _registrationFactory = registrationFactory;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _license = license;
        _outcomeRegistry = outcomeRegistry;
        _replayClassifier = replayClassifier;
    }

    /// <summary>
    /// Subscribe to <see cref="IConfigurationManager.CurrentChanged"/>.
    /// Called by HostStartup AFTER MarkReady — the gateway must not
    /// react to a CurrentChanged event mid-boot. Idempotent.
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

    // ─────────────────────────────────────────────────────────────────
    // Threading invariant: the handler returns in microseconds.
    // No await, no blocking work, no exceptions allowed to escape.
    // ─────────────────────────────────────────────────────────────────
    private void OnCurrentChanged(object? sender, ConfigurationChangeEventArgs e)
    {
        // Fire-and-forget hop to the threadpool. The apply mutex
        // releases the moment this method returns.
        _ = Task.Run(() => ReconcileSafelyAsync(e), _shutdownCts.Token);
    }

    private async Task ReconcileSafelyAsync(ConfigurationChangeEventArgs e)
    {
        // Outer safety net: last-resort catch for any uncaught
        // exception in ReconcileAsync — prevents silent worker-thread
        // death.
        try
        {
            await _reconcileSemaphore.WaitAsync(_shutdownCts.Token).ConfigureAwait(false);
            try
            {
                await ReconcileAsync(e).ConfigureAwait(false);
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
        // ─── Stale-version skip ────────────────────────────────────
        // Two rapid applies (C1→C2 then C2→C3) queue two reconciles.
        // By the time the C2 reconcile reaches the head of the
        // semaphore queue, the manager's current version is C3 —
        // skip; the C3 reconcile queued behind us is authoritative.
        var ct = _shutdownCts.Token;
        var currentVersionId = _configManager.CurrentVersionId;
        if (!currentVersionId.Equals(e.NewVersionId))
        {
            _logger.LogInformation(
                "RuntimeReloadCoordinator: skipping stale reconcile for {Stale}; current is {Current}.",
                e.NewVersionId, currentVersionId);
            // M.P2.2 phase 3 Q2 verdict: enqueue a terminal Skipped outcome
            // so any ConfigApi waiter on this version gets a truthful response
            // (versus timing out into a misleading "InProgress").
            _outcomeRegistry?.EnqueueSkipped(e.NewVersionId, currentVersionId);
            return;
        }

        // ElapsedMs measures reconcile execution time only — it does NOT
        // include semaphore queue-wait time before this reconcile acquired
        // the gate. Phase 3 plan v2 §2 Q4 + ChatGPT review.
        var stopwatch = Stopwatch.StartNew();

        // The classifier output is the pristine "what the operator changed"
        // record. It is NEVER mutated downstream — locked H of M.P2.3 plan v2.
        var plan = RuntimeReloadClassifier.Classify(e.NewConfiguration, e.Changes);

        // M.P2.3 — synthesize recovery actions for entities skipped from the
        // supervisor at gateway startup by M.P2.1 fail-soft validation. These
        // are ADDITIONAL actions the classifier could not emit because the
        // entity's own config didn't change in the diff; the coordinator
        // catches the cross-record-validity flip against the registry and
        // adds Add actions to the bring-up phases. See ADR-0010.
        var synthesized = ComputeStartupSkipRecoveryActions(plan.Actions, e.NewConfiguration);
        var withRecovery = synthesized.Count == 0
            ? plan.Actions
            : (IReadOnlyList<ReloadAction>)plan.Actions.Concat(synthesized).ToList();

        // M.P2.4 — cascade a Route Restart for every enabled, running route
        // bound to a source this reload is Restarting. A source Restart
        // recreates the source's intake channel; a route left un-rebuilt keeps
        // its old (now-completed) channel reader and silently stops ingesting.
        // The classifier can't emit these (the route's own config text didn't
        // change), so the coordinator synthesizes them — same pristine-plan
        // discipline as the M.P2.3 startup-skip recovery pass. Deduped against
        // every prior action so the classifier's (or recovery's) intent wins.
        var rebindCascade = ComputeSourceRestartRouteRebindActions(withRecovery, e.NewConfiguration);
        var withSourceCascade = rebindCascade.Count == 0
            ? withRecovery
            : (IReadOnlyList<ReloadAction>)withRecovery.Concat(rebindCascade).ToList();

        // The rejected-replay-sink set is computed HERE, ahead of the sink→route cascade
        // below, because the cascade must never synthesize a route Restart for a sink whose
        // in-place hot-replace is about to be REJECTED: ComputeRoutesDependingOnRejectedSinks
        // would then match that synthesized action and fault the route with
        // HOST.REPLAY_ROUTE_DEPENDS_ON_REJECTED_SINK — spraying faults over routes the
        // operator never touched, for a rejection whose whole point is "change nothing".
        // Hoisting the computation is behaviour-neutral: it only inspects Sink Restart
        // actions, and neither cascade pass emits any.
        var rejectedReplaySinkReplacements = ComputeRejectedReplaySinkHotReplacements(withSourceCascade);

        // The sink-side mirror of the M.P2.4 source cascade. A sink Restart/Add builds a
        // BRAND-NEW adapter instance, but a running route captured the OLD ISinkAdapter in
        // its RouteDefinition and RouteWorker built one SinkPublisher per captured instance
        // for the life of the worker — nothing re-resolves. Without a route rebind the route
        // keeps publishing into the stopped-and-disposed adapter while the fresh one is bound
        // to nothing at all. Same synthesis discipline as the source pass.
        var sinkRebindCascade = ComputeSinkRestartRouteRebindActions(
            withSourceCascade, e.NewConfiguration, rejectedReplaySinkReplacements);
        var effectiveActions = sinkRebindCascade.Count == 0
            ? withSourceCascade
            : (IReadOnlyList<ReloadAction>)withSourceCascade.Concat(sinkRebindCascade).ToList();

        if (effectiveActions.Count == 0)
        {
            stopwatch.Stop();
            EnqueueCompletedOutcome(
                e.NewVersionId,
                applied: Array.Empty<string>(),
                restarted: Array.Empty<string>(),
                faulted: Array.Empty<FaultedReloadEntry>(),
                stopwatch.ElapsedMilliseconds);
            return;
        }

        var newConfig = e.NewConfiguration;

        var applied = new List<string>();
        var restarted = new List<string>();
        var faulted = new List<FaultedReloadEntry>();

        // K1.3 R4/R8 guard: REJECT an in-place hot-replace of a replay-aware sink. A Sink Restart swaps
        // the live adapter instance under a still-running route; a replay-aware sink owns that route's
        // protected replay cursor + live session, so it must NOT be hot-swapped. The supported path for
        // changing a replay sink is a new routeId (Route Remove + Add with a fresh sink id), which never
        // appears as a Restart of the existing sink. Rejected ids are faulted and EXCLUDED from the sink
        // teardown (A3) and bring-up (B2), so the old instance keeps running untouched. Full hot-replace
        // (the coordinator ↔ driver dance) is a deferred K2 follow-up. The set itself is computed above,
        // before the sink→route cascade, so no synthesized route ever depends on a rejected sink.
        foreach (var sinkId in rejectedReplaySinkReplacements)
        {
            const string code = "HOST.REPLAY_SINK_HOT_REPLACE_REJECTED";
            var message =
                $"Sink '{sinkId}' is replay-aware and cannot be hot-replaced in place; apply a replay-sink " +
                "change via a new sink identity and a new route id (the replay store is bound to both).";
            await RegisterAndAuditFaultAsync(ConfigurationFaultKind.Sink, sinkId, code, message).ConfigureAwait(false);
            faulted.Add(new FaultedReloadEntry
            {
                InstanceId = sinkId,
                Kind = ConfigurationFaultKind.Sink,
                ErrorCode = code,
                Message = message,
            });
        }

        // A rejected replay-sink hot-replace must be a DEPENDENCY-CONSISTENT rejection, not a partial
        // apply: every route Add/Restart that depends on a rejected sink is ALSO suppressed (excluded
        // from route teardown A1 + bring-up B3) and faulted — otherwise a Restart would end + recreate
        // its session behind the unchanged old sink, and an Add would bind a NEW route to the rejected
        // (still-live, old-config) sink. The invariant: rejecting a replay-sink hot-replace leaves BOTH
        // the live sink AND every dependent route (existing session, or a would-be new binding) unchanged.
        var rejectedReplayRouteReplacements =
            ComputeRoutesDependingOnRejectedSinks(rejectedReplaySinkReplacements, effectiveActions);
        foreach (var routeId in rejectedReplayRouteReplacements)
        {
            const string code = "HOST.REPLAY_ROUTE_DEPENDS_ON_REJECTED_SINK";
            var message =
                $"Route '{routeId}' was not added or restarted because it references a replay-aware sink " +
                "whose in-place hot-replace was rejected; apply a replay-sink change via a new sink identity " +
                "and a new route id.";
            await RegisterAndAuditFaultAsync(ConfigurationFaultKind.Route, routeId, code, message).ConfigureAwait(false);
            faulted.Add(new FaultedReloadEntry
            {
                InstanceId = routeId,
                Kind = ConfigurationFaultKind.Route,
                ErrorCode = code,
                Message = message,
            });
        }

        // ─── Phase A: teardown (routes → sources → sinks) ──────────

        // A1. Stop routes flagged by the plan (Remove or Restart teardown half).
        //
        // K1.3 A4: this teardown runs in the routes → sources → sinks order, so a replay-aware route's
        // EndSessionAsync completes (Core) BEFORE the coordinator stops/restarts its sink (A3/B2). The
        // end reason is threaded EXPLICITLY as ConfigurationReplaced — a config-driven teardown, never a
        // bare operator Stop — so a replay sink emits its death with the correct reason before re-begin.
        foreach (var action in EnumerateActions(effectiveActions, ConfigurationEntityKind.Route, teardown: true))
        {
            // A route depending on a rejected replay-sink hot-replace is left running unchanged — do NOT
            // tear it down (that would end + recreate its replay session behind a rejected sink change).
            if (rejectedReplayRouteReplacements.Contains(action.EntityId)) continue;

            var fault = await TryWithFaultAsync(ConfigurationFaultKind.Route, action.EntityId, async () =>
            {
                await _routingEngine
                    .UnregisterRouteAsync(action.EntityId, ReplaySessionEndReason.ConfigurationReplaced, ct)
                    .ConfigureAwait(false);
                _diagnostics.RemoveRoute(action.EntityId);
            }).ConfigureAwait(false);
            if (fault is not null) faulted.Add(fault);
        }

        // A2. Stop sources flagged by the plan (Remove or Restart teardown half).
        foreach (var action in EnumerateActions(effectiveActions, ConfigurationEntityKind.Source, teardown: true))
        {
            var fault = await TryWithFaultAsync(ConfigurationFaultKind.Source, action.EntityId, () =>
                _sourceSupervisor.RemoveAsync(action.EntityId, ct)).ConfigureAwait(false);
            if (fault is not null) faulted.Add(fault);
        }

        // A3. Stop sinks. Locked rule (§5.4): compute from new config —
        //     plan-driven Remove/Restart PLUS orphan cleanup (sinks
        //     supervised but not referenced by any enabled route in
        //     newConfig). Orphan cleanup is NOT a fault (§5.4.1):
        //     the supervisor.RemoveAsync happy path returns normally,
        //     TryWithFaultAsync registers no fault.
        var sinksToStop = ComputeSinksToStop(effectiveActions, newConfig);
        foreach (var sinkId in sinksToStop)
        {
            // A rejected replay-sink hot-replace leaves the live instance untouched (never torn down).
            if (rejectedReplaySinkReplacements.Contains(sinkId)) continue;

            var fault = await TryWithFaultAsync(ConfigurationFaultKind.Sink, sinkId, () =>
                _sinkSupervisor.RemoveAsync(sinkId, ct)).ConfigureAwait(false);
            if (fault is not null) faulted.Add(fault);
        }

        // ─── Phase B: bring-up (sources → sinks → routes) ──────────
        //
        // Enabled checks are hoisted out of the lambda body (phase 3 §5.2)
        // so the Applied/Restarted bookkeeping can skip disabled instances
        // — a disabled record SHOULDN'T appear in the reload outcome as
        // "Applied", since nothing actually came up.

        // B1. Start sources flagged by the plan (Add + Restart bring-up half).
        foreach (var action in EnumerateActions(effectiveActions, ConfigurationEntityKind.Source, teardown: false))
        {
            var src = (SourceInstanceConfig)action.NewConfig!;
            if (!src.Enabled) continue;

            var fault = await TryWithFaultAsync(ConfigurationFaultKind.Source, action.EntityId, async () =>
            {
                var reg = _registrationFactory.BuildSource(
                    src, newConfig.Gateway,
                    sourceId => ResolveRouteForSource(sourceId, newConfig),
                    _license, _faultRegistry, _serviceProvider);
                if (reg is null) return;
                await _sourceSupervisor.AddAsync(reg, ct).ConfigureAwait(false);
                _faultRegistry.ClearFor(ConfigurationFaultKind.Source, src.InstanceId);
            }).ConfigureAwait(false);

            if (fault is not null) faulted.Add(fault);
            else if (action.Op == ReloadOp.Add) applied.Add(action.EntityId);
            else if (action.Op == ReloadOp.Restart) restarted.Add(action.EntityId);
        }

        // B2. Start sinks flagged by the plan (Add + Restart bring-up half).
        foreach (var action in EnumerateActions(effectiveActions, ConfigurationEntityKind.Sink, teardown: false))
        {
            // A rejected replay-sink hot-replace is NOT re-added — the old instance was never torn down.
            if (rejectedReplaySinkReplacements.Contains(action.EntityId)) continue;

            var sink = (SinkInstanceConfig)action.NewConfig!;
            if (!sink.Enabled) continue;

            var fault = await TryWithFaultAsync(ConfigurationFaultKind.Sink, action.EntityId, async () =>
            {
                var reg = _registrationFactory.BuildSink(
                    sink, newConfig.Gateway,
                    sinkId => ResolveFirstRouteForSink(sinkId, newConfig),
                    _license, _faultRegistry, _serviceProvider);
                if (reg is null) return;
                await _sinkSupervisor.AddAsync(reg, ct).ConfigureAwait(false);
                _faultRegistry.ClearFor(ConfigurationFaultKind.Sink, sink.InstanceId);
            }).ConfigureAwait(false);

            if (fault is not null) faulted.Add(fault);
            else if (action.Op == ReloadOp.Add) applied.Add(action.EntityId);
            else if (action.Op == ReloadOp.Restart) restarted.Add(action.EntityId);
        }

        // B3. Bring up routes (Add + Restart bring-up half). Uses the
        //     phase-2.b RouteDefinitionFactory.BuildOne (pure extraction).
        var sinkLookup = BuildSinkLookup();
        foreach (var action in EnumerateActions(effectiveActions, ConfigurationEntityKind.Route, teardown: false))
        {
            // A route depending on a rejected replay-sink hot-replace is NOT brought up — a Restart was
            // never torn down (A1, keeps running on its existing definition), and an Add must not bind a
            // new route to the rejected (still-live, old-config) sink.
            if (rejectedReplayRouteReplacements.Contains(action.EntityId)) continue;

            var rc = (RouteConfig)action.NewConfig!;
            if (!rc.Enabled) continue;

            var fault = await TryWithFaultAsync(ConfigurationFaultKind.Route, action.EntityId, async () =>
            {
                var def = _routeDefFactory.BuildOne(rc, newConfig.Gateway,
                    _sourceSupervisor, sinkLookup, _faultRegistry);
                if (def is null) return;
                _diagnostics.EnsureRoute(rc.RouteId);
                await _routingEngine.RegisterRouteAsync(def, ct).ConfigureAwait(false);
                await _routingEngine.StartRouteAsync(rc.RouteId, ct).ConfigureAwait(false);
                _faultRegistry.ClearFor(ConfigurationFaultKind.Route, rc.RouteId);
            }).ConfigureAwait(false);

            if (fault is not null)
            {
                faulted.Add(fault);
            }
            else
            {
                // The route's diagnostics subtree was DELETED by the A1 teardown
                // (_diagnostics.RemoveRoute drops source + sink state with it). Endpoints
                // that this reload left running never push state again on their own — the
                // supervisors only report on Initialize/Start/Stop — so without this the
                // rebuilt route carries a null sink AdapterState (Studio: a destination row
                // with no status pill, or "No destinations attached" when nothing else ever
                // created the entry) and a source stuck at the Created default (Studio:
                // "source is created, no readings arriving" next to a live points counter).
                RepublishEndpointStatesForRoute(rc, sinkLookup);

                if (action.Op == ReloadOp.Add) applied.Add(action.EntityId);
                else if (action.Op == ReloadOp.Restart) restarted.Add(action.EntityId);
            }
        }

        stopwatch.Stop();
        EnqueueCompletedOutcome(
            e.NewVersionId,
            applied,
            restarted,
            faulted,
            stopwatch.ElapsedMilliseconds);
    }

    // ReloadOutcome fault entries are best-effort reconcile observations only.
    // The authoritative fault surface remains IConfigurationFaultRegistry —
    // faults registered by BuildSource/BuildSink/BuildOne returning null
    // without throwing are NOT captured here (they go straight to the
    // registry and surface via /diagnostics/configuration-faults).
    //
    // Outcome enqueue must remain non-blocking and in-memory only — do not
    // introduce await / storage / observer semantics here (guardrails K-N,
    // M.P2.2 phase 3 plan v2 §2). If this method ever grows an async call,
    // the reconcile semaphore's latency budget is contaminated.
    private void EnqueueCompletedOutcome(
        ConfigurationVersionId versionId,
        IReadOnlyList<string> applied,
        IReadOnlyList<string> restarted,
        IReadOnlyList<FaultedReloadEntry> faulted,
        long elapsedMs)
    {
        if (_outcomeRegistry is null) return;

        _outcomeRegistry.EnqueueCompleted(new ReloadOutcome
        {
            Status = ReloadStatus.Completed,
            NewVersionId = versionId,
            AppliedInstances = applied,
            RestartedInstances = restarted,
            FaultedInstances = faulted,
            ElapsedMs = elapsedMs,
        });
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Filter a flat action list to the entries for one entity kind, on
    /// either the teardown half (Remove + Restart) or the bring-up half
    /// (Add + Restart). Takes a raw list rather than a
    /// <see cref="ConfigurationReloadPlan"/> so the M.P2.3 recovery-
    /// synthesis pass can pass an <c>effectiveActions</c> = classifier
    /// + synthesized list without mutating the pristine
    /// <see cref="ConfigurationReloadPlan.Actions"/>.
    /// </summary>
    private static IEnumerable<ReloadAction> EnumerateActions(
        IReadOnlyList<ReloadAction> actions,
        ConfigurationEntityKind kind,
        bool teardown)
    {
        foreach (var a in actions)
        {
            if (a.Kind != kind) continue;
            var match = teardown
                ? a.Op is ReloadOp.Remove or ReloadOp.Restart
                : a.Op is ReloadOp.Add or ReloadOp.Restart;
            if (match) yield return a;
        }
    }

    /// <summary>
    /// K1.3 R4/R8: the set of sink ids whose in-place hot-replace (a Sink <see cref="ReloadOp.Restart"/>)
    /// must be REJECTED because EITHER the currently-live adapter OR the incoming configuration is
    /// replay-aware. A replay-aware sink owns its route's protected replay cursor + live session and
    /// cannot be swapped under a running route; and an ordinary→replay-aware in-place swap would put a
    /// replay-aware sink under a route registered with no replay-store activation / tracked intake /
    /// driver — a silent mode switch. Either direction is corruption. The supported path is a new routeId
    /// (Route Remove + Add with a fresh sink id), which never surfaces as a Restart of the existing sink.
    /// The reload leaves the live instance untouched and faults the id.
    /// </summary>
    private HashSet<string> ComputeRejectedReplaySinkHotReplacements(IReadOnlyList<ReloadAction> actions)
    {
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in actions)
        {
            if (a.Kind == ConfigurationEntityKind.Sink && a.Op == ReloadOp.Restart && MustRejectReplaySinkRestart(a))
            {
                rejected.Add(a.EntityId);
            }
        }

        return rejected;
    }

    /// <summary>
    /// K1.3 R4/R8 (dependency-consistent rejection): the set of route ids being ADDED or RESTARTED whose
    /// new definition references a rejected replay-sink hot-replace. These routes are suppressed from
    /// route teardown (A1, Restart only) + bring-up (B3, Add + Restart) and faulted, so a rejected sink
    /// change never becomes a partial apply — neither ending + recreating an existing route's replay
    /// session behind an unchanged live sink (Restart), nor binding a NEW route to the rejected (still-
    /// live, old-config) sink (Add). A Remove is a deliberate teardown and is not suppressed. Empty when
    /// nothing was rejected.
    /// </summary>
    private static HashSet<string> ComputeRoutesDependingOnRejectedSinks(
        HashSet<string> rejectedSinks, IReadOnlyList<ReloadAction> actions)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (rejectedSinks.Count == 0)
        {
            return result;
        }

        foreach (var a in actions)
        {
            if (a.Kind != ConfigurationEntityKind.Route || a.Op is not (ReloadOp.Add or ReloadOp.Restart))
            {
                continue;
            }

            if (a.NewConfig is RouteConfig route && route.SinkInstanceIds.Any(rejectedSinks.Contains))
            {
                result.Add(a.EntityId);
            }
        }

        return result;
    }

    /// <summary>Reject an in-place Sink Restart when the live OR the incoming adapter is replay-aware.</summary>
    private bool MustRejectReplaySinkRestart(ReloadAction action)
    {
        if (LiveSinkIsReplayAware(action.EntityId))
        {
            return true;
        }

        // Incoming side: classify from config alone (no instantiation). Inert in K1.3 (no classifier
        // registered until K2's Sparkplug sink); the live-side check above still fires.
        return _replayClassifier is not null
            && action.NewConfig is SinkInstanceConfig incoming
            && _replayClassifier.IsReplayAware(incoming);
    }

    /// <summary>True when the sink instance currently supervised under <paramref name="sinkInstanceId"/>
    /// is a replay-aware adapter (<see cref="IReplayAwareSinkAdapter"/>).</summary>
    private bool LiveSinkIsReplayAware(string sinkInstanceId)
    {
        foreach (var reg in _sinkSupervisor.Registrations)
        {
            if (string.Equals(reg.Adapter.InstanceId, sinkInstanceId, StringComparison.Ordinal))
            {
                return reg.Adapter is IReplayAwareSinkAdapter;
            }
        }

        return false;
    }

    /// <summary>
    /// Locked rule (§5.4): compute the set of sinks to stop as
    ///   (plan-driven Remove + Restart teardown halves)
    /// ∪ (sinks currently supervised but not referenced by any enabled
    ///   route in the new configuration — orphan cleanup)
    /// </summary>
    private List<string> ComputeSinksToStop(
        IReadOnlyList<ReloadAction> actions,
        GatewayConfiguration newConfig)
    {
        // (1) + (2) — plan-driven Remove/Restart.
        var planSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in actions)
        {
            if (a.Kind == ConfigurationEntityKind.Sink
                && a.Op is ReloadOp.Remove or ReloadOp.Restart)
            {
                planSet.Add(a.EntityId);
            }
        }

        // (3) — orphan cleanup. Build the set of sink ids still
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

        // Result = plan-driven sinks + supervised sinks not in
        // stillReferenced. Dedup so we never double-stop.
        var result = new List<string>(planSet);
        foreach (var reg in _sinkSupervisor.Registrations)
        {
            var id = reg.Adapter.InstanceId;
            if (stillReferenced.Contains(id)) continue;
            if (planSet.Contains(id)) continue;
            result.Add(id);
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────
    // M.P2.3 — Synthesize recovery Add actions for entities skipped from
    // the supervisor at gateway startup by M.P2.1 fail-soft cross-record
    // validation. See ADR-0010.
    //
    // Locked invariants (M.P2.3 plan v2 §3):
    //   H: pristine plan.Actions is never mutated. The synthesized list
    //      is returned as a separate IReadOnlyList; the caller combines
    //      via effectiveActions = plan.Actions ∪ synthesized.
    //   I: synthesis precondition includes "not already active in the
    //      relevant runtime registry/supervisor". Routes live in
    //      _routingEngine, sources in _sourceSupervisor, sinks in
    //      _sinkSupervisor — each gets its own membership check.
    //
    // Scope (M.P2.3 plan §4 Q1 + reality-check 2026-05-17):
    //   CONFIG.SOURCE_WITHOUT_ROUTE
    //   CONFIG.SINK_WITHOUT_ROUTE
    //   CONFIG.ROUTE_REFERENCES_MISSING_SOURCE
    //   CONFIG.ROUTE_REFERENCES_MISSING_SINK
    // ─────────────────────────────────────────────────────────────────

    private List<ReloadAction> ComputeStartupSkipRecoveryActions(
        IReadOnlyList<ReloadAction> classifierActions,
        GatewayConfiguration newConfig)
    {
        // Dedup precondition #5: classifier action already targets this
        // (kind, entityId) → skip synthesis. The classifier's intent wins.
        var classifierKeys = new HashSet<(ConfigurationEntityKind Kind, string EntityId)>();
        foreach (var a in classifierActions)
        {
            classifierKeys.Add((a.Kind, a.EntityId));
        }

        var synthesized = new List<ReloadAction>();
        foreach (var fault in _faultRegistry.GetFaults())
        {
            // ConfigurationFaultKind and ConfigurationEntityKind are
            // distinct enums (different namespaces, different numeric
            // values) — map before dedup.
            if (classifierKeys.Contains((MapFaultKindToEntityKind(fault.Kind), fault.InstanceId)))
            {
                continue;
            }

            var action = TrySynthesizeRecoveryAction(fault, newConfig);
            if (action is not null)
            {
                synthesized.Add(action);
            }
        }
        return synthesized;
    }

    // ─────────────────────────────────────────────────────────────────
    // M.P2.4 — Cascade route rebinds when a source is Restarted.
    //
    // SourceSupervisor teardown+Add (the reload's source Restart) creates a
    // BRAND-NEW intake channel + ISourceIntake for the source. A route bound
    // to that source via RouteDefinition.Source captured the OLD channel
    // reader when it was registered. If the route is not itself rebuilt, its
    // intake pump stays parked in WaitToReadAsync on the old (now-completed)
    // channel while the new channel fills to capacity and back-pressures the
    // source poll loop to a standstill — the route shows Running but
    // pipeline.pointsIn freezes at 0 and nothing is enqueued. The classifier
    // cannot emit a Route action because the route's own config text is
    // unchanged, so the coordinator synthesizes a Route Restart, mirroring
    // ComputeStartupSkipRecoveryActions.
    // ─────────────────────────────────────────────────────────────────
    private List<ReloadAction> ComputeSourceRestartRouteRebindActions(
        IReadOnlyList<ReloadAction> priorActions,
        GatewayConfiguration newConfig)
    {
        // Sources whose intake channel is being recreated by this reload.
        var restartedSources = new HashSet<string>(StringComparer.Ordinal);
        // Routes already targeted by a prior action — classifier / recovery
        // intent wins (dedup precondition #5). A prior Restart already rebuilds
        // the route; a prior Remove intentionally tears it down — never
        // resurrect it.
        var priorRouteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in priorActions)
        {
            if (a.Kind == ConfigurationEntityKind.Source && a.Op == ReloadOp.Restart)
            {
                restartedSources.Add(a.EntityId);
            }
            else if (a.Kind == ConfigurationEntityKind.Route)
            {
                priorRouteIds.Add(a.EntityId);
            }
        }

        if (restartedSources.Count == 0)
        {
            return new List<ReloadAction>();
        }

        var cascaded = new List<ReloadAction>();
        foreach (var route in newConfig.Routes)
        {
            if (!route.Enabled) continue;                                           // #3
            if (!restartedSources.Contains(route.SourceInstanceId)) continue;        // not bound to a restarted source
            if (priorRouteIds.Contains(route.RouteId)) continue;                     // #5 — prior intent wins
            if (!IsRouteCrossRecordValidNow(route, newConfig)) continue;             // #2
            // #4 — only a route that is actually running has a stale binding to
            // rebind. An unregistered route has no live worker; its own Add path
            // (classifier or startup-skip recovery) brings it up correctly.
            if (!_routingEngine.RegisteredRouteIds.Contains(route.RouteId)) continue;

            cascaded.Add(new ReloadAction
            {
                Op = ReloadOp.Restart,
                Kind = ConfigurationEntityKind.Route,
                EntityId = route.RouteId,
                NewConfig = route,
            });
        }
        return cascaded;
    }

    // ─────────────────────────────────────────────────────────────────
    // Cascade route rebinds when a SINK is Restarted or Added.
    //
    // The mirror of the M.P2.4 source pass, and it closes the same class of
    // bug from the other side. RouteDefinition.Sinks holds ISinkAdapter
    // INSTANCES, and RouteWorker builds one SinkPublisher per captured
    // instance when the worker starts — nothing ever re-resolves the sink id.
    // A sink Restart (A3 stop + dispose, B2 build + start) therefore leaves
    // the running route publishing into a DISPOSED adapter while the brand-new
    // instance is bound to no route at all. A source edit already cascaded a
    // route rebind; a destination edit did not.
    //
    // Same preconditions as the source pass (#2 cross-record validity, #3
    // enabled, #4 actually registered, #5 prior intent wins), plus one that is
    // specific to this side: a sink whose in-place hot-replace is REJECTED
    // (K1.3 R4/R8) is EXCLUDED. Its live instance is deliberately left
    // untouched, so its routes have nothing to rebind — and synthesizing for
    // them would only feed ComputeRoutesDependingOnRejectedSinks a route to
    // fault that the operator never edited.
    // ─────────────────────────────────────────────────────────────────
    private List<ReloadAction> ComputeSinkRestartRouteRebindActions(
        IReadOnlyList<ReloadAction> priorActions,
        GatewayConfiguration newConfig,
        HashSet<string> rejectedReplaySinkReplacements)
    {
        // Sinks whose adapter instance this reload replaces (Restart) or
        // introduces (Add). Either way a route bound to the id is holding a
        // binding that is stale or absent.
        var rebuiltSinks = new HashSet<string>(StringComparer.Ordinal);
        var priorRouteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in priorActions)
        {
            if (a.Kind == ConfigurationEntityKind.Sink && a.Op is ReloadOp.Restart or ReloadOp.Add)
            {
                // The rejected sink keeps its ORIGINAL live instance — no rebind is owed.
                if (rejectedReplaySinkReplacements.Contains(a.EntityId)) continue;
                rebuiltSinks.Add(a.EntityId);
            }
            else if (a.Kind == ConfigurationEntityKind.Route)
            {
                priorRouteIds.Add(a.EntityId);
            }
        }

        if (rebuiltSinks.Count == 0)
        {
            return new List<ReloadAction>();
        }

        var cascaded = new List<ReloadAction>();
        foreach (var route in newConfig.Routes)
        {
            if (!route.Enabled) continue;                                            // #3
            if (!route.SinkInstanceIds.Any(rebuiltSinks.Contains)) continue;         // no stale sink binding
            if (priorRouteIds.Contains(route.RouteId)) continue;                     // #5 — prior intent wins
            if (!IsRouteCrossRecordValidNow(route, newConfig)) continue;             // #2
            // #4 — only a registered route has a live worker holding a stale
            // instance. An unregistered route is brought up by its own Add path.
            if (!_routingEngine.RegisteredRouteIds.Contains(route.RouteId)) continue;

            cascaded.Add(new ReloadAction
            {
                Op = ReloadOp.Restart,
                Kind = ConfigurationEntityKind.Route,
                EntityId = route.RouteId,
                NewConfig = route,
            });
        }
        return cascaded;
    }

    private static ConfigurationEntityKind MapFaultKindToEntityKind(ConfigurationFaultKind kind) =>
        kind switch
        {
            ConfigurationFaultKind.Source => ConfigurationEntityKind.Source,
            ConfigurationFaultKind.Sink => ConfigurationEntityKind.Sink,
            ConfigurationFaultKind.Route => ConfigurationEntityKind.Route,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped ConfigurationFaultKind."),
        };

    private ReloadAction? TrySynthesizeRecoveryAction(
        ConfigurationFault fault,
        GatewayConfiguration newConfig)
    {
        return (fault.Kind, fault.ErrorCode) switch
        {
            (ConfigurationFaultKind.Source, "CONFIG.SOURCE_WITHOUT_ROUTE")
                => TrySynthesizeSourceAdd(fault.InstanceId, newConfig),
            (ConfigurationFaultKind.Sink, "CONFIG.SINK_WITHOUT_ROUTE")
                => TrySynthesizeSinkAdd(fault.InstanceId, newConfig),
            (ConfigurationFaultKind.Route, "CONFIG.ROUTE_REFERENCES_MISSING_SOURCE") or
            (ConfigurationFaultKind.Route, "CONFIG.ROUTE_REFERENCES_MISSING_SINK")
                => TrySynthesizeRouteAdd(fault.InstanceId, newConfig),
            _ => null,
        };
    }

    private ReloadAction? TrySynthesizeSourceAdd(string instanceId, GatewayConfiguration newConfig)
    {
        var src = FindSource(instanceId, newConfig);
        if (src is null || !src.Enabled) return null;                                  // #3
        if (!IsSourceCrossRecordValidNow(instanceId, newConfig)) return null;           // #2
        if (_sourceSupervisor.SourceInstanceIds.Contains(instanceId)) return null;      // #4
        return new ReloadAction
        {
            Op = ReloadOp.Add,
            Kind = ConfigurationEntityKind.Source,
            EntityId = instanceId,
            NewConfig = src,
        };
    }

    private ReloadAction? TrySynthesizeSinkAdd(string instanceId, GatewayConfiguration newConfig)
    {
        var sink = FindSink(instanceId, newConfig);
        if (sink is null || !sink.Enabled) return null;                                 // #3
        if (!IsSinkCrossRecordValidNow(instanceId, newConfig)) return null;             // #2
        if (_sinkSupervisor.Registrations.Any(r =>
                string.Equals(r.Adapter.InstanceId, instanceId, StringComparison.Ordinal)))
        {
            return null;                                                                // #4
        }
        return new ReloadAction
        {
            Op = ReloadOp.Add,
            Kind = ConfigurationEntityKind.Sink,
            EntityId = instanceId,
            NewConfig = sink,
        };
    }

    private ReloadAction? TrySynthesizeRouteAdd(string routeId, GatewayConfiguration newConfig)
    {
        var route = FindRoute(routeId, newConfig);
        if (route is null || !route.Enabled) return null;                               // #3
        if (!IsRouteCrossRecordValidNow(route, newConfig)) return null;                 // #2
        if (_routingEngine.RegisteredRouteIds.Contains(routeId)) return null;           // #4
        return new ReloadAction
        {
            Op = ReloadOp.Add,
            Kind = ConfigurationEntityKind.Route,
            EntityId = routeId,
            NewConfig = route,
        };
    }

    private static SourceInstanceConfig? FindSource(string id, GatewayConfiguration cfg)
    {
        foreach (var s in cfg.Sources)
        {
            if (string.Equals(s.InstanceId, id, StringComparison.Ordinal)) return s;
        }
        return null;
    }

    private static SinkInstanceConfig? FindSink(string id, GatewayConfiguration cfg)
    {
        foreach (var s in cfg.Sinks)
        {
            if (string.Equals(s.InstanceId, id, StringComparison.Ordinal)) return s;
        }
        return null;
    }

    private static RouteConfig? FindRoute(string id, GatewayConfiguration cfg)
    {
        foreach (var r in cfg.Routes)
        {
            if (string.Equals(r.RouteId, id, StringComparison.Ordinal)) return r;
        }
        return null;
    }

    /// <summary>
    /// Cross-record validity for a source: at least one enabled route in
    /// <paramref name="newConfig"/> references it via <c>SourceInstanceId</c>.
    /// </summary>
    private static bool IsSourceCrossRecordValidNow(string sourceInstanceId, GatewayConfiguration newConfig)
    {
        foreach (var r in newConfig.Routes)
        {
            if (r.Enabled && string.Equals(r.SourceInstanceId, sourceInstanceId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Cross-record validity for a sink: at least one enabled route in
    /// <paramref name="newConfig"/> includes it in <c>SinkInstanceIds</c>.
    /// </summary>
    private static bool IsSinkCrossRecordValidNow(string sinkInstanceId, GatewayConfiguration newConfig)
    {
        foreach (var r in newConfig.Routes)
        {
            if (!r.Enabled) continue;
            foreach (var sid in r.SinkInstanceIds)
            {
                if (string.Equals(sid, sinkInstanceId, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Cross-record validity for a route: the referenced source AND all
    /// referenced sinks exist and are <c>Enabled</c> in
    /// <paramref name="newConfig"/>. The supervisor population check is
    /// the coordinator's job during B1/B2; this predicate only validates
    /// the config-level shape.
    /// </summary>
    private static bool IsRouteCrossRecordValidNow(RouteConfig route, GatewayConfiguration newConfig)
    {
        var src = FindSource(route.SourceInstanceId, newConfig);
        if (src is null || !src.Enabled) return false;
        foreach (var sinkId in route.SinkInstanceIds)
        {
            var sink = FindSink(sinkId, newConfig);
            if (sink is null || !sink.Enabled) return false;
        }
        return true;
    }

    private static string? ResolveRouteForSource(
        string sourceInstanceId,
        GatewayConfiguration newConfig)
    {
        foreach (var route in newConfig.Routes)
        {
            if (route.Enabled
                && string.Equals(route.SourceInstanceId, sourceInstanceId, StringComparison.Ordinal))
            {
                return route.RouteId;
            }
        }
        return null;
    }

    private static string? ResolveFirstRouteForSink(
        string sinkInstanceId,
        GatewayConfiguration newConfig)
    {
        foreach (var route in newConfig.Routes)
        {
            if (route.Enabled
                && route.SinkInstanceIds.Contains(sinkInstanceId, StringComparer.Ordinal))
            {
                return route.RouteId;
            }
        }
        return null;
    }

    /// <summary>
    /// Re-push the LIVE adapter state of a route's source and every one of its
    /// destinations after the reload has brought that route back up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Route teardown (A1) calls <c>RuntimeDiagnosticsCollector.RemoveRoute</c>,
    /// which drops the whole per-route subtree — the source entry and every sink
    /// entry go with it. The supervisors only push state on Initialize / Start /
    /// Stop boundaries, so an endpoint this reload correctly LEFT RUNNING (a
    /// destination during a source-only edit, a source during a destination edit)
    /// never publishes again and the rebuilt route reports it forever as a null
    /// sink AdapterState or a source defaulted to <c>Created</c>. The collector is
    /// the single state store, so the only honest repair is to republish what the
    /// adapters themselves currently report.
    /// </para>
    /// <para>
    /// State is read from the adapter's own <c>State</c> property — no
    /// <c>CheckHealthAsync</c>, no device I/O — because this runs inside the
    /// reconcile gate and must stay cheap and non-blocking. <c>lastError</c> is
    /// passed as null: this is a state republish, not an error observation, and
    /// the collector's own rules decide what that does to a stale error (a sink
    /// keeps it; a Running source clears it, which is the truthful reading of a
    /// source the supervisor still has running).
    /// </para>
    /// <para>
    /// Locked decision #10 (per-adapter isolation): a diagnostics republish must
    /// never be able to fail a reload. Everything here is best-effort and
    /// contained per route.
    /// </para>
    /// </remarks>
    private void RepublishEndpointStatesForRoute(
        RouteConfig route,
        Dictionary<string, ISinkAdapter> sinkLookup)
    {
        // BuildOne returns null (missing source / missing sink, already faulted) without
        // throwing, so a fault-free bring-up does NOT prove the route came up. Republishing
        // for a route that never registered would resurrect the diagnostics subtree the
        // teardown just dropped, for a route with no worker behind it.
        if (!_routingEngine.RegisteredRouteIds.Contains(route.RouteId))
        {
            return;
        }

        try
        {
            // Source side. GetAdapter returns null when the source is not
            // supervised — nothing live to report, and the route would not have
            // built at all in that case.
            var sourceAdapter = _sourceSupervisor.GetAdapter(route.SourceInstanceId);
            if (sourceAdapter is not null)
            {
                _diagnostics.RecordSourceState(
                    route.RouteId,
                    sourceAdapter.InstanceId,
                    sourceAdapter.ProtocolName,
                    sourceAdapter.State,
                    lastError: null);
            }

            // Destination side. Every sink the route binds, not just the ones this
            // reload touched — the wiped subtree took all of them.
            foreach (var sinkId in route.SinkInstanceIds)
            {
                if (!sinkLookup.TryGetValue(sinkId, out var sinkAdapter)) continue;
                _diagnostics.RecordSinkAdapterState(
                    route.RouteId,
                    sinkAdapter.InstanceId,
                    sinkAdapter.State,
                    lastError: null);
            }
        }
        catch (Exception ex)
        {
            // Diagnostics are observational: a misbehaving adapter's State getter
            // must not fault the route it belongs to, nor stop the reload loop.
            _logger.LogWarning(ex,
                "RuntimeReloadCoordinator: could not republish endpoint diagnostics state for route {Route}. " +
                "The route is running; its Studio status pills may lag until the next adapter transition.",
                route.RouteId);
        }
    }

    private Dictionary<string, ISinkAdapter> BuildSinkLookup()
    {
        var lookup = new Dictionary<string, ISinkAdapter>(StringComparer.Ordinal);
        foreach (var reg in _sinkSupervisor.Registrations)
        {
            lookup[reg.Adapter.InstanceId] = reg.Adapter;
        }
        return lookup;
    }

    /// <summary>
    /// Per-instance fault-on-throw wrapper. Bounds each step at
    /// <see cref="PerInstanceTimeoutMs"/>. Translates exceptions to
    /// fault entries via <see cref="RegisterAndAuditFaultAsync"/>.
    /// Never lets exceptions escape — fail-soft is unconditional
    /// (ADR-0004).
    /// </summary>
    /// <returns>
    /// <c>null</c> when the wrapped action completed without throwing
    /// (the "success" path). A <see cref="FaultedReloadEntry"/> when
    /// the action threw and the coordinator caught + audited the fault
    /// (the "swallowed" path) — the caller uses this to populate the
    /// M.P2.2 phase 3 <see cref="ReloadOutcome.FaultedInstances"/>
    /// list. The fault is also registered in <see cref="_faultRegistry"/>
    /// and appended to the audit chain regardless of whether the caller
    /// inspects the return value.
    /// </returns>
    private async Task<FaultedReloadEntry?> TryWithFaultAsync(
        ConfigurationFaultKind kind,
        string entityId,
        Func<Task> action)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
            cts.CancelAfter(TimeSpan.FromMilliseconds(PerInstanceTimeoutMs));
            await action().ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            throw;  // bubble shutdown
        }
        catch (OperationCanceledException)
        {
            const string code = "HOST.RECONCILE_TIMEOUT";
            var message = $"Reconcile step exceeded {PerInstanceTimeoutMs}ms; instance left in last-known state.";
            await RegisterAndAuditFaultAsync(kind, entityId, code, message).ConfigureAwait(false);
            return new FaultedReloadEntry { InstanceId = entityId, Kind = kind, ErrorCode = code, Message = message };
        }
        catch (Exception ex)
        {
            const string code = "HOST.RECONCILE_FAILED";
            await RegisterAndAuditFaultAsync(kind, entityId, code, ex.Message).ConfigureAwait(false);
            return new FaultedReloadEntry { InstanceId = entityId, Kind = kind, ErrorCode = code, Message = ex.Message };
        }
    }

    /// <summary>
    /// Register a fault in the live registry AND append a durable
    /// audit-chain entry. The append is AWAITED (correction #1 from
    /// Phase 2 design v2). If the audit append itself throws, log
    /// Critical with full fault detail and continue; do NOT
    /// re-register (would loop).
    /// </summary>
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

        try
        {
            await _configManager.AppendRuntimeFaultAsync(fault, _shutdownCts.Token)
                .ConfigureAwait(false);
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

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        Unsubscribe();
        _shutdownCts.Cancel();

        // Bounded drain. Wait at most DisposeDrainTimeoutMs for the
        // in-flight reconcile to release the semaphore.
        var acquired = await _reconcileSemaphore.WaitAsync(DisposeDrainTimeoutMs).ConfigureAwait(false);
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
}
