// ============================================================================
// File: Diagnostics/ScanBlockKey.cs
// Purpose: Stable identity for a single scan block across diagnostic
//          snapshots. The planner produces ScanBlock instances fresh on
//          every rebuild, so per-block metrics need a config-derived key
//          rather than an object reference.
// Reference: docs/PHASE3_EXECUTION_PLAN.md F5
// ============================================================================

using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Diagnostics;

/// <summary>
/// Identifies one scan block by the tuple that uniquely places it in
/// the Modbus address space of a configuration: slave unit id, register
/// class (function code), start address, and register/bit count.
/// </summary>
/// <remarks>
/// <para>
/// Two blocks from different <see cref="ScanPlan"/> rebuilds compare equal
/// as long as the underlying config's coalesced layout hasn't changed —
/// that's the property the diagnostics collector needs to keep history
/// stable across no-op config reloads.
/// </para>
/// <para>Cheap by design — a readonly record struct, four fields, 6 bytes.</para>
/// </remarks>
public readonly record struct ScanBlockKey(
    byte UnitId,
    ModbusRegisterClass RegisterClass,
    ushort StartAddress,
    ushort Count)
{
    /// <summary>Derive a key from the enclosing <see cref="ScanGroup"/> and <see cref="ScanBlock"/>.</summary>
    public static ScanBlockKey From(ScanGroup group, ScanBlock block)
        => new(group.UnitId, group.RegisterClass, block.StartAddress, block.Count);

    /// <summary>Human-readable form — useful in log lines and failure messages.</summary>
    public override string ToString()
        => $"unit={UnitId} {RegisterClass} @ {StartAddress}+{Count}";
}
