// ============================================================================
// File: Eremos/SlowSinkDecorator.cs
// Purpose: Test-only ISinkAdapter wrapper that artificially delays
//          PublishAsync calls to inject backpressure for the EREMOS V2
//          revalidation Gate 8 (sink backpressure behaviour) per v2 plan
//          §6.4.
//
//          The decorator passes every other ISinkAdapter call straight
//          through to the wrapped sink — only PublishAsync receives the
//          configurable delay. This isolates the sink-slowness scenario
//          from broker outages (which use DedicatedTestBroker.Stop) and
//          adapter-lifecycle issues (which would surface in InitializeAsync
//          / StartAsync / StopAsync).
//
//          The delay value is settable at runtime via the
//          PerPublishDelayMs property — tests can leave it 0 during the
//          steady-state phase, set it to 100ms+ during the backpressure
//          phase, then drop it back to 0 to verify recovery.
//
// Reference: docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md §4.2.3 + §6.4
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Integration.Tests.Eremos;

/// <summary>
/// Wraps an <see cref="ISinkAdapter"/> and adds an artificial delay to
/// every <see cref="PublishAsync"/> call. Used to inject deterministic
/// backpressure for Gate 8 in the EREMOS V2 revalidation.
/// </summary>
/// <remarks>
/// All non-publish ISinkAdapter members are pass-through to the inner
/// sink — InstanceId, ProtocolName, Capabilities, State, InitializeAsync,
/// StartAsync, StopAsync, CheckHealthAsync, UpdateCurrentValuesAsync,
/// ValidateConfigAsync, DisposeAsync. PublishAsync is the only method
/// the decorator perturbs.
/// </remarks>
public sealed class SlowSinkDecorator : ISinkAdapter
{
    private readonly ISinkAdapter _inner;
    private int _perPublishDelayMs;
    private long _publishCount;

    public SlowSinkDecorator(ISinkAdapter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>
    /// The per-publish delay in milliseconds. Default 0 (pass-through).
    /// Settable at runtime — set to e.g. 100ms during the backpressure
    /// phase, then reset to 0 to verify recovery.
    /// </summary>
    public int PerPublishDelayMs
    {
        get => Volatile.Read(ref _perPublishDelayMs);
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    "PerPublishDelayMs must be non-negative.");
            }
            Volatile.Write(ref _perPublishDelayMs, value);
        }
    }

    /// <summary>
    /// Total PublishAsync invocations on this decorator. Useful for
    /// asserting the routing engine kept calling Publish even while the
    /// sink was slow.
    /// </summary>
    public long PublishCount => Interlocked.Read(ref _publishCount);

    // ─── Pass-through identity / state ────────────────────────────────

    public string InstanceId => _inner.InstanceId;
    public string ProtocolName => _inner.ProtocolName;
    public SinkCapabilities Capabilities => _inner.Capabilities;
    public AdapterState State => _inner.State;

    // ─── Pass-through lifecycle ────────────────────────────────────────

    public Task InitializeAsync(SinkConfiguration config, CancellationToken ct) =>
        _inner.InitializeAsync(config, ct);

    public Task StartAsync(CancellationToken ct) => _inner.StartAsync(ct);

    public Task StopAsync(CancellationToken ct) => _inner.StopAsync(ct);

    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct) => _inner.CheckHealthAsync(ct);

    public Task<ValidationResult> ValidateConfigAsync(SinkConfiguration config, CancellationToken ct) =>
        _inner.ValidateConfigAsync(config, ct);

    public Task UpdateCurrentValuesAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct) =>
        _inner.UpdateCurrentValuesAsync(points, ct);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    // ─── The one method the decorator perturbs ─────────────────────────

    /// <summary>
    /// Publishes a batch via the inner sink, with an artificial
    /// <see cref="PerPublishDelayMs"/>-millisecond delay BEFORE the inner
    /// call. The delay honours the cancellation token so a Stop during
    /// the delay window returns promptly with <see cref="OperationCanceledException"/>
    /// — matching the inner adapter's existing cancellation behaviour.
    /// </summary>
    public async Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _publishCount);

        var delay = PerPublishDelayMs;
        if (delay > 0)
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        return await _inner.PublishAsync(points, ct).ConfigureAwait(false);
    }
}
