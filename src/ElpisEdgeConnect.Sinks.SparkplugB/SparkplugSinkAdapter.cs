// ============================================================================
// File: SparkplugSinkAdapter.cs
// Purpose: The thin IReplayAwareSinkAdapter façade for the Sparkplug B sink
//          (plan v3 §1.1/§B2). Holds NO protocol state: it forwards every call
//          into the sole-owner SparkplugSessionActor and exposes the actor's
//          state. Fails closed on the base (context-free) publish path and on the
//          pull path — a replay-aware push sink is only ever published to via the
//          PublishContext overload (plan v3 §8). Advertises the LocalTransport
//          delivery capability (QoS-0 Sparkplug; K4 consumes it for route
//          delivery-boundary validation).
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.1, §8, §9.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Sinks.SparkplugB.Configuration;
using ElpisEdgeConnect.Sinks.SparkplugB.Session;

namespace ElpisEdgeConnect.Sinks.SparkplugB;

/// <summary>
/// Sparkplug B sink adapter. A replay-aware, push-only, single-owner session
/// actor for one Edge Node. The façade implements the Core contract surface and
/// delegates all behavior to <see cref="SparkplugSessionActor"/>.
/// </summary>
public sealed class SparkplugSinkAdapter : IReplayAwareSinkAdapter
{
    /// <summary>
    /// The advertised delivery capability: durable store-and-forward with a
    /// local-transport acknowledgment boundary (Sparkplug B v1 publishes at
    /// QoS 0, so a broker/application ack cannot be offered). K4 reads this for
    /// route delivery-boundary validation.
    /// </summary>
    public static readonly DeliveryCapabilities AdvertisedDeliveryCapabilities = new()
    {
        SupportsStoreAndForward = true,
        AcknowledgementBoundary = DeliveryAcknowledgementBoundary.LocalTransport,
    };

    private readonly SparkplugSessionActor _actor;

    /// <summary>
    /// Construct a lifecycle-only adapter (no identity store — cannot begin a session).
    /// INTERNAL: a public adapter must be wired with a store (slice-4 review B1), so it never
    /// reports Running/Healthy while structurally unable to begin a session.
    /// </summary>
    /// <param name="instanceId">The sink instance id (non-empty).</param>
    internal SparkplugSinkAdapter(string instanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        InstanceId = instanceId;
        _actor = new SparkplugSessionActor(instanceId);
    }

    /// <summary>
    /// Construct a production adapter with the injected gateway identity store (K4). The store's
    /// eager construction is its readiness; the actor uses the real MQTTnet transport.
    /// </summary>
    /// <param name="instanceId">The sink instance id (non-empty).</param>
    /// <param name="store">The gateway identity store singleton (owned by K4; never disposed here).</param>
    public SparkplugSinkAdapter(string instanceId, Store.ISparkplugIdentityStateStore store)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        ArgumentNullException.ThrowIfNull(store);
        InstanceId = instanceId;
        _actor = new SparkplugSessionActor(instanceId, store);
    }

    /// <summary>Test seam: construct the façade over a supplied actor.</summary>
    /// <param name="instanceId">The sink instance id (non-empty).</param>
    /// <param name="actor">The session actor to delegate to.</param>
    internal SparkplugSinkAdapter(string instanceId, SparkplugSessionActor actor)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        ArgumentNullException.ThrowIfNull(actor);
        InstanceId = instanceId;
        _actor = actor;
    }

    /// <inheritdoc/>
    public string InstanceId { get; }

    /// <inheritdoc/>
    public string ProtocolName => SparkplugBProtocol.ProtocolName;

    /// <inheritdoc/>
    public SinkCapabilities Capabilities => SinkCapabilities.Push;

    /// <inheritdoc/>
    public AdapterState State => _actor.State;

    /// <summary>The protocol substate (diagnostics; mirrors the session actor).</summary>
    public SparkplugProtocolState ProtocolState => _actor.ProtocolState;

    /// <inheritdoc/>
    public Task InitializeAsync(SinkConfiguration config, CancellationToken ct)
        => _actor.InitializeAsync(config, ct);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken ct)
        => _actor.StartAsync(ct);

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken ct)
        => _actor.StopAsync(ct);

    /// <inheritdoc/>
    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
        => _actor.CheckHealthAsync(ct);

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SinkConfiguration config, CancellationToken ct)
        => ct.IsCancellationRequested
            ? Task.FromCanceled<ValidationResult>(ct)
            : Task.FromResult(SparkplugSinkConfigurationValidator.Validate(config));

    /// <summary>
    /// Fail closed. A replay-aware sink is NEVER published to through the base
    /// context-free path — Core always uses the <see cref="PublishContext"/>
    /// overload (plan v3 §8). Routing this sink here is a wiring defect.
    /// </summary>
    /// <param name="points">Ignored.</param>
    /// <param name="ct">Ignored.</param>
    /// <returns>Never returns; always throws.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Task<PublishResult> PublishAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct)
        => throw new NotSupportedException(
            "SparkplugSinkAdapter is replay-aware; publish via the PublishContext overload, never the base path.");

    /// <summary>
    /// Fail closed. Sparkplug B is a push sink; there is no pull/current-value
    /// surface.
    /// </summary>
    /// <param name="points">Ignored.</param>
    /// <param name="ct">Ignored.</param>
    /// <returns>Never returns; always throws.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Task UpdateCurrentValuesAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct)
        => throw new NotSupportedException("SparkplugSinkAdapter is push-only; UpdateCurrentValues is not supported.");

    /// <inheritdoc/>
    public Task BeginReplaySessionAsync(ReplaySessionStart start, CancellationToken cancellationToken)
        => _actor.BeginReplaySessionAsync(start, cancellationToken);

    /// <inheritdoc/>
    public Task RebirthAsync(ReplaySessionRebirth rebirth, CancellationToken cancellationToken)
        => _actor.RebirthAsync(rebirth, cancellationToken);

    /// <inheritdoc/>
    public Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points, PublishContext context, CancellationToken cancellationToken)
        => _actor.PublishAsync(points, context, cancellationToken);

    /// <inheritdoc/>
    public Task CompleteCatchUpAsync(ReplaySessionCutover cutover, CancellationToken cancellationToken)
        => _actor.CompleteCatchUpAsync(cutover, cancellationToken);

    /// <inheritdoc/>
    public Task EndSessionAsync(ReplaySessionEnd sessionEnd, CancellationToken cancellationToken)
        => _actor.EndSessionAsync(sessionEnd, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
        => _actor.DisposeAsync();
}
