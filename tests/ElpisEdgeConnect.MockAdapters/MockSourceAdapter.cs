// ============================================================================
// File: MockSourceAdapter.cs
// Purpose: Production-shaped ISourceAdapter implementation for D phase 4
//          supervisors and D phase 6 integration scenarios. Emits a
//          deterministic monotonic sequence of canonical points; failure
//          and pacing are controlled via TaskCompletionSource and counter
//          hooks (NEVER wall-clock).
// Reference: ARCHITECTURE_BLUEPRINT.md §4.2 (LOCKED ISourceAdapter contract)
// Milestone: D — phase 3.
//
// Design rules:
//   * Implements the locked ISourceAdapter contract end-to-end. State
//     transitions follow AdapterStateTransitions.
//   * No randomness, no wall-clock dependencies in the failure-injection
//     path. Tests get deterministic control via:
//       - PollGate (SemaphoreSlim) — admit N polls then block until
//         released, OR start at int.MaxValue for "free pass".
//       - FailUntilSignaled (TCS) — every poll throws AdapterException
//         until the TCS is signaled.
//       - FailNextPoll counter — drop N polls then resume.
//       - PointsPerPoll — how many points each successful poll returns.
//       - Stop emitting after a configured total (Complete()-equivalent).
//   * Sequence numbers come from the canonical factory and are strictly
//     monotonic per adapter instance.
//   * BrowseTags and SubscribeAsync are minimal stubs — phase 6 scenarios
//     do not exercise them. Capabilities only declares Polling.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.MockAdapters;

/// <summary>
/// Deterministic mock source adapter. Each <see cref="PollAsync"/> call
/// returns the next batch of <see cref="PointsPerPoll"/> canonical points
/// with strictly monotonic sequence numbers, unless deterministic failure
/// or pacing controls intervene.
/// </summary>
public sealed class MockSourceAdapter : ISourceAdapter
{
    private readonly object _gate = new();
    private CanonicalDataPointFactory? _factory;
    private long _pollCount;
    private long _emittedCount;
    private AdapterError? _lastError;
    private DateTime? _lastSuccessAt;

    /// <summary>Construct a mock source for the given identity.</summary>
    public MockSourceAdapter(
        string instanceId,
        string protocolName = "mock",
        string deviceId = "mock-device",
        string? deviceName = null)
    {
        InstanceId = instanceId;
        ProtocolName = protocolName;
        DeviceId = deviceId;
        DeviceName = deviceName;
    }

    /// <inheritdoc/>
    public string InstanceId { get; }

    /// <inheritdoc/>
    public string ProtocolName { get; }

    /// <summary>The physical device id this mock claims to read from.</summary>
    public string DeviceId { get; }

    /// <summary>Optional device display name.</summary>
    public string? DeviceName { get; }

    /// <inheritdoc/>
    public SourceCapabilities Capabilities => SourceCapabilities.Polling | SourceCapabilities.TestConnect;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    // ----- Deterministic control surface (intended for D6 scenarios) -----

    /// <summary>
    /// Optional gate that admits one poll per <c>Release()</c>. When null,
    /// polls run free. Construct with initial count <c>0</c> to block all
    /// polls until the test signals; <c>int.MaxValue</c> for free pass.
    /// </summary>
    public SemaphoreSlim? PollGate { get; set; }

    /// <summary>
    /// Optional failure window. While set to a non-completed TCS, every
    /// <see cref="PollAsync"/> call throws <see cref="AdapterException"/>.
    /// Complete the TCS to heal.
    /// </summary>
    public TaskCompletionSource? FailUntilSignaled { get; set; }

    /// <summary>
    /// Counter of upcoming polls that should fail. Decremented per use.
    /// Useful for transient failure tests.
    /// </summary>
    public int FailNextPolls { get; set; }

    /// <summary>
    /// Number of canonical points each successful poll returns. Default 1.
    /// Set higher for burst tests.
    /// </summary>
    public int PointsPerPoll { get; set; } = 1;

    /// <summary>
    /// Optional cap on the lifetime emitted-point count. When set, polls
    /// past the cap return an empty batch. Null = unlimited.
    /// </summary>
    public long? StopAfterPoints { get; set; }

    /// <summary>
    /// If set, <see cref="InitializeAsync"/> throws this exception
    /// before doing any other work. Used by M.P2.2 phase 2.a tests to
    /// pin the supervisor's rollback-on-Add-failure path.
    /// </summary>
    public AdapterException? ThrowOnInitialize { get; set; }

    /// <summary>
    /// If set, <see cref="InitializeAsync"/> throws this generic
    /// (non-<see cref="AdapterException"/>) exception. Used to verify
    /// the supervisor propagates non-adapter exceptions cleanly.
    /// </summary>
    public Exception? ThrowOnInitializeGeneric { get; set; }

