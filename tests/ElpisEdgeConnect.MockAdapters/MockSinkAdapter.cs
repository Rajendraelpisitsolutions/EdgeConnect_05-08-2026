// ============================================================================
// File: MockSinkAdapter.cs
// Purpose: Production-shaped ISinkAdapter implementation for D phase 4
//          supervisors and D phase 6 integration scenarios. Mirrors the
//          deterministic-control surface of the existing routing-tests
//          FakeSinkAdapter, but implements the locked ISinkAdapter contract
//          end-to-end (Initialize / Start / Stop / Publish / health).
// Reference: ARCHITECTURE_BLUEPRINT.md §4.3 (LOCKED ISinkAdapter contract)
// Milestone: D — phase 3.
//
// Design rules:
//   * Implements the locked ISinkAdapter contract end-to-end. State
//     transitions follow AdapterStateTransitions.
//   * Failure injection is deterministic:
//       - PublishGate (SemaphoreSlim) — admit N publishes then block.
//       - FailUntilSignaled (TCS) — every publish fails until signaled.
//       - FailNextPublishes counter — drop N then resume.
//       - PublishDelay — kept for backwards compat with old routing tests
//         but PublishGate is preferred for new D-phase tests.
//   * Capabilities only declares Push (no Pull/Browse/Transactional).
//   * Captures published points so D scenarios can assert ordering /
//     completeness.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.MockAdapters;

/// <summary>
/// Deterministic mock sink adapter. Captures every published point in
/// publish order; failure and pacing are controlled via TCS / SemaphoreSlim
/// hooks instead of wall-clock waits.
/// </summary>
public sealed class MockSinkAdapter : ISinkAdapter
{
    private readonly ConcurrentQueue<CanonicalDataPoint> _published = new();
    private long _publishedCount;
    private long _attemptCount;
    private AdapterError? _lastError;
    private DateTime? _lastSuccessAt;

    /// <summary>Construct a mock sink for the given identity.</summary>
    public MockSinkAdapter(string instanceId, string protocolName = "mock")
    {
        InstanceId = instanceId;
        ProtocolName = protocolName;
    }

    /// <inheritdoc/>
    public string InstanceId { get; }

    /// <inheritdoc/>
    public string ProtocolName { get; }

    /// <inheritdoc/>
    public SinkCapabilities Capabilities => SinkCapabilities.Push | SinkCapabilities.TestConnect;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    // ----- Deterministic control surface (intended for D6 scenarios) -----

    /// <summary>
    /// Optional gate that admits one publish per <c>Release()</c>. When
    /// null, publishes run free. Construct with initial count <c>0</c>
    /// to block all publishes until the test signals.
    /// </summary>
    public SemaphoreSlim? PublishGate { get; set; }

    /// <summary>
    /// Optional failure window. While set to a non-completed TCS, every
    /// publish returns a failed <see cref="PublishResult"/>. Complete the
    /// TCS to heal.
    /// </summary>
    public TaskCompletionSource? FailUntilSignaled { get; set; }

    /// <summary>
    /// Counter of upcoming publishes that should fail. Decremented per use.
    /// </summary>
    public int FailNextPublishes { get; set; }

    /// <summary>
    /// Optional artificial wall-clock delay per publish. Prefer
    /// <see cref="PublishGate"/> for D-phase tests; this field is kept
    /// for compatibility with the older routing-test fakes.
    /// </summary>
    public TimeSpan PublishDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// When <c>true</c> (default), every successfully published point is
    /// retained in <see cref="PublishedPoints"/> for test assertions.
    /// Set to <c>false</c> for long-running scenarios (D phase 8 leak
    /// harness) where retaining every point across a 4-hour or 7-day run
    /// would balloon the managed heap into the gigabyte range. The
    /// <see cref="PublishedCount"/> counter increments regardless.
    /// </summary>
    public bool CaptureHistory { get; set; } = true;

    /// <summary>
    /// If set, <see cref="InitializeAsync"/> throws this exception
    /// before doing any other work. Used by M.P2.2 phase 2.a tests to
    /// pin the supervisor's rollback-on-Add-failure path.
    /// </summary>
    public AdapterException? ThrowOnInitialize { get; set; }

