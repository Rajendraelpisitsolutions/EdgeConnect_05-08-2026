// ============================================================================
// File: Scanning/ModbusTagWidth.cs
// Purpose: Resolve a tag's on-wire width (in registers for FC03/FC04, in
//          bits for FC01/FC02) from its configured Datatype string. F2
//          uses this to pack blocks safely; F3 uses it to slice the
//          response payload.
// Reference: PHASE3_EXECUTION_PLAN.md §5 (Per-tag configuration),
//            §6 (Scan-group planner)
// ============================================================================

using System;
using System.Globalization;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

/// <summary>
/// Maps a <see cref="ModbusTagDefinition"/> to the number of registers (or
/// bits, for bit-oriented reads) it consumes on the wire.
/// </summary>
/// <remarks>
/// <para>Kept separate from the planner so F3's decoder can share it — the
/// decoder needs the same width to slice payloads.</para>
/// </remarks>
internal static class ModbusTagWidth
{
    /// <summary>
    /// Compute the tag's width. For bit-oriented reads
    /// (<see cref="ModbusRegisterClass.Coil"/>,
    /// <see cref="ModbusRegisterClass.DiscreteInput"/>) the width is always
    /// 1 (bits); <see cref="ModbusTagDefinition.Datatype"/> is ignored. For
    /// register reads the width is derived from the datatype:
    /// <list type="bullet">
    ///   <item><c>bool</c>, <c>int16</c>, <c>uint16</c>, unspecified: 1 register</item>
    ///   <item><c>int32</c>, <c>uint32</c>, <c>float32</c>: 2 registers</item>
    ///   <item><c>int64</c>, <c>uint64</c>, <c>float64</c>: 4 registers</item>
    ///   <item><c>stringN</c> (e.g. <c>string16</c>): ceil(N / 2) registers</item>
    /// </list>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a <c>stringN</c> datatype has a non-positive or absent
    /// length — the planner cannot size the block without a concrete N.
    /// </exception>
    public static ushort Resolve(ModbusTagDefinition tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (tag.RegisterClass.IsBitRead())
        {
            return 1;
        }

        var datatype = tag.Datatype?.Trim();
        if (string.IsNullOrEmpty(datatype))
        {
            return 1;
        }

        var normalized = datatype.ToLowerInvariant();
        switch (normalized)
        {
            case "bool":
            case "int16":
            case "uint16":
                return 1;
            case "int32":
            case "uint32":
            case "float32":
                return 2;
            case "int64":
            case "uint64":
            case "float64":
                return 4;
        }

        if (normalized.StartsWith("string", StringComparison.Ordinal))
        {
            return StringWidth(tag, normalized);
        }

        // Unknown datatype — treat as a single register. F3's decoder will
        // surface a clearer error when it tries to decode.
        return 1;
    }

    private static ushort StringWidth(ModbusTagDefinition tag, string normalized)
    {
        // "string16" → 16 chars → 8 registers
        // "string"   → missing length — reject
        var suffix = normalized["string".Length..];
        if (string.IsNullOrEmpty(suffix))
        {
            throw new ArgumentException(
                $"Tag '{tag.Name}' has datatype 'string' without a length; use 'stringN' (e.g. 'string16').",
                nameof(tag));
        }
        if (!int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var charCount) ||
            charCount <= 0)
        {
            throw new ArgumentException(
                $"Tag '{tag.Name}' has datatype '{tag.Datatype}' with a non-positive length.",
                nameof(tag));
        }
        // Two characters per register — round up for odd lengths.
        return (ushort)((charCount + 1) / 2);
    }
}
