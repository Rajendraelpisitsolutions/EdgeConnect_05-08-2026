// ============================================================================
// File: Decoding/ModbusDatatype.cs
// Purpose: Canonical set of Modbus datatypes the decoder understands, plus
//          helpers for parsing user-supplied datatype strings from config.
//
//          Kept as an enum + parser (rather than raw strings carried deep
//          into the decoder) so we can:
//            - reject unknown datatypes at ValidateConfigAsync time,
//            - centralize the datatype → register-width and
//              datatype → CanonicalValueType mappings,
//            - extend once (per-datatype) instead of touching every
//              switch statement.
// ============================================================================

using System;
using System.Globalization;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Decoding;

/// <summary>Datatypes supported by the Modbus decoder.</summary>
public enum ModbusDatatype
{
    /// <summary>Single boolean — used only for <c>Coil</c> / <c>DiscreteInput</c> tags.</summary>
    Bool = 0,

    /// <summary>16-bit signed integer. 1 register.</summary>
    Int16 = 1,

    /// <summary>16-bit unsigned integer. 1 register.</summary>
    UInt16 = 2,

    /// <summary>32-bit signed integer. 2 registers.</summary>
    Int32 = 3,

    /// <summary>32-bit unsigned integer. 2 registers.</summary>
    UInt32 = 4,

    /// <summary>IEEE-754 single-precision float. 2 registers.</summary>
    Float32 = 5,

    /// <summary>64-bit signed integer. 4 registers.</summary>
    Int64 = 6,

    /// <summary>64-bit unsigned integer. 4 registers.</summary>
    UInt64 = 7,

    /// <summary>IEEE-754 double-precision float. 4 registers.</summary>
    Float64 = 8,

    /// <summary>ASCII string packed two chars per register, high char first. Length in chars is carried separately.</summary>
    StringN = 9,
}

/// <summary>
/// Parsed form of a datatype hint from config — the enum plus the string
/// length (chars) for <see cref="ModbusDatatype.StringN"/>. Register width
/// here is <b>always</b> derived from <see cref="ByteCount"/>, never stored
/// redundantly.
/// </summary>
public readonly record struct ModbusDatatypeSpec(ModbusDatatype Datatype, int StringLengthChars = 0)
{
    /// <summary>Number of wire bytes this value occupies.</summary>
    public int ByteCount => Datatype switch
    {
        ModbusDatatype.Bool or ModbusDatatype.Int16 or ModbusDatatype.UInt16 => 2,
        ModbusDatatype.Int32 or ModbusDatatype.UInt32 or ModbusDatatype.Float32 => 4,
        ModbusDatatype.Int64 or ModbusDatatype.UInt64 or ModbusDatatype.Float64 => 8,
        ModbusDatatype.StringN => RoundedRegisterCount() * 2,
        _ => throw new ArgumentOutOfRangeException(),
    };

    /// <summary>Number of Modbus registers this value consumes on the wire.</summary>
    public int RegisterCount => Datatype == ModbusDatatype.Bool ? 1 : (ByteCount + 1) / 2;

    /// <summary>
    /// Mapping from decoded value type to <see cref="CanonicalValueType"/>.
    /// <c>uint32</c> / <c>uint64</c> are widened to <see cref="CanonicalValueType.Long"/>
    /// since the canonical model has no unsigned 64-bit carrier.
    /// </summary>
    public CanonicalValueType CanonicalType => Datatype switch
    {
        ModbusDatatype.Bool => CanonicalValueType.Boolean,
        ModbusDatatype.Int16 or ModbusDatatype.UInt16 or ModbusDatatype.Int32 => CanonicalValueType.Integer,
        ModbusDatatype.UInt32 or ModbusDatatype.Int64 or ModbusDatatype.UInt64 => CanonicalValueType.Long,
        ModbusDatatype.Float32 => CanonicalValueType.Float,
        ModbusDatatype.Float64 => CanonicalValueType.Double,
        ModbusDatatype.StringN => CanonicalValueType.String,
        _ => throw new ArgumentOutOfRangeException(),
    };

    /// <summary>
    /// True if this datatype accepts a <see cref="ModbusTagDefinition.Scale"/>
    /// or <see cref="ModbusTagDefinition.Offset"/>. False for <c>bool</c>
    /// and <c>stringN</c>.
    /// </summary>
    public bool SupportsScaleOffset => Datatype is not (ModbusDatatype.Bool or ModbusDatatype.StringN);

    private int RoundedRegisterCount()
    {
        if (StringLengthChars <= 0)
        {
            throw new InvalidOperationException("stringN datatype is missing a positive length.");
        }
        return (StringLengthChars + 1) / 2;
    }
}

/// <summary>
/// Parse a user-supplied datatype hint (e.g. <c>"uint16"</c>, <c>"string16"</c>).
/// Case-insensitive. Throws <see cref="ArgumentException"/> for unrecognised
/// or malformed values.
/// </summary>
public static class ModbusDatatypeParser
{
    /// <summary>
    /// Parse a datatype hint, or return the supplied default when
    /// <paramref name="raw"/> is null / empty.
    /// </summary>
    /// <param name="raw">Raw datatype string from config.</param>
    /// <param name="defaultSpec">
    /// Fallback used when <paramref name="raw"/> is absent — typically
    /// <c>Bool</c> for bit classes and <c>UInt16</c> for register classes.
    /// </param>
    public static ModbusDatatypeSpec Parse(string? raw, ModbusDatatypeSpec defaultSpec)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultSpec;
        }
        var normalized = raw.Trim().ToLowerInvariant();
        return normalized switch
        {
            "bool" => new ModbusDatatypeSpec(ModbusDatatype.Bool),
            "int16" => new ModbusDatatypeSpec(ModbusDatatype.Int16),
            "uint16" => new ModbusDatatypeSpec(ModbusDatatype.UInt16),
            "int32" => new ModbusDatatypeSpec(ModbusDatatype.Int32),
            "uint32" => new ModbusDatatypeSpec(ModbusDatatype.UInt32),
            "float32" => new ModbusDatatypeSpec(ModbusDatatype.Float32),
            "int64" => new ModbusDatatypeSpec(ModbusDatatype.Int64),
            "uint64" => new ModbusDatatypeSpec(ModbusDatatype.UInt64),
            "float64" => new ModbusDatatypeSpec(ModbusDatatype.Float64),
            _ when normalized.StartsWith("string", StringComparison.Ordinal) => ParseString(normalized, raw),
            _ => throw new ArgumentException(
                $"Unknown Modbus datatype '{raw}'. Supported: bool, int16, uint16, int32, uint32, float32, int64, uint64, float64, stringN.",
                nameof(raw)),
        };
    }

    private static ModbusDatatypeSpec ParseString(string normalized, string raw)
    {
        var suffix = normalized["string".Length..];
        if (string.IsNullOrEmpty(suffix))
        {
            throw new ArgumentException(
                $"Datatype '{raw}' is missing a length — use 'stringN' (e.g. 'string16').");
        }
        if (!int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length <= 0)
        {
            throw new ArgumentException(
                $"Datatype '{raw}' has a non-positive length.");
        }
        return new ModbusDatatypeSpec(ModbusDatatype.StringN, length);
    }
}
