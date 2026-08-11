// ============================================================================
// File: Scanning/ScanPlan.cs
// Purpose: Immutable scan-plan DTOs produced by ScanPlanner and consumed by
//          the adapter's polling loop (F3 wires this in). A plan is a
//          flattened schedule: one group per (intervalMs, unitId, FC), each
//          group carrying a list of FC-safe blocks, each block carrying
//          the tag entries that fall inside it.
// Reference: PHASE3_EXECUTION_PLAN.md §6
// ============================================================================

using System.Collections.Generic;
using System.Linq;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

/// <summary>
/// The complete scan plan for a Modbus source — a list of scan groups, each
/// of which holds the contiguous read blocks the adapter will issue.
/// </summary>
/// <remarks>
/// <para>A plan is immutable: each <c>ModbusTcpSourceConfiguration</c> change
/// produces a fresh plan. The adapter's polling loop then drives each group
/// on its own timer.</para>
/// </remarks>
public sealed record ScanPlan
{
    /// <summary>All scan groups in the plan. Never <see langword="null"/>; may be empty for a tag-less config.</summary>
    public required IReadOnlyList<ScanGroup> Groups { get; init; }

    /// <summary>An empty plan — convenience for adapter initialization paths.</summary>
    public static ScanPlan Empty { get; } = new() { Groups = System.Array.Empty<ScanGroup>() };

    /// <summary>Total count of blocks across all groups (diagnostics helper).</summary>
    public int TotalBlockCount => Groups.Sum(g => g.Blocks.Count);

    /// <summary>Total count of tag entries across all groups (diagnostics helper).</summary>
    public int TotalTagCount => Groups.Sum(g => g.Blocks.Sum(b => b.Entries.Count));
}

/// <summary>
/// A single scan group — every tag in this group shares the same poll
/// interval, the same Modbus unit id, and the same register class (and
/// therefore the same function code). The adapter drives one timer per
/// group.
/// </summary>
public sealed record ScanGroup
{
    /// <summary>Poll interval in milliseconds shared by every tag in this group.</summary>
    public required int IntervalMs { get; init; }

    /// <summary>Modbus unit id (1..247) shared by every tag in this group.</summary>
    public required byte UnitId { get; init; }

    /// <summary>Register class shared by every tag in this group.</summary>
    public required ModbusRegisterClass RegisterClass { get; init; }

    /// <summary>Modbus function code corresponding to <see cref="RegisterClass"/>.</summary>
    public byte FunctionCode => RegisterClass.ToFunctionCode();

    /// <summary>Blocks in this group, each a single FC-safe read transaction.</summary>
    public required IReadOnlyList<ScanBlock> Blocks { get; init; }
}

/// <summary>
/// One contiguous read — equivalent to a single on-wire FC request. Carries
/// the address range the adapter asks the slave to return, plus the tag
/// entries whose addresses fall within that range.
/// </summary>
/// <remarks>
/// <para><see cref="Count"/> is always within the function code's hard limit
/// (125 registers for FC03/FC04, 2000 bits for FC01/FC02). The greedy packer
/// refuses to merge tags that would push a block past that limit.</para>
/// </remarks>
public sealed record ScanBlock
{
    /// <summary>Zero-based start address of the block.</summary>
    public required ushort StartAddress { get; init; }

    /// <summary>
    /// Size of the block. Registers for FC03/FC04, bits for FC01/FC02.
    /// Always &gt; 0 and &lt;= the function code's hard limit.
    /// </summary>
    public required ushort Count { get; init; }

    /// <summary>Tag entries that fall within this block. Never empty.</summary>
    public required IReadOnlyList<ScanBlockEntry> Entries { get; init; }

    /// <summary>
    /// Build the <see cref="ModbusReadRequest"/> that materializes this block
    /// against the executor. The register class and unit id come from the
    /// enclosing <see cref="ScanGroup"/>.
    /// </summary>
    public ModbusReadRequest ToRequest(byte unitId, ModbusRegisterClass registerClass)
        => new(unitId, registerClass, StartAddress, Count);
}

/// <summary>
/// A single tag inside a <see cref="ScanBlock"/>. The adapter decoder (F3)
/// uses <see cref="Offset"/> and <see cref="Width"/> to slice the block
/// payload down to this tag's raw values before applying datatype
/// normalization.
/// </summary>
public sealed record ScanBlockEntry
{
    /// <summary>The tag this entry refers to.</summary>
    public required ModbusTagDefinition Tag { get; init; }

    /// <summary>
    /// Offset of this tag's first register/bit from the block's start
    /// address. Always &gt;= 0 and &lt; block count.
    /// </summary>
    public required ushort Offset { get; init; }

    /// <summary>
    /// How many registers (FC03/FC04) or bits (FC01/FC02) this tag occupies.
    /// Derived from <see cref="ModbusTagDefinition.Datatype"/>. Always &gt;= 1.
    /// </summary>
    public required ushort Width { get; init; }
}
