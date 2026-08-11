// ============================================================================
// File: SimulatedTag.cs
// Purpose: A single simulated controller tag — its CIP atomic type and a
//          time-varying value generator. Values trace a 30s sine with a stable
//          per-name phase so distinct tags show distinct curves (mirrors the
//          EthernetIpDemoClient value model so demo and simulator look alike).
// ============================================================================

using System.Globalization;

namespace ElpisEdgeConnect.Tools.EthernetIpSimulator;

/// <summary>CIP atomic element types the simulator can serve.</summary>
internal enum SimCipType
{
    Bool,
    Sint,
    Int,
    Dint,
    Lint,
    Real,
    Lreal,
}

internal static class SimCipTypeExtensions
{
    /// <summary>The CIP on-wire type code (little-endian UINT) for this type.</summary>
    public static ushort TypeCode(this SimCipType t) => t switch
    {
        SimCipType.Bool => 0x00C1,
        SimCipType.Sint => 0x00C2,
        SimCipType.Int => 0x00C3,
        SimCipType.Dint => 0x00C4,
        SimCipType.Lint => 0x00C5,
        SimCipType.Real => 0x00CA,
        SimCipType.Lreal => 0x00CB,
        _ => 0x00C4,
    };

    /// <summary>Parse a Logix datatype name (case-insensitive). Null on unknown.</summary>
    public static SimCipType? ParseOrNull(string raw) => raw.Trim().ToUpperInvariant() switch
    {
        "BOOL" => SimCipType.Bool,
        "SINT" => SimCipType.Sint,
        "INT" => SimCipType.Int,
        "DINT" => SimCipType.Dint,
        "LINT" => SimCipType.Lint,
        "REAL" => SimCipType.Real,
        "LREAL" => SimCipType.Lreal,
        _ => null,
    };
}

/// <summary>A named simulated tag with a deterministic time-varying value.</summary>
internal sealed class SimulatedTag
{
    public SimulatedTag(string name, SimCipType type)
    {
        Name = name;
        Type = type;
        _phase = StablePhase(name);
    }

    public string Name { get; }

    public SimCipType Type { get; }

    private readonly double _phase;

    /// <summary>
    /// The CIP value bytes (little-endian) for this tag at <paramref name="elapsedSeconds"/>,
    /// plus a human-readable rendering for logging.
    /// </summary>
    public (byte[] Bytes, string Display) Encode(double elapsedSeconds)
    {
        var sine = Math.Sin((2.0 * Math.PI * elapsedSeconds / 30.0) + _phase);
        switch (Type)
        {
            case SimCipType.Bool:
            {
                var v = sine >= 0.0;
                return ([(byte)(v ? 1 : 0)], v ? "true" : "false");
            }
            case SimCipType.Sint:
            {
                var v = (sbyte)Math.Round(50 + 40 * sine);
                return ([(byte)v], v.ToString(CultureInfo.InvariantCulture));
            }
            case SimCipType.Int:
            {
                var v = (short)Math.Round(500 + 400 * sine);
                return (Le16((ushort)v), v.ToString(CultureInfo.InvariantCulture));
            }
            case SimCipType.Dint:
            {
                var v = (int)Math.Round(5000 + 4000 * sine);
                return (Le32((uint)v), v.ToString(CultureInfo.InvariantCulture));
            }
            case SimCipType.Lint:
            {
                var v = (long)Math.Round(50_000 + 40_000 * sine);
                return (Le64((ulong)v), v.ToString(CultureInfo.InvariantCulture));
            }
            case SimCipType.Real:
            {
                var v = (float)(100.0 + 50.0 * sine);
                return (BitConverter.GetBytes(v), v.ToString("0.###", CultureInfo.InvariantCulture));
            }
            case SimCipType.Lreal:
            {
                var v = 100.0 + 50.0 * sine;
                return (BitConverter.GetBytes(v), v.ToString("0.###", CultureInfo.InvariantCulture));
            }
            default:
                return (Le32(0), "0");
        }
    }

    private static byte[] Le16(ushort v) => [(byte)(v & 0xFF), (byte)(v >> 8)];

    private static byte[] Le32(uint v) =>
        [(byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF), (byte)((v >> 24) & 0xFF)];

    private static byte[] Le64(ulong v)
    {
        var b = new byte[8];
        for (var i = 0; i < 8; i++)
        {
            b[i] = (byte)((v >> (8 * i)) & 0xFF);
        }
        return b;
    }

    /// <summary>Deterministic phase in <c>[0, 2π)</c> derived from the tag name.</summary>
    private static double StablePhase(string name)
    {
        var acc = 0;
        foreach (var c in name)
        {
            acc = unchecked((acc * 31) + c);
        }
        return ((acc & 0x7FFFFFFF) % 360) * Math.PI / 180.0;
    }
}
