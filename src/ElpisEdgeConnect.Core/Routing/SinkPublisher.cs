// ============================================================================
// File: Routing/SinkPublisher.cs
// Purpose: Per-sink publish-with-retry helper. Owns the retry state machine
//          for a single sink on a single route and is the ONLY place that
//          knows how to turn a batch + DeliveryPolicyConfig into an outcome.
//          RouteWorker calls PublishWithRetryAsync for each dequeued batch.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.4, §19.7
// Milestone: C3 Commit 2 (phase 3).
//
// Scope rules (phase 4):
//   * Retry behavior centers here.
//   * Replay state is owned by this class via a small ReplayCoordinator
//     helper. There is NO side queue: drain uses the same per-sink cursor
//     and buffer that normal publishes use.
//   * Transitions fired through the diagnostics seam:
//       first failure            → OnSinkDegraded
//       first success after fail → OnSinkDraining (BeginDrain)
//       empty dequeue in draining → OnSinkRecovered (via NotifyBufferEmpty)
//   * A recovered sink drains backlog BEFORE resuming hot-path consumption
//     — enforced by the sink loop's in-order cursor, not by this class.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Routing;

internal sealed class SinkPublisher
{
    private readonly string _routeId;
    private readonly ISinkAdapter _sink;
    private readonly DeliveryPolicyConfig _policy;
    private readonly RetryStateMachine _retry;
    private readonly ReplayCoordinator _replay = new();
    private readonly IRoutingEngineDiagnostics _diagnostics;
    private readonly RouteLifecycleManager _lifecycle;
    private bool _isDegraded;

    public SinkPublisher(
        string routeId,
        ISinkAdapter sink,
        DeliveryPolicyConfig policy,
        IRoutingEngineDiagnostics diagnostics,
        RouteLifecycleManager lifecycle)
    {
        _routeId = routeId;
        _sink = sink;
        _policy = policy;
        _retry = new RetryStateMachine(policy);
        _diagnostics = diagnostics;
        _lifecycle = lifecycle;
    }

    public string SinkInstanceId => _sink.InstanceId;
    public int CurrentAttempt => _retry.Attempt;
    public bool IsDegraded => _isDegraded;
    public bool IsDraining => _replay.IsDraining;

    /// <summary>
    /// Publish a batch, applying retry/backoff and delivery-mode rules.
    /// Returns the outcome the caller uses to decide whether to advance the
    /// buffer cursor.
    /// </summary>
    public async Task<PublishOutcome> PublishWithRetryAsync(
        IReadOnlyList<CanonicalDataPoint> batch,
        CancellationToken ct)
    {
        while (true)
        {
            PublishResult result;
            try
            {
                result = await _sink.PublishAsync(batch, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                result = PublishResult.Failed(
                    new Errors.AdapterError
                    {
                        Code = "CORE.SINK_PUBLISH_THREW",
                        Category = Errors.ErrorCategory.Internal,
                        Message = "Publish threw an unhandled exception.",
                        Retryable = true,
                    },
                    TimeSpan.Zero);
            }

            if (result.Success)
            {
                if (_isDegraded)
                {
                    // First success after degradation: enter the drain phase.
                    // OnSinkRecovered is NOT fired here — it fires only when
                    // the sink's cursor catches up with the buffer tail
                    // (see NotifyBufferEmpty). This preserves the
                    // degraded → draining → running invariant.
                    _isDegraded = false;
                    _replay.BeginDrain();
                    _diagnostics.OnSinkDraining(new SinkDrainingEvent
                    {
                        RouteId = _routeId,
                        SinkInstanceId = _sink.InstanceId,
                        ObservedAtUtc = DateTime.UtcNow,
                    });
                    // Route-level transition: delegated to the lifecycle
                    // manager, which owns all route state rules.
                    _lifecycle.NotifySinkDraining(_sink.InstanceId);
                }
                _retry.RecordSuccess();
                return PublishOutcome.Acked;
            }

            // Failed publish. Ask the delivery-mode handler what to do next.
            var outcome = DeliveryModeHandler.HandleFailure(_policy.Mode, _retry);

            if (!_isDegraded)
            {
                _isDegraded = true;
                _diagnostics.OnSinkDegraded(new SinkDegradedEvent
                {
                    RouteId = _routeId,
                    SinkInstanceId = _sink.InstanceId,
                    LastError = result.Error ?? new Errors.AdapterError
                    {
                        Code = "CORE.SINK_PUBLISH_FAILED",
                        Category = Errors.ErrorCategory.Internal,
                        Message = "publish failed (no error detail)",
                    },
                    RetryAttempts = _retry.Attempt,
                    ObservedAtUtc = DateTime.UtcNow,
                });
                // Route-level transition: delegated to the lifecycle manager.
                _lifecycle.NotifySinkDegraded(_sink.InstanceId);
            }

            switch (outcome)
            {
                case PublishOutcome.DroppedAndAdvanced:
                    // AtMostOnce: cursor advances, retry counter is meaningless.
                    _retry.Reset();
                    return PublishOutcome.DroppedAndAdvanced;

                case PublishOutcome.RetryScheduled:
                    var delay = _retry.RecordFailureAndComputeBackoff();
                    try
                    {
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    continue;

                case PublishOutcome.RetriesExhausted:
                    // Leave the cursor where it is; the worker loop will wait
                    // for the next wake and re-attempt with a fresh budget.
                    // NO replay coordination — that is phase 4.
                    _retry.Reset();
                    return PublishOutcome.RetriesExhausted;

                default:
                    throw new InvalidOperationException($"Unexpected outcome {outcome}.");
            }
        }
    }

    /// <summary>
    /// Called by <see cref="RouteWorker"/> when the sink's per-cursor
    /// dequeue returned empty. If the sink is currently draining, this
    /// completes the drain phase and emits <see cref="SinkRecoveredEvent"/>.
    /// Otherwise it is a no-op. This is the single path out of Draining —
    /// the publisher itself never decides it has "caught up" just because
    /// a publish succeeded.
    /// </summary>
    public void NotifyBufferEmpty()
    {
        if (_replay.TryCompleteDrain())
        {
            _diagnostics.OnSinkRecovered(new SinkRecoveredEvent
            {
                RouteId = _routeId,
                SinkInstanceId = _sink.InstanceId,
                ObservedAtUtc = DateTime.UtcNow,
            });
            // Route-level transition: delegated to the lifecycle manager.
            _lifecycle.NotifySinkRecovered(_sink.InstanceId);
        }
    }

}
