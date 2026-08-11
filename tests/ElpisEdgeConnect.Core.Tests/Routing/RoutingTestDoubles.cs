// ============================================================================
// File: Routing/RoutingTestDoubles.cs
// Purpose: Shared test doubles for routing-engine tests — a channel-backed
//          ISourceIntake, an in-memory ISinkAdapter that records published
//          batches, and an IRouteBufferFactory that always returns
//          InMemoryBuffer regardless of the policy mode.
// Milestone: C3 Commit 2 (phase 1 — happy path).
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Core.Tests.Routing;

internal sealed class FakeSourceIntake : ISourceIntake
{
    private readonly Channel<CanonicalDataPoint> _channel;

    public FakeSourceIntake(string sourceInstanceId, int capacity = 1024)
    {
        SourceInstanceId = sourceInstanceId;
        _channel = Channel.CreateBounded<CanonicalDataPoint>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public string SourceInstanceId { get; }
    public ChannelReader<CanonicalDataPoint> Reader => _channel.Reader;

    public ValueTask WriteAsync(CanonicalDataPoint point, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(point, ct);

    public void Complete() => _channel.Writer.TryComplete();
}

internal sealed class FakeSinkAdapter : ISinkAdapter
{
    private readonly ConcurrentQueue<CanonicalDataPoint> _published = new();
    private long _publishedCount;

    public FakeSinkAdapter(string instanceId, string protocolName = "fake")
    {
        InstanceId = instanceId;
        ProtocolName = protocolName;
    }

    /// <summary>
    /// Optional per-publish delay. Used by multi-sink tests to create a
    /// slow sink. Prefer <see cref="PublishGate"/> for deterministic control.
    /// </summary>
    public TimeSpan PublishDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// DETERMINISTIC throttle. When set, every publish call awaits this
    /// gate before proceeding. Tests can hold the gate closed (publish
    /// blocks), then signal it (publish resumes). Replaces wall-clock
    /// PublishDelay for tests that need strict before/after ordering.
    /// Call <see cref="SemaphoreSlim.Release()"/> once per publish that
    /// should be admitted, or create with initial count = int.MaxValue and
    /// wait-free pass. Null means no gate.
    /// </summary>
    public SemaphoreSlim? PublishGate { get; set; }

    /// <summary>
    /// Number of upcoming publish calls that should return a failed
    /// <see cref="PublishResult"/>. Decremented on each use; transient
    /// failure simulation for retry tests.
    /// </summary>
    public int FailNext { get; set; }

    /// <summary>When true, every publish fails until set false.</summary>
    public bool FailPermanently { get; set; }

    /// <summary>
    /// DETERMINISTIC failure window. When set to a non-completed TCS, every
    /// publish fails until the TCS is completed. After completion, publishes
    /// succeed (or follow other rules). Replaces the FailPermanently flag
    /// toggle for tests that need strict before/after ordering. Null means
    /// no failure window.
    /// </summary>
    public TaskCompletionSource? FailUntilSignaled { get; set; }

    private long _attemptCount;
    public long AttemptCount => Interlocked.Read(ref _attemptCount);

    public string InstanceId { get; }
    public string ProtocolName { get; }
    public SinkCapabilities Capabilities => SinkCapabilities.Push;
    public AdapterState State { get; private set; } = AdapterState.Created;

    public long PublishedCount => Interlocked.Read(ref _publishedCount);
    public IReadOnlyCollection<CanonicalDataPoint> PublishedPoints => _published.ToArray();

    public Task InitializeAsync(SinkConfiguration config, CancellationToken ct)
    {
        State = AdapterState.Initializing;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        State = AdapterState.Running;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        State = AdapterState.Stopped;
        return Task.CompletedTask;
    }

    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
        => Task.FromResult(new AdapterHealth
        {
            State = State,
            Level = HealthLevel.Healthy,
            CheckedAt = DateTime.UtcNow,
        });

    public async Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _attemptCount);
        var sw = Stopwatch.StartNew();

        // Deterministic throttle — preferred over PublishDelay.
        if (PublishGate is { } gate)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }
        else if (PublishDelay > TimeSpan.Zero)
        {
            await Task.Delay(PublishDelay, ct).ConfigureAwait(false);
        }

