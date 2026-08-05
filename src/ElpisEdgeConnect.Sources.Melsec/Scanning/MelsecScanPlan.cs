// ============================================================================
// File: Scanning/MelsecScanPlan.cs
// Purpose: Output types for the MELSEC scan planner — deterministic word-unit
//          read blocks plus per-tag decode mappings and typed planning errors.
//          Pure data; no transport or adapter coupling.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Sources.Melsec.Wire;

namespace ElpisEdgeConnect.Sources.Melsec.Scanning;

/// <summary>A typed, per-tag planning/validation error for a tag that cannot be planned.</summary>
/// <param name="TagName">The offending tag name.</param>
/// <param name="Code">Error code (e.g. <c>MELSEC.DEVICE_NOT_IMPLEMENTED</c>).</param>
/// <param name="Message">Operator-facing explanation.</param>
public sealed record MelsecPlanningError(string TagName, string Code, string Message);

/// <summary>Maps one configured tag onto its position within a read block.</summary>
public sealed record MelsecScanBlockEntry
{
    /// <summary>The originating tag definition (carries word order, scale/offset, unit).</summary>
    public required MelsecTagDefinition Tag { get; init; }

    /// <summary>The parsed device address.</summary>
    public required MelsecAddress Address { get; init; }

    /// <summary>Resolved datatype to decode.</summary>
    public required MelsecDatatype Datatype { get; init; }

    /// <summary>Byte offset of this value within the block's returned word payload.</summary>
    public required int ByteOffset { get; init; }

    /// <summary>Bit to extract for a boolean (word-bit index, or bit offset within a packed bit-device word); null otherwise.</summary>
    public int? BitIndex { get; init; }

    /// <summary>Convenience accessor for the tag name.</summary>
    public string TagName => Tag.Name;
}

/// <summary>
/// One planned word-unit (0401/0000) read: a device, a head device number, and a
/// count of returned 16-bit words, with the tag mappings it satisfies.
/// </summary>
public sealed record MelsecScanBlock
{
    /// <summary>Wire device code for the read.</summary>
    public required MelsecDeviceCode DeviceCode { get; init; }

    /// <summary>Operator device symbol (diagnostics).</summary>
    public required string DeviceSymbol { get; init; }

    /// <summary>Head device number for the read.</summary>
    public required int HeadDeviceNumber { get; init; }

    /// <summary>Number of returned 16-bit words (the request point count).</summary>
    public required int WordCount { get; init; }

    /// <summary>Scan-rate bucket (ms) this block belongs to.</summary>
    public required int ScanRateMs { get; init; }

    /// <summary>Tag mappings satisfied by this block.</summary>
    public required IReadOnlyList<MelsecScanBlockEntry> Entries { get; init; }
}

/// <summary>The result of planning a tag set: deterministic blocks plus unplannable-tag errors.</summary>
public sealed record MelsecScanPlan
{
    /// <summary>Deterministically ordered read blocks.</summary>
    public required IReadOnlyList<MelsecScanBlock> Blocks { get; init; }

    /// <summary>Tags that could not be planned, with typed reasons.</summary>
    public required IReadOnlyList<MelsecPlanningError> Errors { get; init; }
}
