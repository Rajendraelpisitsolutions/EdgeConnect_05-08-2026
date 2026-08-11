// ============================================================================
// File: MelsecConfigEnums.cs
// Purpose: Connection-level enums for the MELSEC source configuration —
//          transport, MC frame mode, device-family profile, and multi-word
//          byte/word order. All are config-accepted; Slice 1 validation
//          rejects everything except Tcp + Mc3EBinary + Modern (ADR-0033 Rule 2).
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md
// ============================================================================

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>Transport for the MC/SLMP connection.</summary>
public enum MelsecTransportProtocol
{
    /// <summary>TCP (the only transport implemented in Slice 1).</summary>
    Tcp = 1,

    /// <summary>UDP — config-accepted, validation-rejected in Slice 1 (connectionless,
    /// needs a different reliability model than TCP single-flight/reconnect).</summary>
    Udp = 2,
}

/// <summary>MC Protocol / SLMP frame mode.</summary>
public enum MelsecFrameMode
{
    /// <summary>MC 3E binary — the only mode implemented in Slice 1.</summary>
    Mc3EBinary = 1,

    /// <summary>MC 3E ASCII — config-accepted, validation-rejected in Slice 1.</summary>
    Mc3EAscii = 2,

    /// <summary>MC 4E binary (adds a request/response serial) — config-accepted,
    /// validation-rejected in Slice 1.</summary>
    Mc4EBinary = 3,

    /// <summary>MC 1E binary (legacy A-series frame) — config-accepted,
    /// validation-rejected in Slice 1.</summary>
    Mc1EBinary = 4,
}

/// <summary>CPU family profile — governs batch-read point caps and frame compatibility.</summary>
public enum MelsecDeviceProfile
{
    /// <summary>Modern iQ-R / iQ-L / Q / L families (3E binary). The only profile
    /// supported in Slice 1; word-batch-read cap is 960 words.</summary>
    Modern = 1,

    /// <summary>QnA family — config-accepted, validation-rejected in Slice 1
    /// (lower batch-read cap, TBD until customer hardware confirms).</summary>
    QnA = 2,

    /// <summary>A-series family (1E frame) — config-accepted, validation-rejected
    /// in Slice 1.</summary>
    ACpu = 3,

    /// <summary>iQ-F / FX5 family (3E binary over built-in Ethernet). Profile data
    /// exists in <c>Profiles.MelsecProfiles</c> (A-2 Gate A-2I) but the profile is
    /// INTERNAL/TESTABLE ONLY — config-accepted, validation-rejected until the
    /// profile selector is separately approved (Gate A-2O).</summary>
    IqF = 4,
}

/// <summary>Word order for multi-word (32/64-bit) values. Customer PLC/program
/// conventions vary, so this is a per-tag choice (ADR-0033, plan v2 Δ3).</summary>
public enum MelsecWordOrder
{
    /// <summary>Low word at the lower device address (default — most MELSEC programs).</summary>
    LowWordFirst = 1,

    /// <summary>High word at the lower device address.</summary>
    HighWordFirst = 2,
}
