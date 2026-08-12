// ============================================================================
// File: MelsecDevice.cs
// Purpose: Single source of truth for Slice-1 MELSEC device metadata — symbol,
//          radix, and word/bit nature — layered on top of the wire device-code
//          byte (Wire.MelsecDeviceCode). The code byte is NOT duplicated here;
//          each descriptor references the Wire enum. Also lists the recognized-
//          but-unsupported devices that must reject with DEVICE_NOT_IMPLEMENTED.
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md (Rules 3, 4)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ElpisEdgeConnect.Sources.Melsec.Wire;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>Whether a device is word-addressed or bit-addressed.</summary>
public enum MelsecDeviceKind
{
    /// <summary>Word device (D, W, R, ZR) — read directly as 16-bit words.</summary>
    Word,

    /// <summary>Bit device (M, X, Y, B) — read via word-unit blocks, then bit-extracted.</summary>
    Bit,
}

/// <summary>
/// Metadata for one supported MELSEC device: its symbol, wire code byte, address
/// number radix, and word/bit nature.
/// </summary>
public sealed record MelsecDeviceDescriptor
{
    /// <summary>Operator-facing device symbol, e.g. <c>"D"</c>, <c>"ZR"</c>.</summary>
    public required string Symbol { get; init; }

    /// <summary>Wire device-code byte (the single source of truth in <see cref="MelsecDeviceCode"/>).</summary>
    public required MelsecDeviceCode Code { get; init; }

    /// <summary>Address-number radix — 8 (octal, iQ-F X/Y operator labels),
    /// 10 (decimal) or 16 (hexadecimal).</summary>
    public required int Radix { get; init; }

    /// <summary>Word or bit device.</summary>
    public required MelsecDeviceKind Kind { get; init; }

    /// <summary>
    /// True for word devices whose current value is a SINGLE word only — no
    /// 32-bit (2-word) datatypes. Timer/counter current values (TN/STN/CN) set
    /// this: the 2-word/long current-value form is the excluded long family
    /// (LTN/LSTN/LCN, subcommand 0002). Default false (D/W/R/ZR/SD/SW allow 32-bit).
    /// </summary>
    public bool SingleWordOnly { get; init; }
}

