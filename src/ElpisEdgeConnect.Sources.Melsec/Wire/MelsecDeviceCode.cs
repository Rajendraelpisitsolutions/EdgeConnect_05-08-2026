// ============================================================================
// File: Wire/MelsecDeviceCode.cs
// Purpose: MELSEC device codes as sent on the wire in SLMP / MC 3E BINARY
//          frames. The enum value IS the exact device-code byte per Mitsubishi
//          SH(NA)-080008. Slice-1 supports these read devices only (ADR-0033
//          Rule 4); every other recognized device is rejected at config time.
//
//          GOLDEN/SPEC NOTE: these byte values are spec-derived and remain
//          FIELD-UNVERIFIED until the customer Part B capture confirms them.
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md
// ============================================================================

namespace ElpisEdgeConnect.Sources.Melsec.Wire;

/// <summary>
/// MELSEC device codes for SLMP / MC 3E binary frames. The underlying byte is
/// the exact device-code byte transmitted in a batch-read request.
/// </summary>
public enum MelsecDeviceCode : byte
{
    /// <summary>Data register <c>D</c> (0xA8). Decimal address radix.</summary>
    D = 0xA8,

    /// <summary>Link register <c>W</c> (0xB4). Hexadecimal address radix.</summary>
    W = 0xB4,

    /// <summary>File register <c>R</c> (0xAF). Decimal address radix.</summary>
    R = 0xAF,

    /// <summary>Serial-access file register <c>ZR</c> (0xB0). Hexadecimal address radix.</summary>
    ZR = 0xB0,

    /// <summary>Internal relay <c>M</c> (0x90). Decimal address radix.</summary>
    M = 0x90,

    /// <summary>Input <c>X</c> (0x9C). Hexadecimal address radix.</summary>
    X = 0x9C,

    /// <summary>Output <c>Y</c> (0x9D). Hexadecimal address radix.</summary>
    Y = 0x9D,

    /// <summary>Link relay <c>B</c> (0xA0). Hexadecimal address radix.</summary>
    B = 0xA0,

    /// <summary>Special relay <c>SM</c> (0x91). Decimal address radix. (A-3a)</summary>
    SM = 0x91,

    /// <summary>Special register <c>SD</c> (0xA9). Decimal address radix. (A-3a)</summary>
    SD = 0xA9,

    /// <summary>Link special relay <c>SB</c> (0xA1). Hexadecimal address radix. (A-3a)</summary>
    SB = 0xA1,

    /// <summary>Link special register <c>SW</c> (0xB5). Hexadecimal address radix. (A-3a)</summary>
    SW = 0xB5,

    // ── A-3b timers / counters (codes [MC] SH(NA)-080008-AB §8.1 p68, confirmed
    //    on FX5 by [COM] SH(NA)-082625ENG-J; audit 2026-07-03-melsec-a3b0-…). All
    //    decimal, all on the 0401/0000 word-unit path. Long/extended families are
    //    NOT here (2-byte iQ-R-native codes, subcommand 0002). ──
    /// <summary>Timer contact <c>TS</c> (0xC1). Bit device.</summary>
    TS = 0xC1,
    /// <summary>Timer coil <c>TC</c> (0xC0). Bit device.</summary>
    TC = 0xC0,
    /// <summary>Timer current value <c>TN</c> (0xC2). Word device (1-word).</summary>
    TN = 0xC2,
    /// <summary>Retentive-timer contact <c>STS</c> (0xC7). Bit device.</summary>
    STS = 0xC7,
    /// <summary>Retentive-timer coil <c>STC</c> (0xC6). Bit device.</summary>
    STC = 0xC6,
    /// <summary>Retentive-timer current value <c>STN</c> (0xC8). Word device (1-word).</summary>
    STN = 0xC8,
    /// <summary>Counter contact <c>CS</c> (0xC4). Bit device.</summary>
    CS = 0xC4,
    /// <summary>Counter coil <c>CC</c> (0xC3). Bit device.</summary>
    CC = 0xC3,
    /// <summary>Counter current value <c>CN</c> (0xC5). Word device (1-word).</summary>
    CN = 0xC5,
}