    /// <summary>
    /// If set, <see cref="StartAsync"/> throws this exception after
    /// <see cref="InitializeAsync"/> succeeds. Used to pin the
    /// rollback path when the adapter init succeeds but start fails.
    /// </summary>
    public AdapterException? ThrowOnStart { get; set; }

    /// <summary>
    /// Optional barrier. If set, <see cref="InitializeAsync"/> awaits
    /// the task before completing. Used by tests that need to hold
    /// AddAsync mid-flight to prove lifecycle-gate serialization or
    /// dispose-during-Add behavior.
    /// </summary>
    public TaskCompletionSource? InitializeBarrier { get; set; }

    /// <summary>Total polls observed (successful + failed).</summary>
    public long PollCount
    {
        get { lock (_gate) { return _pollCount; } }
    }

    /// <summary>Total points emitted across all successful polls.</summary>
    public long EmittedCount
    {
        get { lock (_gate) { return _emittedCount; } }
    }

    // ----- ISourceAdapter -----

    /// <inheritdoc/>
    public async Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
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

        // Build the factory now that we know our identity. Use a stable
        // mock gateway id; tests that need to assert against the gateway
        // id can substitute via a derived class if needed.
        _factory = new CanonicalDataPointFactory(
            gatewayId: "mock-gateway",
            sourceInstanceId: InstanceId,
            protocolName: ProtocolName,
            deviceId: DeviceId,
            deviceName: DeviceName);
        State = AdapterState.Initialized;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken ct)
    {
        if (_factory is null)
        {
            throw new InvalidOperationException(
                $"MockSourceAdapter '{InstanceId}' must be initialized before start.");
        }
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
    public async Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct)
    {
        if (_factory is null || State != AdapterState.Running)
        {
            throw new InvalidOperationException(
                $"MockSourceAdapter '{InstanceId}' is not running.");
        }

        // Deterministic pacing: respect the gate if set.
        if (PollGate is { } gate)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }

        Interlocked.Increment(ref _pollCount);

        // Deterministic failure: TCS-gated.
        var window = FailUntilSignaled;
        if (window is not null && !window.Task.IsCompleted)
        {
            ThrowMockFailure();
        }

        // Deterministic transient failures.
        if (FailNextPolls > 0)
        {
            FailNextPolls--;
            ThrowMockFailure();
        }

        // Stop semantics: when the lifetime cap is hit, return empty.
        lock (_gate)
        {
            if (StopAfterPoints is { } cap && _emittedCount >= cap)
            {
                return Array.Empty<CanonicalDataPoint>();
            }
        }

        var batchSize = PointsPerPoll;
        if (batchSize <= 0)
        {
            return Array.Empty<CanonicalDataPoint>();
        }

        // Honor StopAfterPoints when the batch would exceed the cap.
        if (StopAfterPoints is { } maxPoints)
        {
            lock (_gate)
            {
                var remaining = maxPoints - _emittedCount;
                if (remaining <= 0)
                {
                    return Array.Empty<CanonicalDataPoint>();
                }
                if (remaining < batchSize)
                {
                    batchSize = (int)remaining;
                }
            }
        }

        var now = DateTime.UtcNow;
        var points = new CanonicalDataPoint[batchSize];
        for (var i = 0; i < batchSize; i++)
        {
            points[i] = _factory.CreatePoint(
                tagName: "mock-tag",
                tagPath: $"{DeviceId}/mock-tag",
                value: 0L,
                valueType: CanonicalValueType.Long,
                quality: DataQuality.Good,
                deviceTimestamp: now,
                gatewayTimestamp: now);
        }

        lock (_gate)
        {
            _emittedCount += batchSize;
            _lastSuccessAt = now;
            _lastError = null;
        }

        return points;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Subscription mode is not declared in Capabilities; this stub
        // exists only to satisfy the interface and is not used by the
        // D phase 4-6 supervisors. Yield nothing and return.
        await Task.CompletedTask;
        yield break;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<TagDefinition>>(Array.Empty<TagDefinition>());

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct)
        => Task.FromResult(ValidationResult.Success());

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        State = AdapterState.Stopped;
        return ValueTask.CompletedTask;
    }

    private void ThrowMockFailure()
    {
        var err = new AdapterError
        {
            Code = "MOCK.SOURCE_FAILED",
            Category = ErrorCategory.Network,
            Message = "deterministic mock failure",
            Retryable = true,
        };
        lock (_gate)
        {
            _lastError = err;
        }
        throw new AdapterException(err);
    }
}