        // Deterministic failure window — preferred over FailPermanently.
        var gated = FailUntilSignaled;
        var shouldFail = FailPermanently || (gated is not null && !gated.Task.IsCompleted);

        if (!shouldFail && FailNext > 0)
        {
            FailNext--;
            shouldFail = true;
        }

        if (shouldFail)
        {
            return PublishResult.Failed(
                new ElpisEdgeConnect.Core.Errors.AdapterError
                {
                    Code = "TEST.SINK_FAILED",
                    Category = ElpisEdgeConnect.Core.Errors.ErrorCategory.Network,
                    Message = "simulated sink failure",
                    Retryable = true,
                },
                sw.Elapsed);
        }

        foreach (var p in points)
        {
            _published.Enqueue(p);
        }
        Interlocked.Add(ref _publishedCount, points.Count);
        return PublishResult.Successful(points.Count, sw.Elapsed);
    }

    public Task UpdateCurrentValuesAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct) => Task.CompletedTask;

    public Task<ValidationResult> ValidateConfigAsync(
        SinkConfiguration config,
        CancellationToken ct) => Task.FromResult(ValidationResult.Success());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Recording diagnostics used by phase-4 replay tests to pin the order of
/// degraded/draining/recovered events. Thread-safe via a simple lock.
/// </summary>
internal sealed class RecordingRoutingDiagnostics : IRoutingEngineDiagnostics
{
    private readonly object _gate = new();
    private readonly List<string> _events = new();

    public IReadOnlyList<string> Events
    {
        get { lock (_gate) { return _events.ToArray(); } }
    }

    public void OnRouteStateChanged(RouteStateChangedEvent evt)
    {
        lock (_gate) { _events.Add($"route:{evt.RouteId}:{evt.From}->{evt.To}"); }
    }

    public void OnSinkDegraded(SinkDegradedEvent evt)
    {
        lock (_gate) { _events.Add($"degraded:{evt.SinkInstanceId}"); }
    }

    public void OnSinkDraining(SinkDrainingEvent evt)
    {
        lock (_gate) { _events.Add($"draining:{evt.SinkInstanceId}"); }
    }

    public void OnSinkRecovered(SinkRecoveredEvent evt)
    {
        lock (_gate) { _events.Add($"recovered:{evt.SinkInstanceId}"); }
    }

    public void OnBackpressureDropped(BackpressureDroppedEvent evt)
    {
        lock (_gate) { _events.Add($"bp:{evt.RouteId}:{evt.DroppedCount}"); }
    }
}

/// <summary>
/// Minimal <see cref="IReplayAwareSinkAdapter"/> test double for K1.3 registration/wiring
/// tests. The replay lifecycle methods are no-ops (slice 1 never starts the worker); the
/// context-free base <see cref="ISinkAdapter.PublishAsync(IReadOnlyList{CanonicalDataPoint},
/// CancellationToken)"/> throws, since a replay-aware sink is never published to via the base path.
/// </summary>
internal sealed class FakeReplayAwareSink : IReplayAwareSinkAdapter
{
    public FakeReplayAwareSink(string instanceId, string protocolName = "fake-replay")
    {
        InstanceId = instanceId;
        ProtocolName = protocolName;
    }

    public string InstanceId { get; }
    public string ProtocolName { get; }
    public SinkCapabilities Capabilities => SinkCapabilities.Push;
    public AdapterState State { get; private set; } = AdapterState.Created;