/// <summary>
/// Registry of Slice-1 supported devices and the recognized-but-unsupported set.
/// </summary>
public static class MelsecDevices
{
    // Radix: hex for X, Y, B, W, ZR; decimal for D, R, M (ADR-0033 Rule 3 — ZR is HEX).
    private static readonly Dictionary<string, MelsecDeviceDescriptor> SupportedBySymbol =
        new(StringComparer.Ordinal)
        {
            ["D"] = new() { Symbol = "D", Code = MelsecDeviceCode.D, Radix = 10, Kind = MelsecDeviceKind.Word },
            ["W"] = new() { Symbol = "W", Code = MelsecDeviceCode.W, Radix = 16, Kind = MelsecDeviceKind.Word },
            ["R"] = new() { Symbol = "R", Code = MelsecDeviceCode.R, Radix = 10, Kind = MelsecDeviceKind.Word },
            ["ZR"] = new() { Symbol = "ZR", Code = MelsecDeviceCode.ZR, Radix = 16, Kind = MelsecDeviceKind.Word },
            ["M"] = new() { Symbol = "M", Code = MelsecDeviceCode.M, Radix = 10, Kind = MelsecDeviceKind.Bit },
            ["X"] = new() { Symbol = "X", Code = MelsecDeviceCode.X, Radix = 16, Kind = MelsecDeviceKind.Bit },
            ["Y"] = new() { Symbol = "Y", Code = MelsecDeviceCode.Y, Radix = 16, Kind = MelsecDeviceKind.Bit },
            ["B"] = new() { Symbol = "B", Code = MelsecDeviceCode.B, Radix = 16, Kind = MelsecDeviceKind.Bit },
            // A-3a special devices ([MC] SH(NA)-080008-AB §8.1 p68; audited in
            // docs/sessions/2026-07-03-melsec-a3a-audit.md).
            ["SM"] = new() { Symbol = "SM", Code = MelsecDeviceCode.SM, Radix = 10, Kind = MelsecDeviceKind.Bit },
            ["SD"] = new() { Symbol = "SD", Code = MelsecDeviceCode.SD, Radix = 10, Kind = MelsecDeviceKind.Word },
            ["SB"] = new() { Symbol = "SB", Code = MelsecDeviceCode.SB, Radix = 16, Kind = MelsecDeviceKind.Bit },
            ["SW"] = new() { Symbol = "SW", Code = MelsecDeviceCode.SW, Radix = 16, Kind = MelsecDeviceKind.Word },
            // A-3b timers/counters (audit 2026-07-03-melsec-a3b0-…). Contacts/coils
            // are bit; current values (TN/STN/CN) are SINGLE-word only.
            ["TS"] = new() { Symbol = "TS", Code = MelsecDeviceCode.TS, Radix = 10, Kind = MelsecDeviceKind.Bit },
            ["TC"] = new() { Symbol = "TC", Code = MelsecDeviceCode.TC, Radix = 10, Kind = MelsecDeviceKind.Bit },
            ["TN"] = new() { Symbol = "TN", Code = MelsecDeviceCode.TN, Radix = 10, Kind = MelsecDeviceKind.Word, SingleWordOnly = true },
            ["STS"] = new() { Symbol = "STS", Code = MelsecDeviceCode.STS, Radix = 10, Kind = MelsecDeviceKind.Bit },
            ["STC"] = new() { Symbol = "STC", Code = MelsecDeviceCode.STC, Radix = 10, Kind = MelsecDeviceKind.Bit },
            ["STN"] = new() { Symbol = "STN", Code = MelsecDeviceCode.STN, Radix = 10, Kind = MelsecDeviceKind.Word, SingleWordOnly = true },
            ["CS"] = new() { Symbol = "CS", Code = MelsecDeviceCode.CS, Radix = 10, Kind = MelsecDeviceKind.Bit },
            ["CC"] = new() { Symbol = "CC", Code = MelsecDeviceCode.CC, Radix = 10, Kind = MelsecDeviceKind.Bit },
            ["CN"] = new() { Symbol = "CN", Code = MelsecDeviceCode.CN, Radix = 10, Kind = MelsecDeviceKind.Word, SingleWordOnly = true },
        };

    // Recognized MELSEC devices that are NOT implemented (ADR-0033 Rule 4).
    // Bare T/C/ST are AMBIGUOUS timer/counter prefixes — rejected with a
    // suggestion (see MelsecAddressParser.AmbiguousPrefixSuggestion). The long /
    // extended families (L*, subcommand 0002) stay excluded per A-3b plan v2 §4.
    private static readonly HashSet<string> RecognizedUnsupportedSymbols =
        new(StringComparer.Ordinal)
        {
            "DX", "DY", "L", "F", "V", "S", "Z",
            "T", "C", "ST",  // ambiguous bare timer/counter/retentive prefixes
            "LTS", "LTC", "LTN", "LSTS", "LSTC", "LSTN", "LCS", "LCC", "LCN", "LZ", // long/extended
        };

    /// <summary>Comma-separated list of supported symbols, for error messages.</summary>
    public const string SupportedList =
        "D, W, R, ZR, M, X, Y, B, SM, SD, SB, SW, TS, TC, TN, STS, STC, STN, CS, CC, CN";

    /// <summary>Resolve a supported device descriptor by symbol.</summary>
    public static bool TryGet(string symbol, [MaybeNullWhen(false)] out MelsecDeviceDescriptor descriptor) =>
        SupportedBySymbol.TryGetValue(symbol, out descriptor);

    /// <summary>True for a recognized MELSEC device that Slice 1 does not implement.</summary>
    public static bool IsRecognizedUnsupported(string symbol) => RecognizedUnsupportedSymbols.Contains(symbol);

    /// <summary>True for any recognized symbol — supported or recognized-but-unsupported.</summary>
    public static bool IsKnownSymbol(string symbol) =>
        SupportedBySymbol.ContainsKey(symbol) || RecognizedUnsupportedSymbols.Contains(symbol);
}
