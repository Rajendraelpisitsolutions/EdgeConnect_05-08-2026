// ============================================================================
// File: Diagnostics/ModbusBlockMetricsSnapshot.cs
// Purpose: Immutable, JSON-friendly snapshot of a single block's runtime
//          metrics. Returned from ModbusDiagnosticsCollector.Snapshot() and
//          flattened into AdapterHealth.Metrics by the adapter.
//
// SNAPSHOT SEMANTICS:
//   Snapshots are eventually consistent, NOT transactionally atomic.
//   Under heavy poll load a snapshot may briefly show tx count N while
//   the mean RTT reflects the first N-1 samples. This is by design —
//   diagnostics never block the poll loop. Treat fields as "approximate
//   at this instant" rather than "all captured at the same logical tick."
// Reference: docs/PHASE3_EXECUTION_PLAN.md F5
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Diagnostics;

/// <summary>
/// Immutable snapshot of one <see cref="ScanBlockKey"/>'s metrics. Fields
/// are JSON-primitive so the snapshot flattens cleanly into
/// <see cref="ElpisEdgeConnect.Core.Adapters.AdapterHealth.Metrics"/>.
/// </summary>
/// <remarks>
/// Failure counters partition exactly: <c>TransportErrors + SlaveExceptions
/// + DecodeErrors == Failures</c> (decode errors come from the adapter's
/// post-transaction decoding step, not the executor).
/// </remarks>
public sealed record ModbusBlockMetricsSnapshot
{
    /// <summary>The block this snapshot belongs to.</summary>
    public required ScanBlockKey Key { get; init; }

    /// <summary>Total transactions dispatched against this block.</summary>
    public required long Transactions { get; init; }

    /// <summary>Transactions the executor returned <c>IsSuccess = true</c> for.</summary>
    public required long Successes { get; init; }

    /// <summary>Transactions that ended in any kind of failure.</summary>
    public required long Failures { get; init; }

    /// <summary>Cumulative number of per-transaction retries the executor performed.</summary>
    public required long Retries { get; init; }

    /// <summary>Failures classified as transport errors (timeout, socket, connect).</summary>
    public required long TransportErrors { get; init; }

    /// <summary>Failures caused by a slave-returned exception code (0x01..0x0B).</summary>
    public required long SlaveExceptions { get; init; }

    /// <summary>Decode-time failures (bad byte order, truncated payload, bad datatype at runtime).</summary>
    public required long DecodeErrors { get; init; }

    /// <summary>Mean of every successful-transaction RTT seen since the collector was built (Welford).</summary>
    public double? RttMeanMs { get; init; }

    /// <summary>Smallest successful-transaction RTT seen.</summary>
    public double? RttMinMs { get; init; }

    /// <summary>Largest successful-transaction RTT seen.</summary>
    public double? RttMaxMs { get; init; }

    /// <summary>
    /// Approximate p95 of the last N successful-transaction RTTs (N defaults
    /// to 100 in the collector). Degrades gracefully — below N samples it's
    /// a simple percentile of whatever is present.
    /// </summary>
    public double? RttP95Ms { get; init; }

    /// <summary>RTT of the most recent successful transaction — "what's happening right now".</summary>
    public double? RttLatestMs { get; init; }

    /// <summary>UTC timestamp of the most recent success, or <see langword="null"/> if none yet.</summary>
    public DateTime? LastSuccessAt { get; init; }

    /// <summary>UTC timestamp of the most recent failure, or <see langword="null"/> if none yet.</summary>
    public DateTime? LastFailureAt { get; init; }

    /// <summary>
    /// Stable error code of the most recent failure (e.g. <c>MODBUS.TIMEOUT</c>).
    /// Only the code is carried so Prometheus-label cardinality stays bounded —
    /// full error messages live in the logs, not here.
    /// </summary>
    public string? LastErrorCode { get; init; }

    /// <summary>Error category of the most recent failure (e.g. <c>Network</c>).</summary>
    public string? LastErrorCategory { get; init; }
}
