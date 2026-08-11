// ============================================================================
// File: Diagnostics/DiagnosticsCollectorOptions.cs
// Purpose: Tunable retention sizes for the diagnostics collector. Defaults
//          come from DiagnosticsConstants. Capped at MaxEventRetention so
//          a misconfigured value cannot blow up memory.
// Reference: ARCHITECTURE_BLUEPRINT.md §13
// Milestone: C4 — phase 2.
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Construction-time tuning for <see cref="RuntimeDiagnosticsCollector"/>.
/// </summary>
public sealed record DiagnosticsCollectorOptions
{
    /// <summary>Per-route ring buffer for <c>RouteStateChangedEvent</c>.</summary>
    public int RouteEventRetention { get; init; } =
        DiagnosticsConstants.DefaultRouteEventRetention;

    /// <summary>Per-sink ring buffer for degraded/draining/recovered events.</summary>
    public int SinkEventRetention { get; init; } =
        DiagnosticsConstants.DefaultSinkEventRetention;

    /// <summary>Per-route ring buffer for <c>BackpressureDroppedEvent</c>.</summary>
    public int BackpressureEventRetention { get; init; } =
        DiagnosticsConstants.DefaultBackpressureEventRetention;

    /// <summary>Default options instance — uses <see cref="DiagnosticsConstants"/> values.</summary>
    public static DiagnosticsCollectorOptions Default { get; } = new();

    /// <summary>
    /// Validate and clamp the options to the documented range. Returns a
    /// new instance with any out-of-range values replaced by clamped ones.
    /// Throws <see cref="ArgumentOutOfRangeException"/> on negative values.
    /// </summary>
    public DiagnosticsCollectorOptions Validate()
    {
        // Report the exact property that failed validation rather than
        // attributing every out-of-range error to RouteEventRetention.
        // Operators debugging a misconfigured host otherwise get a
        // misleading paramName in the exception message.
        if (RouteEventRetention <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RouteEventRetention),
                RouteEventRetention,
                $"{nameof(RouteEventRetention)} must be > 0.");
        }
        if (SinkEventRetention <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SinkEventRetention),
                SinkEventRetention,
                $"{nameof(SinkEventRetention)} must be > 0.");
        }
        if (BackpressureEventRetention <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BackpressureEventRetention),
                BackpressureEventRetention,
                $"{nameof(BackpressureEventRetention)} must be > 0.");
        }
        return new DiagnosticsCollectorOptions
        {
            RouteEventRetention = Math.Min(RouteEventRetention, DiagnosticsConstants.MaxEventRetention),
            SinkEventRetention = Math.Min(SinkEventRetention, DiagnosticsConstants.MaxEventRetention),
            BackpressureEventRetention =
                Math.Min(BackpressureEventRetention, DiagnosticsConstants.MaxEventRetention),
        };
    }
}
