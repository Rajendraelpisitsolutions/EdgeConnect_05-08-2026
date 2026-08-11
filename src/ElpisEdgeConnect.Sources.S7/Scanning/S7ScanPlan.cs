// ============================================================================
// File: Scanning/S7ScanPlan.cs
// Purpose: Immutable plan that the S7 source adapter executes on every
//          poll cycle. Built once at Initialize time from the tag list
//          via S7ScanPlanner. Each Group corresponds to one scan-rate
//          bucket within one (area, dbNumber) tuple; each Block within
//          a group is a single Sharp7 ReadArea call covering one or
//          more entries.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I (mirrors Modbus's
//            ScanPlan structure)
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.S7.Scanning;

/// <summary>Top-level scan plan — the union of every group.</summary>
public sealed record S7ScanPlan(IReadOnlyList<S7ScanGroup> Groups);

/// <summary>
/// One scan-rate × (area, dbNumber) bucket. The poll loop fires a
/// group when its scan-rate has elapsed since the last fire.
/// </summary>
public sealed record S7ScanGroup(
    int IntervalMs,
    S7MemoryArea Area,
    int DbNumber,
    IReadOnlyList<S7ScanBlock> Blocks);

/// <summary>
/// One contiguous byte range within a group, mapped to one Sharp7
/// ReadArea call. May serve multiple tags (one per
/// <see cref="S7ScanBlockEntry"/>).
/// </summary>
public sealed record S7ScanBlock(
    int StartByte,
    int ByteCount,
    IReadOnlyList<S7ScanBlockEntry> Entries);

/// <summary>
/// One tag's position within a block — its byte offset from the
/// block's <see cref="S7ScanBlock.StartByte"/>, its bit offset (for
/// <see cref="S7Datatype.Bool"/>), and the typed spec needed to decode.
/// </summary>
public sealed record S7ScanBlockEntry(
    S7TagDefinition Tag,
    S7Address ParsedAddress,
    S7DatatypeSpec Spec,
    int BlockRelativeByteOffset);