    /// <summary>
    /// If set, <see cref="InitializeAsync"/> throws this generic
    /// (non-<see cref="AdapterException"/>) exception. Used to verify
    /// the supervisor's boot-time per-adapter isolation captures it
    /// as HOST.SINK_START_THREW.
    /// </summary>
    public Exception? ThrowOnInitializeGeneric { get; set; }

    /// <summary>
    /// If set, <see cref="StartAsync"/> throws this exception after
    /// <see cref="InitializeAsync"/> succeeds.
    /// </summary>
    public AdapterException? ThrowOnStart { get; set; }

    /// <summary>
    /// Optional barrier. If set, <see cref="InitializeAsync"/> awaits
    /// the task before completing. Used by tests that need to hold
    /// AddAsync mid-flight to prove lifecycle-gate serialization or
    /// dispose-during-Add behavior.
    /// </summary>
    public TaskCompletionSource? InitializeBarrier { get; set; }

    /// <summary>Total publish attempts (successful + failed).</summary>
    public long AttemptCount => Interlocked.Read(ref _attemptCount);

    /// <summary>Total points captured by successful publishes.</summary>
    public long PublishedCount => Interlocked.Read(ref _publishedCount);

    /// <summary>Snapshot of every published point in publish order.</summary>
    public IReadOnlyCollection<CanonicalDataPoint> PublishedPoints => _published.ToArray();

    // ----- ISinkAdapter -----

    /// <inheritdoc/>
    public async Task InitializeAsync(SinkConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        State = AdapterState.Initializing;

        // M.P2.2 phase 2.a test hooks. Throw FIRST (before any state
        // is built) so tests pin the rollback path cleanly.
        if (ThrowOnInitialize is { } adapterEx)
        {
            throw adapterEx;
        }
        if (ThrowOnInitializeGeneric is { } generic)
        {
            throw generic;
        }
        if (InitializeBarrier is { } barrier)
        {
            await barrier.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        State = AdapterState.Initialized;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken ct)
    {
        if (ThrowOnStart is { } adapterEx)
        {
            throw adapterEx;
        }
        State = AdapterState.Running;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken ct)
    {
        State = AdapterState.Stopped;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
    {
        var health = new AdapterHealth
        {
            State = State,
            Level = _lastError is null ? HealthLevel.Healthy : HealthLevel.Degraded,
            CheckedAt = DateTime.UtcNow,
            LastSuccessAt = _lastSuccessAt,
            LastError = _lastError,
        };
        return Task.FromResult(health);
    }

    /// <inheritdoc/>
    public async Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(points);
        Interlocked.Increment(ref _attemptCount);
        var sw = Stopwatch.StartNew();

        // Deterministic throttle preferred; PublishDelay kept for compat.
        if (PublishGate is { } gate)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }
        else if (PublishDelay > TimeSpan.Zero)
        {
            await Task.Delay(PublishDelay, ct).ConfigureAwait(false);
        }

        var window = FailUntilSignaled;
        var shouldFail = window is not null && !window.Task.IsCompleted;

        if (!shouldFail && FailNextPublishes > 0)
        {
            FailNextPublishes--;
            shouldFail = true;
        }

        if (shouldFail)
        {
            var err = new AdapterError
            {
                Code = "MOCK.SINK_FAILED",
                Category = ErrorCategory.Network,
                Message = "deterministic mock sink failure",
                Retryable = true,
            };
            _lastError = err;
            return PublishResult.Failed(err, sw.Elapsed);
        }

        if (CaptureHistory)
        {
            foreach (var p in points)
            {
                _published.Enqueue(p);
            }
        }
        Interlocked.Add(ref _publishedCount, points.Count);
        _lastSuccessAt = DateTime.UtcNow;
        _lastError = null;
        return PublishResult.Successful(points.Count, sw.Elapsed);
    }

    /// <inheritdoc/>
    public Task UpdateCurrentValuesAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SinkConfiguration config, CancellationToken ct)
        => Task.FromResult(ValidationResult.Success());

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        State = AdapterState.Stopped;
        return ValueTask.CompletedTask;
    }
}