    /// <summary>
    /// Test-harness opt-in for slices BEFORE the ReplayRouteDriver exists (K1.3 slice 2): when
    /// true, the base context-free PublishAsync drains successfully instead of throwing, so the
    /// legacy sink loop stays benign while the tracked INTAKE is exercised. Default false honors
    /// the eventual contract (the base path is never used on a replay sink).
    /// </summary>
    public bool DrainViaBasePublish { get; init; }

    public Task InitializeAsync(SinkConfiguration config, CancellationToken ct)
    {
        State = AdapterState.Initializing;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        State = AdapterState.Running;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        State = AdapterState.Stopped;
        return Task.CompletedTask;
    }

    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
        => Task.FromResult(new AdapterHealth { State = State, Level = HealthLevel.Healthy, CheckedAt = DateTime.UtcNow });

    public Task<PublishResult> PublishAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct)
        => DrainViaBasePublish
            ? Task.FromResult(PublishResult.Successful(points.Count, TimeSpan.Zero))
            : throw new NotSupportedException("A replay-aware sink is never published to via the base (context-free) path.");

    public Task UpdateCurrentValuesAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct) => Task.CompletedTask;

    public Task<ValidationResult> ValidateConfigAsync(SinkConfiguration config, CancellationToken ct)
        => Task.FromResult(ValidationResult.Success());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ---- replay lifecycle: recording double for the K1.3 driver (slice 3) ----

    private readonly object _replayLock = new();
    private readonly List<PublishContext> _publishContexts = new();
    private readonly List<CanonicalDataPoint> _publishedReplay = new();
    private readonly List<string> _replayEvents = new();
    private long _publishEntered;

    /// <summary>
    /// When set, each phase-aware publish awaits this gate (before recording/deciding a result) — lets a
    /// test hold a publish IN FLIGHT while it injects an out-of-band rebirth, so ordering is deterministic.
    /// </summary>
    public SemaphoreSlim? ReplayPublishGate { get; set; }

    /// <summary>Count of phase-aware publishes that have ENTERED (before the gate) — an in-flight probe.</summary>
    public long PublishEnteredCount => Interlocked.Read(ref _publishEntered);

    /// <summary>The value of <see cref="PublishEnteredCount"/> captured at the most recent RebirthAsync.</summary>
    public long PublishEnteredAtLastRebirth { get; private set; }

    /// <summary>
    /// Ordered lifecycle log: <c>pub:e{epoch}:{first}-{last}</c> per admitted phase publish and
    /// <c>rebirth:e{epoch}</c> per RebirthAsync — lets a test assert a rebirth precedes the NEXT subrange.
    /// </summary>
    public IReadOnlyList<string> ReplayEvents { get { lock (_replayLock) { return _replayEvents.ToArray(); } } }

    /// <summary>Number of BeginReplaySessionAsync calls.</summary>
    public int BeginCount { get; private set; }

    /// <summary>Number of CompleteCatchUpAsync calls.</summary>
    public int CompleteCatchUpCount { get; private set; }

    /// <summary>Number of phase-aware PublishAsync calls (INCLUDING retries).</summary>
    public int ReplayPublishCallCount { get; private set; }

    /// <summary>
    /// For the next N phase-aware publishes, return a PARTIAL result (Success but Accepted &lt; Count)
    /// so the driver's strict-ack rule rejects it and retries. Decremented per use.
    /// </summary>
    public int PartialNext { get; set; }

    /// <summary>When set, BeginReplaySessionAsync throws this (to test birth-failure route faulting).</summary>
    public Exception? BeginThrows { get; set; }

    /// <summary>When set, RebirthAsync throws this (to test failed-rebirth route faulting + no epoch promotion).</summary>
    public Exception? RebirthThrows { get; set; }

    /// <summary>
    /// When set, RebirthAsync awaits this gate before completing — lets a test hold the DATA path
    /// paused mid-rebirth while it appends more source points (proving intake continues meanwhile).
    /// </summary>
    public SemaphoreSlim? RebirthGate { get; set; }

    /// <summary>When set, every phase-aware publish returns this result (e.g. a non-retryable failure).</summary>
    public PublishResult? PublishResultOverride { get; set; }

    /// <summary>
    /// When true, the NEXT phase-aware publish models a first-observed metric (A2/C3): it awaits
    /// <see cref="IReplaySessionHost.RequestRebirthAsync"/> for the current session/epoch RETURNING,
    /// then returns a PARTIAL result so the driver acks nothing and processes the rebirth first. The
    /// flag self-clears after firing.
    /// </summary>
    public bool RequestRebirthOnNextPublish { get; set; }

    /// <summary>Number of RebirthAsync calls.</summary>
    public int RebirthCount { get; private set; }

    /// <summary>The most recent RebirthAsync inputs, or null.</summary>
    public ReplaySessionRebirth? LastRebirth { get; private set; }

    /// <summary>The host captured from the most recent BeginReplaySessionAsync (the reverse rebirth channel).</summary>
    public IReplaySessionHost? LastHost { get; private set; }

    /// <summary>The session id from the most recent BeginReplaySessionAsync, or null.</summary>
    public ReplaySessionId? LastSessionId { get; private set; }

    /// <summary>The contexts of every phase-aware publish call (including retries), in order.</summary>
    public IReadOnlyList<PublishContext> PublishContexts { get { lock (_replayLock) { return _publishContexts.ToArray(); } } }

    /// <summary>Points delivered via a FULLY-successful phase-aware publish (not partial retries).</summary>
    public IReadOnlyList<CanonicalDataPoint> ReplayPublishedPoints { get { lock (_replayLock) { return _publishedReplay.ToArray(); } } }

    public Task BeginReplaySessionAsync(ReplaySessionStart start, CancellationToken ct)
    {
        lock (_replayLock) { BeginCount++; LastSessionId = start.SessionId; LastHost = start.Host; }
        if (BeginThrows is { } ex)
        {
            throw ex;
        }

        return Task.CompletedTask;
    }

    public async Task RebirthAsync(ReplaySessionRebirth rebirth, CancellationToken ct)
    {
        lock (_replayLock)
        {
            RebirthCount++;
            LastRebirth = rebirth;
            PublishEnteredAtLastRebirth = Interlocked.Read(ref _publishEntered);
            _replayEvents.Add($"rebirth:e{rebirth.Epoch.Value}");
        }

        if (RebirthGate is { } gate)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }

        if (RebirthThrows is { } ex)
        {
            throw ex;
        }
    }

    public async Task<PublishResult> PublishAsync(IReadOnlyList<CanonicalDataPoint> points, PublishContext context, CancellationToken ct)
    {
        Interlocked.Increment(ref _publishEntered);
        if (ReplayPublishGate is { } gate)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }

        lock (_replayLock)
        {
            ReplayPublishCallCount++;
            _publishContexts.Add(context);
            _replayEvents.Add($"pub:e{context.Epoch.Value}:{context.BatchFirstSequence}-{context.BatchLastSequence}");

            if (PublishResultOverride is { } forced)
            {
                return forced;
            }

            if (RequestRebirthOnNextPublish)
            {
                // Fall through past the lock to the async first-observed-metric path below.
                RequestRebirthOnNextPublish = false;
            }
            else if (PartialNext > 0)
            {
                PartialNext--;
                return Partial(points); // fails the driver's strict-ack rule (Accepted != Count)
            }
            else
            {
                _publishedReplay.AddRange(points);
                return PublishResult.Successful(points.Count, TimeSpan.Zero);
            }
        }

        // First-observed-metric path (A2/C3): the adapter awaits RequestRebirthAsync RETURNING (the
        // request is queued on the host) BEFORE returning its not-full-success result — the driver's
        // happens-before. It then reports PARTIAL for this subrange (the metric is not yet in the birth
        // catalogue), so the driver acks nothing and processes the rebirth first.
        var host = LastHost ?? throw new InvalidOperationException(
            "No host captured — BeginReplaySessionAsync must run before a rebirth-triggering publish.");
        await host.RequestRebirthAsync(
            RebirthRequest.Create(context.SessionId, context.Epoch, RebirthReason.SchemaChange, "first-observed metric"), ct)
            .ConfigureAwait(false);
        return Partial(points);
    }

    private static PublishResult Partial(IReadOnlyList<CanonicalDataPoint> points) => new()
    {
        Success = true,
        AcceptedCount = Math.Max(0, points.Count - 1),
        RejectedCount = 1,
        Latency = TimeSpan.Zero,
    };

    public Task CompleteCatchUpAsync(ReplaySessionCutover cutover, CancellationToken ct)
    {
        lock (_replayLock) { CompleteCatchUpCount++; }
        return Task.CompletedTask;
    }

    /// <summary>Number of EndSessionAsync calls (must be exactly 1 for a begun session on a graceful stop).</summary>
    public int EndSessionCount { get; private set; }

    /// <summary>The reason from the most recent EndSessionAsync, or null.</summary>
    public ReplaySessionEndReason? LastEndReason { get; private set; }

    /// <summary>When set, EndSessionAsync throws this (to test End-failure isolation during shutdown).</summary>
    public Exception? EndThrows { get; set; }

    /// <summary>When true, EndSessionAsync blocks until ITS token is cancelled (to test the End bound).</summary>
    public bool EndBlocksUntilCancelled { get; set; }

    /// <summary>Set true when a blocking EndSessionAsync observed ITS OWN token being cancelled (the bound fired).</summary>
    public bool EndTokenObservedCancellation { get; private set; }

    /// <summary>When set, EndSessionAsync awaits this gate before completing (to hold the route Stopping).</summary>
    public SemaphoreSlim? EndGate { get; set; }

    public async Task EndSessionAsync(ReplaySessionEnd sessionEnd, CancellationToken ct)
    {
        lock (_replayLock) { EndSessionCount++; LastEndReason = sessionEnd.Reason; }
        if (EndThrows is { } ex)
        {
            throw ex;
        }

        if (EndBlocksUntilCancelled)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                EndTokenObservedCancellation = true;
                throw;
            }
        }

        if (EndGate is { } gate)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }
    }
}

