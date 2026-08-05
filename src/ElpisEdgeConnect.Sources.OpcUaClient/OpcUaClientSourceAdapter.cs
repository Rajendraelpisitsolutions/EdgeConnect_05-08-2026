// ============================================================================
// File: OpcUaClientSourceAdapter.cs
// Purpose: ISourceAdapter implementation for OPC UA Client — subscribes to
//          an upstream OPC UA Server (FactoryTalk SCADA, Kepware, vendor
//          PLCs, future Aveva PI Server). Emits CanonicalDataPoints into
//          the routing pipeline via the standard adapter contract.
//
//          THIS IS PR 1 of the OPC UA Client adapter series (per plan
//          v2.1 §5.1). The adapter shell + lifecycle + ValidateConfigAsync
//          land here; subsequent PRs add:
//             PR 2 — Session lifecycle + cert manager
//             PR 3 — Subscription factory + monitored items
//             PR 4 — Type mapper + notification dispatcher
//             PR 5 — Browse service (consumes ITagBrowseService contract)
//             PR 6 — Reconnect coordinator + ReconfigureAsync override
//
//          Each PR is mergeable on its own (existing tests + new tests
//          pass; build clean); the adapter degrades gracefully in
//          intermediate states (e.g., PR 1's SubscribeAsync returns an
//          empty stream because no Session is opened yet).
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §5.1
//            docs/decisions/0015-wizard-contract.md (Layer 5 — Rule 11)
//            docs/ARCHITECTURE_BLUEPRINT.md §4.2 (ISourceAdapter)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Subscription-mode source adapter for OPC UA Servers via the OPC
/// Foundation .NET Standard stack.
/// </summary>
public sealed class OpcUaClientSourceAdapter : ISourceAdapter, Core.Adapters.Retirement.ISourceRetirement
{
    private readonly object _stateLock = new();
    private readonly ILogger _logger;
    private readonly IOpcUaClientConnectionEstablisher _connectionEstablisher;
    private readonly IOpcUaClientSubscriptionFactory _subscriptionFactory;
    private readonly Func<int, OpcUaTypeMapper, ILogger, INotificationDispatcher>? _dispatcherFactory;
    private readonly Func<ILogger, IOpcUaReconnectHandlerWrapper>? _reconnectWrapperFactory;
    private readonly Func<ILogger, IOpcUaReconfigureExecutor>? _reconfigureExecutorFactory;
    private readonly NotificationQueueDepthProbe _queueDepthProbe = new();

    private OpcUaClientSourceConfiguration? _config;
    private ISession? _session;
    private IReadOnlyList<Subscription>? _subscriptions;
    private INotificationDispatcher? _dispatcher;
    private OpcUaReconnectCoordinator? _coordinator;
    private Action<CoordinatorStateChange>? _coordinatorStateHandler;

    // Slice 0 commit 3.0 retirement — durable, idempotent, cached under _stateLock.
    private Core.Adapters.Retirement.AdapterRetirementOperation? _retirement;

    private AdapterError? _lastError;
    private DateTime? _lastSuccessAtUtc;
    private string _gatewayId = "edgeconnect-unknown-gateway";

    // PR 6b — reconfigure single-flight guard + counters
    private int _currentlyReconfiguring;
    private long _reconfigureCount;
    private DateTime? _lastReconfigureUtc;
    private int _lastReconfigureChangeCount;

    /// <inheritdoc/>
    public string InstanceId { get; }

    /// <inheritdoc/>
    public string ProtocolName => OpcUaClientSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc/>
    /// <remarks>
    /// OPC UA Client supports Subscription (native OPC UA mechanism),
    /// Browse (UA <c>Browse</c> + <c>BrowseNext</c> via the
    /// per-protocol <c>ITagBrowseService</c> implementation — PR 5),
    /// Discovery (server's address space is the discovery surface),
    /// Quality (UA notifications carry <c>StatusCode</c> natively), and
    /// TestConnect (read-only session create + close — PR 2). Polling
    /// is NOT supported by design — subscription is the protocol's
    /// idiom and the platform's hot path optimisation target.
    /// </remarks>
    public SourceCapabilities Capabilities =>
        SourceCapabilities.Subscription
        | SourceCapabilities.Browse
        | SourceCapabilities.Discovery
        | SourceCapabilities.Quality
        | SourceCapabilities.TestConnect;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    /// <summary>
    /// Construct an OPC UA Client source adapter with the production
    /// connection establisher and subscription factory.
    /// </summary>
    public OpcUaClientSourceAdapter(string instanceId, ILogger<OpcUaClientSourceAdapter> logger)
        : this(instanceId, logger, new DefaultOpcUaClientConnectionEstablisher(), new DefaultOpcUaClientSubscriptionFactory(logger))
    {
    }

    /// <summary>
    /// Construct using a non-generic logger with production seams
    /// (test convenience for non-Session-touching tests).
    /// </summary>
    internal OpcUaClientSourceAdapter(string instanceId, ILogger logger)
        : this(instanceId, logger, new DefaultOpcUaClientConnectionEstablisher(), new DefaultOpcUaClientSubscriptionFactory(logger))
    {
    }

    /// <summary>
    /// Construct using a non-generic logger and an explicit connection
    /// establisher (test convenience for Session-touching tests; the
    /// subscription factory defaults to the production impl).
    /// </summary>
    internal OpcUaClientSourceAdapter(
        string instanceId,
        ILogger logger,
        IOpcUaClientConnectionEstablisher connectionEstablisher)
        : this(instanceId, logger, connectionEstablisher, new DefaultOpcUaClientSubscriptionFactory(logger))
    {
    }

    /// <summary>
    /// Construct with explicit seams for both connection establishment
    /// AND subscription creation. Test convenience.
    /// </summary>
    internal OpcUaClientSourceAdapter(
        string instanceId,
        ILogger logger,
        IOpcUaClientConnectionEstablisher connectionEstablisher,
        IOpcUaClientSubscriptionFactory subscriptionFactory)
        : this(instanceId, logger, connectionEstablisher, subscriptionFactory, dispatcherFactory: null)
    {
    }

    /// <summary>
    /// Construct with explicit seams for connection, subscription, AND
    /// notification dispatch. Test convenience — passes a substituted
    /// dispatcher (e.g., from NSubstitute) via the
    /// <paramref name="dispatcherFactory"/>. Production code goes through
    /// the overload that defaults to the real
    /// <see cref="NotificationDispatcher"/>.
    /// </summary>
    internal OpcUaClientSourceAdapter(
        string instanceId,
        ILogger logger,
        IOpcUaClientConnectionEstablisher connectionEstablisher,
        IOpcUaClientSubscriptionFactory subscriptionFactory,
        Func<int, OpcUaTypeMapper, ILogger, INotificationDispatcher>? dispatcherFactory)
        : this(instanceId, logger, connectionEstablisher, subscriptionFactory, dispatcherFactory, reconnectWrapperFactory: null)
    {
    }

