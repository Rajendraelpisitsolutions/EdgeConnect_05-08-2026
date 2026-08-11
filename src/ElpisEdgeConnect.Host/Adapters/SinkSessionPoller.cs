// ============================================================================
// File: Adapters/SinkSessionPoller.cs
// Purpose: BackgroundService that periodically polls every registered
//          sink that implements ISessionTrackingSink and pushes its
//          ActiveSessions snapshot into ISinkSessionHealthSink. The
//          diagnostics surface and Prometheus instruments read live
//          from there.
//
//          Lives in the host (not Core) because Core never references
//          a concrete sink module per Locked Decision #1. The poller
//          is generic — it inspects the SinkRegistration list and
//          dispatches polymorphically through ISessionTrackingSink.
// Reference: ARCHITECTURE_BLUEPRINT.md §13, Locked Decision #1
//            docs/PHASE4_EXECUTION_PLAN.md Milestone H.2
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Periodic poller that snapshots active sessions on every
/// <see cref="ISessionTrackingSink"/> and pushes them to the
/// diagnostics surface via <see cref="ISinkSessionHealthSink"/>.
/// </summary>
public sealed class SinkSessionPoller : BackgroundService
{
    /// <summary>
    /// Default poll cadence. Cheap to read (in-memory snapshot per
    /// adapter), so 5 seconds is plenty fresh for SCADA/MES dashboards
    /// without taxing the GC.
    /// </summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<SinkRegistration> _registrations;
    private readonly ISinkSessionHealthSink _healthSink;
    private readonly ILogger<SinkSessionPoller> _logger;
    private readonly TimeSpan _interval;

    /// <summary>
    /// Construct the poller. <paramref name="registrations"/> is the
    /// full sink list from DI; the poller filters at poll time to
    /// those whose adapter implements
    /// <see cref="ISessionTrackingSink"/>, so the list can grow
    /// without code change.
    /// </summary>
    public SinkSessionPoller(
        IEnumerable<SinkRegistration> registrations,
        ISinkSessionHealthSink healthSink,
        ILogger<SinkSessionPoller> logger,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(healthSink);
        ArgumentNullException.ThrowIfNull(logger);

        _registrations = new List<SinkRegistration>(registrations);
        _healthSink = healthSink;
        _logger = logger;
        _interval = interval ?? DefaultInterval;
    }

    /// <summary>
    /// Cadence at which the poller ticks. Defaults to
    /// <see cref="DefaultInterval"/>.
    /// </summary>
    public TimeSpan Interval => _interval;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Filter registrations once at start — the set doesn't change
        // for the host's lifetime.
        var sessionSinks = new List<(string RouteId, string SinkInstanceId, ISessionTrackingSink Sink)>();
        foreach (var reg in _registrations)
        {
            if (reg.Adapter is ISessionTrackingSink tracking)
            {
                sessionSinks.Add((reg.RouteId, reg.Adapter.InstanceId, tracking));
            }
        }

        if (sessionSinks.Count == 0)
        {
            _logger.LogDebug(
                "SinkSessionPoller: no session-tracking sinks registered; entering idle wait.");
            // Even with zero sinks we honor the lifecycle and just wait for cancellation.
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
            return;
        }

        _logger.LogInformation(
            "SinkSessionPoller: polling {Count} session-tracking sink(s) every {IntervalMs}ms.",
            sessionSinks.Count, _interval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                PollOnce(sessionSinks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SinkSessionPoller: poll iteration failed; will retry next tick.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void PollOnce(IReadOnlyList<(string RouteId, string SinkInstanceId, ISessionTrackingSink Sink)> sessionSinks)
    {
        foreach (var (routeId, sinkInstanceId, sink) in sessionSinks)
        {
            IReadOnlyList<SinkSessionSummary> snapshot;
            try
            {
                snapshot = sink.ActiveSessions ?? Array.Empty<SinkSessionSummary>();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "SinkSessionPoller: ActiveSessions threw for sink '{SinkInstanceId}'; treating as empty.",
                    sinkInstanceId);
                snapshot = Array.Empty<SinkSessionSummary>();
            }

            _healthSink.RecordActiveSessions(routeId, sinkInstanceId, snapshot);
        }
    }
}