internal sealed class InMemoryRouteBufferFactory : IRouteBufferFactory
{
    public Task<IMessageBuffer> CreateAsync(
        string routeId,
        BufferPolicy policy,
        CancellationToken cancellationToken)
    {
        var effective = policy with { Mode = ElpisEdgeConnect.Core.Configuration.BufferMode.InMemory };
        IMessageBuffer buffer = new InMemoryBuffer(routeId, effective);
        return Task.FromResult(buffer);
    }
}

internal static class RoutingTestData
{
    public const string GatewayId = "gw-test";
    public const string SourceInstanceId = "src-test";

    public static CanonicalDataPoint MakePoint(long sequence, string tag = "Spindle/Load", double value = 42.0)
    {
        var now = DateTime.UtcNow;
        return new CanonicalDataPoint
        {
            GatewayId = GatewayId,
            SourceInstanceId = SourceInstanceId,
            ProtocolName = "fake",
            DeviceId = "dev1",
            TagName = tag,
            TagPath = tag,
            Value = value,
            ValueType = CanonicalValueType.Double,
            Quality = DataQuality.Good,
            DeviceTimestamp = now,
            GatewayTimestamp = now,
            SequenceNumber = sequence,
        };
    }

    public static BufferPolicy DefaultBufferPolicy() => new()
    {
        Mode = ElpisEdgeConnect.Core.Configuration.BufferMode.InMemory,
        MaxDepth = 10_000,
        DropPolicy = ElpisEdgeConnect.Core.Configuration.DropPolicy.Block,
    };

    public static ElpisEdgeConnect.Core.Configuration.DeliveryPolicyConfig DefaultDeliveryPolicy() => new();
}