    /// <summary>
    /// Construct with explicit seams for connection, subscription,
    /// notification dispatch, AND reconnect handling. Test convenience —
    /// passes a substituted reconnect wrapper (e.g., from NSubstitute)
    /// via the <paramref name="reconnectWrapperFactory"/>. Production code
    /// goes through the overload that defaults to the real
    /// <see cref="DefaultOpcUaReconnectHandlerWrapper"/>.
    /// </summary>
    internal OpcUaClientSourceAdapter(
        string instanceId,
        ILogger logger,
        IOpcUaClientConnectionEstablisher connectionEstablisher,
        IOpcUaClientSubscriptionFactory subscriptionFactory,
        Func<int, OpcUaTypeMapper, ILogger, INotificationDispatcher>? dispatcherFactory,
        Func<ILogger, IOpcUaReconnectHandlerWrapper>? reconnectWrapperFactory)
        : this(instanceId, logger, connectionEstablisher, subscriptionFactory, dispatcherFactory, reconnectWrapperFactory, reconfigureExecutorFactory: null)
    {
    }

    /// <summary>
    /// Construct with the full seam set including the hot-reconfigure
    /// executor (PR 6b). Test convenience — production code uses the
    /// default-constructor overload, which defaults to
    /// <see cref="DefaultOpcUaReconfigureExecutor"/>.
    /// </summary>
    internal OpcUaClientSourceAdapter(
        string instanceId,
        ILogger logger,
        IOpcUaClientConnectionEstablisher connectionEstablisher,
        IOpcUaClientSubscriptionFactory subscriptionFactory,
        Func<int, OpcUaTypeMapper, ILogger, INotificationDispatcher>? dispatcherFactory,
        Func<ILogger, IOpcUaReconnectHandlerWrapper>? reconnectWrapperFactory,
        Func<ILogger, IOpcUaReconfigureExecutor>? reconfigureExecutorFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(connectionEstablisher);
        ArgumentNullException.ThrowIfNull(subscriptionFactory);
        InstanceId = instanceId;
        _logger = logger;
        _connectionEstablisher = connectionEstablisher;
        _subscriptionFactory = subscriptionFactory;
        _dispatcherFactory = dispatcherFactory;
        _reconnectWrapperFactory = reconnectWrapperFactory;
        _reconfigureExecutorFactory = reconfigureExecutorFactory;
    }

    private INotificationDispatcher BuildDispatcher(
        int channelCapacity,
        OpcUaTypeMapper typeMapper) =>
        _dispatcherFactory is not null
            ? _dispatcherFactory(channelCapacity, typeMapper, _logger)
            : new NotificationDispatcher(channelCapacity, typeMapper, _logger);

    private IOpcUaReconnectHandlerWrapper BuildReconnectWrapper() =>
        _reconnectWrapperFactory is not null
            ? _reconnectWrapperFactory(_logger)
            : new DefaultOpcUaReconnectHandlerWrapper(_logger);

    private IOpcUaReconfigureExecutor BuildReconfigureExecutor() =>
        _reconfigureExecutorFactory is not null
            ? _reconfigureExecutorFactory(_logger)
            : new DefaultOpcUaReconfigureExecutor(_logger);

