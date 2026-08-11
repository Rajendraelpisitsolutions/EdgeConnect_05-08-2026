// ============================================================================
// File: Diagnostics/ModbusDiagnosticsSnapshot.cs
// Purpose: Top-level diagnostics snapshot returned by
//          ModbusDiagnosticsCollector.Snapshot(). Holds every per-block
//          snapshot plus a global (FC, exceptionCode) aggregation.
// Reference: docs/PHASE3_EXECUTION_PLAN.md F5
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Diagnostics;

/// <summary>
/// Immutable, eventually-consistent snapshot of all runtime metrics the
/// Modbus adapter has collected. See
/// <see cref="ModbusBlockMetricsSnapshot"/> for per-block fields — this
/// record carries only the global slices.
/// </summary>
public sealed record ModbusDiagnosticsSnapshot
{
    /// <summary>Per-block snapshots, one per configured <see cref="ScanBlockKey"/>.</summary>
    public required IReadOnlyList<ModbusBlockMetricsSnapshot> Blocks { get; init; }

    /// <summary>
    /// Count of slave-exception responses observed across all blocks, keyed
    /// by <c>"0xFC/0xEC"</c> (e.g. <c>"0x03/0x02"</c> for FC03 Illegal Data
    /// Address). Stable, Prometheus-label-safe strings.
    /// </summary>
    public required IReadOnlyDictionary<string, long> SlaveExceptionsByCode { get; init; }
}
