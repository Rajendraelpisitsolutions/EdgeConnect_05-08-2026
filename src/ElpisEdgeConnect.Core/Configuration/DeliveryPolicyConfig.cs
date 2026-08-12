// ============================================================================
// File: Configuration/DeliveryPolicyConfig.cs
// Purpose: Per-route delivery policy configuration.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.4, §19.4 (Retry), §19.7 (Delivery modes)
// Milestones: B1 (initial shape) + C3 commit 1 (extended for routing engine)
//
// C3 commit 1 extension (additive — no shape removal):
//   - Renamed RetryBackoffMs → InitialBackoffMs to match the C3 routing
//     engine's exponential backoff vocabulary. Default lowered from 1000 ms
//     to 100 ms per the C3 design review's locked defaults. There are no
//     existing consumers of the old name; the rename is safe.
//   - Added MaxBackoffMs   (cap on the exponential growth)
//   - Added BackoffMultiplier (the exponential factor)
//   - Added JitterPercent   (randomized jitter to prevent thundering herd)
//   - Cross-record validator rule 12 enforces the field ranges and the
//     relationship MaxBackoffMs >= InitialBackoffMs.
// ============================================================================

using System.ComponentModel.DataAnnotations;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Per-route delivery policy. Controls delivery semantics, retry / backoff
/// behavior, and fanout parallelism. Used by the C3 routing engine to drive
/// <c>SinkPublisher</c> retry state machines and the
/// <c>FanoutDispatcher</c>.
/// </summary>
public sealed record DeliveryPolicyConfig
{
    /// <summary>
    /// Delivery semantic for the route. Default
    /// <see cref="DeliveryMode.AtLeastOnce"/> per blueprint §19.7.
    /// <see cref="DeliveryMode.AtMostOnce"/> requires
    /// <see cref="BufferMode.None"/> (cross-record rule 7).
    /// <c>ExactlyOnce</c> is intentionally NOT a member of
    /// <see cref="DeliveryMode"/> per blueprint §19.7 — not supported in v1.
    /// The rejection is enforced at the enum level (a pinned B1 test
    /// asserts the absence of an <c>ExactlyOnce</c> member); no validator
    /// rule is required.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public DeliveryMode Mode { get; init; } = DeliveryMode.AtLeastOnce;

    /// <summary>
    /// The acknowledgment boundary this route requires (blueprint §19.7, ADR-0036).
    /// Default <see cref="DeliveryAcknowledgementBoundary.None"/> so configurations
    /// written before this field deserialize without failure and retain their
    /// existing runtime behavior. Route validation rejects a route whose required
    /// boundary exceeds the sink's advertised
    /// <see cref="DeliveryCapabilities.AcknowledgementBoundary"/> — e.g. a
    /// <see cref="DeliveryAcknowledgementBoundary.Broker"/> requirement on a
    /// local-transport-only sink (Sparkplug B v1, QoS 0). New or edited
    /// <see cref="DeliveryMode.AtLeastOnce"/> routes should set this explicitly; a
    /// UI renders <see cref="DeliveryAcknowledgementBoundary.None"/> as
    /// "acknowledgment boundary not specified", never as an unqualified guarantee.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public DeliveryAcknowledgementBoundary RequiredAcknowledgementBoundary { get; init; } =
        DeliveryAcknowledgementBoundary.None;

    /// <summary>
    /// Maximum number of retry attempts per failed batch before the sink is
    /// marked <c>Degraded</c> per blueprint §19.4. Default 5. Set to 0 to
    /// disable retry (the sink degrades on the first failure).
    /// </summary>
    [Range(0, 100, ErrorMessage = "MaxRetries must be between 0 and 100.")]
    [BundleTier(BundleTier.Include)]
    public int MaxRetries { get; init; } = 5;

    /// <summary>
    /// Initial retry backoff in milliseconds. The retry state machine
    /// applies exponential backoff starting from this value, capped at
    /// <see cref="MaxBackoffMs"/> and randomized by <see cref="JitterPercent"/>.
    /// Default 100 ms.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "InitialBackoffMs must be at least 1.")]
    [BundleTier(BundleTier.Include)]
    public int InitialBackoffMs { get; init; } = 100;

    /// <summary>
    /// Cap on the exponential backoff curve in milliseconds. The backoff
    /// will not exceed this value regardless of attempt count. Must be
    /// greater than or equal to <see cref="InitialBackoffMs"/> (cross-record
    /// rule 12). Default 30,000 ms (30 seconds).
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "MaxBackoffMs must be at least 1.")]
    [BundleTier(BundleTier.Include)]
    public int MaxBackoffMs { get; init; } = 30_000;

    /// <summary>
    /// Multiplier applied to the previous backoff to compute the next one.
    /// Standard exponential backoff uses 2.0. Must be at least 1.0. Default 2.0.
    /// </summary>
    [Range(typeof(double), "1.0", "10.0",
        ErrorMessage = "BackoffMultiplier must be between 1.0 and 10.0.")]
    [BundleTier(BundleTier.Include)]
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Randomized jitter applied to each backoff interval, expressed as a
    /// percentage of the computed backoff. A value of 10 means the actual
    /// delay is uniform in <c>[backoff × 0.9, backoff × 1.1]</c>. Default 10.
    /// </summary>
    [Range(0, 100, ErrorMessage = "JitterPercent must be between 0 and 100.")]
    [BundleTier(BundleTier.Include)]
    public int JitterPercent { get; init; } = 10;

    /// <summary>
    /// True if the routing engine should publish to fanout sinks in parallel
    /// rather than sequentially. Default <c>true</c>. Per blueprint §19.2,
    /// fanout is independent per sink regardless of this flag — this controls
    /// only whether the dispatch happens concurrently or one-after-another.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public bool FanoutParallel { get; init; } = true;
}