    /// <inheritdoc/>
    public Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config is not OpcUaClientSourceConfiguration typedConfig)
        {
            TransitionState(AdapterState.Failed);
            throw new InvalidOperationException(
                $"Expected OpcUaClientSourceConfiguration; got {config.GetType().FullName}.");
        }

        if (!string.Equals(typedConfig.InstanceId, InstanceId, StringComparison.Ordinal))
        {
            TransitionState(AdapterState.Failed);
            throw new InvalidOperationException(
                $"Config InstanceId '{typedConfig.InstanceId}' does not match adapter InstanceId '{InstanceId}'.");
        }

        // Coherence checks raise loudly here so a misconfigured adapter
        // never reaches StartAsync (which would surface the same problem
        // at first session-create — worse operator UX).
        ValidateCoherence(typedConfig);

        _config = typedConfig;
        TransitionState(AdapterState.Initialized);
        _logger.LogInformation(
            "OPC UA Client source {InstanceId} initialized: endpoint={EndpointUrl}, security={SecurityMode}, auth={AuthMode}, monitoredItems={MonitoredItemCount}",
            InstanceId,
            typedConfig.EndpointUrl,
            typedConfig.SecurityMode,
            typedConfig.AuthMode,
            typedConfig.MonitoredItems.Count);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// PR 2 opens the Session via the configured session factory.
    /// Fail-soft pattern matches FOCAS2 / Modbus: an initial connect
    /// failure transitions to Failed, captures the error to
    /// <c>_lastError</c>, and leaves the adapter in a state the
    /// reconnect coordinator (PR 6) can recover from. Subscription
    /// wiring lands in PR 3; for PR 2 the Session is open but no
    /// subscriptions are created.
    /// </remarks>
    public async Task StartAsync(CancellationToken ct)
    {
        if (_config is null)
        {
            throw new InvalidOperationException(
                "Cannot start an uninitialised adapter. Call InitializeAsync first.");
        }

        TransitionState(AdapterState.Starting);

        try
        {
            _session = await _connectionEstablisher
                .EstablishAsync(_config, sessionName: $"EdgeConnect.OpcUaClient.{InstanceId}", ct)
                .ConfigureAwait(false);

            // Attach subscriptions for the configured monitored items.
            // The factory handles the 100K-item per-session cap; throws
            // OPCUA.TOO_MANY_MONITORED_ITEMS on over-budget configs.
            _subscriptions = await _subscriptionFactory
                .CreateSubscriptionsAsync(_session, _config, ct)
                .ConfigureAwait(false);

            // v2.1 §1.3 hot path — instantiate the dispatcher AFTER
            // subscriptions exist (so we know the channel capacity from
            // config) and wire FastDataChangeCallback so the OPC stack's
            // publish thread feeds the channel non-blockingly.
            var typeMapper = new OpcUaTypeMapper(
                gatewayId: _gatewayId,
                sourceInstanceId: InstanceId,
                protocolName: ProtocolName,
                deviceId: _config.DeviceId);
            _dispatcher = BuildDispatcher(_config.NotificationChannelCapacity, typeMapper);
            foreach (var sub in _subscriptions)
            {
                sub.FastDataChangeCallback = _dispatcher.OnNotification;
            }

            // PR 6a — wire the reconnect coordinator. It subscribes to
            // the session's KeepAliveEvent; the next bad keep-alive will
            // trigger BeginReconnect via the wrapper and raise
            // StateChanged when the attempt finishes. The dispatcher
            // SURVIVES the reconnect (per OpcUaReconnectCoordinator
            // locked invariant #4) — channel + backlog drain through.
            _coordinator = new OpcUaReconnectCoordinator(BuildReconnectWrapper(), _logger);
            _coordinatorStateHandler = OnCoordinatorStateChanged;
            _coordinator.StateChanged += _coordinatorStateHandler;
            _coordinator.Attach(_session);

            _lastError = null;
            _lastSuccessAtUtc = DateTime.UtcNow;
            TransitionState(AdapterState.Running);
            _logger.LogInformation(
                "OPC UA Client source {InstanceId} session opened against {EndpointUrl}; "
                + "{SubscriptionCount} subscriptions / {MonitoredItemCount} monitored items active.",
                InstanceId,
                _config.EndpointUrl,
                _subscriptions.Count,
                _config.MonitoredItems.Count);
        }
        catch (OperationCanceledException)
        {
            TransitionState(AdapterState.Stopped);
            throw;
        }
        catch (Exception ex)
        {
            var error = new AdapterError
            {
                Code = ClassifyConnectError(ex),
                Category = ErrorCategory.Network,
                Message = $"Initial session create failed: {ex.Message}",
                Retryable = true,
            };
            _lastError = error;
            TransitionState(AdapterState.Failed);
            _logger.LogError(ex,
                "OPC UA Client source {InstanceId} initial session create failed: {ErrorCode}",
                InstanceId, error.Code);
            // Fail-soft: do not throw. Reconnect coordinator (PR 6) will
            // retry on its own cadence. For PR 2 the adapter sits in
            // Failed until StopAsync clears state.
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct)
    {
        if (State is AdapterState.Stopped or AdapterState.Created)
        {
            return;
        }

        TransitionState(AdapterState.Stopping);

        // PR 6a — detach + dispose the coordinator FIRST. This unhooks
        // the KeepAlive event from the session so a teardown-time bad
        // keep-alive (e.g., from session.Close itself) doesn't trigger
        // a phantom reconnect against a tearing-down session.
        if (_coordinator is { } coordinator)
        {
            if (_coordinatorStateHandler is { } handler)
            {
                try { coordinator.StateChanged -= handler; } catch { /* best-effort */ }
                _coordinatorStateHandler = null;
            }
            coordinator.Detach();
            try { await coordinator.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            _coordinator = null;
        }

        // Stop the dispatcher BEFORE we tear down subscriptions —
        // outstanding notifications drain (or count as
        // DroppedAtShutdown after the timeout). Doing this first means
        // subscription teardown doesn't race with in-flight callbacks.
        if (_dispatcher is NotificationDispatcher concreteDispatcher)
        {
            try { await concreteDispatcher.StopAsync(ct).ConfigureAwait(false); } catch { /* best-effort */ }
        }

        // Dispose subscriptions BEFORE closing the session. OPC stack
        // ownership semantics: session.Close cleans up server-side state
        // for subscriptions it still owns, but disposing them first
        // releases the client-side resources cleanly and avoids
        // subscription-orphan logs from the stack.
        if (_subscriptions is { } subscriptions)
        {
            foreach (var subscription in subscriptions)
            {
                try { subscription.Delete(silent: true); } catch { /* best-effort */ }
                try { subscription.Dispose(); } catch { /* best-effort */ }
            }
            _subscriptions = null;
        }

        if (_session is { } session)
        {
            try
            {
                // Close is synchronous on the OPC stack; the CloseAsync
                // overload is preferred where available for cooperative
                // cancellation.
                await session.CloseAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "OPC UA Client source {InstanceId} encountered an error while closing the session; proceeding to Stopped state.",
                    InstanceId);
            }
            finally
            {
                try { session.Dispose(); } catch { /* best-effort */ }
                _session = null;
            }
        }

        TransitionState(AdapterState.Stopped);
        _logger.LogInformation("OPC UA Client source {InstanceId} stopped.", InstanceId);
    }

    /// <summary>
    /// Slice 0 commit 3.0 (inert) — begin a durable quiescence attestation across
    /// the OPC UA surfaces. Idempotent: concurrent/repeated calls return the same
    /// operation. The drain pump is supervisor-owned, so Worker is NotApplicable;
    /// CallbackDrain (notification ingress + channel) and BackgroundWork (reconnect
    /// coordinator) are the two applicable surfaces.
    /// <para>
    /// Non-blocking (Blocker 2): under <see cref="_stateLock"/> we ONLY capture the
    /// dispatcher/coordinator/subscription references and set the authoritative
    /// dispatcher ingress flag — no UA-stack calls. Subscription unwire/delete,
    /// coordinator detach/dispose, and the dispatcher drain all run in the async
    /// resolution path, off the lock, over the captured locals.
    /// </para>
    /// </summary>
    public Core.Adapters.Retirement.AdapterRetirementOperation BeginRetirement(
        Core.Adapters.Retirement.AdapterRetirementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_stateLock)
        {
            if (_retirement is { } existing)
            {
                return existing;
            }

            // Capture references under the lock; do NOT touch the UA stack here.
            var dispatcher = _dispatcher;
            var subscriptions = _subscriptions;
            var coordinator = _coordinator;
            var handler = _coordinatorStateHandler;

            _retirement = Retirement.OpcUaRetirement.Begin(
                closeIngressFlag: () => (dispatcher as NotificationDispatcher)?.BeginRetiringIngress(),
                unwireSubscriptions: () => UnwireSubscriptionsAsync(subscriptions),
                stopBackgroundWork: () => StopReconnectCoordinatorAsync(coordinator, handler),
                drainCallbacks: () => DrainCallbacksAsync(dispatcher),
                context);
            return _retirement;
        }
    }

    /// <summary>
    /// ASYNC best-effort (off the state lock) — null + delete the captured
    /// subscriptions so the stack stops delivering at the source. The dispatcher
    /// ingress flag (set synchronously in Begin) is the authoritative gate, so a
    /// failure here does NOT gate proof.
    /// </summary>
    private static Task UnwireSubscriptionsAsync(IReadOnlyList<Subscription>? subscriptions)
    {
        if (subscriptions is { } subs)
        {
            foreach (var subscription in subs)
            {
                try { subscription.FastDataChangeCallback = null; } catch { /* best-effort */ }
                try { subscription.Delete(silent: true); } catch { /* best-effort */ }
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// ASYNC (off the state lock) — detach + dispose the captured reconnect
    /// coordinator so it can no longer re-wire callbacks. A dispose fault propagates
    /// → <c>BackgroundWork = Unproven</c> (fail closed). No coordinator → nothing to
    /// quiesce.
    /// </summary>
    private static async Task StopReconnectCoordinatorAsync(
        OpcUaReconnectCoordinator? coordinator, Action<CoordinatorStateChange>? handler)
    {
        if (coordinator is null)
        {
            return;
        }

        if (handler is not null)
        {
            try { coordinator.StateChanged -= handler; } catch { /* best-effort */ }
        }

        coordinator.Detach();
        await coordinator.DisposeAsync().ConfigureAwait(false); // throws → BackgroundWork Unproven
    }

    /// <summary>
    /// ASYNC (off the state lock) — complete + drain the dispatcher channel.
    /// Passes <see cref="CancellationToken.None"/>: the HOST observation token must
    /// NOT cancel the drain (Blocker 1) — only the dispatcher's own drain budget may
    /// yield a terminal not-fully-drained result. A null dispatcher (constructed
    /// adapter) → nothing queued → fully drained; a non-concrete dispatcher on an
    /// initialized adapter could own queued work we cannot drain → fail closed.
    /// </summary>
    private static Task<CallbackDrainResult> DrainCallbacksAsync(INotificationDispatcher? dispatcher)
    {
        if (dispatcher is NotificationDispatcher concrete)
        {
            return concrete.RetireAndDrainAsync(CancellationToken.None);
        }

        if (dispatcher is not null)
        {
            // Non-concrete dispatcher with unknown queue state — cannot prove drain.
            return Task.FromResult(new CallbackDrainResult
            {
                FullyDrained = false,
                DroppedAtShutdown = 0,
                RejectedAfterRetirement = 0,
            });
        }

        return Task.FromResult(new CallbackDrainResult
        {
            FullyDrained = true,
            DroppedAtShutdown = 0,
            RejectedAfterRetirement = 0,
        });
    }

    /// <summary>
    /// Coordinator state-change handler. Drives the adapter's
    /// AdapterState transitions Running → Degraded → Running / Failed
    /// per the PR 6a locked lifecycle (v2.1 §1.3.5).
    ///
    /// On a successful reconnect (Transfer or Recreate) the new session
    /// reference replaces <see cref="_session"/>, the active
    /// subscriptions are snapshotted from <c>newSession.Subscriptions</c>,
    /// and FastDataChangeCallback is re-wired on each one. The dispatcher
    /// instance persists across the reconnect (locked invariant #4) — its
    /// channel + backlog drain through.
    /// </summary>
    private void OnCoordinatorStateChanged(CoordinatorStateChange change)
    {
        if (change.EnteredReconnect)
        {
            // Running → Degraded. We DO NOT change _session or
            // _subscriptions here — the existing references stay live
            // while the stack's reconnect handler retries. Notifications
            // simply pause until the new session is up.
            TransitionState(AdapterState.Degraded);
            _logger.LogWarning(
                "OPC UA Client source {InstanceId} entered reconnect; AdapterState → Degraded.",
                InstanceId);
            return;
        }

        var result = change.Result;
        if (result is null)
        {
            // Defensive — coordinator contract says non-null Result when
            // leaving reconnect. If it ever happens, log loudly and
            // treat as terminal failure.
            _logger.LogError(
                "OPC UA Client source {InstanceId} coordinator raised StateChanged(EnteredReconnect=false) with null Result; treating as Failed.",
                InstanceId);
            TransitionState(AdapterState.Failed);
            return;
        }

        switch (result.Mode)
        {
            case ReconnectMode.Transfer:
            case ReconnectMode.Recreate:
                if (result.NewSession is { } newSession)
                {
                    RewireForReconnectedSession(newSession, result.Mode);
                    _lastError = null;
                    _lastSuccessAtUtc = DateTime.UtcNow;
                    TransitionState(AdapterState.Running);
                    _logger.LogInformation(
                        "OPC UA Client source {InstanceId} reconnect succeeded via {Mode}; AdapterState → Running.",
                        InstanceId, result.Mode);
                }
                else
                {
                    // Mode reports success but no session — defensive
                    // fallback. Treat as terminal failure rather than
                    // silently leaving the adapter in Degraded.
                    _logger.LogError(
                        "OPC UA Client source {InstanceId} reconnect reported {Mode} but NewSession was null; treating as Failed.",
                        InstanceId, result.Mode);
                    _lastError = new AdapterError
                    {
                        Code = "OPCUA.RECONNECT_INCONSISTENT",
                        Category = ErrorCategory.Internal,
                        Message = $"Reconnect mode {result.Mode} reported success but no new session was provided.",
                        Retryable = false,
                    };
                    TransitionState(AdapterState.Failed);
                }
                break;

            case ReconnectMode.Failed:
                _lastError = new AdapterError
                {
                    Code = "OPCUA.RECONNECT_EXHAUSTED",
                    Category = ErrorCategory.Network,
                    Message = result.Error?.Message
                        ?? "Reconnect retry budget exhausted; the stack could not re-establish the session.",
                    Retryable = false,
                };
                TransitionState(AdapterState.Failed);
                _logger.LogError(result.Error,
                    "OPC UA Client source {InstanceId} reconnect terminally failed; AdapterState → Failed.",
                    InstanceId);
                break;

            case ReconnectMode.Unknown:
            default:
                // Shouldn't happen — the wrapper only emits
                // Transfer/Recreate/Failed. Log + leave state alone.
                _logger.LogWarning(
                    "OPC UA Client source {InstanceId} received unexpected ReconnectMode {Mode}; leaving AdapterState unchanged.",
                    InstanceId, result.Mode);
                break;
        }
    }

    /// <summary>
    /// After a Transfer or Recreate reconnect, refresh <see cref="_session"/>,
    /// snapshot <see cref="_subscriptions"/> from the new session, and
    /// re-wire FastDataChangeCallback so the dispatcher continues to
    /// receive notifications.
    ///
    /// For Transfer the stack typically reuses the SAME
    /// <see cref="Subscription"/> instances on the new session (so
    /// FastDataChangeCallback would already be wired) — re-wiring is
    /// still safe (idempotent assignment). For Recreate the stack made
    /// NEW Subscription instances; re-wiring is mandatory.
    /// </summary>
    private void RewireForReconnectedSession(ISession newSession, ReconnectMode mode)
    {
        _session = newSession;

        // ISession.Subscriptions is the stack's authoritative view of
        // active subscriptions on the (now-current) session.
        var refreshed = new List<Subscription>();
        foreach (var sub in newSession.Subscriptions)
        {
            refreshed.Add(sub);
        }
        _subscriptions = refreshed;

        if (_dispatcher is { } dispatcher)
        {
            foreach (var sub in refreshed)
            {
                try { sub.FastDataChangeCallback = dispatcher.OnNotification; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "OPC UA Client source {InstanceId} failed to re-wire FastDataChangeCallback on a subscription after {Mode} reconnect; notifications from that subscription may be lost until next reconnect.",
                        InstanceId, mode);
                }
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// PR 6b override per v2.1 §1.3.5 — active-set snapshot semantics for
    /// MonitoredItem changes. Endpoint / security / auth / channel-capacity
    /// changes fall through to the base Stop+Initialize+Start sequence
    /// because they require a fresh session and a fresh dispatcher.
    ///
    /// MonitoredItem-only changes are applied surgically via the
    /// <see cref="IOpcUaReconfigureExecutor"/> — the session, dispatcher,
    /// and untouched subscriptions remain live so the notification stream
    /// only pauses for the items being modified.
    ///
    /// Single-flight: a second concurrent call throws
    /// <see cref="InvalidOperationException"/> with <c>OPCUA.RECONFIGURE_IN_FLIGHT</c>.
    /// Reconnect-in-flight: throws with <c>OPCUA.RECONFIGURE_WHILE_RECONNECTING</c>
    /// and embeds retry-friendly metadata in the message so the management
    /// API can render a "retry in 2s" UX (amendment #4, user lock 2026-05-29).
    /// </remarks>
    public async Task ReconfigureAsync(SourceConfiguration newConfig, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(newConfig);

        if (newConfig is not OpcUaClientSourceConfiguration typedNew)
        {
            throw new InvalidOperationException(
                $"OPCUA.RECONFIGURE_CONFIG_WRONG_TYPE: expected OpcUaClientSourceConfiguration; "
                + $"got {newConfig.GetType().FullName}.");
        }

        if (!string.Equals(typedNew.InstanceId, InstanceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"OPCUA.RECONFIGURE_INSTANCE_ID_MISMATCH: new config InstanceId '{typedNew.InstanceId}' "
                + $"does not match adapter InstanceId '{InstanceId}'.");
        }

        // Validate the new config eagerly. Per ISourceAdapter.ReconfigureAsync
        // Rule 5 — invalid config must not touch the active set.
        var validation = await ValidateConfigAsync(typedNew, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            var first = validation.Errors.Count > 0 ? validation.Errors[0] : null;
            throw new InvalidOperationException(
                $"OPCUA.RECONFIGURE_CONFIG_INVALID: {first?.Code ?? "OPCUA.UNKNOWN"}: "
                + $"{first?.Message ?? "validation failed"}");
        }

        // Reconfigure on a non-Running adapter has no live session to mutate.
        // PR 6b amendment — explicit error code so the management API
        // surfaces a clear remediation rather than a generic null-ref.
        if (State is not (AdapterState.Running or AdapterState.Degraded))
        {
            throw new InvalidOperationException(
                $"OPCUA.RECONFIGURE_NOT_RUNNING: adapter is in state '{State}'; hot reconfigure "
                + "requires a live session (Running or Degraded). InitializeAsync + StartAsync "
                + "the new config instead.");
        }

        // Restart-required check. v2.1 §1.3.5 active-set snapshot scope is
        // monitored items only; structural changes (endpoint, security,
        // tuning knobs that change subscription topology, channel
        // capacity) cascade and need a full restart.
        if (_config is null || RequiresFullRestart(_config, typedNew))
        {
            _logger.LogInformation(
                "OPC UA Client source {InstanceId} reconfigure requires full restart (endpoint / "
                + "security / channel / global tuning changed); falling through to Stop+Init+Start.",
                InstanceId);
            await StopAsync(ct).ConfigureAwait(false);
            await InitializeAsync(typedNew, ct).ConfigureAwait(false);
            await StartAsync(ct).ConfigureAwait(false);
            return;
        }

        // Reconnect-in-flight gate — embed retry-friendly metadata in the
        // error message (amendment #4) so the management API can render
        // a friendly "the adapter is recovering, please retry in ~2s" UX.
        if (_coordinator is { } coord && coord.GetCounters() is { CurrentlyReconnecting: true } counters)
        {
            throw new InvalidOperationException(
                $"OPCUA.RECONFIGURE_WHILE_RECONNECTING: adapter is recovering "
                + $"(lastReconnectMode={counters.LastReconnectMode}); "
                + $"retryable=true; suggestedBackoffMs=2000.");
        }

        // Single-flight on reconfigure itself.
        if (Interlocked.CompareExchange(ref _currentlyReconfiguring, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "OPCUA.RECONFIGURE_IN_FLIGHT: another reconfigure is already in flight on this "
                + "adapter; retryable=true; suggestedBackoffMs=500.");
        }

        try
        {
            // Compute diff.
            var diff = OpcUaMonitoredItemDiff.Compute(
                _config.MonitoredItems,
                typedNew.MonitoredItems);

            // Idempotent path (amendment #5) — no executor work, just
            // accept the new config reference (which may carry cosmetic
            // DisplayName / DeviceId changes the diff treats as Unchanged).
            if (diff.IsIdempotent)
            {
                _config = typedNew;
                Interlocked.Increment(ref _reconfigureCount);
                _lastReconfigureUtc = DateTime.UtcNow;
                _lastReconfigureChangeCount = 0;
                _logger.LogInformation(
                    "OPC UA Client source {InstanceId} reconfigure was idempotent; no executor invocation.",
                    InstanceId);
                return;
            }

            if (_session is null)
            {
                // Defensive — State check above should prevent this, but
                // race conditions in test fixtures might land here.
                throw new InvalidOperationException(
                    "OPCUA.RECONFIGURE_NO_SESSION: adapter has no live session despite being in "
                    + $"state '{State}'; this indicates a lifecycle bug.");
            }

            var executor = BuildReconfigureExecutor();
            var existing = _subscriptions ?? Array.Empty<Subscription>();
            var result = await executor.ApplyAsync(_session, existing, diff, typedNew, ct).ConfigureAwait(false);

            // Re-wire FastDataChangeCallback on any new subs so the
            // dispatcher receives their notifications.
            if (_dispatcher is { } dispatcher)
            {
                foreach (var newSub in result.NewSubscriptions)
                {
                    try { newSub.FastDataChangeCallback = dispatcher.OnNotification; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "OPC UA Client source {InstanceId} failed to wire FastDataChangeCallback "
                            + "on a newly-allocated subscription during reconfigure; notifications from "
                            + "that subscription may be lost until the next reconnect.",
                            InstanceId);
                    }
                }
            }

            // Dispose emptied subs.
            foreach (var emptied in result.RemovedSubscriptions)
            {
                try { emptied.Delete(silent: true); } catch { /* best-effort */ }
                try { emptied.Dispose(); } catch { /* best-effort */ }
            }

            _subscriptions = result.FinalSubscriptions;
            _config = typedNew;

            Interlocked.Increment(ref _reconfigureCount);
            _lastReconfigureUtc = DateTime.UtcNow;
            _lastReconfigureChangeCount = diff.ChangeCount;
            _logger.LogInformation(
                "OPC UA Client source {InstanceId} reconfigure applied: added={Added}, removed={Removed}, "
                + "modified={Modified}; subscriptionsActive={SubsActive}.",
                InstanceId, result.ItemsAdded, result.ItemsRemoved, result.ItemsModified,
                result.FinalSubscriptions.Count);
        }
        catch (InvalidOperationException)
        {
            // Validation / cap-exceeded — leave adapter Running, surface
            // to caller. Active set was NOT mutated (pre-validation
            // contract pinned in test).
            throw;
        }
        catch (Exception ex)
        {
            // Strategy A (best-effort + Degraded, user lock 2026-05-29) —
            // mid-flight failures don't roll back. Adapter transitions
            // to Degraded; the coordinator + operator recover via
            // reconnect or restart.
            _lastError = new AdapterError
            {
                Code = "OPCUA.RECONFIGURE_PARTIAL_FAILURE",
                Category = ErrorCategory.Protocol,
                Message = $"Hot reconfigure failed mid-apply: {ex.Message}. Active subscription set "
                    + "may be partially mutated; reconnect or restart to recover.",
                Retryable = false,
            };
            TransitionState(AdapterState.Degraded);
            _logger.LogError(ex,
                "OPC UA Client source {InstanceId} reconfigure failed mid-apply; transitioning to Degraded.",
                InstanceId);
            throw new InvalidOperationException(_lastError.Code + ": " + _lastError.Message, ex);
        }
        finally
        {
            Interlocked.Exchange(ref _currentlyReconfiguring, 0);
        }
    }

    /// <summary>
    /// Returns true when changing from <paramref name="oldConfig"/> to
    /// <paramref name="newConfig"/> requires a full session restart
    /// (cannot be hot-applied via the diff executor). The scope of the
    /// active-set hot path is intentionally narrow: monitored items
    /// only. Everything else cascades.
    /// </summary>
    private static bool RequiresFullRestart(
        OpcUaClientSourceConfiguration oldConfig,
        OpcUaClientSourceConfiguration newConfig)
    {
        return !string.Equals(oldConfig.EndpointUrl, newConfig.EndpointUrl, StringComparison.Ordinal)
            || !string.Equals(oldConfig.ApplicationUri, newConfig.ApplicationUri, StringComparison.Ordinal)
            || oldConfig.SecurityMode != newConfig.SecurityMode
            || oldConfig.AuthMode != newConfig.AuthMode
            || !CredentialsEquivalent(oldConfig.Credentials, newConfig.Credentials)
            || oldConfig.PublishingIntervalMs != newConfig.PublishingIntervalMs
            || oldConfig.KeepAliveCount != newConfig.KeepAliveCount
            || oldConfig.LifetimeCount != newConfig.LifetimeCount
            || oldConfig.MaxNotificationsPerPublish != newConfig.MaxNotificationsPerPublish
            || oldConfig.SamplingIntervalMs != newConfig.SamplingIntervalMs
            || oldConfig.DefaultAnalogQueueSize != newConfig.DefaultAnalogQueueSize
            || oldConfig.NotificationChannelCapacity != newConfig.NotificationChannelCapacity
            || !string.Equals(oldConfig.DeviceId, newConfig.DeviceId, StringComparison.Ordinal);
    }

    private static bool CredentialsEquivalent(
        OpcUaClientCredentials? a,
        OpcUaClientCredentials? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return string.Equals(a.Username, b.Username, StringComparison.Ordinal)
            && string.Equals(a.Password, b.Password, StringComparison.Ordinal)
            && string.Equals(a.CertificatePath, b.CertificatePath, StringComparison.Ordinal);
    }

    /// <summary>
    /// Read-only probe for the wizard's "Test Connection" button. Opens a
    /// SEPARATE session (does not touch <see cref="_session"/>), performs
    /// a no-op read of the server's <c>ServerStatus.State</c> node, and
    /// closes. Honours ADR-0015 Rule 6 — idempotent, no side effects on
    /// running state, no draft mutation.
    /// </summary>
    /// <remarks>
    /// PR 2 implements TestConnectAsync; the wizard wires this in PR 7.
    /// Returns a structured result (success + server-reported state OR
    /// failure + diagnostic message) — the wizard renders this directly
    /// without classifying further.
    /// </remarks>
    public async Task<TestConnectResult> TestConnectAsync(
        OpcUaClientSourceConfiguration config,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);

        ISession? probeSession = null;
        try
        {
            probeSession = await _connectionEstablisher
                .EstablishAsync(config, sessionName: $"EdgeConnect.OpcUaClient.{InstanceId}.TestConnect", ct)
                .ConfigureAwait(false);

            // Read the standard ServerStatus.State node — a no-op
            // read that every OPC UA Server supports. Failure → the
            // session connected but the server is in a degraded state
            // (operator-actionable signal).
            var (statusOk, _) = await ReadServerStatusAsync(probeSession, ct).ConfigureAwait(false);
            return new TestConnectResult
            {
                Success = statusOk,
                EndpointUrl = config.EndpointUrl,
                ServerState = statusOk ? "Running" : "Unknown",
                Message = statusOk
                    ? $"Connected to {config.EndpointUrl}; server is Running."
                    : $"Connected to {config.EndpointUrl} but ServerStatus read failed.",
            };
        }
        catch (Exception ex)
        {
            return new TestConnectResult
            {
                Success = false,
                EndpointUrl = config.EndpointUrl,
                ServerState = null,
                Message = $"Connect failed: {ex.Message}",
                ErrorCode = ClassifyConnectError(ex),
            };
        }
        finally
        {
            if (probeSession is not null)
            {
                try { await probeSession.CloseAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* best-effort */ }
                try { probeSession.Dispose(); } catch { /* best-effort */ }
            }
        }
    }

    /// <inheritdoc/>
    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
    {
        var level = State switch
        {
            AdapterState.Running => HealthLevel.Healthy,
            AdapterState.Degraded => HealthLevel.Degraded,
            AdapterState.Failed => HealthLevel.Unhealthy,
            AdapterState.Stopped => HealthLevel.Unknown,
            _ => HealthLevel.Unknown,
        };

        // Configured-vs-active metrics per PR 3 amendment #2 (user lock
        // 2026-05-29). Operators distinguish "configured 30,000 but only
        // 29,850 active" (a problem) from "configured 29,850 / active
        // 29,850" (steady state). Invaluable during reconnect
        // troubleshooting.
        var configuredItems = _config?.MonitoredItems.Count ?? 0;
        var configuredSubs = configuredItems == 0
            ? 0
            : (configuredItems + OpcUaClientSubscriptionPlanner.MaxItemsPerSubscription - 1)
              / OpcUaClientSubscriptionPlanner.MaxItemsPerSubscription;

        var activeSubs = _subscriptions?.Count ?? 0;
        var activeItems = 0;
        // Task #51 — count MonitoredItems whose server-returned status
        // is Bad. The factory's InspectMonitoredItemStatuses logs each
        // rejection at Warning level; this count surfaces the same data
        // numerically so the SourceDetail page can distinguish
        // "subscribed but silent" (badStatus > 0) from "healthy quiet
        // subscription" (badStatus == 0 and notificationsReceived == 0
        // means the tags genuinely don't change).
        var badStatusItems = 0;
        if (_subscriptions is { } subs)
        {
            foreach (var subscription in subs)
            {
                activeItems += (int)subscription.MonitoredItemCount;
                foreach (var item in subscription.MonitoredItems)
                {
                    if (item.Status?.Error is { } err && ServiceResult.IsBad(err))
                    {
                        badStatusItems++;
                    }
                }
            }
        }

        // v2.1 §1.3 hot path — surface the dispatcher's 4 counters per
        // PR 4 amendment #4 + the queue-depth probe per amendment #3.
        var dispatcherCounters = _dispatcher?.GetCounters();
        var notificationQueueDepth = SumQueueDepthAcrossSubscriptions();

        // PR 6a — coordinator counters. When the coordinator hasn't been
        // built yet (adapter pre-Start or post-Stop) surface zeros / null
        // / Unknown so operators see a stable shape rather than a
        // shrinking metric set.
        var coordinatorCounters = _coordinator?.GetCounters();

        var metrics = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            // INTENT — what the operator asked for via config
            ["configuredMonitoredItems"] = configuredItems,
            ["configuredSubscriptions"] = configuredSubs,
            // ACTUAL — what is alive in the running session
            ["monitoredItemsActive"] = activeItems,
            ["subscriptionsActive"] = activeSubs,
            // PER-ITEM STATUS — task #51. Non-zero means server rejected
            // those items (BadNodeIdUnknown / BadAttributeIdInvalid /
            // BadUserAccessDenied / BadSecurityChecksFailed / etc.).
            // Check host console for per-item warning log lines.
            ["monitoredItemsWithBadStatus"] = badStatusItems,
            // HOT-PATH counters (PR 4 amendment #4)
            ["notificationsReceived"] = dispatcherCounters?.Received ?? 0L,
            ["notificationsDispatched"] = dispatcherCounters?.Dispatched ?? 0L,
            ["notificationsDroppedDueToBackpressure"] = dispatcherCounters?.DroppedDueToBackpressure ?? 0L,
            ["notificationsDroppedAtShutdown"] = dispatcherCounters?.DroppedAtShutdown ?? 0L,
            // QUEUE-DEPTH capability + value (PR 4 amendment #3)
            ["notificationQueueDepthAvailable"] = _queueDepthProbe.IsAvailable,
            ["notificationQueueDepth"] = notificationQueueDepth,
            // RECONNECT counters (PR 6a + amendments #1, #2)
            ["reconnectsViaTransfer"] = coordinatorCounters?.ReconnectsViaTransfer ?? 0L,
            ["reconnectsViaRecreate"] = coordinatorCounters?.ReconnectsViaRecreate ?? 0L,
            ["reconnectsFailed"] = coordinatorCounters?.ReconnectsFailed ?? 0L,
            ["currentlyReconnecting"] = coordinatorCounters?.CurrentlyReconnecting ?? false,
            ["lastSuccessfulReconnectUtc"] = (object?)coordinatorCounters?.LastSuccessfulReconnectUtc
                ?? "never",
            ["lastReconnectMode"] = (coordinatorCounters?.LastReconnectMode ?? ReconnectMode.Unknown).ToString(),
            // RECONFIGURE counters (PR 6b + amendment #6 currentlyReconfiguring)
            ["currentlyReconfiguring"] = Interlocked.CompareExchange(ref _currentlyReconfiguring, 0, 0) != 0,
            ["reconfigureCount"] = Interlocked.Read(ref _reconfigureCount),
            ["lastReconfigureUtc"] = (object?)_lastReconfigureUtc ?? "never",
            ["lastReconfigureChangeCount"] = _lastReconfigureChangeCount,
        };

        var health = new AdapterHealth
        {
            State = State,
            Level = level,
            CheckedAt = DateTime.UtcNow,
            LastSuccessAt = _lastSuccessAtUtc,
            LastError = _lastError,
            Metrics = metrics,
        };
        return Task.FromResult(health);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// OPC UA Client does NOT support polling. The runtime checks
    /// <see cref="Capabilities"/> before invoking this and SHOULD never
    /// call <see cref="PollAsync"/>; the defensive throw here surfaces
    /// the bug loudly if it ever does.
    /// </remarks>
    public Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct) =>
        throw new NotSupportedException(
            "OPC UA Client is a subscription-mode adapter; use SubscribeAsync. "
            + "Capabilities flags advertise Subscription only — the runtime should not call PollAsync.");

    /// <inheritdoc/>
    /// <remarks>
    /// Drains the notification dispatcher's bounded channel via
    /// <see cref="INotificationDispatcher.ConsumeAsync"/>. Yields
    /// CanonicalDataPoints in subscription-publish order (per v2.1 §19.6
    /// per-source ordering invariant). Exits cleanly on
    /// <paramref name="ct"/> cancellation OR adapter stop.
    /// </remarks>
    public async IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null)
        {
            // Adapter not yet Running (or in Failed state) — empty stream.
            await Task.Yield();
            yield break;
        }

        await foreach (var cdp in dispatcher.ConsumeAsync(ct).ConfigureAwait(false))
        {
            yield return cdp;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runtime-facing surface — returns the operator-configured monitored
    /// items as <see cref="TagDefinition"/>s so the route engine + tag
    /// inventory tooling can enumerate what this adapter watches. This
    /// is distinct from the wizard-facing browse done via
    /// <c>OpcUaClientBrowseService : ITagBrowseService</c> (PR 5), which
    /// discovers the SERVER's address space; <c>BrowseTagsAsync</c>
    /// reflects only what the operator has CONFIGURED.
    /// </remarks>
    public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
    {
        if (_config is null)
        {
            return Task.FromResult<IReadOnlyList<TagDefinition>>(Array.Empty<TagDefinition>());
        }

        var definitions = new List<TagDefinition>(_config.MonitoredItems.Count);
        foreach (var item in _config.MonitoredItems)
        {
            definitions.Add(new TagDefinition
            {
                Name = item.DisplayName,
                Path = item.DisplayName,
                // ValueType is unknown at config time — resolved at first
                // notification by OpcUaTypeMapper (PR 4a). Surfacing
                // Null here so consumers see "type discovery pending."
                ValueType = CanonicalValueType.Null,
                Description = item.NodeId,
            });
        }
        return Task.FromResult<IReadOnlyList<TagDefinition>>(definitions);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config is not OpcUaClientSourceConfiguration typed)
        {
            return Task.FromResult(ValidationResult.Failure(
                "OPCUA.CONFIG_WRONG_TYPE",
                $"Expected OpcUaClientSourceConfiguration; got {config.GetType().FullName}.",
                "$"));
        }

        if (string.IsNullOrWhiteSpace(typed.EndpointUrl)
            || !Uri.TryCreate(typed.EndpointUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "opc.tcp", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ValidationResult.Failure(
                "OPCUA.CONFIG_INVALID_ENDPOINT",
                $"EndpointUrl must be an absolute opc.tcp:// URI: '{typed.EndpointUrl}'.",
                "$.EndpointUrl"));
        }

        if (string.IsNullOrWhiteSpace(typed.ApplicationUri))
        {
            return Task.FromResult(ValidationResult.Failure(
                "OPCUA.CONFIG_MISSING_APPLICATION_URI",
                "ApplicationUri is required.",
                "$.ApplicationUri"));
        }

        if (typed.PublishingIntervalMs <= 0
            || typed.SamplingIntervalMs <= 0)
        {
            return Task.FromResult(ValidationResult.Failure(
                "OPCUA.CONFIG_INVALID_INTERVALS",
                "PublishingIntervalMs and SamplingIntervalMs must be strictly positive.",
                "$"));
        }

        if (typed.LifetimeCount < typed.KeepAliveCount * 3)
        {
            return Task.FromResult(ValidationResult.Failure(
                "OPCUA.CONFIG_LIFETIME_TOO_SHORT",
                $"LifetimeCount ({typed.LifetimeCount}) must be ≥ 3× KeepAliveCount "
                + $"({typed.KeepAliveCount}); per v2.1 §2.5 the locked default ratio is 3:1.",
                "$.LifetimeCount"));
        }

        if (typed.NotificationChannelCapacity < OpcUaClientSourceConfiguration.MinimumNotificationChannelCapacity
            || typed.NotificationChannelCapacity > OpcUaClientSourceConfiguration.MaximumNotificationChannelCapacity)
        {
            return Task.FromResult(ValidationResult.Failure(
                "OPCUA.CONFIG_CHANNEL_CAPACITY_OUT_OF_RANGE",
                $"NotificationChannelCapacity ({typed.NotificationChannelCapacity}) must lie within "
                + $"[{OpcUaClientSourceConfiguration.MinimumNotificationChannelCapacity}, "
                + $"{OpcUaClientSourceConfiguration.MaximumNotificationChannelCapacity}]. "
                + "Per PR 4 amendment #1 — below the minimum the channel cannot absorb a single "
                + "stack publish-cycle at 30K/sec; above the maximum a backpressure problem turns "
                + "into a memory problem.",
                "$.NotificationChannelCapacity"));
        }

        try
        {
            ValidateCoherence(typed);
        }
        catch (InvalidOperationException ex)
        {
            var code = ExtractErrorCode(ex.Message) ?? "OPCUA.CONFIG_INCOHERENT";
            return Task.FromResult(ValidationResult.Failure(code, ex.Message, "$"));
        }

        return Task.FromResult(ValidationResult.Success());
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (State is AdapterState.Running or AdapterState.Degraded or AdapterState.Starting)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await StopAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>
    /// Coherence checks — fail loudly on combinations that are valid
    /// individually but invalid together. UserName + None security is
    /// the canonical example (credentials in cleartext on the wire).
    /// </summary>
    private static void ValidateCoherence(OpcUaClientSourceConfiguration config)
    {
        if (config.AuthMode == OpcUaAuthMode.UserName && config.SecurityMode == OpcUaSecurityMode.None)
        {
            throw new InvalidOperationException(
                "OPCUA.UNSAFE_USERNAME_OVER_NONE: UserName authentication requires SecurityMode != None — "
                + "credentials would be transmitted in cleartext. Use SignAndEncrypt (recommended) or "
                + "switch AuthMode to Anonymous / Certificate.");
        }

        if (config.AuthMode == OpcUaAuthMode.UserName
            && (config.Credentials is null
                || string.IsNullOrEmpty(config.Credentials.Username)
                || string.IsNullOrEmpty(config.Credentials.Password)))
        {
            throw new InvalidOperationException(
                "OPCUA.USERNAME_CREDENTIALS_MISSING: UserName auth requires both "
                + "Credentials.Username and Credentials.Password to be set.");
        }

        if (config.AuthMode == OpcUaAuthMode.Certificate
            && (config.Credentials is null || string.IsNullOrEmpty(config.Credentials.CertificatePath)))
        {
            throw new InvalidOperationException(
                "OPCUA.CERT_PATH_MISSING: Certificate auth requires Credentials.CertificatePath to be set.");
        }
    }

    private static string? ExtractErrorCode(string message)
    {
        var colonIndex = message.IndexOf(':');
        if (colonIndex <= 0) return null;
        var prefix = message[..colonIndex];
        return prefix.StartsWith("OPCUA.", StringComparison.Ordinal) ? prefix : null;
    }

    private void TransitionState(AdapterState target)
    {
        lock (_stateLock)
        {
            State = target;
        }
    }

    /// <summary>
    /// Sum the per-subscription notification cache depth across all
    /// active subscriptions. Returns -1 when the reflection probe is
    /// unavailable (operator distinguishes via the paired
    /// <c>notificationQueueDepthAvailable</c> metric per PR 4 amendment
    /// #3). Each individual probe failure also yields -1, so any -1 in
    /// the per-sub sum propagates.
    /// </summary>
    private long SumQueueDepthAcrossSubscriptions()
    {
        if (!_queueDepthProbe.IsAvailable || _subscriptions is null)
        {
            return -1L;
        }
        var total = 0L;
        foreach (var subscription in _subscriptions)
        {
            var depth = _queueDepthProbe.Depth(subscription);
            if (depth < 0)
            {
                return -1L;
            }
            total += depth;
        }
        return total;
    }

    /// <summary>
    /// Read the server's <c>ServerStatus.State</c> node. Used by
    /// <see cref="TestConnectAsync"/> as a no-op probe.
    /// </summary>
    private static async Task<(bool Success, string? StateText)> ReadServerStatusAsync(ISession session, CancellationToken ct)
    {
        try
        {
            var nodesToRead = new ReadValueIdCollection
            {
                new ReadValueId
                {
                    NodeId = VariableIds.Server_ServerStatus_State,
                    AttributeId = Attributes.Value,
                },
            };
            var readResponse = await session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: nodesToRead,
                ct: ct).ConfigureAwait(false);

            var results = readResponse?.Results;
            if (results is null || results.Count == 0 || StatusCode.IsBad(results[0].StatusCode))
            {
                return (false, null);
            }
            return (true, results[0].Value?.ToString());
        }
        catch
        {
            return (false, null);
        }
    }

    /// <summary>
    /// Translate a connect failure into a structured error code so
    /// diagnostics + the wizard's Test Connection panel show
    /// operator-readable categories rather than raw exception messages.
    /// </summary>
    private static string ClassifyConnectError(Exception ex) => ex switch
    {
        ServiceResultException sre when sre.StatusCode == StatusCodes.BadCertificateUntrusted
            => "OPCUA.SERVER_CERT_UNTRUSTED",
        ServiceResultException sre when sre.StatusCode == StatusCodes.BadCertificateHostNameInvalid
            => "OPCUA.SERVER_CERT_HOSTNAME_INVALID",
        ServiceResultException sre when sre.StatusCode == StatusCodes.BadSecurityChecksFailed
            => "OPCUA.SECURITY_CHECKS_FAILED",
        ServiceResultException sre when sre.StatusCode == StatusCodes.BadUserAccessDenied
            => "OPCUA.AUTH_DENIED",
        ServiceResultException sre when sre.StatusCode == StatusCodes.BadIdentityTokenInvalid
            => "OPCUA.AUTH_TOKEN_INVALID",
        ServiceResultException sre when sre.StatusCode == StatusCodes.BadConnectionRejected
            => "OPCUA.CONNECTION_REJECTED",
        System.Net.Sockets.SocketException
            => "OPCUA.CONNECT_NETWORK_ERROR",
        TimeoutException
            => "OPCUA.CONNECT_TIMEOUT",
        _ => "OPCUA.CONNECT_FAILED",
    };
}

/// <summary>
/// Outcome of <see cref="OpcUaClientSourceAdapter.TestConnectAsync"/>.
/// Rendered directly by the wizard's Test Connection panel.
/// </summary>
public sealed record TestConnectResult
{
    /// <summary>True when the probe connected AND read ServerStatus successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>The endpoint the probe attempted.</summary>
    public required string EndpointUrl { get; init; }

    /// <summary>Server-reported state (e.g. "Running") when <see cref="Success"/> is true.</summary>
    public string? ServerState { get; init; }

    /// <summary>Operator-readable result message.</summary>
    public required string Message { get; init; }

    /// <summary>Classified error code on failure; <see langword="null"/> on success.</summary>
    public string? ErrorCode { get; init; }
}
