// ============================================================================
// File: Diagnostics/DiagnosticsConstants.cs
// Purpose: Default capacities and naming conventions for the diagnostics
//          collector. Centralizing them here keeps the magic numbers out of
//          the collector and the tests, and gives a single place to revise
//          when the management API tunes them.
// Reference: ARCHITECTURE_BLUEPRINT.md §13
// Milestone: C4 — phase 1.
// ============================================================================

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Locked defaults for the diagnostics collector.
/// </summary>
public static class DiagnosticsConstants
{
    /// <summary>Default ring-buffer size for per-route lifecycle events.</summary>
    public const int DefaultRouteEventRetention = 256;

    /// <summary>Default ring-buffer size for per-sink degraded/draining/recovered events.</summary>
    public const int DefaultSinkEventRetention = 128;

    /// <summary>Default ring-buffer size for per-route backpressure drop events.</summary>
    public const int DefaultBackpressureEventRetention = 128;

    /// <summary>Hard ceiling on any retention setting (defensive).</summary>
    public const int MaxEventRetention = 4096;

    /// <summary>
    /// Meter name used by the C4 metric instruments. Phase 5 wires this
    /// up to <c>System.Diagnostics.Metrics</c>; the host's Prometheus
    /// exporter (D1) uses this name as the source.
    /// </summary>
    public const string MeterName = "Elpis.EdgeConnect.Core";

    /// <summary>Instrument-name prefix for collector counters.</summary>
    public const string InstrumentPrefix = "elpis_edgeconnect_";
}
